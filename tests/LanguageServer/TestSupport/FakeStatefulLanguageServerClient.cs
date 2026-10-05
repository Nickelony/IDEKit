using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Testing;

/// <summary>
/// In-memory <see cref="ILanguageServerClient"/> test double that models the connection session of the real client, replays configured responses,
/// and records the traffic the provider produces.
/// </summary>
/// <remarks>
/// The double completes sends and raises <see cref="DiagnosticsPublished"/>
/// and <see cref="SemanticTokensRefreshRequested"/> synchronously, so a provider test observes the trigger and its effects in one call
/// instead of racing a delivery loop. Capability reads fall back to their unnegotiated defaults until a start succeeds, and again after the active
/// connection becomes unhealthy, mirroring the real client contract. It stays apart from <see cref="FakeLanguageServerClient"/>, whose readiness,
/// generation and capabilities are plain settable values that tests combine with handler hooks: a start here issues the next connection generation
/// and publishes the capabilities, a send requires a ready connection, a simulated connection failure detaches the session, and responses are canned
/// per method.
/// </remarks>
internal sealed class FakeStatefulLanguageServerClient : ILanguageServerClient
{
	private static readonly JsonSerializerOptions s_responseDeserializationOptions = new()
	{
		// The real client deserializes with case-insensitive property matching, so the fake accepts
		// the same payload casing a live server produces.
		PropertyNameCaseInsensitive = true,
	};

	private readonly object _sentSyncRoot = new();

	private readonly List<(string Method, JsonElement Parameters)> _sentNotifications = [];
	private readonly List<(string Method, JsonElement Parameters)> _sentRequests = [];

	private readonly List<string> _sentMethodNames = [];
	private readonly Queue<JsonElement> _semanticTokensFullResponses = [];
	private readonly Queue<(TaskCompletionSource<bool> Gate, JsonElement? Response)> _availableSemanticTokensFullRequestGates = new();
	private readonly Queue<TaskCompletionSource<bool>> _pendingSemanticTokensFullRequestGates = new();

	private TaskCompletionSource<bool>? _hoverRequestGate;
	private TaskCompletionSource<bool>? _openNotificationGate;
	private TaskCompletionSource<bool>? _startGate;
	private TaskCompletionSource<bool>? _changeNotificationGate;
	private TaskCompletionSource<bool>? _watchedFilesNotificationGate;

	private long _lastIssuedConnectionGeneration;

	private bool _isDisposed;
	private bool _capabilitySnapshotPublished;

	private readonly TaskCompletionSource<bool> _changeNotificationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<bool> _closeNotificationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private bool _isReady = true;

	/// <summary>
	/// Whether the fake reports a ready connection session. Setting it to <see langword="false"/> clears the published capability snapshot,
	/// mirroring the real client contract where capabilities read their unnegotiated defaults once the active connection is gone.
	/// </summary>
	public bool IsReady
	{
		get => _isReady;
		set
		{
			_isReady = value;

			if (!value)
				_capabilitySnapshotPublished = false;
		}
	}

	public long ConnectionGeneration { get; private set; }
	public bool StartResult { get; set; } = true;

	/// <summary>
	/// The exception observed by the most recent startup attempt, or <see langword="null"/> when that attempt succeeded, returned a failed result,
	/// or has not run yet. The value is cleared when a new startup attempt begins, mirroring the real client contract.
	/// </summary>
	public Exception? LastStartupException { get; private set; }

	public JsonElement CodeActionResponse { get; set; }
	public JsonElement CodeActionResolveResponse { get; set; }

	/// <summary>
	/// Answers a <c>codeAction/resolve</c> request with the result of the handler, which receives the request parameters,
	/// instead of the fixed <see cref="CodeActionResolveResponse"/>; a handler can hold the answer back while the test changes a document.
	/// </summary>
	public Func<JsonElement, CancellationToken, Task<JsonElement>>? CodeActionResolveHandler { get; set; }
	public JsonElement CompletionResponse { get; set; }
	public JsonElement CompletionResolveResponse { get; set; }
	public JsonElement DefinitionResponse { get; set; }
	public JsonElement DocumentSymbolResponse { get; set; }
	public JsonElement WorkspaceSymbolResponse { get; set; }
	public JsonElement WorkspaceSymbolResolveResponse { get; set; }

