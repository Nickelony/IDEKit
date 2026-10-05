namespace Nickelony.IDEKit.FileWatching;

/// <summary>Holds the hook that exposes the nondeterministic point in <see cref="FileWatcher"/>'s start path.</summary>
/// <remarks>
/// The type is internal and the hook defaults to <see langword="null"/>, so the default configuration installs no hook and the hook-guarded branch
/// is skipped.
/// </remarks>
internal sealed class FileWatcherTestHooks
{
	/// <summary>The shared hooks instance that installs no hook.</summary>
	internal static FileWatcherTestHooks None { get; } = new();

	/// <summary>
	/// The hook invoked for each file-system watcher after it is registered in the owned set and before it starts raising events, so a watcher error
	/// can be raised deterministically inside the activation window, or <see langword="null"/>.
	/// </summary>
	internal Action<FileSystemWatcher>? WatcherActivation { get; init; }
}
