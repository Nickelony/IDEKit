using System.Text.Json;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// In-memory <see cref="ILanguageServerClient"/> double for the Roslyn provider tests: capability flags and the
/// semantic-token legend are settable, transport sends are recorded per method, and requests return the response
/// the test's send handler builds.
/// </summary>
/// <remarks>
/// The double completes sends and raises the refresh events synchronously, so a provider test observes a trigger
/// and its effects in one call instead of racing a delivery pump; the real client's thread-pooled delivery is
/// covered by the Client suite. The Lua and Provider suites carry their own doubles with deliberately different
/// dialects; all three share the real client's contract as their reference, not each other.
/// </remarks>
internal sealed class FakeLanguageServerClient : ILanguageServerClient
{
	private readonly object _sentSyncRoot = new();
	private readonly List<(string Method, JsonElement Parameters)> _sentNotifications = [];
	private readonly List<(string Method, JsonElement Parameters)> _sentRequests = [];
	private readonly List<string> _sentMethodNames = [];
	private EventHandler? _diagnosticRefreshRequested;
	private bool _isDisposed;

	/// <inheritdoc/>
	public bool IsReady { get; set; } = true;

	/// <inheritdoc/>
	public Exception? LastStartupException => null;

	/// <inheritdoc/>
	public long TransportGeneration { get; set; } = 1;

	/// <inheritdoc/>
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

	/// <summary>
	/// Gets or sets the handler that produces one request result, or <see langword="null"/> to return the default
	/// response so the framework's documented fallback values surface.
	/// </summary>
	public Func<string, object, CancellationToken, Task<object?>>? SendRequestHandler { get; set; }

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

	/// <inheritdoc/>
	public event EventHandler<TransportUnavailableEventArgs>? TransportUnavailable
	{
		add { }
		remove { }
	}

	/// <summary>
	/// Raises <see cref="DiagnosticRefreshRequested"/>, so a provider test can drive the pull-diagnostics refresh
	/// fan-out the way the Roslyn language server does through <c>workspace/diagnostic/refresh</c>.
	/// </summary>
	public void RaiseDiagnosticRefreshRequested()
		=> _diagnosticRefreshRequested?.Invoke(this, EventArgs.Empty);

	/// <summary>
	/// Raises <see cref="DiagnosticsPublished"/> with the supplied payload, so a test can exercise the push path.
	/// </summary>
	/// <param name="parameters">The published diagnostics payload.</param>
	public void PublishDiagnostics(PublishDiagnosticsParams parameters)
		=> DiagnosticsPublished?.Invoke(this, new DiagnosticsPublishedEventArgs(parameters));

	/// <inheritdoc/>
	public Task<bool> StartAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		return Task.FromResult(IsReady);
	}

	/// <inheritdoc/>
	public bool TryMarkTransportUnhealthy(long transportGeneration) => false;

	/// <inheritdoc/>
	public Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		JsonElement serializedParameters = parameters is null
			? JsonSerializer.SerializeToElement<object?>(null)
			: JsonSerializer.SerializeToElement(parameters);

		lock (_sentSyncRoot)
		{
			_sentNotifications.Add((method, serializedParameters));
			_sentMethodNames.Add(method);
		}

		return Task.CompletedTask;
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

		if (SendRequestHandler is null)
			return default!;

		object? result = await SendRequestHandler(method, parameters, cancellationToken).ConfigureAwait(false);

		if (result is TResult typedResult)
			return typedResult;

		if (result is null)
			return default!;

		throw new InvalidOperationException(
			$"The configured send handler returned '{result.GetType()}', which is not assignable to '{typeof(TResult)}'.");
	}

	/// <inheritdoc/>
	public void Dispose() => _isDisposed = true;

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		_isDisposed = true;
		return ValueTask.CompletedTask;
	}

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
	/// Waits until at least <paramref name="expectedCount"/> sends for a method were recorded.
	/// </summary>
	/// <param name="method">The LSP method name.</param>
	/// <param name="expectedCount">The expected minimum send count.</param>
	/// <param name="timeout">The maximum wait time.</param>
	/// <returns><see langword="true"/> when the expected count was reached; otherwise, <see langword="false"/>.</returns>
	public Task<bool> WaitForMethodCountAsync(string method, int expectedCount, TimeSpan timeout)
		=> TestPolling.ForConditionAsync(() => GetSentMethodCount(method) >= expectedCount, timeout);
}
