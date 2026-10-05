namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed class WorkspaceConventionsTests
{
	[TestMethod]
	public void WatchSpecifications_CoverLuaFilesRecursivelyAndRootConfigurationFiles()
	{
		IReadOnlyList<FileWatchSpecification> specifications = WorkspaceConventions.WatchSpecifications;

		Assert.HasCount(2, specifications);

		// LuaLS serves every on-disk Lua file in the workspace tree, so the Lua pattern is recursive.
		Assert.AreEqual("*.lua", specifications[0].Filter);
		Assert.IsTrue(specifications[0].IncludeSubdirectories);

		// LuaLS loads its settings from the workspace-level configuration file, so the configuration
		// pattern stays in the workspace root.
		Assert.AreEqual(".luarc.*", specifications[1].Filter);
		Assert.IsFalse(specifications[1].IncludeSubdirectories);
	}

	[TestMethod]
	public void IsConfigurationPath_MatchesOnlyTheLoadableLuarcExtensions()
	{
		string workspaceRoot = Path.GetTempPath();

		Assert.IsTrue(WorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, ".luarc.json")));
		Assert.IsTrue(WorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, ".luarc.jsonc")));

		// The predicate is a superset of watcher delivery: it accepts the prefix in any directory, while
		// the root-only watch specification is what decides which paths actually reach it.
		Assert.IsTrue(WorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, "scripts", ".luarc.json")));

		Assert.IsFalse(WorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, "luarc.json")));
		Assert.IsFalse(WorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, "config.luarc.json")));

		// Only the two extensions LuaLS loads refresh the settings: a sidecar, an editor backup, or another format
		// that merely shares the prefix must not trigger a workspace/didChangeConfiguration round trip.
		Assert.IsFalse(WorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, ".luarc.json.bak")));
		Assert.IsFalse(WorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, ".luarc.json~")));
		Assert.IsFalse(WorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, ".luarc.yaml")));

		if (LocalPathComparisonPolicy.ForCurrentPlatform.IgnoreCase)
		{
			Assert.IsTrue(
				WorkspaceConventions.IsConfigurationPath(Path.Combine(workspaceRoot, ".LUARC.JSON")),
				"Configuration detection follows the local-path comparison policy, which is case-insensitive on this host."
			);
		}
	}
}
