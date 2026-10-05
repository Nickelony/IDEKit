namespace Nickelony.LanguageServer.Provider;

/// <summary>The owner callbacks that the change forwarding and the per-root watch supervisors are built from.</summary>
/// <param name="ClientAccessor">Returns the active language-server client when available.</param>
/// <param name="Disposal">The disposal of the owner, whose start stops forwarding.</param>
/// <param name="EnsureConnectionStartedAsync">Ensures the connection is running on demand before forwarding file changes.</param>
/// <param name="MarkConnectionUnhealthy">
/// Attempts to mark one observed connection generation unhealthy after forwarding failures; the callback handles its own failure.
/// </param>
/// <param name="RaiseWorkspaceFileWatchFailed">Reports unrecoverable watcher failures to the owner.</param>
internal sealed record WorkspaceChangeOwnerContext(
	Func<ILanguageServerClient?> ClientAccessor,
	ProviderLifetime Disposal,
	Func<CancellationToken, Task<bool>> EnsureConnectionStartedAsync,
	Action<long> MarkConnectionUnhealthy,
	Action<WorkspaceFileWatchFailure> RaiseWorkspaceFileWatchFailed
);
