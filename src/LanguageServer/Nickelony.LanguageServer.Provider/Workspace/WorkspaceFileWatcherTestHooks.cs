namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Holds the hooks that expose the non-deterministic points in <see cref="WorkspaceFileWatcher"/>'s start path.
/// </summary>
/// <remarks>
/// The type is internal and each hook defaults to <see langword="null"/>, so the default configuration installs no
/// hook and every hook-guarded branch is skipped.
/// </remarks>
internal sealed class WorkspaceFileWatcherTestHooks
{
	/// <summary>
	/// Gets the shared hooks instance that installs no hook.
	/// </summary>
	public static WorkspaceFileWatcherTestHooks None { get; } = new();

	/// <summary>
	/// Gets the hook invoked for each file-system watcher after it is registered in the owned set and before it
	/// starts raising events, so a test can raise a watcher error deterministically inside the activation window, or
	/// <see langword="null"/>.
	/// </summary>
	public Action<FileSystemWatcher>? WatcherActivation { get; init; }

	/// <summary>
	/// Gets the hook that replaces the built-in file-system watcher factory, so a test can make watcher creation
	/// fail deterministically, or <see langword="null"/> to use the built-in factory.
	/// </summary>
	public Func<string, WorkspaceWatchSpecification, FileSystemWatcher>? FileSystemWatcherFactory { get; init; }
}
