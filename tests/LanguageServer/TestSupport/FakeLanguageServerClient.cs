using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Testing;

/// <summary>
/// An <see cref="ILanguageServerClient"/> double whose readiness, connection generation and capabilities are plain settable values,
/// with handler hooks for requests, notifications, startup and disposal, recorded sends and default fallback responses.
/// </summary>
/// <remarks>
/// <para>
/// It stays apart from <see cref="FakeStatefulLanguageServerClient"/>, which models the session contract of the real client and replays
/// canned per-method responses. Here nothing couples the values: a send needs only an undisposed client, and a start, a loss or a request result
/// happens when a test sets a value or supplies a handler. A test can therefore build states that the real session never reaches, such as a ready
/// client with connection generation zero, or sends on a client that is not ready.
/// </para>
/// <para>
/// A test that scripts requests through <see cref="SendRequestHandler"/> and reads the recorded sends, as the Roslyn tests do, uses this double.
/// </para>
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

	/// <summary>Whether the client reports a ready session.</summary>
	/// <remarks>
	/// A ready session is a started session, so a test that constructs the client ready must also set a nonzero <see cref="ConnectionGeneration"/>.
	/// </remarks>
	public bool IsReady { get; set; } = true;

	/// <summary>
	/// The reported connection generation. Tests set it directly to simulate a connection replacement without a full
	/// <see cref="EnsureStartedAsync"/> cycle.
	/// </summary>
	public long ConnectionGeneration { get; set; }

	/// <summary>
	/// The handler that produces one request result, or <see langword="null"/> to return the default response so the framework's documented fallback
	/// values surface.
	/// </summary>
	public Func<string, object, CancellationToken, Task<object?>>? SendRequestHandler { get; set; }

	/// <summary>
	/// The handler that runs for one notification before it is recorded, or <see langword="null"/> to record notifications directly.
	/// Set a handler to inject connection failures (throw <see cref="IOException"/>/<see cref="OperationCanceledException"/>) or to gate a send
	/// on test coordination.
	/// </summary>
	public Func<string, object?, CancellationToken, Task>? SendNotificationHandler { get; set; }

	/// <summary>The settings document <see cref="PushSettingsAsync"/> sends, standing in for the real client's settings provider.</summary>
	public object PushedSettings { get; set; } = new { };

	/// <summary>
	/// When <see langword="false"/>, <see cref="TryMarkConnectionUnhealthy"/> returns <see langword="false"/> without detaching.
	/// Defaults to <see langword="true"/>, which applies the real generation-matching semantics.
	/// </summary>
	public bool TryMarkConnectionUnhealthyResult { get; set; } = true;

	/// <summary>
	/// When <see langword="true"/>, <see cref="TryMarkConnectionUnhealthy"/> throws an <see cref="InvalidOperationException"/> after it records
	/// the generation.
	/// </summary>
	public bool ThrowOnTryMarkConnectionUnhealthy { get; set; }

	public int SendRequestCount => Volatile.Read(ref _sendRequestCount);

	/// <summary>
	/// Returns the number of <c>textDocument/didClose</c> notifications that hit the simulated disposal race
	/// (<see cref="ThrowObjectDisposedOnDidClose"/>).
	/// </summary>
	public int SimulatedDidCloseDisposalRaceCount => Volatile.Read(ref _simulatedDidCloseDisposalRaceCount);

	/// <summary>A stable snapshot of the connection generations observed at each request send, in send order.</summary>
	public IReadOnlyList<long> SendRequestGenerations
	{
		get
		{
			lock (_sentSyncRoot)
				return [.. _sendRequestGenerations];
		}
	}

	/// <summary>A stable snapshot of the generations passed to <see cref="TryMarkConnectionUnhealthy"/>, in call order.</summary>
	public IReadOnlyList<long> MarkedUnhealthyGenerations
	{
		get
		{
			lock (_sentSyncRoot)
				return [.. _markedUnhealthyGenerations];
		}
	}

	/// <summary>Whether <see cref="EnsureStartedAsync"/> succeeds.</summary>
	/// <remarks>
	/// Defaults to <see langword="true"/>. Ignored when <see cref="EnsureStartedAsyncHandler"/> is set, and while <see cref="IsReady"/>
	/// is <see langword="true"/> because a ready client returns before it reads this value.
	/// </remarks>
	public bool StartResult { get; set; } = true;

	/// <summary>
	/// A handler that replaces the default <see cref="EnsureStartedAsync"/> behavior for tests that gate, fault, or fail a startup attempt.
	/// Every start runs the handler and activates a fresh connection generation on success, even when the client currently reports a ready session.
	/// </summary>
	public Func<CancellationToken, Task<bool>>? EnsureStartedAsyncHandler { get; set; }

	public int StartCallCount => Volatile.Read(ref _startCallCount);

	/// <inheritdoc/>
	public Exception? LastStartupException { get; set; }

	public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

	/// <summary>
	/// An optional task both <see cref="Dispose"/> and <see cref="DisposeAsync"/> wait on (blocking for the synchronous overload)
	/// before reporting the client disposed. A test holds this to keep a provider teardown in progress while it asserts that a later dispose caller
	/// waits for it.
	/// </summary>
	public Task? DisposeGate { get; set; }

	/// <summary>An optional callback that runs when a dispose call starts, before the client reports itself disposed.</summary>
	public Action? DisposeObserver { get; set; }

	/// <summary>The synchronization mode <see cref="NegotiatedCapabilities"/> reports.</summary>
	/// <remarks>
	/// Kept across a simulated connection loss, unlike the real client.
	/// </remarks>
	public TextDocumentSyncKind TextDocumentSyncKind { get; set; } = TextDocumentSyncKind.Incremental;

	public IReadOnlyList<string> SemanticTokenTypes { get; set; } = [];

	public IReadOnlyList<string> SemanticTokenModifiers { get; set; } = [];

	public bool SupportsCompletionResolve { get; set; }

	public bool SupportsDocumentSymbols { get; set; }

	public bool SupportsCodeActions { get; set; }

	public bool SupportsCodeActionResolve { get; set; }

	public bool SupportsReferences { get; set; }

	public bool SupportsRename { get; set; }

	public bool SupportsFormatting { get; set; }

	public bool SupportsHover { get; set; }

	public bool SupportsDefinition { get; set; }

	public bool SupportsSignatureHelp { get; set; }

	public bool SupportsSemanticTokensFull { get; set; }

	public bool SupportsPullDiagnostics { get; set; }

	/// <inheritdoc/>
	public NegotiatedServerCapabilities NegotiatedCapabilities =>
		new()
		{
			TextDocumentSyncKind = TextDocumentSyncKind,
			SemanticTokenTypes = SemanticTokenTypes,
			SemanticTokenModifiers = SemanticTokenModifiers,
			SupportsCompletionResolve = SupportsCompletionResolve,
			SupportsDocumentSymbols = SupportsDocumentSymbols,
			SupportsCodeActions = SupportsCodeActions,
			SupportsCodeActionResolve = SupportsCodeActionResolve,
			SupportsReferences = SupportsReferences,
			SupportsRename = SupportsRename,
			SupportsFormatting = SupportsFormatting,
			SupportsHover = SupportsHover,
			SupportsDefinition = SupportsDefinition,
			SupportsSignatureHelp = SupportsSignatureHelp,
			SupportsSemanticTokensFull = SupportsSemanticTokensFull,
			SupportsPullDiagnostics = SupportsPullDiagnostics,
		};

	/// <inheritdoc/>
	public event EventHandler<DiagnosticsPublishedEventArgs>? DiagnosticsPublished;

	/// <inheritdoc/>
	public event EventHandler? SemanticTokensRefreshRequested
	{
		add => _semanticTokensRefreshRequested += value;
		remove => _semanticTokensRefreshRequested -= value;
	}

	private EventHandler? _semanticTokensRefreshRequested;

	/// <inheritdoc/>
	public event EventHandler? DiagnosticRefreshRequested
	{
		add => _diagnosticRefreshRequested += value;
		remove => _diagnosticRefreshRequested -= value;
	}

	private EventHandler? _diagnosticRefreshRequested;

	/// <summary>
	/// Raises <see cref="DiagnosticRefreshRequested"/>, so a provider test can drive the pull-diagnostics refresh fan-out the way a pull-only
	/// server would.
	/// </summary>
	public void RaiseDiagnosticRefreshRequested() => _diagnosticRefreshRequested?.Invoke(this, EventArgs.Empty);

	/// <inheritdoc/>
	public event EventHandler<ConnectionUnavailableEventArgs>? ConnectionUnavailable
	{
		add => _connectionUnavailable += value;
		remove => _connectionUnavailable -= value;
	}

	private EventHandler<ConnectionUnavailableEventArgs>? _connectionUnavailable;

	/// <summary>The number of handlers subscribed to <see cref="DiagnosticsPublished"/>.</summary>
	public int DiagnosticsPublishedSubscriberCount => DiagnosticsPublished?.GetInvocationList().Length ?? 0;

	/// <summary>The number of handlers subscribed to <see cref="SemanticTokensRefreshRequested"/>.</summary>
	public int SemanticTokensRefreshRequestedSubscriberCount => _semanticTokensRefreshRequested?.GetInvocationList().Length ?? 0;

	/// <summary>The number of handlers subscribed to <see cref="DiagnosticRefreshRequested"/>.</summary>
	public int DiagnosticRefreshRequestedSubscriberCount => _diagnosticRefreshRequested?.GetInvocationList().Length ?? 0;

	/// <summary>The number of handlers subscribed to <see cref="ConnectionUnavailable"/>.</summary>
	public int ConnectionUnavailableSubscriberCount => _connectionUnavailable?.GetInvocationList().Length ?? 0;

	public bool IsDisposed { get; private set; }

	/// <summary>
	/// Whether didClose notifications throw <see cref="ObjectDisposedException"/>, simulating a disposal race on the best-effort close paths.
	/// </summary>
	public bool ThrowObjectDisposedOnDidClose { get; set; }

	/// <summary>
	/// Raises <see cref="ConnectionUnavailable"/> for one generation, applying the capability snapshot the real client reports before raising
	/// the event when the generation matches the active one.
	/// </summary>
	/// <param name="connectionGeneration">The generation reported as unavailable.</param>
	public void RaiseConnectionUnavailable(long connectionGeneration)
	{
		if (connectionGeneration == ConnectionGeneration)
			ApplyConnectionLossSnapshot();

		_connectionUnavailable?.Invoke(this, new ConnectionUnavailableEventArgs(connectionGeneration));
	}

	/// <inheritdoc/>
	public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ObjectDisposedException.ThrowIf(IsDisposed, this);

		Interlocked.Increment(ref _startCallCount);

		// Mirror the real client's fast path: a ready session returns immediately with the current generation.
		// While a handler is set, the attempt always runs it and activates a fresh generation when it succeeds,
		// so a test can model an attempt that replaces the session even though the client reports ready.
		if (IsReady && EnsureStartedAsyncHandler is null)
			return true;

		bool started = EnsureStartedAsyncHandler is null ? StartResult : await EnsureStartedAsyncHandler(cancellationToken).ConfigureAwait(false);

		if (!started)
			return false;

		ConnectionGeneration++;
		IsReady = true;
		return true;
	}

	/// <inheritdoc/>
	public bool TryMarkConnectionUnhealthy(long connectionGeneration)
	{
		lock (_sentSyncRoot)
			_markedUnhealthyGenerations.Add(connectionGeneration);

		if (ThrowOnTryMarkConnectionUnhealthy)
			throw new InvalidOperationException("Simulated failure while marking the connection unhealthy.");

		if (!TryMarkConnectionUnhealthyResult)
			return false;

		if (connectionGeneration != ConnectionGeneration)
			return false;

		bool wasReady = IsReady;

		if (wasReady)
			ApplyConnectionLossSnapshot();

		// The real client raises the connection-unavailable event for a generation that was ready before it was
		// invalidated, so provider-level tests observe the same recovery path.
		if (wasReady)
			_connectionUnavailable?.Invoke(this, new ConnectionUnavailableEventArgs(connectionGeneration));

		return true;
	}

	/// <summary>
	/// Applies the capability snapshot the real client reports after its active connection is lost: no ready session and no negotiated capabilities.
	/// The synchronization mode is deliberately kept so tests can drive post-loss document flows; see <see cref="TextDocumentSyncKind"/>.
	/// </summary>
	private void ApplyConnectionLossSnapshot()
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
		ObjectDisposedException.ThrowIf(IsDisposed, this);

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
	/// <remarks>
	/// Sends a <c>workspace/didChangeConfiguration</c> notification that carries <see cref="PushedSettings"/>,
	/// through the same path as <see cref="SendNotificationAsync"/>, so a handler and a recorded send observe it like any other notification.
	/// </remarks>
	public Task PushSettingsAsync(CancellationToken cancellationToken) =>
		SendNotificationAsync(LspMethodNames.DidChangeConfiguration, new DidChangeConfigurationParams(PushedSettings), cancellationToken);

	/// <inheritdoc/>
	public async Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ObjectDisposedException.ThrowIf(IsDisposed, this);

		JsonElement serializedParameters = JsonSerializer.SerializeToElement(parameters);

		lock (_sentSyncRoot)
		{
			_sentRequests.Add((method, serializedParameters));
			_sentMethodNames.Add(method);
		}

		Interlocked.Increment(ref _sendRequestCount);

		long connectionGeneration = ConnectionGeneration;

		lock (_sentSyncRoot)
			_sendRequestGenerations.Add(connectionGeneration);

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
			$"The configured send handler returned '{result.GetType()}', which is not assignable to '{typeof(TResult)}'."
		);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Interlocked.Increment(ref _disposeCallCount);
		DisposeObserver?.Invoke();

		DisposeGate?.GetAwaiter().GetResult();

		IsDisposed = true;
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		Interlocked.Increment(ref _disposeCallCount);
		DisposeObserver?.Invoke();

		if (DisposeGate is not null)
			await DisposeGate.ConfigureAwait(false);

		IsDisposed = true;
	}

	/// <summary>Raises <see cref="DiagnosticsPublished"/> with the supplied payload.</summary>
	/// <param name="parameters">The published diagnostics payload.</param>
	public void PublishDiagnostics(PublishDiagnosticsParams parameters) =>
		DiagnosticsPublished?.Invoke(this, new DiagnosticsPublishedEventArgs(parameters));

	/// <summary>Returns the number of sent notifications and requests for a method.</summary>
	/// <param name="method">The LSP method name.</param>
	/// <returns>The number of recorded sends for the method.</returns>
	public int GetSentMethodCount(string method)
	{
		lock (_sentSyncRoot)
			return _sentMethodNames.Count(sentMethod => string.Equals(sentMethod, method, StringComparison.Ordinal));
	}

	/// <summary>Returns the number of sent notifications of a method whose <c>textDocument.uri</c> names one document.</summary>
	/// <param name="method">The LSP method name.</param>
	/// <param name="documentUri">The URI of the document.</param>
	/// <returns>The number of recorded notifications of the method for the document.</returns>
	public int GetSentDocumentMethodCount(string method, string documentUri)
	{
		lock (_sentSyncRoot)
		{
			return _sentNotifications.Count(notification =>
				string.Equals(notification.Method, method, StringComparison.Ordinal)
				&& notification.Parameters.ValueKind == JsonValueKind.Object
				&& notification.Parameters.TryGetProperty("textDocument", out JsonElement textDocument)
				&& textDocument.TryGetProperty("uri", out JsonElement uri)
				&& string.Equals(uri.GetString(), documentUri, StringComparison.Ordinal)
			);
		}
	}

	/// <summary>A snapshot of the recorded sent method names, in send order.</summary>
	/// <returns>The sent method names in send order.</returns>
	public string[] GetSentMethodNames()
	{
		lock (_sentSyncRoot)
			return [.. _sentMethodNames];
	}

	/// <summary>Returns the parameters of the most recently sent notification for a method.</summary>
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

	/// <summary>Returns the parameters of the most recently sent request for a method.</summary>
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

	/// <summary>Returns a snapshot of the notification methods that reached <see cref="SendNotificationHandler"/>.</summary>
	/// <returns>The attempted notification method names in send order.</returns>
	/// <remarks>
	/// The snapshot includes notifications that were abandoned or failed before they were recorded as sent. It stays empty while no handler is set.
	/// </remarks>
	public string[] GetAttemptedNotificationMethodNames()
	{
		lock (_sentSyncRoot)
			return [.. _attemptedNotificationMethodNames];
	}

	/// <summary>Waits until at least <paramref name="expectedCount"/> sends for a method were recorded.</summary>
	/// <param name="method">The LSP method name.</param>
	/// <param name="expectedCount">The expected minimum send count.</param>
	/// <param name="timeout">The maximum wait time.</param>
	/// <returns><see langword="true"/> when the expected count was reached; otherwise, <see langword="false"/>.</returns>
	public Task<bool> WaitForMethodCountAsync(string method, int expectedCount, TimeSpan timeout) =>
		TestPolling.ForConditionAsync(() => GetSentMethodCount(method) >= expectedCount, timeout);

	/// <summary>Waits until at least <paramref name="expectedCount"/> notifications of a method were recorded for one document.</summary>
	/// <param name="method">The LSP method name.</param>
	/// <param name="documentUri">The URI of the document.</param>
	/// <param name="expectedCount">The expected minimum send count.</param>
	/// <param name="timeout">The maximum wait time.</param>
	/// <returns><see langword="true"/> when the expected count was reached; otherwise, <see langword="false"/>.</returns>
	public Task<bool> WaitForDocumentMethodCountAsync(string method, string documentUri, int expectedCount, TimeSpan timeout) =>
		TestPolling.ForConditionAsync(() => GetSentDocumentMethodCount(method, documentUri) >= expectedCount, timeout);
}
