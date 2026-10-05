using Nickelony.IDEKit.Core.Identifiers;

namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// Creates the profile that makes <see cref="LanguageServerFeatureProvider"/> serve C# or Visual Basic through the Roslyn language server.
/// </summary>
/// <remarks>
/// Each provider serves one language, so a workspace that holds both languages uses one provider and one profile per language.
/// The repository's <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md">consumer integration guide</see>
/// describes the construction, disposal, and threading contract.
/// </remarks>
public static class RoslynLanguageServerProfileFactory
{
	// The fallback message, and the shared identifier rule extended with '.', so a dotted member access resolves as one selectable run. The
	// Roslyn language server reports precise ranges, so the extension only widens the degenerate-range fallback.
	private static readonly DiagnosticMappingPolicy s_diagnosticMappingPolicy = new()
	{
		UnknownMessage = "Unknown diagnostic.",

		WordCharacters = IdentifierCharacterPolicy.Create(static character =>
			IdentifierCharacterPolicy.Default.IsPartCharacter(character) || character == '.'
		),
	};

	// The Roslyn language server reads its options from the configuration push and the workspace/configuration requests, which the profile derives
	// from its own options rather than from a workspace file, so no change to a workspace file can invalidate the settings. A change to a watched
	// build input is still forwarded to the server, which reloads the affected projects itself.
	private static readonly Func<string, bool> s_isConfigurationPath = static _ => false;

	/// <summary>The client capabilities the Roslyn language server needs beyond the shared baseline.</summary>
	/// <remarks>
	/// The advertised set is bounded by what the shared provider framework consumes end to end. The code-action kinds mirror the quickfix,
	/// refactor and source kinds the Roslyn language server serves. The provider maps kinds as opaque strings and applies the literal action's edits,
	/// so the set bounds which actions the server may return and excludes no kind the provider can apply.
	/// </remarks>
	internal static LanguageServerClientCapabilitiesOptions ClientCapabilities { get; } =
		LanguageServerClientCapabilitiesOptions.Default with
		{
			CodeActionKinds =
			[
				"",
				"quickfix",
				"refactor",
				"refactor.extract",
				"refactor.inline",
				"refactor.rewrite",
				"source",
				"source.organizeImports",
				"source.fixAll",
			],

			SupportsPullDiagnostics = true,
		};

	/// <summary>Creates the profile of a shipped language.</summary>
	/// <param name="language">The language that the profile serves.</param>
	/// <param name="options">The Roslyn session options to apply, or <see langword="null"/> for the defaults.</param>
	/// <returns>
	/// A profile whose watch specifications are the language's own source and project patterns followed by the shared build inputs. The options apply
	/// to a client that the provider creates itself, not to one that the host supplies.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="language"/> is not a defined value.</exception>
	public static LanguageServerProfile Create(RoslynLanguage language, RoslynLanguageServerOptions? options = null) =>
		Create(RoslynLanguageDescriptor.For(language), options);

	/// <summary>Creates the profile of a language that a descriptor describes.</summary>
	/// <param name="descriptor">The language to serve.</param>
	/// <param name="options">The Roslyn session options to apply, or <see langword="null"/> for the defaults.</param>
	/// <returns>The profile.</returns>
	internal static LanguageServerProfile Create(RoslynLanguageDescriptor descriptor, RoslynLanguageServerOptions? options = null)
	{
		RoslynLanguageServerOptions serverOptions = options ?? RoslynLanguageServerOptions.Default;
		string display = descriptor.DisplayName;

		return new LanguageServerProfile
		{
			LanguageId = descriptor.LanguageId,
			LanguageDisplayName = display,
			ServerDisplayName = "Roslyn language server",
			WatchSpecifications = WorkspaceConventions.ComposeWatchSpecifications(descriptor.WatchSpecifications),
			DiagnosticMappingPolicy = s_diagnosticMappingPolicy,
			ClientCapabilities = ClientCapabilities,
			Launch = StdioLanguageServerTransportOptions.Default with { ServerArguments = ServerArgumentsBuilder.Build(serverOptions) },

			Hooks = new LanguageServerProfileHooks
			{
				IsConfigurationPath = s_isConfigurationPath,
				BuildSettings = () => SettingsBuilder.Build(serverOptions),
				BuildInitializationOptions = () => InitializationOptionsBuilder.Build(serverOptions),
				BuildSessionStartNotifications = roots => ProjectLoadNotificationBuilder.Build(descriptor, serverOptions, roots),
			},
		};
	}
}
