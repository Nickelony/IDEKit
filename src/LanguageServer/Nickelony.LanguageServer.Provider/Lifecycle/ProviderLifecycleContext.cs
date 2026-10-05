namespace Nickelony.LanguageServer.Provider;

/// <summary>The collaborators a <see cref="ProviderLifecycleCoordinator"/> is built from.</summary>
/// <param name="Provider">
/// The client, the tracked documents whose restart the coordinator replays, the disposal whose start ends its work, the tunables, the display name
/// and the logger, which every provider collaborator shares.
/// </param>
/// <param name="StartupState">The startup flags, counters and provider state the coordinator drives.</param>
/// <param name="Events">The events that report state changes to the host.</param>
/// <param name="FailureReporter">
/// The reporter that logs a startup failure and raises it to the host once per persistence until the next successful start.
/// </param>
/// <param name="TestHooks">The hooks that expose nondeterministic points in the startup path.</param>
/// <param name="WorkspaceRootsDisplayText">The workspace roots as one string for log text.</param>
/// <param name="Timeouts">The request timeout tracking that a successful startup resets for its connection generation.</param>
internal sealed record ProviderLifecycleContext(
	ProviderContext Provider,
	StartupState StartupState,
	ProviderEvents Events,
	StartupFailureReporter FailureReporter,
	LanguageServerFeatureProviderTestHooks TestHooks,
	string WorkspaceRootsDisplayText,
	RequestTimeoutCounter Timeouts
);
