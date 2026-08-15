namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Defines the transport and capability surface a host uses to run and talk to a language server.
/// </summary>
/// <remarks>
/// <para>
/// Thread safety: capability reads (<see cref="IsReady"/>, <see cref="TransportGeneration"/>,
/// <see cref="TextDocumentSyncKind"/>, <see cref="SemanticTokenTypes"/>, <see cref="SemanticTokenModifiers"/>,
/// and the <c>Supports*</c> properties) return lock-free immutable snapshots and may be read from any thread.
/// Each read observes one consistent published snapshot, so a single read never mixes transport generations;
/// values read at different moments can belong to different generations (re-read them after <see cref="StartAsync"/>).
/// A <c>Supports*</c> member reads <see langword="false"/> while the capability has not been negotiated; this
/// surface does not distinguish an unknown capability from an unsupported one.
/// <see cref="SendRequestAsync{TResult}"/> and <see cref="SendNotificationAsync"/> may be
/// called concurrently; each call targets the ready session it observed. <see cref="StartAsync"/> serializes
/// concurrent startup attempts.
/// </para>
/// <para>
/// Restart contract: transport failures mark the current transport unhealthy and raise <see cref="TransportUnavailable"/>;
/// the client never restarts itself. The host calls <see cref="StartAsync"/> again to create a new session,
/// then re-synchronizes workspace state. A caller that detects a transport failure on a generation it captured
/// should invalidate that generation with <see cref="TryMarkTransportUnhealthy"/> so a stale caller cannot
/// invalidate a newer transport. The <see cref="TransportUnavailable"/> event already reports an invalidated
/// generation and must not be answered with another invalidation.
/// </para>
/// <para>
/// Disposal: <see cref="IDisposable.Dispose"/> and <see cref="IAsyncDisposable.DisposeAsync"/> share one teardown path
/// driven by a single <c>DisposeWaitTimeout</c> budget; later teardown stages are skipped once the budget is exhausted.
/// Only the first dispose caller performs teardown, and every caller observes its completion: a later caller waits for
/// the teardown the first caller started instead of returning while it continues in the background.
/// </para>
/// <para>
/// Transport usability: every failure caused by the transport not being usable is reported as a type derived from
/// <see cref="LanguageServerTransportException"/> (itself an <see cref="IOException"/>), so a single catch covers "no
/// ready session yet", "became unavailable", and "superseded".
/// </para>
/// <para>
/// A send that races disposal either throws <see cref="ObjectDisposedException"/> (disposal already started) or
/// surfaces as <see cref="LanguageServerTransportUnavailableException"/> when the transport is torn down mid-send.
/// <see cref="StartAsync"/> returns <see langword="false"/> when disposal wins after its entry check. Event delivery
/// stops when disposal completes the callback pumps and the subscriber drains: no server callback starts a new
/// delivery, a payload that a subscriber had queued but not started is dropped, and a handler that already started
/// runs to completion.
/// </para>
/// <para>
/// Event delivery: each subscribed handler runs independently on the thread pool; reentrant notifications for the
/// same handler are serialized, and repeated pending payloads may coalesce to the latest while a handler is still
/// busy. Different handlers may run concurrently, handler failures are isolated, and handlers must marshal to the
/// thread their consumer requires. Server callbacks from an attached session are delivered even before the client reports
/// <see cref="IsReady"/>; only sessions whose transport was detached or marked unhealthy are fenced.
/// </para>
/// </remarks>
public interface ILanguageServerClient : IDisposable, IAsyncDisposable
{
	/// <summary>
	/// Gets a value indicating whether the language server finished initialization and can accept requests.
	/// </summary>
	bool IsReady { get; }

	/// <summary>
	/// Gets the exception observed by the most recent startup attempt, or <see langword="null"/> when that attempt
	/// succeeded or failed without an exception. The value is cleared when a new startup attempt begins.
	/// </summary>
	Exception? LastStartupException { get; }

