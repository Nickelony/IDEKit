using System.Diagnostics;
using System.Text.Json;

namespace Nickelony.LanguageServer.Testing;

/// <summary>
/// In-memory <see cref="ILanguageServerClient"/> double shared by the Roslyn-backed language package tests (C# and
/// Visual Basic): the capabilities the provider reads are settable, and transport sends are recorded per method so a
/// test can inspect the payloads the provider emits.
/// </summary>
/// <remarks>
/// The double completes sends synchronously, so a provider test observes a trigger and its effects in one call
/// instead of racing a delivery pump; the real client's thread-pooled delivery is covered by the Client suite. The
/// other language-server suites carry their own doubles with deliberately different dialects; all of them share the
/// real client's contract as their reference, not each other.
/// </remarks>
internal sealed class FakeLanguageServerClient : ILanguageServerClient
{
	private readonly object _sentSyncRoot = new();
	private readonly List<(string Method, JsonElement Parameters)> _sentNotifications = [];
	private readonly List<(string Method, JsonElement Parameters)> _sentRequests = [];
	private readonly List<string> _sentMethodNames = [];
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

	// The events are declared with no-op accessors because this double covers the send path only: a provider test
	// in these suites drives document synchronization and identity, while the push and refresh deliveries are covered
	// by the Roslyn and Provider suites.

	/// <inheritdoc/>
	public event EventHandler<DiagnosticsPublishedEventArgs>? DiagnosticsPublished
	{
		add { }
		remove { }
	}

	/// <inheritdoc/>
	public event EventHandler? SemanticTokensRefreshRequested
	{
		add { }
		remove { }
	}

	/// <inheritdoc/>
	public event EventHandler? DiagnosticRefreshRequested
	{
		add { }
		remove { }
	}

	/// <inheritdoc/>
	public event EventHandler<TransportUnavailableEventArgs>? TransportUnavailable
	{
		add { }
		remove { }
	}

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
	public Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		JsonElement serializedParameters = JsonSerializer.SerializeToElement(parameters);

		lock (_sentSyncRoot)
		{
			_sentRequests.Add((method, serializedParameters));
			_sentMethodNames.Add(method);
		}

		// No request is expected in these suites; the documented fallback value keeps the contract intact if one is.
		return Task.FromResult(default(TResult)!);
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
	public async Task<bool> WaitForMethodCountAsync(string method, int expectedCount, TimeSpan timeout)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();

		while (GetSentMethodCount(method) < expectedCount)
		{
			if (stopwatch.Elapsed >= timeout)
				return false;

			await Task.Delay(TestPolling.DefaultPollInterval).ConfigureAwait(false);
		}

		return true;
	}
}
