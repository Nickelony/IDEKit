using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Wires a <see cref="DocumentSynchronizer"/> over a fake client without a provider, and records the signals it raises.</summary>
internal sealed class SynchronizerTestHost : IDisposable
{
	private readonly object _syncRoot = new();
	private readonly List<string> _signals = [];

	internal SynchronizerTestHost(
		FakeLanguageServerClient? client = null,
		LanguageServerFeatureProviderOptions? providerOptions = null,
		ILogger? logger = null
	)
	{
		Client = client ?? new FakeLanguageServerClient { IsReady = false };

		LanguageServerFeatureProviderOptions options = providerOptions ?? LanguageServerFeatureProviderOptions.Default;

		// Every collaborator that compares paths reads the same options, as the provider composition builds them.
		Documents = new DocumentTracker(options);
		Scheduler = new DocumentOperationScheduler(options);
		PendingCloses = new PendingCloseTracker(options);

		Events = new ProviderEvents(new object(), Disposal, NullLogger.Instance);

		var startupState = new StartupState(Client, Disposal);

		var failureReporter = new StartupFailureReporter(
			new StartupFailureReporterContext(
				startupState,
				Events,
				WorkspaceRootsDisplayText: "workspace",
				ProviderDisplayName: "Test",
				TransientStartupFailureMessage: "start failed",
				PersistentStartupFailureMessage: "start failed",
				MissingClientFailureMessage: "no client",
				options,
				Logger: NullLogger.Instance
			)
		);

		var timeouts = new RequestTimeoutCounter(options.RequestTimeoutRestartThreshold);

		// The lifecycle writes to a no-op logger, so the log a test inspects holds only what the collaborator under test wrote.
		var lifecycleProvider = new ProviderContext(Client, Documents, Disposal, providerDisplayName: "Test", options, logger: NullLogger.Instance);

		Lifecycle = new ProviderLifecycleCoordinator(
			new ProviderLifecycleContext(
				Provider: lifecycleProvider,
				startupState,
				Events,
				failureReporter,
				TestHooks,
				WorkspaceRootsDisplayText: "workspace",
				timeouts
			)
		);

		Runner = new RequestRunner(
			new RequestRunnerContext(
				WorkspaceRootsDisplayText: "workspace",
				Client,
				options,
				Disposal,
				EnsureConnectionStartedAsync: Lifecycle.EnsureConnectionStartedAsync,
				timeouts,
				Logger: NullLogger.Instance
			)
		);

		Context = new ProviderContext(Client, Documents, Disposal, providerDisplayName: "Test", options, logger: logger ?? NullLogger.Instance);

		var signals = new DocumentSignals(
			Synchronized: document => Record($"synchronized:{Path.GetFileName(document.FilePath)}:v{document.Version}"),
			Invalidated: filePath => Record($"invalidated:{Path.GetFileName(filePath)}"),
			Renamed: filePath => Record($"moved:{Path.GetFileName(filePath)}")
		);

		var notificationSender = new DocumentNotificationSender(Context, languageId: static () => "test");

		Closer = new DocumentCloser(Context, Scheduler, PendingCloses, notificationSender, signals);
		ReleaseRunner = new DocumentRequestReleaseRunner(Context, Scheduler, PendingCloses, Closer);

		Replay = new DocumentRestartReplayRunner(Context, Scheduler, notificationSender, signals);
		Renamer = new DocumentRenameCoordinator(Context, Scheduler, PendingCloses, Lifecycle, notificationSender, signals);

		Synchronizer = new DocumentSynchronizer(
			new DocumentSynchronizerContext(Provider: Context, Scheduler, PendingCloses, Lifecycle, notificationSender, Closer, signals, Renamer)
		);

		Lifecycle.Attach(Replay, new StubWorkspaceWatchRestoration());
	}

	internal FakeLanguageServerClient Client { get; }

	internal ProviderLifetime Disposal { get; } = new();

	internal ProviderEvents Events { get; }

	internal DocumentTracker Documents { get; }

	internal LanguageServerFeatureProviderTestHooks TestHooks { get; } = new();

	internal ProviderLifecycleCoordinator Lifecycle { get; }

	internal ProviderContext Context { get; }

	internal RequestRunner Runner { get; }

	internal DocumentOperationScheduler Scheduler { get; }

	internal PendingCloseTracker PendingCloses { get; }

	internal DocumentCloser Closer { get; }

	internal DocumentRequestReleaseRunner ReleaseRunner { get; }

	internal DocumentSynchronizer Synchronizer { get; }

	internal DocumentRestartReplayRunner Replay { get; }

	internal DocumentRenameCoordinator Renamer { get; }

	/// <summary>The signals raised so far, in order, as <c>kind:file</c> entries; a synchronization also names the version.</summary>
	internal IReadOnlyList<string> Signals
	{
		get
		{
			lock (_syncRoot)
				return [.. _signals];
		}
	}

	private void Record(string signal)
	{
		lock (_syncRoot)
			_signals.Add(signal);
	}

	public void Dispose() => Client.Dispose();

	private sealed class StubWorkspaceWatchRestoration : IWorkspaceWatchRestoration
	{
		public Task RestoreWorkspaceWatchingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}
}
