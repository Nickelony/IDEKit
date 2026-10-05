namespace Nickelony.LanguageServer.Provider;

/// <summary>Builds the collaborators of one language-server session and wires them to each other.</summary>
/// <remarks>
/// <para>
/// The provider creates one composition from the values its constructor validated,
/// and reaches every collaborator through it. The lifecycle coordinator reaches the synchronizer's replay and the workspace restore through two
/// contracts that are attached once the composition has built them, because the lifecycle is built before its participants.
/// </para>
/// <para>
/// A feature reads no field of the provider: it receives the <see cref="Context"/>, so the same wiring serves a collaborator test that builds
/// the context by hand.
/// </para>
/// </remarks>
internal sealed class ProviderComposition
{
	/// <param name="owner">The provider that raises the events, passed to every handler as its sender.</param>
	/// <param name="client">The language-server client, or <see langword="null"/> when no server is configured.</param>
	/// <param name="workspaceRootDirectoryPaths">The normalized workspace root directories; an empty list models a folderless provider.</param>
	/// <param name="profile">The data and rules of the language, with watch specifications that the constructor already validated.</param>
	/// <param name="options">The provider tunables, already validated.</param>
	/// <param name="testHooks">The hooks that expose nondeterministic points and the document signals to a test.</param>
	/// <param name="workspaceFileWatcherFactory">
	/// The factory that replaces the workspace file watcher of every root, or <see langword="null"/> for the default.
	/// </param>
	/// <param name="workspaceSnapshotTrackerFactory">
	/// The factory that replaces the snapshot tracker of every root, or <see langword="null"/> for the default.
	/// </param>
	/// <param name="logger">The logger instance.</param>
	internal ProviderComposition(
		object owner,
		ILanguageServerClient? client,
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		LanguageServerProfile profile,
		LanguageServerFeatureProviderOptions options,
		LanguageServerFeatureProviderTestHooks testHooks,
		FileWatcherFactory? workspaceFileWatcherFactory,
		FileSnapshotTrackerFactory? workspaceSnapshotTrackerFactory,
		ILogger logger
	)
	{
		string providerDisplayName = profile.LanguageDisplayName;
		IReadOnlyList<FileWatchSpecification> watchSpecifications = profile.WatchSpecifications;

		Disposal = new ProviderLifetime();
		Events = new ProviderEvents(owner, Disposal, logger);
		Scheduler = new DocumentOperationScheduler(options);
		Documents = new DocumentTracker(options);
		Context = new ProviderContext(client, Documents, Disposal, providerDisplayName, options, logger);

		// An empty root list models a folderless provider, matching the folderless client session the shared
		// path helper also supports; the workspace change dispatcher then creates no watch supervisor, so no external
		// change is discovered unless the host bridges file events itself. Log text uses a folderless label so
		// the workspace-root display string stays non-empty for the logger and the request runner.
		string workspaceRootsDisplayText = workspaceRootDirectoryPaths.Count == 0 ? "<folderless>" : string.Join(", ", workspaceRootDirectoryPaths);

		var startupState = new StartupState(client, Disposal);

		var failureReporter = new StartupFailureReporter(
			new StartupFailureReporterContext(
				startupState,
				Events,
				workspaceRootsDisplayText,
				providerDisplayName,
				TransientStartupFailureMessage: profile.TransientStartupFailureMessage,
				PersistentStartupFailureMessage: profile.PersistentStartupFailureMessage,
				MissingClientFailureMessage: profile.MissingClientFailureMessage,
				options,
				logger
			)
		);

		var timeouts = new RequestTimeoutCounter(options.RequestTimeoutRestartThreshold);

		Lifecycle = new ProviderLifecycleCoordinator(
			new ProviderLifecycleContext(Provider: Context, startupState, Events, failureReporter, testHooks, workspaceRootsDisplayText, timeouts)
		);

		// The provider validates the option values before it composes, so the runner itself does not re-validate them.
		RequestRunner = new RequestRunner(
			new RequestRunnerContext(
				workspaceRootsDisplayText,
				client,
				options,
				Disposal,
				EnsureConnectionStartedAsync: Lifecycle.EnsureConnectionStartedAsync,
				timeouts,
				logger
			)
		);

		DiagnosticsSource = new DiagnosticsSource(
			Context,
			RequestRunner,
			diagnosticMappingPolicy: profile.DiagnosticMappingPolicy,
			Scheduler,
			Events
		);

		SemanticTokensSource = new SemanticTokensSource(Context, RequestRunner, Scheduler, Events);

		// A synchronized document refreshes its semantic tokens before it pulls its diagnostics, an invalidated document cancels both in that
		// order, and a renamed document announces its diagnostics before its tokens.
		IServerPayloadRefresher[] refreshOrder = [SemanticTokensSource, DiagnosticsSource];
		IServerPayloadRefresher[] announceOrder = [DiagnosticsSource, SemanticTokensSource];

		var signals = new DocumentSignals(
			Synchronized: document =>
			{
				foreach (IServerPayloadRefresher source in refreshOrder)
					source.Trigger(document);

				testHooks.DocumentSynchronizedObserver?.Invoke(document);
			},

			Invalidated: filePath =>
			{
				foreach (IServerPayloadRefresher source in refreshOrder)
					source.Cancel(filePath);

				testHooks.DocumentInvalidatedObserver?.Invoke(filePath);
			},

			Renamed: filePath =>
			{
				foreach (IServerPayloadRefresher source in announceOrder)
					source.AnnounceRenamed(filePath);

				testHooks.DocumentRenamedObserver?.Invoke(filePath);
			}
		);

		var pendingCloses = new PendingCloseTracker(options);
		var notificationSender = new DocumentNotificationSender(Context, languageId: () => profile.LanguageId);

		Closer = new DocumentCloser(Context, Scheduler, pendingCloses, notificationSender, signals);

		var renamer = new DocumentRenameCoordinator(Context, Scheduler, pendingCloses, Lifecycle, notificationSender, signals);
		var replay = new DocumentRestartReplayRunner(Context, Scheduler, notificationSender, signals);

		Synchronizer = new DocumentSynchronizer(
			new DocumentSynchronizerContext(Provider: Context, Scheduler, pendingCloses, Lifecycle, notificationSender, Closer, signals, renamer)
		);

		var releaseRunner = new DocumentRequestReleaseRunner(Context, Scheduler, pendingCloses, Closer);

		RequestPipeline = new DocumentRequestPipeline(Context, RequestRunner, Lifecycle, Synchronizer, releaseRunner);

		Completion = new CompletionProvider(RequestPipeline, Context, RequestRunner);
		Hover = new HoverProvider(RequestPipeline);
		Definition = new DefinitionProvider(RequestPipeline, options);
		References = new ReferenceProvider(RequestPipeline, options);
		Rename = new RenameProvider(RequestPipeline, options, logger);
		Formatting = new FormattingProvider(RequestPipeline);
		SignatureHelp = new SignatureHelpProvider(RequestPipeline);
		DocumentSymbols = new DocumentSymbolProvider(RequestPipeline, options, logger);
		WorkspaceSymbols = new WorkspaceSymbolProvider(Context, RequestRunner, Lifecycle);
		CodeActions = new CodeActionProvider(pipeline: RequestPipeline, Context, RequestRunner);

		var workspaceFactory = new WorkspaceWatchingFactory(
			options,
			watchSpecifications,
			workspaceFileWatcherFactory,
			workspaceSnapshotTrackerFactory,
			logger
		);

		var workspaceOwner = new WorkspaceChangeOwnerContext(
			ClientAccessor: () => client,
			Disposal,
			EnsureConnectionStartedAsync: Lifecycle.EnsureConnectionStartedAsync,
			MarkConnectionUnhealthy: Lifecycle.MarkConnectionUnhealthy,
			RaiseWorkspaceFileWatchFailed: Events.RaiseWorkspaceFileWatchFailed
		);

		var sender = new WorkspaceChangeSender(
			owner: workspaceOwner,
			isConfigurationPath: profile.Hooks.IsConfigurationPath,
			providerDisplayName,
			logger
		);

		WorkspaceChanges = new WorkspaceChangeDispatcher(
			new WorkspaceChangeDispatcherContext(
				workspaceRootDirectoryPaths,
				workspaceRootsDisplayText,
				providerDisplayName,
				watchSpecifications,
				FileWatcherFactory: workspaceFactory.CreateFileWatcher,
				FileSnapshotTrackerFactory: workspaceFactory.CreateSnapshotTracker,
				sender,
				Owner: workspaceOwner,
				ObserveBackgroundTask: Context.ObserveBackgroundTask,
				options,
				logger
			)
		);

		Lifecycle.Attach(replay, WorkspaceChanges);
	}

