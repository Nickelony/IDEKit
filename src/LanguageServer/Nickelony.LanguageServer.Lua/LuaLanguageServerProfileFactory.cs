using Nickelony.IDEKit.Core.Identifiers;

namespace Nickelony.LanguageServer.Lua;

/// <summary>Creates the profile that makes <see cref="LanguageServerFeatureProvider"/> serve Lua through LuaLS.</summary>
/// <remarks>
/// The provider owns the language-server client that it creates and the workspace watcher the framework creates;
/// see the repository's <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md">consumer integration guide</see>
/// for the construction, disposal, and threading contract.
/// </remarks>
public static class LuaLanguageServerProfileFactory
{
	// The Lua fallback message, and a word rule that extends the shared identifier rule with '.', ':', ''' and '"', so Lua member accesses and
	// quoted names resolve as one selectable run.
	private static readonly DiagnosticMappingPolicy s_diagnosticMappingPolicy = new()
	{
		UnknownMessage = "Unknown Lua diagnostic.",

		WordCharacters = IdentifierCharacterPolicy.Create(static character =>
			IdentifierCharacterPolicy.Default.IsPartCharacter(character) || character is '.' or ':' or '\'' or '"'
		),
	};

	/// <summary>The client capabilities LuaLS needs beyond the shared baseline.</summary>
	/// <remarks>
	/// The advertised code-action kinds mirror the kinds LuaLS itself serves. The provider maps kinds as opaque strings, so the set only bounds
	/// which literal code actions LuaLS may return. The <c>global</c> semantic-token modifier extends the LSP modifier set in lua-language-server,
	/// so this package declares it instead of the shared token vocabulary.
	/// </remarks>
	internal static LanguageServerClientCapabilitiesOptions ClientCapabilities { get; } =
		LanguageServerClientCapabilitiesOptions.Default with
		{
			CodeActionKinds = ["", "quickfix", "refactor.rewrite", "refactor.extract"],
			ExtraSemanticTokenModifiers = ["global"],
		};

	/// <summary>Creates the Lua profile.</summary>
	/// <param name="options">The LuaLS settings overrides to apply for this workspace, or <see langword="null"/> for the defaults.</param>
	/// <returns>
	/// A profile whose watch specifications mirror every Lua file and the LuaLS configuration files. The options apply to a client
	/// that the provider creates itself, not to one that the host supplies.
	/// </returns>
	public static LanguageServerProfile Create(LuaLanguageServerOptions? options = null)
	{
		LuaLanguageServerOptions luaOptions = options ?? LuaLanguageServerOptions.Default;

		return new LanguageServerProfile
		{
			LanguageId = "lua",
			LanguageDisplayName = "Lua",
			ServerDisplayName = "Lua language server",
			WatchSpecifications = WorkspaceConventions.WatchSpecifications,
			DiagnosticMappingPolicy = s_diagnosticMappingPolicy,
			ClientCapabilities = ClientCapabilities,

			Hooks = new LanguageServerProfileHooks
			{
				IsConfigurationPath = WorkspaceConventions.IsConfigurationPath,
				BuildSettings = () => SettingsBuilder.Build(luaOptions),
				BuildInitializationOptions = InitializationOptionsBuilder.Build,
			},
		};
	}
}
