namespace Nickelony.LanguageServer.Provider;

/// <summary>The collaborators and values a <see cref="RequestRunner"/> is built from.</summary>
/// <param name="WorkspaceRootsDisplayText">
/// The comma-joined normalized workspace root paths used in log text, or the folderless marker when there are none.
/// </param>
/// <param name="Client">The language-server client, or <see langword="null"/> when no server is configured.</param>
/// <param name="Options">The provider tunables that carry the request-timeout policy.</param>
/// <param name="Disposal">The disposal of the owning provider, whose start and token the runner observes.</param>
/// <param name="EnsureConnectionStartedAsync">Ensures the connection is running before the single retry attempt.</param>
/// <param name="Timeouts">The tracker of the consecutive request timeouts, which decides when a timeout-driven restart is due.</param>
/// <param name="Logger">The logger instance.</param>
internal sealed record RequestRunnerContext(
	string WorkspaceRootsDisplayText,
	ILanguageServerClient? Client,
	LanguageServerFeatureProviderOptions Options,
	ProviderLifetime Disposal,
	Func<CancellationToken, Task<bool>> EnsureConnectionStartedAsync,
	RequestTimeoutCounter Timeouts,
	ILogger Logger
);
