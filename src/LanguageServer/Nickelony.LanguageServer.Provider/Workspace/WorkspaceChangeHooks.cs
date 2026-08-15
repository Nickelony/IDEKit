namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Groups the owner hooks the workspace change coordinator and its per-root watch scopes depend on,
/// so the constructors that consume them stay readable and positional mistakes between the delegate
/// parameters are impossible.
/// </summary>
/// <param name="ClientAccessor">Returns the active language-server client when available.</param>
/// <param name="IsDisposedAccessor">Returns whether the owner has been disposed.</param>
/// <param name="EnsureTransportStartedAsync">Ensures the transport is running on demand before forwarding file changes.</param>
/// <param name="TryMarkTransportUnhealthy">Attempts to mark one observed transport generation unhealthy after forwarding failures; the callback handles its own failure.</param>
/// <param name="RaiseWorkspaceWatcherFailed">Reports unrecoverable watcher failures to the owner.</param>
internal sealed record WorkspaceChangeHooks(
	Func<ILanguageServerClient?> ClientAccessor,
	Func<bool> IsDisposedAccessor,
	Func<CancellationToken, Task<bool>> EnsureTransportStartedAsync,
	Action<long> TryMarkTransportUnhealthy,
	Action<WorkspaceWatcherFailure> RaiseWorkspaceWatcherFailed);
