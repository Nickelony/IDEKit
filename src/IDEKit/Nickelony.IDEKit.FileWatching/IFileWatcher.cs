namespace Nickelony.IDEKit.FileWatching;

/// <summary>Defines the file-watching engine that a watch supervisor drives for one workspace root.</summary>
/// <remarks>
/// <para>
/// A test replaces the watching engine by returning an implementation of this interface from a <see cref="FileWatcherFactory"/>.
/// </para>
/// <para>
/// Implementations report runtime failures through the callback supplied to the factory that created them: the callback stays
/// valid until the watcher is disposed. The supervisor starts and tracks a replacement watcher first and disposes the failed watcher afterwards,
/// so a failed watcher must tolerate the replacement being active before its own disposal.
/// </para>
/// </remarks>
public interface IFileWatcher : IDisposable
{
	/// <summary>Starts watching the workspace root.</summary>
	/// <returns>
	/// The start result; <see cref="FileWatcherStartOutcome.Started"/> and <see cref="FileWatcherStartOutcome.AlreadyRunning"/> count
	/// as a started watcher.
	/// </returns>
	/// <remarks>
	/// The supervisor calls this member at most once for a given watcher instance; a watcher that already runs
	/// should report <see cref="FileWatcherStartOutcome.AlreadyRunning"/>.
	/// </remarks>
	FileWatcherStartResult Start();
}