	/// <summary>
	/// Gets the current transport generation for the active language-server session.
	/// Returns <c>0</c> when no transport session is active.
	/// </summary>
	/// <remarks>
	/// A session whose transport was marked unhealthy keeps its generation until it is detached, so callers can fence
	/// stale work against the invalidated generation.
	/// </remarks>
	long TransportGeneration { get; }

	/// <summary>
	/// Gets the text-document synchronization mode negotiated with the language server.
	/// Returns <see cref="TextDocumentSyncKind.None"/> until initialization completes successfully,
	/// and again after the active transport is detached or becomes unavailable.
	/// </summary>
	TextDocumentSyncKind TextDocumentSyncKind { get; }

	/// <summary>
	/// Gets the semantic token types reported by the server capabilities.
	/// The returned list is a read-only snapshot and must not be treated as mutable storage.
	/// </summary>
	IReadOnlyList<string> SemanticTokenTypes { get; }

	/// <summary>
	/// Gets the semantic token modifiers reported by the server capabilities.
	/// The returned list is a read-only snapshot and must not be treated as mutable storage.
	/// </summary>
	IReadOnlyList<string> SemanticTokenModifiers { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>completionItem/resolve</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsCompletionResolve { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>textDocument/documentSymbol</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsDocumentSymbols { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>textDocument/codeAction</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsCodeActions { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>codeAction/resolve</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability; the
	/// resolve flow itself stays host-owned because it sends an action payload back over the transport.
	/// </summary>
	bool SupportsCodeActionResolve { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>textDocument/references</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsReferences { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>textDocument/rename</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsRename { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>textDocument/formatting</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsFormatting { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>textDocument/hover</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsHover { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>textDocument/definition</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsDefinition { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports <c>textDocument/signatureHelp</c>.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsSignatureHelp { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports full semantic token requests.
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsSemanticTokensFull { get; }

	/// <summary>
	/// Gets a value indicating whether the server supports pull diagnostics (<c>textDocument/diagnostic</c>).
	/// Returns <see langword="false"/> until the active transport explicitly negotiates the capability.
	/// </summary>
	bool SupportsPullDiagnostics { get; }

	/// <summary>
	/// Raised when the server publishes diagnostics for a document URI.
	/// Each subscribed handler runs independently on the thread pool.
	/// </summary>
	/// <remarks>
	/// The active client is the sender, and each handler receives its own detached diagnostics snapshot: the outer
	/// diagnostics sequence is copied one level deep, while the nested related-information and tag values stay shared
	/// and are read-only. Diagnostics coalesce per document URI by arrival order; a file URI coalesces on its
	/// normalized local path with the platform's local-path identity, any other URI goes by its exact text, and the
	/// LSP version field is not used for routing. See the type remarks for the shared delivery contract.
	/// </remarks>
	event EventHandler<DiagnosticsPublishedEventArgs>? DiagnosticsPublished;

	/// <summary>
	/// Raised when the server requests a semantic token refresh for open documents.
	/// Each subscribed handler runs independently on the thread pool.
	/// </summary>
	/// <remarks>
	/// The active client is the sender, and the event carries <see cref="System.EventArgs.Empty"/>. Repeated pending
	/// refresh requests may coalesce while a handler is still busy. See the type remarks for the shared delivery
	/// contract.
	/// </remarks>
	event EventHandler? SemanticTokensRefreshRequested;

	/// <summary>
	/// Raised when the server requests a pull-diagnostics refresh for open documents.
	/// Each subscribed handler runs independently on the thread pool.
	/// </summary>
	/// <remarks>
	/// The active client is the sender, and the event carries <see cref="System.EventArgs.Empty"/>. Repeated pending
	/// refresh requests may coalesce while a handler is still busy. See the type remarks for the shared delivery
	/// contract.
	/// </remarks>
	event EventHandler? DiagnosticRefreshRequested;

	/// <summary>
	/// Raised when the active ready transport becomes unavailable.
	/// The active client is the sender, and the event identifies the transport generation that was active
	/// immediately before the loss. Stale transport generations do not raise this event.
	/// </summary>
	/// <remarks>
	/// Handlers run synchronously on the thread that reports the loss, and the reported generation is already
	/// invalidated. Diagnostics payloads queued before the invalidation are dropped when their generation no longer
	/// accepts callbacks; a delivery whose handlers are already running can still complete. Do not call
	/// <see cref="IDisposable.Dispose"/> or block on <see cref="IAsyncDisposable.DisposeAsync"/> from a handler:
	/// teardown awaits the transport loops, including the JSON-RPC completion task that reports the loss on the
	/// disconnect path, so the synchronous wait would spend the disposal budget before teardown can continue.
	/// </remarks>
	event EventHandler<TransportUnavailableEventArgs>? TransportUnavailable;

	/// <summary>
	/// Starts the language-server process and completes the LSP initialization handshake.
	/// </summary>
	/// <param name="cancellationToken">A token that can cancel startup.</param>
	/// <returns>
	/// <see langword="true"/> when the client is ready; otherwise, <see langword="false"/> when startup failed, was
	/// canceled by disposal, or disposal completed while startup was in flight.
	/// </returns>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before startup completes.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	Task<bool> StartAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Marks one specific transport generation unhealthy only when it is still the active generation.
	/// </summary>
	/// <param name="transportGeneration">The observed transport generation to invalidate.</param>
	/// <returns><see langword="true"/> when the observed generation was still active and was marked unhealthy; otherwise, <see langword="false"/>.</returns>
	bool TryMarkTransportUnhealthy(long transportGeneration);

	/// <summary>
	/// Sends a JSON-RPC notification to the language server.
	/// </summary>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The notification payload, or <see langword="null"/> to send the notification without parameters.</param>
	/// <param name="cancellationToken">A token that can cancel waiting for local dispatch; a notification already handed to the transport may still be sent.</param>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before local dispatch completes.</exception>
	/// <exception cref="ArgumentException"><paramref name="method"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">
	/// The client was already disposed before the send started; a disposal that interrupts an in-flight send is
	/// reported as <see cref="LanguageServerTransportUnavailableException"/> instead.
	/// </exception>
	/// <exception cref="LanguageServerTransportUnavailableException">
	/// No ready transport is available, such as before <see cref="StartAsync"/> completes or after the transport was
	/// marked unhealthy, or the notification fails because the transport is no longer usable. A failure observed on
	/// a live session also marks it unhealthy and raises <see cref="TransportUnavailable"/>; a failure with no ready
	/// session has nothing to invalidate, so no event is raised.
	/// </exception>
	Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken);

	/// <summary>
	/// Sends a JSON-RPC request to the language server and waits for a typed response payload.
	/// </summary>
	/// <remarks>
	/// The transport itself does not apply a default request timeout; callers own timeout and retry policy
	/// through the supplied <paramref name="cancellationToken"/>.
	/// </remarks>
	/// <typeparam name="TResult">The typed response payload to deserialize.</typeparam>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request payload.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The typed response payload.</returns>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before the request completes.</exception>
	/// <exception cref="ArgumentException"><paramref name="method"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="parameters"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">
	/// The client was already disposed before the send started; a disposal that interrupts an in-flight send is
	/// reported as <see cref="LanguageServerTransportUnavailableException"/> instead.
	/// </exception>
	/// <exception cref="LanguageServerTransportUnavailableException">
	/// No ready transport is available, such as before <see cref="StartAsync"/> completes or after the transport was
	/// marked unhealthy, or the request fails because the transport is no longer usable. A failure observed on a live
	/// session also marks it unhealthy and raises <see cref="TransportUnavailable"/>; a failure with no ready session
	/// has nothing to invalidate, so no event is raised.
	/// </exception>
	/// <exception cref="LanguageServerRequestRejectedException">
	/// The server answered with a JSON-RPC error response; the transport stays ready and usable, so the caller can map
	/// the rejection to a fallback value. A rejection that arrives after the transport was superseded is reported as
	/// <see cref="LanguageServerTransportChangedException"/> instead.
	/// </exception>
	/// <exception cref="LanguageServerTransportChangedException">
	/// The transport was superseded or marked unhealthy after the response completed, so the completed result or
	/// rejection is discarded.
	/// </exception>
	Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken);
}
