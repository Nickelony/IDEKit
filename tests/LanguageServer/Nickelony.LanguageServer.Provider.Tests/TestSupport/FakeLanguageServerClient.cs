using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Minimal <see cref="ILanguageServerClient"/> fake for framework tests: readiness, transport generation, and
/// capability flags are settable, transport sends are recorded per method, and requests return the default
/// response so the framework's documented fallback values surface. Tests that need richer transport behavior
/// must extend this fake with the behavior under test.
/// </summary>
/// <remarks>
/// The Lua suite carries a second <see cref="ILanguageServerClient"/> double with a deliberately different
/// dialect: it replays configured per-method responses and completes sends synchronously, whereas this one
/// surfaces the framework's documented fallbacks and models transport generations. The two are kept separate
/// because each mirrors the contract its own suite exercises - this one isolates the provider framework, the Lua
/// one isolates the Lua document flows - so the readiness, generation, and disposal semantics stay readable in
/// each instead of collapsing into one over-configured double. Their shared reference is the real client's
/// contract, not each other.
/// </remarks>
internal sealed class FakeLanguageServerClient : ILanguageServerClient
{
	private readonly object _sentSyncRoot = new();
	private readonly List<(string Method, JsonElement Parameters)> _sentNotifications = [];
	private readonly List<(string Method, JsonElement Parameters)> _sentRequests = [];
	private readonly List<string> _sentMethodNames = [];
	private readonly List<string> _attemptedNotificationMethodNames = [];
	private readonly List<long> _sendRequestGenerations = [];
	private readonly List<long> _markedUnhealthyGenerations = [];

	private int _sendRequestCount;
	private int _startCallCount;
	private int _disposeCallCount;
	private int _simulatedDidCloseDisposalRaceCount;

	private bool _isDisposed;

	/// <summary>
	/// Gets or sets a value indicating whether the client reports a ready session. Defaults to
	/// <see langword="true"/>; while it is true (and no <see cref="StartAsyncHandler"/> is set),
	/// <see cref="StartAsync"/> returns immediately with the current transport generation, mirroring the real
	/// client. Set it to <see langword="false"/> to model a session that needs a fresh start. A ready session is
	/// a started session, so a test that constructs the client ready must also set a nonzero
	/// <see cref="TransportGeneration"/> (generation 0 means no session was started yet).
	/// </summary>
	public bool IsReady { get; set; } = true;

	/// <summary>
	/// Gets or sets the reported transport generation. Tests set it directly to simulate a transport replacement
	/// without a full <see cref="StartAsync"/> cycle.
	/// </summary>
	public long TransportGeneration { get; set; }

	/// <summary>
	/// Gets or sets the handler that produces one request result, or <see langword="null"/> to return the default
	/// response so the framework's documented fallback values surface.
	/// </summary>
	public Func<string, object, CancellationToken, Task<object?>>? SendRequestHandler { get; set; }

	/// <summary>
	/// Gets or sets the handler that runs for one notification before it is recorded, or <see langword="null"/> to
	/// record notifications directly. Set a handler to inject transport failures (throw
	/// <see cref="IOException"/>/<see cref="OperationCanceledException"/>) or to gate a send on test coordination.
	/// </summary>
	public Func<string, object?, CancellationToken, Task>? SendNotificationHandler { get; set; }

	/// <summary>
	/// Gets or sets the value <see cref="TryMarkTransportUnhealthy"/> returns. Defaults to
	/// <see langword="true"/>, in which case the fake applies the real generation-matching semantics.
	/// </summary>
	public bool TryMarkTransportUnhealthyResult { get; set; } = true;

	/// <summary>
	/// Gets the number of <see cref="SendRequestAsync{TResult}"/> calls.
	/// </summary>
	public int SendRequestCount => Volatile.Read(ref _sendRequestCount);

	/// <summary>
	/// Gets the number of <c>textDocument/didClose</c> notifications that hit the simulated disposal race
	/// (<see cref="ThrowObjectDisposedOnDidClose"/>).
	/// </summary>
	public int SimulatedDidCloseDisposalRaceCount => Volatile.Read(ref _simulatedDidCloseDisposalRaceCount);

	/// <summary>
	/// Gets a stable snapshot of the transport generations observed at each request send, in send order.
	/// </summary>
	public IReadOnlyList<long> SendRequestGenerations
	{
		get
		{
			lock (_sentSyncRoot)
				return [.. _sendRequestGenerations];
		}
	}