	/// <summary>
	/// Answers a <c>workspace/symbol</c> request with the result of the handler, which receives the request parameters,
	/// instead of the fixed <see cref="WorkspaceSymbolResponse"/>; a handler can hold the answer back until the request token is canceled.
	/// </summary>
	public Func<JsonElement, CancellationToken, Task<JsonElement>>? WorkspaceSymbolHandler { get; set; }

	/// <summary>
	/// Answers a <c>workspaceSymbol/resolve</c> request with the result of the handler, which receives the request parameters,
	/// instead of the fixed <see cref="WorkspaceSymbolResolveResponse"/>; a handler can restart the connection while the server computes.
	/// </summary>
	public Func<JsonElement, CancellationToken, Task<JsonElement>>? WorkspaceSymbolResolveHandler { get; set; }

	public JsonElement FormattingResponse { get; set; }

	/// <summary>
	/// The hover response. Defaults to a JSON <c>null</c> payload, the "no hover content" answer that tests driving document synchronization
	/// through hover requests rely on; tests that assert hover content assign their own payload.
	/// </summary>
	public JsonElement HoverResponse { get; set; } = JsonSerializer.SerializeToElement<object?>(null);

	public JsonElement ReferenceResponse { get; set; }
	public JsonElement RenameResponse { get; set; }
	public JsonElement SignatureHelpResponse { get; set; }

	/// <summary>
	/// Answers a <c>workspace/executeCommand</c> request with the result of the handler, which receives the request parameters, or with JSON
	/// <c>null</c> when no handler is set. A handler can do what a server does while it runs a command, such as asking the host to apply an edit.
	/// </summary>
	public Func<JsonElement, CancellationToken, Task<JsonElement>>? ExecuteCommandHandler { get; set; }

	/// <summary>
	/// The capabilities the double publishes once a start succeeds.
	/// <see cref="NegotiatedCapabilities"/> reads <see cref="NegotiatedServerCapabilities.None"/> until then, and again after the active
	/// connection becomes unhealthy, like the real client.
	/// </summary>
	public TextDocumentSyncKind TextDocumentSyncKind { get; set; } = TextDocumentSyncKind.Incremental;

	public IReadOnlyList<string> SemanticTokenTypes { get; set; } = [];
	public IReadOnlyList<string> SemanticTokenModifiers { get; set; } = [];

	public bool SupportsCompletionResolve { get; set; }
	public bool SupportsDocumentSymbols { get; set; } = true;
	public bool SupportsWorkspaceSymbols { get; set; } = true;
	public bool SupportsWorkspaceSymbolResolve { get; set; }
	public bool SupportsCodeActions { get; set; } = true;
	public bool SupportsCodeActionResolve { get; set; }
	public IReadOnlyList<string> ExecuteCommands { get; set; } = [];
	public bool SupportsReferences { get; set; } = true;
	public bool SupportsRename { get; set; } = true;
	public bool SupportsFormatting { get; set; } = true;
	public bool SupportsHover { get; set; } = true;
	public bool SupportsDefinition { get; set; } = true;
	public bool SupportsSignatureHelp { get; set; } = true;
	public bool SupportsSemanticTokensFull { get; set; } = true;
	public bool SupportsPullDiagnostics { get; set; }
	public IReadOnlyList<string> CompletionTriggerCharacters { get; set; } = [];
	public IReadOnlyList<string> SignatureHelpTriggerCharacters { get; set; } = [];
	public IReadOnlyList<string> SignatureHelpRetriggerCharacters { get; set; } = [];

