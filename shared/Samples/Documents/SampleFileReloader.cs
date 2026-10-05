using System.IO;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.FileWatching;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Reloading;

namespace Nickelony.Samples;

/// <summary>
/// Watches a folder and reloads each tracked document whose file changes on disk through a <see cref="FileReloadCoordinator{TDecision}"/>.
/// </summary>
/// <remarks>
/// A <see cref="FileWatchSupervisor"/> owns each watcher, so a watcher error restarts the watching and replays the changes it missed. The
/// watcher queues the changed paths and starts a pass without waiting for it, because a conflict prompt would stall the watcher and a failing
/// pass would count as a failed dispatch. The coordinator serializes the passes and drains every path queued while one runs.
/// </remarks>
internal sealed class SampleFileReloader : IAsyncDisposable
{
	private readonly FileReloadCoordinator<WorkspaceDocumentConflictResolutionMode?> _reloads;
	private readonly IWorkspaceDocumentReader _documents;
	private readonly Func<string, Task> _reportAsync;
	private readonly FileWatcherFactory _watcherFactory;
	private readonly List<FileWatchSupervisor> _supervisors = [];
	private readonly CancellationTokenSource _lifetime = new();

	private Task _lastPass = Task.CompletedTask;
	private volatile bool _isDisposed;

	/// <param name="documents">The store whose documents the hooks reload, whose path comparison the watchers share.</param>
	/// <param name="hooks">Reload a document, resolve a conflict and report a failed reload.</param>
	/// <param name="reportAsync">Reports a pass that failed outside the hooks, and a watcher that could not be restarted.</param>
	/// <param name="watcherFactory">Creates the watcher of a folder, or <see langword="null"/> for a <see cref="FileWatcher"/> over every file.</param>
	internal SampleFileReloader(
		IWorkspaceDocumentReader documents,
		FileReloadHooks<WorkspaceDocumentConflictResolutionMode?> hooks,
		Func<string, Task> reportAsync,
		FileWatcherFactory? watcherFactory = null
	)
	{
		_reloads = new FileReloadCoordinator<WorkspaceDocumentConflictResolutionMode?>(documents, hooks);
		_documents = documents;
		_reportAsync = reportAsync;
		_watcherFactory = watcherFactory ?? CreateWatcher;
	}

	/// <summary>Raised on a background thread after each batch of changes the watcher saw.</summary>
	internal event EventHandler? FilesChanged;

	/// <summary>Watches every file of a folder and its subfolders, and queues a tracked one for a reload when it changes on disk.</summary>
	/// <exception cref="IOException">The folder cannot be watched.</exception>
	internal async Task WatchFolderAsync(string folder)
	{
		FileWatchFailure? startFailure = null;

		var supervisor = new FileWatchSupervisor(
			new FileWatchSupervisorSpecification(
				WorkspaceRootDirectoryPath: LocalPath.Normalize(folder),
				DisplayName: "Sample",

				// The watcher coalesces a burst of events into one batch, so the sample needs no timer of its own.
				WatchSpecifications: [new FileWatchSpecification("*", IncludeSubdirectories: true)],

				FileWatcherFactory: _watcherFactory,
				FileSnapshotTrackerFactory: CreateTracker
			),

			new FileWatchSupervisorHooks
			{
				IsEnabled = () => true,
				IsDisposed = () => _isDisposed,

				ReportFailure = failure =>
				{
					startFailure = failure;
					_ = _reportAsync($"{failure.WorkspaceRootDirectoryPath} is not watched ({failure.Code}), so changes on disk are not reloaded.");
				},

				DispatchAsync = DispatchAsync,
				ObserveBackgroundTask = (task, description) => _ = ObserveBackgroundTaskAsync(task, description),
			}
		);

		_supervisors.Add(supervisor);
		await supervisor.EnsureWatcherStartedAsync();

		if (!supervisor.IsWatcherActive)
			throw new IOException($"{folder} could not be watched: {startFailure?.Code ?? "the folder is missing"}.", startFailure?.Exception);
	}

	public async ValueTask DisposeAsync()
	{
		_isDisposed = true;
		await _lifetime.CancelAsync();

		foreach (FileWatchSupervisor supervisor in _supervisors)
			supervisor.Dispose();

		await _lastPass;
		_lifetime.Dispose();
	}

	private FileWatcher CreateWatcher(
		string folder,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<IFileWatcher, Exception?> onWatcherFailed
	) =>
		new(
			folder,
			dispatchAsync,
			watchSpecifications: [new FileWatchSpecification("*", IncludeSubdirectories: true)],
			onWatcherFailed,
			pathComparison: _documents.Options.PathComparison
		);

	private FileSnapshotTracker CreateTracker(string folder, IReadOnlyList<FileWatchSpecification> specifications) =>
		new(folder, specifications, _documents.Options.PathComparison);

	private Task DispatchAsync(FileWatchSupervisor supervisor, FileChangeBatch batch, CancellationToken cancellationToken)
	{
		if (_isDisposed)
			return Task.CompletedTask;

		FilesChanged?.Invoke(this, EventArgs.Empty);

		// A deleted file has no content to reload; the coordinator drops a path the store does not track.
		_reloads.QueueFiles(batch.Entries.Where(change => change.Kind != FileChangeKind.Deleted).Select(change => change.Path));
		_lastPass = RunPassAsync();

		// The supervisor reconciles a restart against the changes applied to its snapshot.
		supervisor.ApplyTrackedChanges(batch.Entries);

		return Task.CompletedTask;
	}

	private async Task ObserveBackgroundTaskAsync(Task task, string description)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			await _reportAsync($"{description} failed: {exception.Message}");
		}
	}

	private async Task RunPassAsync()
	{
		try
		{
			await _reloads.ProcessQueuedFilesAsync(_lifetime.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// Disposal stops the pass with its queue intact.
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			await _reportAsync($"Reload failed: {exception.Message}");
		}
	}
}
