namespace Nickelony.LanguageServer.Provider;

/// <summary>The collaborators and values a <see cref="StartupFailureReporter"/> is built from.</summary>
/// <param name="StartupState">The startup flags that claim the one report of each persistence.</param>
/// <param name="Events">The events that report a startup failure to the host.</param>
/// <param name="WorkspaceRootsDisplayText">The workspace roots as one string for log text.</param>
/// <param name="ProviderDisplayName">The provider display name used in log text.</param>
/// <param name="TransientStartupFailureMessage">The message of a transient startup failure, or a blank value to report none.</param>
/// <param name="PersistentStartupFailureMessage">The message of a persistent startup failure, or a blank value to report none.</param>
/// <param name="MissingClientFailureMessage">
/// The message of the failure that reports a provider with no client, or a blank value to report none.
/// </param>
/// <param name="Options">The provider tunables, whose failure threshold the log text names.</param>
/// <param name="Logger">The logger instance.</param>
internal sealed record StartupFailureReporterContext(
	StartupState StartupState,
	ProviderEvents Events,
	string WorkspaceRootsDisplayText,
	string ProviderDisplayName,
	string? TransientStartupFailureMessage,
	string? PersistentStartupFailureMessage,
	string? MissingClientFailureMessage,
	LanguageServerFeatureProviderOptions Options,
	ILogger Logger
);
