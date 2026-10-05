using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Routes the diagnostics payloads and refresh requests that the server sends to the subscribers of <see cref="ILanguageServerClient"/>.
/// </summary>
/// <remarks>
/// Diagnostics flow through the diagnostics coalescer,
/// which keeps the newest payload per document, into the server-message dispatcher, which also delivers the refresh requests; both are observed
/// background loops whose unexpected termination marks the connection unhealthy rather than recreating locally. The recovery model is documented
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class ServerMessageRouter
{
	private readonly BackgroundTaskObserver _backgroundTaskObserver;

	private readonly SequentialDiagnosticsSubscriberSet<EventHandler<DiagnosticsPublishedEventArgs>> _diagnosticsPublishedSubscribers;

	private readonly SequentialSignalSubscriberSet<EventHandler> _semanticTokensRefreshSubscribers;
	private readonly SequentialSignalSubscriberSet<EventHandler> _diagnosticRefreshSubscribers;

	// Guards the start and restart of the background loops.
	private readonly object _backgroundLoopSyncRoot = new();

	private Task _diagnosticsCoalescerTask = Task.CompletedTask;
	private Task _serverMessageDispatcherTask = Task.CompletedTask;

	/// <param name="eventSender">The sender reported to diagnostics and refresh subscribers.</param>
	/// <param name="canAcceptServerMessagesForGeneration">Reports whether a connection generation still accepts server messages.</param>
	/// <param name="testHooks">The test-only hooks, or <see cref="LanguageServerClientTestHooks.None"/> for production.</param>
	/// <param name="backgroundTaskObserver">Observes the background loops, so a loop that ends while the client is active is reported.</param>
	/// <param name="logger">The logger that receives the failures of subscriber handlers.</param>
	/// <param name="lifetimeToken">Signals client disposal to the loops.</param>
	internal ServerMessageRouter(
		object eventSender,
		Func<long, bool> canAcceptServerMessagesForGeneration,
		LanguageServerClientTestHooks testHooks,
		BackgroundTaskObserver backgroundTaskObserver,
		ILogger logger,
		CancellationToken lifetimeToken
	)
	{
		_backgroundTaskObserver = backgroundTaskObserver;

		_diagnosticsPublishedSubscribers = new(
			invokeHandler: (handler, parameters) => handler(eventSender, new DiagnosticsPublishedEventArgs(parameters)),
			logHandlerFailure: exception => logger.LogWarning(exception, "Diagnostics handler threw; later subscribers will still be notified."),
			testHooks
		);

		_semanticTokensRefreshSubscribers = new(
			handler => handler(eventSender, EventArgs.Empty),
			exception => logger.LogWarning(exception, "Semantic tokens refresh request handler threw; later subscribers will still be notified.")
		);

		_diagnosticRefreshSubscribers = new(
			handler => handler(eventSender, EventArgs.Empty),
			exception => logger.LogWarning(exception, "Diagnostic refresh request handler threw; later subscribers will still be notified.")
		);

		ServerMessageDispatcher = new ServerMessageDispatcher(
			canAcceptServerMessagesForGeneration,
			InvokeSemanticTokensRefreshRequested,
			InvokeDiagnosticRefreshRequested,
			InvokeDiagnosticsPublished,
			lifetimeToken
		);

		DiagnosticsCoalescer = new DiagnosticsCoalescer(
			canAcceptServerMessagesForGeneration,
			forward: ServerMessageDispatcher.QueueDiagnostics,
			lifetimeToken
		);
	}

	/// <summary>
	/// The coalescer that keeps the newest diagnostics payload per document and hands the deliverable ones to the server-message dispatcher.
	/// </summary>
	internal DiagnosticsCoalescer DiagnosticsCoalescer { get; }

	/// <summary>The dispatcher that delivers refresh requests and diagnostics to the subscribers.</summary>
	internal ServerMessageDispatcher ServerMessageDispatcher { get; }

	/// <summary>The tracked background server-message dispatcher task.</summary>
	internal Task ServerMessageDispatcherTask
	{
		get => Volatile.Read(ref _serverMessageDispatcherTask);
		set => Volatile.Write(ref _serverMessageDispatcherTask, value);
	}

	/// <summary>The tracked background diagnostics coalescer task.</summary>
	internal Task DiagnosticsCoalescerTask
	{
		get => Volatile.Read(ref _diagnosticsCoalescerTask);
		set => Volatile.Write(ref _diagnosticsCoalescerTask, value);
	}

	/// <summary>Whether the router has delivered every queued payload: both loops are idle and every subscription has finished draining.</summary>
	/// <remarks>
	/// The diagnostics coalescer is read before the server-message dispatcher and the subscribers, because a payload moves
	/// from the first to the second to the third: reading in the opposite order could miss a payload that moves between two reads. A subscriber set
	/// that was completed, or a subscriber that was removed, stops reporting a handler that is still running, so a test waits an absence window
	/// for such a handler. It is an internal state property that tests read to wait for quiescence; production code never branches on it.
	/// </remarks>
	internal bool IsDrained =>
		DiagnosticsCoalescer.IsIdle
		&& ServerMessageDispatcher.IsIdle
		&& _diagnosticsPublishedSubscribers.IsIdle
		&& _semanticTokensRefreshSubscribers.IsIdle
		&& _diagnosticRefreshSubscribers.IsIdle;

	/// <summary>Adds one subscriber to the sequential diagnostics-dispatch set.</summary>
	/// <param name="handler">The subscriber to add.</param>
	internal void AddDiagnosticsSubscriber(EventHandler<DiagnosticsPublishedEventArgs>? handler) => _diagnosticsPublishedSubscribers.Add(handler);

	/// <summary>Removes one subscriber from the sequential diagnostics-dispatch set.</summary>
	/// <param name="handler">The subscriber to remove.</param>
	internal void RemoveDiagnosticsSubscriber(EventHandler<DiagnosticsPublishedEventArgs>? handler) =>
		_diagnosticsPublishedSubscribers.Remove(handler);

	/// <summary>Adds one subscriber to the sequential semantic-token refresh signal set.</summary>
	/// <param name="handler">The subscriber to add.</param>
	internal void AddSemanticTokensRefreshSubscriber(EventHandler? handler) => _semanticTokensRefreshSubscribers.Add(handler);

	/// <summary>Removes one subscriber from the sequential semantic-token refresh signal set.</summary>
	/// <param name="handler">The subscriber to remove.</param>
	internal void RemoveSemanticTokensRefreshSubscriber(EventHandler? handler) => _semanticTokensRefreshSubscribers.Remove(handler);

	/// <summary>Adds one subscriber to the sequential diagnostic refresh signal set.</summary>
	/// <param name="handler">The subscriber to add.</param>
	internal void AddDiagnosticRefreshSubscriber(EventHandler? handler) => _diagnosticRefreshSubscribers.Add(handler);

	/// <summary>Removes one subscriber from the sequential diagnostic refresh signal set.</summary>
	/// <param name="handler">The subscriber to remove.</param>
	internal void RemoveDiagnosticRefreshSubscriber(EventHandler? handler) => _diagnosticRefreshSubscribers.Remove(handler);

	/// <summary>Queues a diagnostics payload for later publication on the diagnostics coalescer.</summary>
	/// <param name="connectionGeneration">The connection generation that published the diagnostics.</param>
	/// <param name="parameters">The diagnostics payload.</param>
	internal void QueueDiagnosticsPublished(long connectionGeneration, PublishDiagnosticsParams parameters) =>
		DiagnosticsCoalescer.Queue(connectionGeneration, parameters);

	/// <summary>Queues a semantic tokens refresh request for background subscriber dispatch.</summary>
	internal void QueueSemanticTokensRefreshRequested() => ServerMessageDispatcher.QueueSemanticTokensRefreshRequested();

	/// <summary>Queues a diagnostic refresh request for background subscriber dispatch.</summary>
	internal void QueueDiagnosticRefreshRequested() => ServerMessageDispatcher.QueueDiagnosticRefreshRequested();

	/// <summary>Dispatches one semantic tokens refresh signal to every current subscriber.</summary>
	internal void InvokeSemanticTokensRefreshRequested() => _semanticTokensRefreshSubscribers.Dispatch();

	/// <summary>Dispatches one diagnostic refresh signal to every current subscriber.</summary>
	internal void InvokeDiagnosticRefreshRequested() => _diagnosticRefreshSubscribers.Dispatch();

	/// <summary>Dispatches one diagnostics payload to every current subscriber.</summary>
	/// <param name="documentKey">The document identity that coalesces repeated payloads per subscriber.</param>
	/// <param name="parameters">The diagnostics payload to dispatch.</param>
	internal void InvokeDiagnosticsPublished(DiagnosticsDocumentKey documentKey, PublishDiagnosticsParams parameters) =>
		_diagnosticsPublishedSubscribers.Dispatch(documentKey, parameters);

	/// <summary>Ensures the server-message dispatcher is running for the current client instance.</summary>
	/// <remarks>
	/// Completed or faulted loops are recreated so a connection restart can recover server-message delivery.
	/// </remarks>
	internal void EnsureServerMessageDispatcherRunning() => EnsureLoopsRunning(includeDiagnosticsCoalescer: false);

	/// <summary>Ensures the server-message dispatcher and the diagnostics coalescer are running for the current client instance.</summary>
	/// <remarks>
	/// Completed or faulted loops are recreated so a connection restart can recover server-message delivery.
	/// </remarks>
	internal void EnsureServerMessageAndDiagnosticsLoopsRunning() => EnsureLoopsRunning(includeDiagnosticsCoalescer: true);

	/// <summary>Completes the wake signals so the background loops finish during disposal, and drops what is still queued.</summary>
	internal void CompleteSignalChannels()
	{
		ServerMessageDispatcher.Complete();
		DiagnosticsCoalescer.Complete();
	}

	/// <summary>
	/// Completes subscriber delivery for disposal, so a payload that is still queued for a subscriber is dropped instead of being delivered.
	/// </summary>
	/// <remarks>
	/// A handler that already started still runs to completion; only the queued-but-not-started payloads are dropped.
	/// </remarks>
	internal void CompleteSubscribers()
	{
		_diagnosticsPublishedSubscribers.Complete();
		_semanticTokensRefreshSubscribers.Complete();
		_diagnosticRefreshSubscribers.Complete();
	}

	/// <summary>
	/// Ensures the server-message dispatcher and, optionally, the diagnostics coalescer are running for the current client instance.
	/// </summary>
	/// <param name="includeDiagnosticsCoalescer">Whether the diagnostics coalescer should also be ensured.</param>
	/// <remarks>
	/// Completed or faulted loops are recreated so a connection restart can recover server-message delivery.
	/// </remarks>
	private void EnsureLoopsRunning(bool includeDiagnosticsCoalescer)
	{
		lock (_backgroundLoopSyncRoot)
		{
			if (_serverMessageDispatcherTask.IsCompleted)
			{
				_backgroundTaskObserver.ForgetLoop(_serverMessageDispatcherTask);

				_serverMessageDispatcherTask = _backgroundTaskObserver.StartObservedLoop(
					ServerMessageDispatcher.RunAsync,
					"server-message dispatcher"
				);
			}

			if (includeDiagnosticsCoalescer && _diagnosticsCoalescerTask.IsCompleted)
			{
				_backgroundTaskObserver.ForgetLoop(_diagnosticsCoalescerTask);
				_diagnosticsCoalescerTask = _backgroundTaskObserver.StartObservedLoop(DiagnosticsCoalescer.RunAsync, "diagnostics coalescer");
			}
		}
	}
}