	/// <summary>
	/// Gets a stable snapshot of the generations passed to <see cref="TryMarkTransportUnhealthy"/>, in call order.
	/// </summary>
	public IReadOnlyList<long> MarkedUnhealthyGenerations
	{
		get
		{
			lock (_sentSyncRoot)
				return [.. _markedUnhealthyGenerations];
		}
	}

	/// <summary>
	/// Gets or sets a value indicating whether <see cref="StartAsync"/> succeeds. Defaults to
	/// <see langword="true"/>. Ignored when <see cref="StartAsyncHandler"/> is set.
	/// </summary>
	public bool StartResult { get; set; } = true;

	/// <summary>
	/// Gets or sets a handler that replaces the default <see cref="StartAsync"/> behavior, for tests that need to
	/// gate, fault, or fail a startup attempt. The default generation and readiness updates run when the handler
	/// reports success. While a handler is set, every start runs the handler and activates a fresh transport
	/// generation when it reports success - even when the client currently reports a ready session - so a test can
	/// model a startup attempt that replaces the session.
	/// </summary>
	public Func<CancellationToken, Task<bool>>? StartAsyncHandler { get; set; }

	/// <summary>
	/// Gets the number of <see cref="StartAsync"/> calls.
	/// </summary>
	public int StartCallCount => Volatile.Read(ref _startCallCount);

	/// <inheritdoc/>
	public Exception? LastStartupException { get; set; }

	/// <summary>
	/// Gets the number of <see cref="Dispose"/> and <see cref="DisposeAsync"/> calls.
	/// </summary>
	public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

	/// <summary>
	/// Gets or sets an optional task both <see cref="Dispose"/> and <see cref="DisposeAsync"/> wait on (blocking for
	/// the synchronous overload) before reporting the client disposed. A test holds this to keep a provider teardown
	/// in progress while it asserts that a later dispose caller waits for it.
	/// </summary>
	public Task? DisposeGate { get; set; }

	/// <inheritdoc/>
	/// <remarks>
	/// The fake keeps the configured <see cref="TextDocumentSyncKind"/> across a simulated transport loss, unlike
	/// the real client, so tests can exercise post-loss flows; set it to <see cref="TextDocumentSyncKind.None"/>
	/// explicitly to simulate a ready session without a negotiated synchronization mode.
	/// </remarks>
	public TextDocumentSyncKind TextDocumentSyncKind { get; set; } = TextDocumentSyncKind.Incremental;

	/// <inheritdoc/>
	public IReadOnlyList<string> SemanticTokenTypes { get; set; } = [];

	/// <inheritdoc/>
	public IReadOnlyList<string> SemanticTokenModifiers { get; set; } = [];

	/// <inheritdoc/>
	public bool SupportsCompletionResolve { get; set; }

	/// <inheritdoc/>
	public bool SupportsDocumentSymbols { get; set; }

	/// <inheritdoc/>
	public bool SupportsCodeActions { get; set; }

	/// <inheritdoc/>
	public bool SupportsCodeActionResolve { get; set; }

	/// <inheritdoc/>
	public bool SupportsReferences { get; set; }

	/// <inheritdoc/>
	public bool SupportsRename { get; set; }

	/// <inheritdoc/>
	public bool SupportsFormatting { get; set; }

	/// <inheritdoc/>
	public bool SupportsHover { get; set; }

	/// <inheritdoc/>
	public bool SupportsDefinition { get; set; }

	/// <inheritdoc/>
	public bool SupportsSignatureHelp { get; set; }

	/// <inheritdoc/>
	public bool SupportsSemanticTokensFull { get; set; }

	/// <inheritdoc/>
	public bool SupportsPullDiagnostics { get; set; }

	/// <inheritdoc/>
	public event EventHandler<DiagnosticsPublishedEventArgs>? DiagnosticsPublished;

	/// <inheritdoc/>
	public event EventHandler? SemanticTokensRefreshRequested
	{
		add { }
		remove { }
	}

	/// <inheritdoc/>
	public event EventHandler? DiagnosticRefreshRequested
	{
		add => _diagnosticRefreshRequested += value;
		remove => _diagnosticRefreshRequested -= value;
	}

	private EventHandler? _diagnosticRefreshRequested;

	/// <summary>
	/// Raises <see cref="DiagnosticRefreshRequested"/>, so a provider test can drive the pull-diagnostics refresh
	/// fan-out the way a pull-only server would.
	/// </summary>
	public void RaiseDiagnosticRefreshRequested()
		=> _diagnosticRefreshRequested?.Invoke(this, EventArgs.Empty);

	/// <inheritdoc/>
	public event EventHandler<TransportUnavailableEventArgs>? TransportUnavailable
	{
		add => _transportUnavailable += value;
		remove => _transportUnavailable -= value;
	}

