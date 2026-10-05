using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.FileWatching;

namespace Nickelony.Testing;

/// <summary>
/// Records every watcher creation and hands out the queued watchers, or a default watcher: a <see cref="StubFileWatcher"/> unless the test switched
/// the root to a real <see cref="FileWatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// A creation records the root, the dispatch delegate and the failure callback the framework passed, so a test can deliver a batch or a failure
/// the way the watcher of that root would. A queued exception makes the next creation for its root throw, and <see cref="ThrowOnCreateFor"/> makes
/// every later creation for a root throw.
/// </para>
/// <para>
/// The default watcher of a root is passive, so a test controls every change the framework sees: the watcher never observes the file system,
/// and a change it would report before a replacement capture could see it cannot empty a reconciliation delta. A test that needs real file-system
/// events switches a root, or every root, to a real watcher with <see cref="UseRealWatcher"/> or <see cref="RealWatcherSpecifications"/>.
/// </para>
/// </remarks>
internal sealed class RecordingFileWatcherFactory
{
	private readonly object _syncRoot = new();
	private readonly List<Creation> _creations = [];

	private readonly Dictionary<string, Queue<object>> _scripts = new(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer);
	private readonly HashSet<string> _throwOnCreate = new(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer);

	// A root with an entry takes it over RealWatcherSpecifications; a null entry is a root that stays passive.
	private readonly Dictionary<string, IReadOnlyList<FileWatchSpecification>?> _rootWatchers = new(
		LocalPathComparisonPolicy.ForCurrentPlatform.Comparer
	);

	/// <summary>
	/// The watch specifications of the real watchers that every root without its own switch gets, or <see langword="null"/> for passive watchers.
	/// </summary>
	public IReadOnlyList<FileWatchSpecification>? RealWatcherSpecifications { get; set; }

	/// <summary>
	/// Whether a passive watcher answers <see cref="FileWatcherStartOutcome.WorkspaceRootMissing"/> to its start while its root does not exist,
	/// as the real watcher does, instead of starting regardless.
	/// </summary>
	public bool PassiveWatchersReportAMissingRoot { get; set; }

	/// <summary>
	/// The failure that the start of every passive watcher reports, as the real watcher does when its file-system watcher cannot be created,
	/// or <see langword="null"/> for starts that succeed.
	/// </summary>
	public Exception? PassiveWatcherStartupFailure { get; set; }

	/// <summary>A stable snapshot of every creation, in call order, including the ones that threw.</summary>
	public IReadOnlyList<Creation> Creations
	{
		get
		{
			lock (_syncRoot)
				return [.. _creations];
		}
	}

	/// <summary>The factory the supervisor, the coordinator context or the provider takes.</summary>
	public FileWatcherFactory Factory => Create;

	/// <summary>Switches one root to real watchers, which observe the file system for the given specifications.</summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root whose later creations return a real watcher.</param>
	/// <param name="watchSpecifications">The specifications the real watcher watches.</param>
	public void UseRealWatcher(string workspaceRootDirectoryPath, IReadOnlyList<FileWatchSpecification> watchSpecifications)
	{
		lock (_syncRoot)
			_rootWatchers[workspaceRootDirectoryPath] = watchSpecifications;
	}

	/// <summary>Keeps one root on passive watchers even when <see cref="RealWatcherSpecifications"/> switches the other roots to real ones.</summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root whose later creations return a passive watcher.</param>
	public void UsePassiveWatcher(string workspaceRootDirectoryPath)
	{
		lock (_syncRoot)
			_rootWatchers[workspaceRootDirectoryPath] = null;
	}

	/// <summary>Queues the watcher the next creation for a root returns.</summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root whose next creation returns the watcher.</param>
	/// <param name="watcher">The watcher to return.</param>
	/// <returns>The queued watcher.</returns>
	public StubFileWatcher Enqueue(string workspaceRootDirectoryPath, StubFileWatcher watcher)
	{
		Script(workspaceRootDirectoryPath, watcher);
		return watcher;
	}

	/// <summary>Queues a failure that makes the next creation for a root throw.</summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root whose next creation throws.</param>
	/// <param name="exception">The exception to throw.</param>
	public void EnqueueFailure(string workspaceRootDirectoryPath, Exception exception) => Script(workspaceRootDirectoryPath, exception);

	/// <summary>Makes every later creation for a root throw, simulating a root whose recovery fails.</summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root whose creations throw.</param>
	public void ThrowOnCreateFor(string workspaceRootDirectoryPath)
	{
		lock (_syncRoot)
			_throwOnCreate.Add(workspaceRootDirectoryPath);
	}

	/// <summary>Returns the creations for one root, in call order.</summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root to filter by.</param>
	/// <returns>The creations whose root equals <paramref name="workspaceRootDirectoryPath"/>.</returns>
	public IReadOnlyList<Creation> CreationsFor(string workspaceRootDirectoryPath)
	{
		StringComparer comparer = LocalPathComparisonPolicy.ForCurrentPlatform.Comparer;
		return [.. Creations.Where(creation => comparer.Equals(creation.WorkspaceRootDirectoryPath, workspaceRootDirectoryPath))];
	}

	/// <summary>
	/// Returns the latest creation for one root that returned a watcher, which carries the dispatch delegate and the failure callback in use.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root to look up.</param>
	/// <returns>The latest creation that did not throw.</returns>
	/// <exception cref="InvalidOperationException">No creation for the root returned a watcher.</exception>
	public Creation LatestFor(string workspaceRootDirectoryPath)
	{
		return CreationsFor(workspaceRootDirectoryPath).LastOrDefault(creation => creation.Watcher is not null)
			?? throw new InvalidOperationException($"No watcher was created for '{workspaceRootDirectoryPath}' yet.");
	}

