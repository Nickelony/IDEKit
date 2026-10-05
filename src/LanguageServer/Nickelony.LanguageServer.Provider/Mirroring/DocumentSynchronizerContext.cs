namespace Nickelony.LanguageServer.Provider;

/// <summary>The collaborators a <see cref="DocumentSynchronizer"/> is built from.</summary>
/// <param name="Provider">The client, the tracked documents, the logger and the helpers every provider collaborator shares.</param>
/// <param name="Scheduler">The scheduler that serializes the operations of one document.</param>
/// <param name="PendingCloses">The tracker of the closes deferred while a request lease kept their document tracked.</param>
/// <param name="Lifecycle">The coordinator that starts the connection and classifies a failed start.</param>
/// <param name="NotificationSender">The sender of the open, change and close notifications.</param>
/// <param name="Closer">The closer that closes the server copy of a document once no lease keeps it open.</param>
/// <param name="Signals">The reactions of the features that consume what happened to a document.</param>
/// <param name="Renamer">The coordinator that renames a tracked document to a new path.</param>
internal sealed record DocumentSynchronizerContext(
	ProviderContext Provider,
	DocumentOperationScheduler Scheduler,
	PendingCloseTracker PendingCloses,
	ProviderLifecycleCoordinator Lifecycle,
	DocumentNotificationSender NotificationSender,
	DocumentCloser Closer,
	DocumentSignals Signals,
	DocumentRenameCoordinator Renamer
);
