namespace Nickelony.LanguageServer.Lua;

/// <summary>Describes the workspace file patterns and configuration paths the Lua profile mirrors to the language server.</summary>
internal static class WorkspaceConventions
{
	internal const string ConfigurationFilePrefix = ".luarc.";

	internal const string ConfigurationFilePattern = ConfigurationFilePrefix + "*";

	internal const string FilePattern = "*.lua";

	/// <summary>
	/// The watch specifications mirrored to the language server: every Lua file in the workspace tree and every LuaLS configuration file
	/// in the workspace root.
	/// </summary>
	/// <remarks>
	/// Configuration files are watched in the workspace root only; every <c>*.lua</c> file is watched recursively because LuaLS analyzes
	/// the whole tree.
	/// </remarks>
	internal static readonly IReadOnlyList<FileWatchSpecification> WatchSpecifications =
	[
		new FileWatchSpecification(FilePattern, IncludeSubdirectories: true),
		new FileWatchSpecification(ConfigurationFilePattern, IncludeSubdirectories: false),
	];

	/// <summary>Determines whether a normalized workspace path should refresh the language-server settings.</summary>
	/// <param name="normalizedPath">The normalized path of the changed file.</param>
	/// <returns><see langword="true"/> when the path is a LuaLS configuration file.</returns>
	/// <remarks>
	/// The comparison uses the <see cref="LocalPathComparisonPolicy"/> of the current platform, whatever path comparison the host sets
	/// on the provider options for document identity. Only the <c>.json</c> and <c>.jsonc</c> extensions are accepted, so a sidecar such as <c>.luarc.json.bak</c>
	/// or an editor backup like <c>.luarc.json~</c> does not trigger a settings refresh.
	/// </remarks>
	internal static bool IsConfigurationPath(string normalizedPath)
	{
		string fileName = Path.GetFileName(normalizedPath);

		if (!fileName.StartsWith(ConfigurationFilePrefix, LocalPathComparisonPolicy.ForCurrentPlatform.Comparison))
			return false;

		string extension = Path.GetExtension(fileName);

		return extension.Equals(".json", LocalPathComparisonPolicy.ForCurrentPlatform.Comparison)
			|| extension.Equals(".jsonc", LocalPathComparisonPolicy.ForCurrentPlatform.Comparison);
	}
}