	private EventHandler<TransportUnavailableEventArgs>? _transportUnavailable;

	/// <summary>
	/// Gets a value indicating whether <see cref="Dispose"/> or <see cref="DisposeAsync"/> ran.
	/// </summary>
	public bool IsDisposed => _isDisposed;

	/// <summary>
	/// Gets or sets a value indicating whether didClose notifications throw <see cref="ObjectDisposedException"/>,
	/// simulating a disposal race on the best-effort close paths.
	/// </summary>
	public bool ThrowObjectDisposedOnDidClose { get; set; }

	/// <summary>
	/// Raises <see cref="TransportUnavailable"/> for one generation, applying the capability snapshot the real
	/// client reports before raising the event when the generation matches the active one.
	/// </summary>
	/// <param name="transportGeneration">The generation reported as unavailable.</param>
	public void RaiseTransportUnavailable(long transportGeneration)
	{
		if (transportGeneration == TransportGeneration)
			ApplyTransportLossSnapshot();

		_transportUnavailable?.Invoke(this, new TransportUnavailableEventArgs(transportGeneration));
	}

	/// <inheritdoc/>
	public async Task<bool> StartAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		Interlocked.Increment(ref _startCallCount);

		// Mirror the real client's fast path: a ready session returns immediately with the current generation.
		// While a handler is set, the attempt always runs it and activates a fresh generation when it succeeds,
		// so a test can model an attempt that replaces the session even though the client reports ready.
		if (IsReady && StartAsyncHandler is null)
			return true;

		bool started = StartAsyncHandler is null
			? StartResult
			: await StartAsyncHandler(cancellationToken).ConfigureAwait(false);

		if (!started)
			return false;