	/// <summary>Returns the passive watcher of a root's latest creation, or <see langword="null"/> when there is none or it was disposed.</summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root to look up.</param>
	/// <returns>The watcher that is still in use for the root.</returns>
	public StubFileWatcher? GetActiveWatcher(string workspaceRootDirectoryPath)
	{
		IFileWatcher? latest = CreationsFor(workspaceRootDirectoryPath).LastOrDefault(creation => creation.Watcher is not null)?.Watcher;
		return latest is StubFileWatcher { IsDisposed: false } watcher ? watcher : null;
	}

	/// <summary>Reports a failure of one watcher the factory returned, through the callback the framework registered with it.</summary>
	/// <param name="watcher">The watcher that fails, which can be one the framework already replaced.</param>
	/// <param name="exception">The failure to report, or <see langword="null"/> for none.</param>
	/// <exception cref="InvalidOperationException">The factory returned no such watcher.</exception>
	public void Fail(IFileWatcher watcher, Exception? exception)
	{
		Creation creation =
			Creations.FirstOrDefault(creation => ReferenceEquals(creation.Watcher, watcher))
			?? throw new InvalidOperationException("The factory returned no such watcher, so no failure callback is registered for it.");

		creation.OnWatcherFailed(watcher, exception);
	}

	private IFileWatcher Create(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<IFileWatcher, Exception?> onWatcherFailed
	)
	{
		object? scripted = null;
		bool throwOnCreate;
		IReadOnlyList<FileWatchSpecification>? realWatcherSpecifications;

		lock (_syncRoot)
		{
			if (_scripts.TryGetValue(workspaceRootDirectoryPath, out Queue<object>? queue) && queue.Count > 0)
				scripted = queue.Dequeue();

			throwOnCreate = _throwOnCreate.Contains(workspaceRootDirectoryPath);

			realWatcherSpecifications = _rootWatchers.TryGetValue(
				workspaceRootDirectoryPath,
				out IReadOnlyList<FileWatchSpecification>? rootSpecifications
			)
				? rootSpecifications
				: RealWatcherSpecifications;
		}

		if (scripted is Exception exception)
		{
			Record(new Creation(workspaceRootDirectoryPath, Watcher: null, dispatchAsync, onWatcherFailed));
			throw exception;
		}

		if (throwOnCreate)
		{
			Record(new Creation(workspaceRootDirectoryPath, Watcher: null, dispatchAsync, onWatcherFailed));
			throw new InvalidOperationException("Simulated watcher factory failure during recovery.");
		}

		IFileWatcher watcher =
			scripted as IFileWatcher
			?? (
				realWatcherSpecifications is null
					? CreatePassiveWatcher(workspaceRootDirectoryPath)
					: new FileWatcher(workspaceRootDirectoryPath, dispatchAsync, watchSpecifications: realWatcherSpecifications, onWatcherFailed)
			);

		Record(new Creation(workspaceRootDirectoryPath, watcher, dispatchAsync, onWatcherFailed));

		return watcher;
	}

	private StubFileWatcher CreatePassiveWatcher(string workspaceRootDirectoryPath)
	{
		return new StubFileWatcher
		{
			RequiredDirectory = PassiveWatchersReportAMissingRoot ? workspaceRootDirectoryPath : null,

			StartResult = PassiveWatcherStartupFailure is null
				? new FileWatcherStartResult(FileWatcherStartOutcome.Started, null)
				: new FileWatcherStartResult(FileWatcherStartOutcome.StartupFailed, PassiveWatcherStartupFailure),
		};
	}

	private void Script(string workspaceRootDirectoryPath, object scripted)
	{
		lock (_syncRoot)
		{
			if (!_scripts.TryGetValue(workspaceRootDirectoryPath, out Queue<object>? queue))
			{
				queue = new Queue<object>();
				_scripts.Add(workspaceRootDirectoryPath, queue);
			}

			queue.Enqueue(scripted);
		}
	}

	private void Record(Creation creation)
	{
		lock (_syncRoot)
			_creations.Add(creation);
	}

	/// <summary>Describes one call of the factory.</summary>
	/// <param name="WorkspaceRootDirectoryPath">The root the watcher was created for.</param>
	/// <param name="Watcher">The watcher the call returned, or <see langword="null"/> when the call threw.</param>
	/// <param name="DispatchAsync">The delegate the watcher forwards its coalesced batches through.</param>
	/// <param name="OnWatcherFailed">The callback the watcher reports its failures through.</param>
	public sealed record Creation(
		string WorkspaceRootDirectoryPath,
		IFileWatcher? Watcher,
		Func<FileChangeBatch, CancellationToken, Task> DispatchAsync,
		Action<IFileWatcher, Exception?> OnWatcherFailed
	)
	{
		/// <summary>Reports a failure of the watcher this call returned, through the callback the framework registered with it.</summary>
		/// <param name="exception">The failure to report, or <see langword="null"/> for none.</param>
		/// <exception cref="InvalidOperationException">The call threw, so there is no watcher to fail.</exception>
		public void Fail(Exception? exception) =>
			OnWatcherFailed(Watcher ?? throw new InvalidOperationException("The creation threw, so it has no watcher to fail."), exception);
	}
}
