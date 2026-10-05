namespace Nickelony.LanguageServer.Provider;

/// <summary>Brings workspace watching back after a start succeeded.</summary>
internal interface IWorkspaceWatchRestoration
{
	/// <summary>Starts the workspace file watcher when it is not running and forwards the changes buffered while it was down.</summary>
	/// <param name="cancellationToken">A token that can cancel the forwarding.</param>
	Task RestoreWorkspaceWatchingAsync(CancellationToken cancellationToken);
}
