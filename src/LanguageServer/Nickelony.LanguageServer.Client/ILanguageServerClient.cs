namespace Nickelony.LanguageServer.Client;

/// <summary>Defines the connection and capability surface a host uses to run and talk to a language server.</summary>
/// <remarks>
/// The threading, restart, failure-classification, and disposal contract is described end to end in the repository's
/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md">consumer integration guide</see>. In brief:
/// the capability read is a lock-free immutable snapshot that reads <see cref="NegotiatedServerCapabilities.None"/>
/// until the capabilities are negotiated; the client never restarts itself; and a connection failure raises <see cref="ConnectionUnavailable"/>
/// so the host restarts through <see cref="EnsureStartedAsync"/> and re-synchronizes workspace state.
/// </remarks>
public interface ILanguageServerClient : IDisposable, IAsyncDisposable
{
	/// <summary>Indicates whether the language server finished initialization and can accept requests.</summary>
	bool IsReady { get; }

	/// <summary>
	/// The exception observed by the most recent startup attempt, or <see langword="null"/> when that attempt succeeded or failed
	/// without an exception.
	/// </summary>
	/// <remarks>
	/// The value is cleared when a new startup attempt begins.
	/// </remarks>
	Exception? LastStartupException { get; }

	/// <summary>The current connection generation for the active language-server session.</summary>
	/// <remarks>
	/// The value is <c>0</c> when no connection session is active. A session whose connection was marked unhealthy keeps its generation
	/// until it is detached, so callers can fence stale work against the invalidated generation.
	/// </remarks>
	long ConnectionGeneration { get; }

	/// <summary>The capabilities the language server negotiated for the current session, as one immutable snapshot.</summary>
	/// <remarks>
	/// The snapshot reads <see cref="NegotiatedServerCapabilities.None"/> until initialization completes successfully, and again after the active
	/// connection is detached or becomes unavailable. A read hands out the whole snapshot, so a caller that combines several
	/// of its members, such as a feature flag with the semantic-token legend, never mixes two connection generations. Read it once per decision,
	/// and read it again after a connection restart.
	/// </remarks>
	NegotiatedServerCapabilities NegotiatedCapabilities { get; }

	/// <summary>Raised when the server publishes diagnostics for a document URI.</summary>
	/// <remarks>
	/// Each subscribed handler runs independently on the thread pool.
	/// The active client is the sender, and each handler receives its own detached diagnostics snapshot: the outer diagnostics sequence is copied
	/// one level deep, while the diagnostic entries and their nested values stay shared and are read-only. Diagnostics coalesce per document
	/// URI by arrival order; a file URI coalesces on its normalized local path with the platform's local-path identity,
	/// any other URI goes by its exact text, and the LSP version field is not used for routing. See the consumer
	/// integration guide (<see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md">ConsumerIntegration.md</see>)
	/// for the shared delivery contract.
	/// </remarks>
	event EventHandler<DiagnosticsPublishedEventArgs>? DiagnosticsPublished;

	/// <summary>Raised when the server requests a semantic token refresh for open documents.</summary>
	/// <remarks>
	/// Each subscribed handler runs independently on the thread pool. The active client is the sender,
	/// and the event carries <see cref="System.EventArgs.Empty"/>. Repeated pending refresh requests may coalesce while a handler is still busy.
	/// See the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md">consumer integration guide</see>
	/// for the shared delivery contract.
	/// </remarks>
	event EventHandler? SemanticTokensRefreshRequested;

	/// <summary>Raised when the server requests a pull-diagnostics refresh for open documents.</summary>
	/// <remarks>
	/// Each subscribed handler runs independently on the thread pool. The active client is the sender,
	/// and the event carries <see cref="System.EventArgs.Empty"/>. Repeated pending refresh requests may coalesce while a handler is still busy.
	/// See the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md">consumer integration guide</see>
	/// for the shared delivery contract.
	/// </remarks>
	event EventHandler? DiagnosticRefreshRequested;

	/// <summary>Raised when the active ready connection becomes unavailable.</summary>
	/// <remarks>
	/// <para>
	/// The active client is the sender, and the event identifies the connection generation that was active immediately before the loss.
	/// Stale connection generations do not raise this event. Handlers run synchronously on the thread that reports the loss.
	/// </para>
	/// <para>
	/// The reported generation is already invalidated. Diagnostics payloads queued before the invalidation are dropped when their generation stops
	/// accepting server messages, but a delivery whose handlers are already running can still complete.
	/// </para>
	/// <para>
	/// Do not call <see cref="IDisposable.Dispose"/> or block on <see cref="IAsyncDisposable.DisposeAsync"/> from a handler, because teardown awaits
	/// the JSON-RPC completion task that reports the loss on the disconnect path, so the synchronous wait would spend the disposal timeout
	/// before teardown can continue.
	/// </para>
	/// </remarks>
	event EventHandler<ConnectionUnavailableEventArgs>? ConnectionUnavailable;