	/// <inheritdoc/>
	public NegotiatedServerCapabilities NegotiatedCapabilities =>
		_capabilitySnapshotPublished
			? new()
			{
				TextDocumentSyncKind = TextDocumentSyncKind,
				SemanticTokenTypes = SemanticTokenTypes,
				SemanticTokenModifiers = SemanticTokenModifiers,
				SupportsCompletionResolve = SupportsCompletionResolve,
				SupportsDocumentSymbols = SupportsDocumentSymbols,
				SupportsWorkspaceSymbols = SupportsWorkspaceSymbols,
				SupportsWorkspaceSymbolResolve = SupportsWorkspaceSymbolResolve,
				SupportsCodeActions = SupportsCodeActions,
				SupportsCodeActionResolve = SupportsCodeActionResolve,
				ExecuteCommands = ExecuteCommands,
				SupportsReferences = SupportsReferences,
				SupportsRename = SupportsRename,
				SupportsFormatting = SupportsFormatting,
				SupportsHover = SupportsHover,
				SupportsDefinition = SupportsDefinition,
				SupportsSignatureHelp = SupportsSignatureHelp,
				SupportsSemanticTokensFull = SupportsSemanticTokensFull,
				SupportsPullDiagnostics = SupportsPullDiagnostics,
				CompletionTriggerCharacters = CompletionTriggerCharacters,
				SignatureHelpTriggerCharacters = SignatureHelpTriggerCharacters,
				SignatureHelpRetriggerCharacters = SignatureHelpRetriggerCharacters,
			}
			: NegotiatedServerCapabilities.None;

	public bool FailStartWhenCancellationRequested { get; set; }
	public bool CancelNextHoverRequestWithoutTimeout { get; set; }
	public Action? BeforeReturningStartResult { get; set; }
	public Action? BeforeReturningHoverResponse { get; set; }
	public Action? BeforePublishingConnectionUnavailable { get; set; }
	public int StartCallCount { get; private set; }
	public int MarkConnectionUnhealthyCallCount { get; private set; }
	public int DisposeCallCount { get; private set; }
	public int TimedOutHoverRequestsRemaining { get; set; }
	public int ConnectionChangedRequestFailuresRemaining { get; set; }

	/// <summary>
	/// Whether a simulated connection change also detaches the connection. The default (<see langword="true"/>) models a send
	/// that crossed onto a connection that has failed, so the provider restarts; set it to <see langword="false"/> to model a send that crossed
	/// onto an already-ready newer connection, where the provider retries without restarting.
	/// </summary>
	public bool ConnectionChangedFailureDetachesConnection { get; set; } = true;

	public string? ThrowIOExceptionOnNextRequestMethod { get; set; }
	public string? ThrowInvalidOperationOnNextRequestMethod { get; set; }
	public bool ThrowIOExceptionOnNextDidChange { get; set; }
	public bool ThrowInvalidOperationOnNextWatchedFilesNotification { get; set; }
	public bool ThrowIOExceptionOnNextWatchedFilesNotification { get; set; }
	public bool ThrowIOExceptionAfterWatchedFilesNotificationGateRelease { get; set; }
	public List<bool> StartCancellationTokenCanBeCanceled { get; } = [];

	public event EventHandler<DiagnosticsPublishedEventArgs>? DiagnosticsPublished;

	public event EventHandler? SemanticTokensRefreshRequested;

	public event EventHandler? DiagnosticRefreshRequested;

	public event EventHandler<ConnectionUnavailableEventArgs>? ConnectionUnavailable;

	public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		StartCallCount++;
		StartCancellationTokenCanBeCanceled.Add(cancellationToken.CanBeCanceled);

		// Mirrors the real client contract: each attempt clears the previous diagnostic, and a throwing
		// attempt records the exception it observed; a plain failed result leaves it null.
		LastStartupException = null;

