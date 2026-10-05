using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.FileWatching;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.Workspace.QuickPick.Tests;

/// <summary>
/// A folder of files on disk with the pieces of the quick-open file list wired over it: a recording factory in place of the real file watchers,
/// an enumerator a test can wrap, a clock a test moves, and the faults the list reported.
/// </summary>
/// <remarks>
/// A test writes and deletes files on disk and then delivers the report a watcher would have made, so the list changes without a timer
/// and without the real watcher.
/// </remarks>
internal sealed class WorkspaceFileTestHost : IDisposable
{
	private readonly List<WorkspaceFileTracker> _trackers = [];

	public WorkspaceFileTestHost() => Root = Folder.Path;

	public static LocalPathComparisonPolicy Comparison { get; } = LocalPathComparisonPolicy.ForCurrentPlatform;

	public static QuickPickGroup FileGroup { get; } = new("files", "file results", order: 1);

	/// <summary>The folder that holds the files, which is the only root unless a test passes others.</summary>
	public string Root { get; }

	public TemporaryDirectory Folder { get; } = new("quickpick-files-");

	/// <summary>The watchers the list created, which a test delivers reports through.</summary>
	public RecordingFileWatcherFactory Watchers { get; } = new();

	/// <summary>The clock that measures the retry interval of a watcher, which a test moves.</summary>
	public FakeTimeProvider Clock { get; } = new();

	/// <summary>The faults the list reported.</summary>
	public List<Exception> Faults { get; } = [];

	/// <summary>Enumerates the files below a folder; defaults to the real walk, and a test wraps it to count or hold the walk.</summary>
	public WorkspaceFileEnumerator Enumerator { get; set; } = WorkspaceFileWalker.List;

	/// <summary>Gets the full path of a path below the folder, written with slashes.</summary>
	public string PathOf(string relativePath) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relativePath));

	/// <summary>Writes a file below the folder, creating the folders above it.</summary>
	public string Write(string relativePath, string content = "text")
	{
		string path = PathOf(relativePath);

		System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);

		return path;
	}

	/// <summary>Creates a tracker over the folder, or over the given roots, that disposal of the host disposes.</summary>
	public WorkspaceFileTracker CreateTracker(
		IReadOnlyList<string>? roots = null,
		IReadOnlyList<string>? excludeGlobs = null,
		int maxFileCount = 50_000,
		QuickPickGroup? fileGroup = null
	)
	{
		var tracker = new WorkspaceFileTracker(
			rootPaths: roots ?? [Root],
			filter: new WorkspaceFileFilter(excludeGlobs ?? [], Comparison),
			maxFileCount,
			createWatcher: Watchers.Factory,
			enumerate: (directory, root, filter, cancellationToken) => Enumerator(directory, root, filter, cancellationToken),
			pathComparison: Comparison,
			fileGroup: fileGroup ?? FileGroup,
			timeProvider: Clock,
			reportFault: Faults.Add
		);

		_trackers.Add(tracker);

		return tracker;
	}

	/// <summary>Creates a provider over a new tracker, with the given hooks.</summary>
	/// <param name="openFileAsync">Opens a file; defaults to doing nothing.</param>
	/// <param name="listOpenEditors">Lists the open documents, or <see langword="null"/> for none.</param>
	/// <param name="excludeGlobs">The globs of the options, unless <paramref name="options"/> is given.</param>
	/// <param name="maxFileCount">The bound of the options, unless <paramref name="options"/> is given.</param>
	/// <param name="options">The options, whose globs and bound the tracker takes.</param>
	public WorkspaceFileQuickPickProvider CreateProvider(
		Func<WorkspaceFileOpenRequest, CancellationToken, Task>? openFileAsync = null,
		Func<IReadOnlyList<OpenEditorDescriptor>>? listOpenEditors = null,
		IReadOnlyList<string>? excludeGlobs = null,
		int maxFileCount = 50_000,
		WorkspaceFileQuickPickOptions? options = null
	)
	{
		options ??= new WorkspaceFileQuickPickOptions { ExcludeGlobs = excludeGlobs ?? [], MaxFileCount = maxFileCount };

		return new(
			tracker: CreateTracker(
				excludeGlobs: options.ExcludeGlobs,
				maxFileCount: options.MaxFileCount,
				fileGroup: new QuickPickGroup("files", options.FileGroupLabel, order: 1)
			),

			openFileAsync: openFileAsync ?? ((_, _) => Task.CompletedTask),
			listOpenEditors,
			options,
			reportFault: Faults.Add
		);
	}

	/// <summary>Delivers the report of the watcher of a root, as the watcher does after its debounce.</summary>
	/// <param name="root">The root whose newest watcher reports.</param>
	/// <param name="changes">The changes of the batch.</param>
	public Task DeliverAsync(string root, params FileChange[] changes)
	{
		RecordingFileWatcherFactory.Creation creation = Watchers.CreationsFor(root).Last(creation => creation.Watcher is not null);
		return creation.DispatchAsync(new FileChangeBatch(changes), CancellationToken.None);
	}

	public Task DeliverAsync(params FileChange[] changes) => DeliverAsync(Root, changes);

	/// <summary>Gets the full paths of the files in the list.</summary>
	public static string[] PathsOf(WorkspaceFileTracker tracker) => [.. tracker.Capture().Items.Select(item => item.Key)];

	public void Dispose()
	{
		foreach (WorkspaceFileTracker tracker in _trackers)
			tracker.Dispose();

		Folder.Dispose();
	}
}
