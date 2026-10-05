using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Builds the provider context a collaborator test needs, without a provider.</summary>
internal static class TestProviderContext
{
	/// <summary>Creates a context over a fresh tracker.</summary>
	/// <param name="client">The language-server client, or <see langword="null"/> for none.</param>
	/// <param name="logger">The logger the collaborators under test write to, or <see langword="null"/> for a no-op logger.</param>
	/// <returns>A context whose display name is <c>Test</c>.</returns>
	internal static ProviderContext Create(ILanguageServerClient? client, ILogger? logger = null) => CreateWithRunner(client, logger).Context;

	/// <summary>Creates a context over a fresh tracker, and the request runner that never restarts the connection.</summary>
	/// <param name="client">The language-server client, or <see langword="null"/> for none.</param>
	/// <param name="logger">The logger the collaborators under test write to, or <see langword="null"/> for a no-op logger.</param>
	/// <returns>A context whose display name is <c>Test</c>, and a runner over the same client and disposal.</returns>
	internal static (ProviderContext Context, RequestRunner Runner) CreateWithRunner(ILanguageServerClient? client, ILogger? logger = null)
	{
		var documents = new DocumentTracker();
		var disposal = new ProviderLifetime();

		var runner = new RequestRunner(
			new RequestRunnerContext(
				WorkspaceRootsDisplayText: @"C:\Workspace",
				client,
				Options: LanguageServerFeatureProviderOptions.Default,
				disposal,
				EnsureConnectionStartedAsync: _ => Task.FromResult(true),
				Timeouts: new RequestTimeoutCounter(LanguageServerFeatureProviderOptions.Default.RequestTimeoutRestartThreshold),
				Logger: NullLogger.Instance
			)
		);

		var context = new ProviderContext(
			client,
			documents,
			disposal,
			providerDisplayName: "Test",
			options: LanguageServerFeatureProviderOptions.Default,
			logger: logger ?? NullLogger.Instance
		);

		return (context, runner);
	}
}