	/// <summary>The disposal handshake and the disposed flag every collaborator observes.</summary>
	internal ProviderLifetime Disposal { get; }

	/// <summary>The events the host subscribes to, which disposal drops.</summary>
	internal ProviderEvents Events { get; }

	/// <summary>The tracker of the documents the provider mirrors, with the payloads cached for them.</summary>
	internal DocumentTracker Documents { get; }

	/// <summary>The scheduler that serializes the operations of one document.</summary>
	internal DocumentOperationScheduler Scheduler { get; }

	/// <summary>The coordinator of startup and restart replay.</summary>
	internal ProviderLifecycleCoordinator Lifecycle { get; }

	/// <summary>The runner that applies the timeout and restart policy to every request.</summary>
	internal RequestRunner RequestRunner { get; }

	/// <summary>What the features and collaborators share.</summary>
	internal ProviderContext Context { get; }

	/// <summary>The source of the diagnostics.</summary>
	internal DiagnosticsSource DiagnosticsSource { get; }

	/// <summary>The source of the semantic tokens.</summary>
	internal SemanticTokensSource SemanticTokensSource { get; }

	/// <summary>The synchronizer that mirrors documents to the server.</summary>
	internal DocumentSynchronizer Synchronizer { get; }

	/// <summary>The closer that closes the server copy of a document once no lease keeps it open.</summary>
	internal DocumentCloser Closer { get; }

	/// <summary>The pipeline that sends a document-scoped request.</summary>
	internal DocumentRequestPipeline RequestPipeline { get; }

	/// <summary>The completion feature.</summary>
	internal CompletionProvider Completion { get; }

	/// <summary>The hover feature.</summary>
	internal HoverProvider Hover { get; }

	/// <summary>The definition feature.</summary>
	internal DefinitionProvider Definition { get; }

	/// <summary>The references feature.</summary>
	internal ReferenceProvider References { get; }

	/// <summary>The rename feature.</summary>
	internal RenameProvider Rename { get; }

	/// <summary>The formatting feature.</summary>
	internal FormattingProvider Formatting { get; }

	/// <summary>The signature-help feature.</summary>
	internal SignatureHelpProvider SignatureHelp { get; }

	/// <summary>The document-symbol feature.</summary>
	internal DocumentSymbolProvider DocumentSymbols { get; }

	/// <summary>The workspace-symbol feature.</summary>
	internal WorkspaceSymbolProvider WorkspaceSymbols { get; }

	/// <summary>The code-action feature.</summary>
	internal CodeActionProvider CodeActions { get; }

	/// <summary>The dispatcher that watches the workspace roots and forwards their changes to the server.</summary>
	internal WorkspaceChangeDispatcher WorkspaceChanges { get; }
}
