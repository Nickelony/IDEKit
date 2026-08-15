namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Defines the file-watching engine that a workspace watch scope drives for one workspace root.
/// </summary>
/// <remarks>
/// <para>
/// The provider framework creates its watcher through the provider base class's <c>CreateWorkspaceFileWatcher</c>
/// hook, which is typed on this interface so a host can replace the watching engine (for example with a host file
/// API). A host can substitute the engine without subclassing by assigning a factory to
/// <see cref="LanguageServerProviderOptions.WorkspaceFileWatcherFactory"/>; the default hook implementation watches
/// through this package's <see cref="WorkspaceFileWatcher"/>.
/// </para>
/// <para>
/// Implementations report runtime failures through the callback supplied to the factory that created them: the
/// callback stays valid until the watcher is disposed. The framework starts and tracks a replacement watcher first
/// and disposes the failed watcher afterwards, so a failed watcher must tolerate the replacement being active
/// before its own disposal.
/// </para>
/// </remarks>
public interface IWorkspaceFileWatcher : IDisposable
{
	/// <summary>
	/// Starts watching the workspace root.
	/// </summary>
	/// <returns>
	/// The start result; <see cref="WorkspaceWatcherStartOutcome.Started"/> and
	/// <see cref="WorkspaceWatcherStartOutcome.AlreadyRunning"/> both count as a started watcher.
	/// </returns>
	/// <remarks>
	/// The framework calls this member at most once for a given watcher instance; a watcher that already runs should
	/// report <see cref="WorkspaceWatcherStartOutcome.AlreadyRunning"/>.
	/// </remarks>
	WorkspaceWatcherStartResult Start();
}