	/// <summary>
	/// Reports a ready client at once, and otherwise starts the language-server process, completes the LSP initialization handshake,
	/// and replaces any session that was marked unhealthy.
	/// </summary>
	/// <param name="cancellationToken">A token that can cancel startup.</param>
	/// <returns>
	/// <see langword="true"/> when the client is ready; otherwise, <see langword="false"/> when startup failed, was canceled by disposal,
	/// or disposal completed while startup was in flight.
	/// </returns>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before startup completes.</exception>
	/// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
	Task<bool> EnsureStartedAsync(CancellationToken cancellationToken);

	/// <summary>Marks one specific connection generation unhealthy only when it is still the active generation.</summary>
	/// <param name="connectionGeneration">The observed connection generation to invalidate.</param>
	/// <returns>
	/// <see langword="true"/> when the observed generation was still active and was marked unhealthy; otherwise, <see langword="false"/>.
	/// </returns>
	bool TryMarkConnectionUnhealthy(long connectionGeneration);

	/// <summary>Sends a JSON-RPC notification to the language server.</summary>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The notification payload, or <see langword="null"/> to send the notification without parameters.</param>
	/// <param name="cancellationToken">
	/// A token that can cancel waiting for local dispatch; a notification already handed to the connection may still be sent.
	/// </param>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before local dispatch completes.</exception>
	/// <exception cref="ArgumentException"><paramref name="method"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">
	/// The client was already disposed before the send started; a disposal that interrupts an in-flight send is reported
	/// as <see cref="LanguageServerConnectionUnavailableException"/> instead.
	/// </exception>
	/// <exception cref="LanguageServerConnectionUnavailableException">
	/// No ready connection is available, such as before <see cref="EnsureStartedAsync"/> completes or after the connection was marked unhealthy,
	/// or the notification fails because the connection is unusable. A failure observed on a live session also marks it unhealthy
	/// and raises <see cref="ConnectionUnavailable"/>; a failure with no ready session has nothing to invalidate, so no event is raised.
	/// </exception>
	Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken);

	/// <summary>
	/// Reads the host's current settings and pushes them to the language server in a <c>workspace/didChangeConfiguration</c> notification.
	/// </summary>
	/// <param name="cancellationToken">
	/// A token that can cancel waiting for local dispatch; a notification already handed to the connection may still be sent.
	/// </param>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before local dispatch completes.</exception>
	/// <exception cref="ObjectDisposedException">
	/// The client was already disposed before the send started; a disposal that interrupts an in-flight send is reported
	/// as <see cref="LanguageServerConnectionUnavailableException"/> instead.
	/// </exception>
	/// <exception cref="LanguageServerConnectionUnavailableException">
	/// No ready connection is available, or the notification fails because the connection is unusable,
	/// exactly as for <see cref="SendNotificationAsync"/>.
	/// </exception>
	/// <remarks>
	/// The settings come from the same source that answers the server's <c>workspace/configuration</c> requests, so the push
	/// and the pull always agree. A fault in that source is logged and no notification is sent.
	/// </remarks>
	Task PushSettingsAsync(CancellationToken cancellationToken);

	/// <summary>Sends a JSON-RPC request to the language server and waits for a typed response payload.</summary>
	/// <typeparam name="TResult">The typed response payload to deserialize.</typeparam>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request payload.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The typed response payload.</returns>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before the request completes.</exception>
	/// <exception cref="ArgumentException"><paramref name="method"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="parameters"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">
	/// The client was already disposed before the send started; a disposal that interrupts an in-flight send is reported
	/// as <see cref="LanguageServerConnectionUnavailableException"/> instead.
	/// </exception>
	/// <exception cref="LanguageServerConnectionUnavailableException">
	/// No ready connection is available, such as before <see cref="EnsureStartedAsync"/> completes or after the connection
	/// was marked unhealthy, or the request fails because the connection is unusable. A failure observed on a live session also marks it unhealthy
	/// and raises <see cref="ConnectionUnavailable"/>; a failure with no ready session has nothing to invalidate, so no event is raised.
	/// </exception>
	/// <exception cref="LanguageServerRequestRejectedException">
	/// The server answered with a JSON-RPC error response; the connection stays ready and usable,
	/// so the caller can map the rejection to a fallback value. A rejection that arrives after the connection was superseded is reported
	/// as <see cref="LanguageServerConnectionSupersededException"/> instead.
	/// </exception>
	/// <exception cref="LanguageServerConnectionSupersededException">
	/// The connection was superseded or marked unhealthy after the response completed, so the completed result or rejection is discarded.
	/// </exception>
	/// <remarks>
	/// The client applies no default request timeout; callers own timeout and retry policy through the supplied <paramref name="cancellationToken"/>.
	/// </remarks>
	Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken);
}