		TransportGeneration++;
		IsReady = true;
		return true;
	}

	/// <inheritdoc/>
	public bool TryMarkTransportUnhealthy(long transportGeneration)
	{
		lock (_sentSyncRoot)
			_markedUnhealthyGenerations.Add(transportGeneration);

		if (!TryMarkTransportUnhealthyResult)
			return false;

		if (transportGeneration != TransportGeneration)
			return false;

		bool wasReady = IsReady;

		if (wasReady)
			ApplyTransportLossSnapshot();

		// The real client raises the transport-unavailable event for a generation that was ready before it was
		// invalidated, so provider-level tests observe the same recovery path.
		if (wasReady)
			_transportUnavailable?.Invoke(this, new TransportUnavailableEventArgs(transportGeneration));

		return true;
	}

	/// <summary>
	/// Applies the capability snapshot the real client reports after its active transport is lost: no ready
	/// session and no negotiated capabilities. The synchronization mode is deliberately kept so tests can drive
	/// post-loss document flows; see <see cref="TextDocumentSyncKind"/>.
	/// </summary>
	private void ApplyTransportLossSnapshot()
	{
		IsReady = false;
		SupportsCompletionResolve = false;
		SupportsDocumentSymbols = false;
		SupportsCodeActions = false;
		SupportsCodeActionResolve = false;
		SupportsReferences = false;
		SupportsRename = false;
		SupportsFormatting = false;
		SupportsHover = false;
		SupportsDefinition = false;
		SupportsSignatureHelp = false;
		SupportsSemanticTokensFull = false;
		SupportsPullDiagnostics = false;
	}

	/// <inheritdoc/>
	public async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (ThrowObjectDisposedOnDidClose && string.Equals(method, "textDocument/didClose", StringComparison.Ordinal))
		{
			Interlocked.Increment(ref _simulatedDidCloseDisposalRaceCount);
			throw new ObjectDisposedException(nameof(FakeLanguageServerClient), "Simulated disposal race on a didClose notification.");
		}

		if (SendNotificationHandler is not null)
		{
			lock (_sentSyncRoot)
				_attemptedNotificationMethodNames.Add(method);

			await SendNotificationHandler(method, parameters, cancellationToken).ConfigureAwait(false);
		}

		JsonElement serializedParameters = parameters is null
			? JsonSerializer.SerializeToElement<object?>(null)
			: JsonSerializer.SerializeToElement(parameters);

		lock (_sentSyncRoot)
		{
			_sentNotifications.Add((method, serializedParameters));
			_sentMethodNames.Add(method);
		}
	}

	/// <inheritdoc/>
	public async Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		JsonElement serializedParameters = JsonSerializer.SerializeToElement(parameters);

		lock (_sentSyncRoot)
		{
			_sentRequests.Add((method, serializedParameters));
			_sentMethodNames.Add(method);
		}

		Interlocked.Increment(ref _sendRequestCount);

		long transportGeneration = TransportGeneration;

		lock (_sentSyncRoot)
			_sendRequestGenerations.Add(transportGeneration);

		if (SendRequestHandler is null)
		{
			// Returning the default response makes the framework surface the request's documented fallback value.
			return default!;
		}

		object? result = await SendRequestHandler(method, parameters, cancellationToken).ConfigureAwait(false);

		if (result is TResult typedResult)
			return typedResult;

		if (result is null)
			return default!;

		throw new InvalidOperationException(
			$"The configured send handler returned '{result.GetType()}', which is not assignable to '{typeof(TResult)}'.");
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Interlocked.Increment(ref _disposeCallCount);

		if (DisposeGate is not null)
			DisposeGate.GetAwaiter().GetResult();

		_isDisposed = true;
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		Interlocked.Increment(ref _disposeCallCount);

		if (DisposeGate is not null)
			await DisposeGate.ConfigureAwait(false);

		_isDisposed = true;
	}

	/// <summary>
	/// Raises <see cref="DiagnosticsPublished"/> with the supplied payload.
	/// </summary>
	/// <param name="parameters">The published diagnostics payload.</param>
	public void PublishDiagnostics(PublishDiagnosticsParams parameters)
		=> DiagnosticsPublished?.Invoke(this, new DiagnosticsPublishedEventArgs(parameters));

	/// <summary>
	/// Gets the number of sent notifications and requests for a method.
	/// </summary>
	/// <param name="method">The LSP method name.</param>
	/// <returns>The number of recorded sends for the method.</returns>
	public int GetSentMethodCount(string method)
	{
		lock (_sentSyncRoot)
			return _sentMethodNames.Count(sentMethod => string.Equals(sentMethod, method, StringComparison.Ordinal));
	}

	/// <summary>
	/// Gets a snapshot of the recorded sent method names in send order.
	/// </summary>
	/// <returns>The sent method names in send order.</returns>
	public string[] GetSentMethodNames()
	{
		lock (_sentSyncRoot)
			return [.. _sentMethodNames];
	}

	/// <summary>
	/// Gets the parameters of the most recently sent notification for a method.
	/// </summary>
	/// <param name="method">The LSP method name.</param>
	/// <returns>The serialized parameters of the last recorded notification.</returns>
	/// <exception cref="InvalidOperationException">No notification for the method was recorded.</exception>
	public JsonElement GetLastNotificationParameters(string method)
	{
		lock (_sentSyncRoot)
		{
			for (int i = _sentNotifications.Count - 1; i >= 0; i--)
			{
				if (string.Equals(_sentNotifications[i].Method, method, StringComparison.Ordinal))
					return _sentNotifications[i].Parameters;
			}
		}

		throw new InvalidOperationException($"No notification for method '{method}' was sent.");
	}

	/// <summary>
	/// Gets the parameters of the most recently sent request for a method.
	/// </summary>
	/// <param name="method">The LSP method name.</param>
	/// <returns>The serialized parameters of the last recorded request.</returns>
	/// <exception cref="InvalidOperationException">No request for the method was recorded.</exception>
	public JsonElement GetLastRequestParameters(string method)
	{
		lock (_sentSyncRoot)
		{
			for (int i = _sentRequests.Count - 1; i >= 0; i--)
			{
				if (string.Equals(_sentRequests[i].Method, method, StringComparison.Ordinal))
					return _sentRequests[i].Parameters;
			}
		}

		throw new InvalidOperationException($"No request for method '{method}' was sent.");
	}

	/// <summary>
	/// Gets a snapshot of the notification methods that reached the transport hook, including notifications that
	/// were abandoned or failed before they were recorded as sent.
	/// </summary>
	/// <returns>The attempted notification method names in send order.</returns>
	public string[] GetAttemptedNotificationMethodNames()
	{
		lock (_sentSyncRoot)
			return [.. _attemptedNotificationMethodNames];
	}

	/// <summary>
	/// Waits until at least <paramref name="expectedCount"/> sends for a method were recorded.
	/// </summary>
	/// <param name="method">The LSP method name.</param>
	/// <param name="expectedCount">The expected minimum send count.</param>
	/// <param name="timeout">The maximum wait time.</param>
	/// <returns><see langword="true"/> when the expected count was reached; otherwise, <see langword="false"/>.</returns>
	public Task<bool> WaitForMethodCountAsync(string method, int expectedCount, TimeSpan timeout)
		=> TestPolling.ForConditionAsync(() => GetSentMethodCount(method) >= expectedCount, timeout);
}
