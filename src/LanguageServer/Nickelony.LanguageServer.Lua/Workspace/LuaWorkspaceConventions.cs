namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Describes the workspace file patterns and configuration paths the Lua provider mirrors to the language server.
/// </summary>
internal static class LuaWorkspaceConventions
{
	internal const string LuaConfigurationFilePrefix = ".luarc.";

	internal const string LuaConfigurationFilePattern = LuaConfigurationFilePrefix + "*";

	internal const string LuaFilePattern = "*.lua";

	/// <summary>
	/// The watch specifications mirrored to the language server: every Lua file in the workspace tree
	/// and every LuaLS configuration file in the workspace root.
	/// </summary>
	/// <remarks>
	/// LuaLS loads its settings from the workspace-level configuration file, <c>.luarc.json</c> by
	/// default; a custom file can be supplied through its <c>--configpath</c> command-line flag, which
	/// this watcher does not cover and this provider cannot pass (it exposes no server-argument
	/// surface). The watcher mirrors configuration files in the root only, and the
	/// prefix pattern covers the <c>.luarc.jsonc</c> form. Every <c>*.lua</c> file is watched
	/// recursively because LuaLS analyzes the whole workspace tree, including vendored library folders.
	/// </remarks>
	internal static readonly IReadOnlyList<WorkspaceWatchSpecification> WatchSpecifications =
	[
		new WorkspaceWatchSpecification(LuaFilePattern, IncludeSubdirectories: true),
		new WorkspaceWatchSpecification(LuaConfigurationFilePattern, IncludeSubdirectories: false)
	];

	/// <summary>
	/// Determines whether a normalized workspace path should refresh the language-server settings.
	/// </summary>
	/// <remarks>
	/// The comparison uses <see cref="LanguageServerPaths.LocalPathComparison"/>, so configuration
	/// detection follows the same path policy as document identity. Only the two extensions LuaLS loads are
	/// accepted, so a sidecar such as <c>.luarc.json.bak</c> or an editor backup like <c>.luarc.json~</c>
	/// does not trigger a settings refresh.
	/// </remarks>
	/// <param name="normalizedPath">The normalized path of the changed file.</param>
	/// <returns><see langword="true"/> when the path is a LuaLS configuration file.</returns>
	internal static bool IsConfigurationPath(string normalizedPath)
	{
		string fileName = Path.GetFileName(normalizedPath);

		if (!fileName.StartsWith(LuaConfigurationFilePrefix, LanguageServerPaths.LocalPathComparison))
			return false;

		string extension = Path.GetExtension(fileName);

		return extension.Equals(".json", LanguageServerPaths.LocalPathComparison)
			|| extension.Equals(".jsonc", LanguageServerPaths.LocalPathComparison);
	}
}
