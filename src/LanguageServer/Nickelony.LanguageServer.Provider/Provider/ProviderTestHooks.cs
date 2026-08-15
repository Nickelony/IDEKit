namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Groups the internal hooks that expose the nondeterministic points in the
/// <see cref="LanguageServerIntelliSenseProviderBase"/> startup path.
/// </summary>
/// <remarks>
/// The hooks add no public surface. Production leaves every hook unset and the hook-guarded branches are skipped.
/// </remarks>
internal sealed class ProviderTestHooks
{
	/// <summary>
	/// Gets or sets the observer invoked as a caller reaches the provider's shared startup lock, or
	/// <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// A caller parked on the shared startup lock has no behavior-level oracle, so the observer provides a
	/// deterministic synchronization point for a contended startup.
	/// </remarks>
	internal Action? StartupLockWaitObserver { get; set; }
}
