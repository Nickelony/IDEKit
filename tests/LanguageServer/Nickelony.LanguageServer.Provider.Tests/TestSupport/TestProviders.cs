using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Creates providers over the test language, whose rules are deterministic, so tests drive the framework pipeline without a language package.
/// </summary>
internal static class TestProviders
{
	/// <summary>
	/// The exact message the test profile reports for a missing client. The embedded unique token lets a test prove the reported failure carries
	/// this profile's own message instead of a framework string that happens to match.
	/// </summary>
	internal static string MissingClientFailureMessage { get; } =
		"The test language server executable is unavailable (" + Guid.NewGuid().ToString("N") + ").";

	/// <summary>The language-specific watch pattern of the test language.</summary>
	internal static IReadOnlyList<FileWatchSpecification> WatchSpecifications { get; } =
	[new FileWatchSpecification("*.test", IncludeSubdirectories: true)];

	/// <summary>The source files and the configuration file of the test language, as a language package would declare them.</summary>
	internal static IReadOnlyList<FileWatchSpecification> WatchSpecificationsWithConfiguration { get; } =
	[new FileWatchSpecification("*.test", IncludeSubdirectories: true), new FileWatchSpecification(".testconfig", IncludeSubdirectories: false)];

	/// <summary>
	/// The profile of the test language: it maps diagnostics with the neutral policy and treats a <c>.test</c>-prefixed file name as configuration.
	/// </summary>
	internal static LanguageServerProfile Profile { get; } = CreateProfile();

	/// <summary>Creates a test-language profile, optionally with another configuration-file probe.</summary>
	/// <param name="isConfigurationPath">The configuration-file probe, or <see langword="null"/> for the <c>.test</c>-prefix rule.</param>
	/// <returns>The profile.</returns>
	internal static LanguageServerProfile CreateProfile(Func<string, bool>? isConfigurationPath = null)
	{
		return new LanguageServerProfile
		{
			LanguageId = "test",
			LanguageDisplayName = "Test",
			ServerDisplayName = "test language server",
			WatchSpecifications = WatchSpecifications,
			DiagnosticMappingPolicy = DiagnosticMappingPolicy.Default,

			Hooks = new LanguageServerProfileHooks
			{
				IsConfigurationPath = isConfigurationPath ?? (static path => Path.GetFileName(path).StartsWith(".test", StringComparison.Ordinal)),
				BuildSettings = LanguageServerProfileHooks.Default.BuildSettings,
				BuildInitializationOptions = LanguageServerProfileHooks.Default.BuildInitializationOptions,
			},

			TransientStartupFailureMessage = "The test language server failed to start.",
			PersistentStartupFailureMessage = "The test language server failed to start repeatedly.",
			MissingClientFailureMessage = MissingClientFailureMessage,
		};
	}

	/// <summary>Creates a provider that drives <paramref name="client"/>.</summary>
	/// <param name="workspaceRootDirectoryPaths">The workspace root directories.</param>
	/// <param name="client">The language-server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for the defaults.</param>
	/// <param name="workspaceFileWatcherFactory">A workspace file watcher factory that replaces the default.</param>
	/// <param name="workspaceSnapshotTrackerFactory">A workspace snapshot tracker factory that replaces the default.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <param name="profile">The profile, or <see langword="null"/> for <see cref="Profile"/>.</param>
	/// <param name="testHooks">The observers of the interleaving points, or <see langword="null"/> for none.</param>
	/// <returns>The provider, which owns <paramref name="client"/>.</returns>
	internal static LanguageServerFeatureProvider Create(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		LanguageServerFeatureProviderOptions? options = null,
		FileWatcherFactory? workspaceFileWatcherFactory = null,
		FileSnapshotTrackerFactory? workspaceSnapshotTrackerFactory = null,
		ILogger? logger = null,
		LanguageServerProfile? profile = null,
		LanguageServerFeatureProviderTestHooks? testHooks = null
	)
	{
		return new LanguageServerFeatureProvider(
			profile: profile ?? Profile,
			workspaceRootDirectoryPaths,
			client,
			options,
			logger,
			workspaceFileWatcherFactory,
			workspaceSnapshotTrackerFactory,
			testHooks
		);
	}

	/// <summary>
	/// Creates a provider whose workspace file watchers come from <paramref name="watchers"/> and watch the sources and the configuration file
	/// of the test language.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">The workspace root directories.</param>
	/// <param name="client">The language-server client, which the provider owns.</param>
	/// <param name="watchers">The factory that records the watchers the provider creates.</param>
	/// <returns>The provider.</returns>
	internal static LanguageServerFeatureProvider CreateWatched(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient client,
		RecordingFileWatcherFactory watchers
	)
	{
		return Create(
			workspaceRootDirectoryPaths,
			client,
			workspaceFileWatcherFactory: watchers.Factory,

			profile: CreateProfile() with
			{
				WatchSpecifications = WatchSpecificationsWithConfiguration,
			}
		);
	}
}
