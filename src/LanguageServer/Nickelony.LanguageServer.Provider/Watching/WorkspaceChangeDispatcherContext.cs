namespace Nickelony.LanguageServer.Provider;

/// <summary>The workspace-change inputs a <see cref="WorkspaceChangeDispatcher"/> is built from.</summary>
/// <param name="WorkspaceRootDirectoryPaths">The normalized workspace root directories to watch.</param>
/// <param name="WorkspaceRootsDisplayText">
/// The comma-joined normalized workspace root paths used in log text, or the folderless marker when there are none.
/// </param>
/// <param name="ProviderDisplayName">The provider display name used in log text.</param>
/// <param name="WatchSpecifications">
/// The file patterns that should be mirrored to the language server, or an empty list to disable workspace watching for all roots.
/// </param>
/// <param name="FileWatcherFactory">Builds the workspace file watcher for one root.</param>
/// <param name="FileSnapshotTrackerFactory">Builds the workspace snapshot tracker for one root.</param>
/// <param name="Sender">The sender that sends a forwarded batch to the language server.</param>
/// <param name="Owner">The owner callbacks used for client access, disposal probing, startup, and failure reporting.</param>
/// <param name="ObserveBackgroundTask">Observes a background task of a watch supervisor, so its fault is logged.</param>
/// <param name="Options">
/// The provider tunables, whose path comparison decides which paths belong to a workspace root and coalesce into one change.
/// </param>
/// <param name="Logger">The logger instance.</param>
internal sealed record WorkspaceChangeDispatcherContext(
	IReadOnlyList<string> WorkspaceRootDirectoryPaths,
	string WorkspaceRootsDisplayText,
	string ProviderDisplayName,
	IReadOnlyList<FileWatchSpecification> WatchSpecifications,
	FileWatcherFactory FileWatcherFactory,
	FileSnapshotTrackerFactory FileSnapshotTrackerFactory,
	WorkspaceChangeSender Sender,
	WorkspaceChangeOwnerContext Owner,
	Action<Task, string> ObserveBackgroundTask,
	LanguageServerFeatureProviderOptions Options,
	ILogger Logger
);