		try
		{
			TaskCompletionSource<bool>? startGate = _startGate;

			if (startGate is not null)
				await startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			if (FailStartWhenCancellationRequested && cancellationToken.IsCancellationRequested)
				throw new OperationCanceledException(cancellationToken);

			IsReady = StartResult;

			if (StartResult)
			{
				ConnectionGeneration = ++_lastIssuedConnectionGeneration;
				_capabilitySnapshotPublished = true;
			}

			BeforeReturningStartResult?.Invoke();

			return StartResult;
		}
		catch (Exception exception)
		{
			LastStartupException = exception;
			throw;
		}
	}

	private void MarkConnectionUnhealthy()
	{
		MarkConnectionUnhealthyCallCount++;
		DetachConnection();
	}

	/// <summary>
	/// Drops the current connection session without counting a <see cref="TryMarkConnectionUnhealthy"/> call. Setting <see cref="IsReady"/> clears
	/// the published capability snapshot; <paramref name="raiseUnavailable"/> selects whether the unavailability event is raised, mirroring a real
	/// send failure that detaches the session.
	/// </summary>
	private void DetachConnection(bool raiseUnavailable = true)
	{
		bool wasReady = IsReady;
		IsReady = false;

		if (raiseUnavailable && wasReady)
			ConnectionUnavailable?.Invoke(this, new ConnectionUnavailableEventArgs(ConnectionGeneration));
	}

	public bool TryMarkConnectionUnhealthy(long connectionGeneration)
	{
		if (connectionGeneration != ConnectionGeneration)
			return false;

		MarkConnectionUnhealthy();
		return true;
	}

	public Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		// Like the real client, a send requires a ready connection session.
		if (!IsReady)
			throw new IOException("The language server connection is not ready.");

		if (cancellationToken.IsCancellationRequested)
			return Task.FromCanceled(cancellationToken);

		lock (_sentSyncRoot)
		{
			_sentMethodNames.Add(method);
			_sentNotifications.Add((method, JsonSerializer.SerializeToElement(parameters)));
		}

		if (method == "textDocument/didChange")
		{
			_changeNotificationObserved.TrySetResult(true);

			if (ThrowIOExceptionOnNextDidChange)
			{
				ThrowIOExceptionOnNextDidChange = false;

				// A real write failure detaches the connection before surfacing, so the fake drops its session first.
				DetachConnection(raiseUnavailable: false);
				throw new IOException("Simulated didChange connection failure.");
			}

			TaskCompletionSource<bool>? changeNotificationGate = _changeNotificationGate;

			if (changeNotificationGate is not null)
				return changeNotificationGate.Task;
		}

		if (method == "workspace/didChangeWatchedFiles" && ThrowIOExceptionOnNextWatchedFilesNotification)
		{
			ThrowIOExceptionOnNextWatchedFilesNotification = false;

			// A real write failure detaches the connection before surfacing, so the fake drops its session first.
			DetachConnection(raiseUnavailable: false);
			throw new IOException("Simulated workspace watcher connection failure.");
		}

		if (method == "workspace/didChangeWatchedFiles" && ThrowInvalidOperationOnNextWatchedFilesNotification)
		{
			ThrowInvalidOperationOnNextWatchedFilesNotification = false;
			throw new InvalidOperationException("Simulated unexpected workspace watcher connection failure.");
		}

		if (method == "workspace/didChangeWatchedFiles" && _watchedFilesNotificationGate is not null)
			return WaitForWatchedFilesNotificationGateAsync();

		if (method == "textDocument/didClose")
			_closeNotificationObserved.TrySetResult(true);

		if (method == "textDocument/didOpen" && _openNotificationGate is not null)
			return _openNotificationGate.Task;

		return Task.CompletedTask;
	}

	// Sends a workspace/didChangeConfiguration notification with an empty settings document through SendNotificationAsync.
	public Task PushSettingsAsync(CancellationToken cancellationToken) =>
		SendNotificationAsync(LspMethodNames.DidChangeConfiguration, new DidChangeConfigurationParams(new { }), cancellationToken);

	public Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		// Like the real client, a send requires a ready connection session.
		if (!IsReady)
			throw new IOException("The language server connection is not ready.");

		RecordRequest(method, parameters);

		if (string.Equals(ThrowIOExceptionOnNextRequestMethod, method, StringComparison.Ordinal))
		{
			ThrowIOExceptionOnNextRequestMethod = null;

			// The real sender marks the observed generation unhealthy (which raises ConnectionUnavailable) before
			// it surfaces the dedicated connection exception, so the fake detaches through the counted path.
			MarkConnectionUnhealthy();

			throw new LanguageServerConnectionUnavailableException($"Simulated {method} connection failure.");
		}

		if (string.Equals(ThrowInvalidOperationOnNextRequestMethod, method, StringComparison.Ordinal))
		{
			ThrowInvalidOperationOnNextRequestMethod = null;
			throw new InvalidOperationException($"Simulated {method} request failure.");
		}

		if (ConnectionChangedRequestFailuresRemaining > 0)
		{
			ConnectionChangedRequestFailuresRemaining--;

			if (ConnectionChangedFailureDetachesConnection)
				DetachConnection();
			else
				ConnectionGeneration = ++_lastIssuedConnectionGeneration;

			throw new LanguageServerConnectionSupersededException();
		}

		if (method == "textDocument/hover")
		{
			if (_hoverRequestGate is not null)
				return WaitForHoverRequestGateAsync<TResult>(cancellationToken);

			if (CancelNextHoverRequestWithoutTimeout)
			{
				CancelNextHoverRequestWithoutTimeout = false;
				throw new OperationCanceledException("Simulated internal hover cancellation.");
			}

			if (TimedOutHoverRequestsRemaining > 0)
			{
				TimedOutHoverRequestsRemaining--;
				return WaitForCancellationAsync<TResult>(cancellationToken);
			}

			if (HoverResponse.ValueKind == JsonValueKind.Null)
			{
				BeforeReturningHoverResponse?.Invoke();
				return Task.FromResult(default(TResult)!);
			}

			if (HoverResponse.ValueKind != JsonValueKind.Undefined)
			{
				BeforeReturningHoverResponse?.Invoke();
				return DeserializeResponseAsync<TResult>(HoverResponse);
			}
		}

		if (method == "textDocument/completion" && CompletionResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(CompletionResponse);

		if (method == "completionItem/resolve" && CompletionResolveResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(CompletionResolveResponse);

		if (method == "textDocument/definition" && DefinitionResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(DefinitionResponse);

		if (method == "textDocument/documentSymbol" && DocumentSymbolResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(DocumentSymbolResponse);

		if (method == "workspace/symbol" && WorkspaceSymbolHandler is not null)
			return AnswerWithHandlerAsync<TResult>(WorkspaceSymbolHandler, parameters, cancellationToken);

		if (method == "workspace/symbol" && WorkspaceSymbolResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(WorkspaceSymbolResponse);

		if (method == "workspaceSymbol/resolve" && WorkspaceSymbolResolveHandler is not null)
			return AnswerWithHandlerAsync<TResult>(WorkspaceSymbolResolveHandler, parameters, cancellationToken);

		if (method == "workspaceSymbol/resolve" && WorkspaceSymbolResolveResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(WorkspaceSymbolResolveResponse);

		if (method == "textDocument/codeAction" && CodeActionResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(CodeActionResponse);

		if (method == "codeAction/resolve" && CodeActionResolveHandler is not null)
			return ResolveCodeActionAsync<TResult>(parameters, cancellationToken);

		if (method == "codeAction/resolve" && CodeActionResolveResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(CodeActionResolveResponse);

		if (method == "workspace/executeCommand")
			return ExecuteCommandAsync<TResult>(parameters, cancellationToken);

		if (method == "textDocument/references" && ReferenceResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(ReferenceResponse);

		if (method == "textDocument/rename" && RenameResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(RenameResponse);

		if (method == "textDocument/formatting" && FormattingResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(FormattingResponse);

		if (method == "textDocument/semanticTokens/full")
		{
			bool hasSemanticTokensFullRequestGate;

			lock (_sentSyncRoot)
				hasSemanticTokensFullRequestGate = _availableSemanticTokensFullRequestGates.Count > 0;

			if (hasSemanticTokensFullRequestGate)
				return WaitForSemanticTokensFullRequestGateAsync<TResult>();

			JsonElement? queuedResponse = null;

			lock (_sentSyncRoot)
			{
				if (_semanticTokensFullResponses.Count > 0)
					queuedResponse = _semanticTokensFullResponses.Dequeue();
			}

			if (queuedResponse is { } response)
				return DeserializeResponseAsync<TResult>(response);

			return DeserializeResponseAsync<TResult>(
				JsonSerializer.SerializeToElement(new { data = new[] { 0, 6, 5, 0, 0 }, resultId = "tokens-1" })
			);
		}

		if (method == "textDocument/signatureHelp" && SignatureHelpResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponseAsync<TResult>(SignatureHelpResponse);

		throw new InvalidOperationException(
			$"The fake language server client received an unconfigured request for '{method}'. Configure a response for it instead of relying on the silent default."
		);
	}

	public string[] GetSentMethodNames()
	{
		lock (_sentSyncRoot)
			return [.. _sentMethodNames];
	}

	public void ClearSentMessages()
	{
		lock (_sentSyncRoot)
		{
			_sentMethodNames.Clear();
			_sentNotifications.Clear();
			_sentRequests.Clear();
		}
	}

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

		throw new InvalidOperationException($"Notification '{method}' was not observed.");
	}

	public JsonElement[] GetNotificationParameters(string method)
	{
		lock (_sentSyncRoot)
		{
			var parameters = new List<JsonElement>();

			for (int i = 0; i < _sentNotifications.Count; i++)
			{
				if (string.Equals(_sentNotifications[i].Method, method, StringComparison.Ordinal))
					parameters.Add(_sentNotifications[i].Parameters);
			}

			return [.. parameters];
		}
	}

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

		throw new InvalidOperationException($"Request '{method}' was not observed.");
	}

	public void BlockNextOpenNotification() =>
		_openNotificationGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void BlockNextEnsureStartedAsync() => _startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void BlockNextHoverRequest() => _hoverRequestGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void ReleaseOpenNotification() => _openNotificationGate?.TrySetResult(true);

	public void ReleaseEnsureStartedAsync()
	{
		TaskCompletionSource<bool>? startGate = _startGate;
		_startGate = null;

		startGate?.TrySetResult(true);
	}

	public void ReleaseHoverRequest()
	{
		TaskCompletionSource<bool>? hoverRequestGate = _hoverRequestGate;
		_hoverRequestGate = null;

		hoverRequestGate?.TrySetResult(true);
	}

	public void BlockNextChangeNotification() =>
		_changeNotificationGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void BlockNextWatchedFilesNotification() =>
		_watchedFilesNotificationGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

	public void BlockNextSemanticTokensFullRequest() => BlockNextSemanticTokensFullRequest(response: null);

	/// <summary>
	/// Blocks the next semantic-token full request and pins <paramref name="response"/> as its reply, so tests can resume gated requests in any order
	/// and still know which response each one receives.
	/// </summary>
	public void BlockNextSemanticTokensFullRequest(JsonElement? response)
	{
		lock (_sentSyncRoot)
		{
			_availableSemanticTokensFullRequestGates.Enqueue(
				(new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), response)
			);
		}
	}

	public void ReleaseChangeNotification()
	{
		TaskCompletionSource<bool>? changeNotificationGate = _changeNotificationGate;
		_changeNotificationGate = null;

		changeNotificationGate?.TrySetResult(true);
	}

	public void ReleaseWatchedFilesNotification()
	{
		TaskCompletionSource<bool>? watchedFilesNotificationGate = _watchedFilesNotificationGate;
		_watchedFilesNotificationGate = null;

		watchedFilesNotificationGate?.TrySetResult(true);
	}

	/// <summary>
	/// Releases the oldest semantic-token full request that is currently parked on a block gate, so gated requests resume in the order they arrived.
	/// </summary>
	public void ReleaseSemanticTokensFullRequest()
	{
		TaskCompletionSource<bool>? semanticTokensFullRequestGate;

		lock (_sentSyncRoot)
		{
			semanticTokensFullRequestGate =
				_pendingSemanticTokensFullRequestGates.Count > 0 ? _pendingSemanticTokensFullRequestGates.Dequeue() : null;
		}

		semanticTokensFullRequestGate?.TrySetResult(true);
	}

	public async Task<bool> WaitForNotificationAsync(string method, TimeSpan timeout)
	{
		// Only notifications with an observation signal can be awaited; any other method would return
		// true immediately even though nothing was observed, making the await vacuous.
		Task observedNotification = method switch
		{
			"textDocument/didChange" => _changeNotificationObserved.Task,
			"textDocument/didClose" => _closeNotificationObserved.Task,
			_ => throw new InvalidOperationException($"The fake language server client has no observation signal for notification '{method}'."),
		};

		Task completedTask = await Task.WhenAny(observedNotification, Task.Delay(timeout)).ConfigureAwait(false);
		return ReferenceEquals(completedTask, observedNotification);
	}

	public Task<bool> WaitForMethodCountAsync(string method, int expectedCount, TimeSpan timeout) =>
		TestPolling.ForConditionAsync(() => GetSentMethodCount(method) >= expectedCount, timeout);

	public void PublishDiagnostics(PublishDiagnosticsParams parameters) =>
		DiagnosticsPublished?.Invoke(this, new DiagnosticsPublishedEventArgs(parameters));

	public void RaiseSemanticTokensRefreshRequested() => SemanticTokensRefreshRequested?.Invoke(this, EventArgs.Empty);

	public void RaiseDiagnosticRefreshRequested() => DiagnosticRefreshRequested?.Invoke(this, EventArgs.Empty);

	public void RaiseConnectionUnavailable(long? connectionGeneration = null)
	{
		long generation = connectionGeneration ?? ConnectionGeneration;
		EventHandler<ConnectionUnavailableEventArgs>? handlers = ConnectionUnavailable;

		// The unhealthy reset keeps the generation number, mirroring the real client contract: a
		// connection that was marked unhealthy keeps its generation until it is detached.
		if (generation == ConnectionGeneration)
		{
			IsReady = false;
			_capabilitySnapshotPublished = false;
		}

		BeforePublishingConnectionUnavailable?.Invoke();
		handlers?.Invoke(this, new ConnectionUnavailableEventArgs(generation));
	}

	public void EnqueueSemanticTokensFullResponse(JsonElement response)
	{
		lock (_sentSyncRoot)
			_semanticTokensFullResponses.Enqueue(response);
	}

	/// <summary>Returns how many notifications and requests of one method the double recorded.</summary>
	/// <param name="method">The LSP method name.</param>
	public int GetSentMethodCount(string method)
	{
		int count = 0;

		lock (_sentSyncRoot)
		{
			for (int i = 0; i < _sentMethodNames.Count; i++)
			{
				if (string.Equals(_sentMethodNames[i], method, StringComparison.Ordinal))
					count++;
			}
		}

		return count;
	}

	private void RecordRequest(string method, object parameters)
	{
		lock (_sentSyncRoot)
		{
			_sentMethodNames.Add(method);
			_sentRequests.Add((method, JsonSerializer.SerializeToElement(parameters)));
		}
	}

	private static async Task<TResult> AnswerWithHandlerAsync<TResult>(
		Func<JsonElement, CancellationToken, Task<JsonElement>> handler,
		object parameters,
		CancellationToken cancellationToken
	)
	{
		JsonElement response = await handler(JsonSerializer.SerializeToElement(parameters), cancellationToken).ConfigureAwait(false);
		return await DeserializeResponseAsync<TResult>(response).ConfigureAwait(false);
	}

	private async Task<TResult> ResolveCodeActionAsync<TResult>(object parameters, CancellationToken cancellationToken)
	{
		JsonElement response = await CodeActionResolveHandler!(JsonSerializer.SerializeToElement(parameters), cancellationToken)
			.ConfigureAwait(false);

		return await DeserializeResponseAsync<TResult>(response).ConfigureAwait(false);
	}

	private async Task<TResult> ExecuteCommandAsync<TResult>(object parameters, CancellationToken cancellationToken)
	{
		JsonElement response = ExecuteCommandHandler is null
			? JsonSerializer.SerializeToElement<object?>(null)
			: await ExecuteCommandHandler(JsonSerializer.SerializeToElement(parameters), cancellationToken).ConfigureAwait(false);

		return await DeserializeResponseAsync<TResult>(response).ConfigureAwait(false);
	}

	private static Task<TResult> DeserializeResponseAsync<TResult>(JsonElement response)
	{
		if (typeof(TResult) == typeof(JsonElement))
			return Task.FromResult((TResult)(object)response);

		TResult result = DeserializeResponse<TResult>(response);
		return Task.FromResult(result);
	}

	private static TResult DeserializeResponse<TResult>(JsonElement response)
	{
		// A configured JSON null models a server that answered null, which is the provider's documented
		// fallback path. The unconfigured case never reaches this method (its ValueKind is Undefined and
		// is rejected by the per-request configuration checks), so null is only observed here when a
		// test explicitly configured it.
		if (response.ValueKind == JsonValueKind.Null)
			return default!;

		TResult? result =
			JsonSerializer.Deserialize<TResult>(response.GetRawText(), s_responseDeserializationOptions)
			?? throw new InvalidOperationException("Expected a non-null JSON response.");

		return result;
	}

	private async Task WaitForWatchedFilesNotificationGateAsync()
	{
		TaskCompletionSource<bool>? watchedFilesNotificationGate = _watchedFilesNotificationGate;

		if (watchedFilesNotificationGate is not null)
			await watchedFilesNotificationGate.Task.ConfigureAwait(false);

		if (ThrowIOExceptionAfterWatchedFilesNotificationGateRelease)
		{
			ThrowIOExceptionAfterWatchedFilesNotificationGateRelease = false;
			throw new IOException("Simulated delayed workspace watcher connection failure.");
		}
	}

	private static async Task<TResult> WaitForCancellationAsync<TResult>(CancellationToken cancellationToken)
	{
		// Parks the request until its token is canceled; the delay never completes on its own.
		await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
		throw new InvalidOperationException("The simulated stalled request completed without an observed cancellation.");
	}

	private async Task<TResult> WaitForSemanticTokensFullRequestGateAsync<TResult>()
	{
		(TaskCompletionSource<bool> Gate, JsonElement? Response) blockedRequest;

		lock (_sentSyncRoot)
		{
			blockedRequest = _availableSemanticTokensFullRequestGates.Dequeue();
			_pendingSemanticTokensFullRequestGates.Enqueue(blockedRequest.Gate);
		}

		await blockedRequest.Gate.Task.ConfigureAwait(false);

		if (blockedRequest.Response is { } gatedResponse)
			return DeserializeResponse<TResult>(gatedResponse);

		JsonElement? queuedResponse = null;

		lock (_sentSyncRoot)
		{
			if (_semanticTokensFullResponses.Count > 0)
				queuedResponse = _semanticTokensFullResponses.Dequeue();
		}

		if (queuedResponse is { } response)
			return DeserializeResponse<TResult>(response);

		return DeserializeResponse<TResult>(JsonSerializer.SerializeToElement(new { data = new[] { 0, 6, 5, 0, 0 }, resultId = "tokens-1" }));
	}

	private async Task<TResult> WaitForHoverRequestGateAsync<TResult>(CancellationToken cancellationToken)
	{
		TaskCompletionSource<bool>? hoverRequestGate = _hoverRequestGate;

		if (hoverRequestGate is not null)
			await hoverRequestGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

		if (HoverResponse.ValueKind == JsonValueKind.Null)
			return default!;

		if (HoverResponse.ValueKind != JsonValueKind.Undefined)
			return DeserializeResponse<TResult>(HoverResponse);

		// A gated hover with no configured response models "no hover content"; the caller observes the
		// provider's documented fallback value.
		return default!;
	}

	/// <summary>
	/// Marks the fake as disposed and releases every parked operation, mirroring the real client's teardown:
	/// use after disposal throws <see cref="ObjectDisposedException"/>, and no blocked test operation can leak a permanently parked task.
	/// Only the first caller performs the teardown; later callers (sync or async) are no-ops.
	/// </summary>
	public void Dispose()
	{
		// Single-teardown contract: the first caller performs the teardown, a later caller is a no-op, and both
		// Dispose and DisposeAsync share the same path so an await-using consumer tears the fake down once.
		if (_isDisposed)
			return;

		DisposeCallCount++;
		_isDisposed = true;
		IsReady = false;

		ReleaseOpenNotification();
		ReleaseEnsureStartedAsync();
		ReleaseHoverRequest();
		ReleaseChangeNotification();
		ReleaseWatchedFilesNotification();

		TaskCompletionSource<bool>[] blockedSemanticTokensGates;

		lock (_sentSyncRoot)
		{
			blockedSemanticTokensGates =
			[
				.. _availableSemanticTokensFullRequestGates.Select(blockedRequest => blockedRequest.Gate),
				.. _pendingSemanticTokensFullRequestGates,
			];

			_availableSemanticTokensFullRequestGates.Clear();
			_pendingSemanticTokensFullRequestGates.Clear();
		}

		foreach (TaskCompletionSource<bool> gate in blockedSemanticTokensGates)
			gate.TrySetResult(true);
	}

	public ValueTask DisposeAsync()
	{
		// Shares the single teardown path with Dispose, so an await-using consumer tears the fake down once.
		Dispose();
		return ValueTask.CompletedTask;
	}

	private void ThrowIfDisposed()
	{
		if (_isDisposed)
			throw new ObjectDisposedException(nameof(FakeStatefulLanguageServerClient));
	}
}
