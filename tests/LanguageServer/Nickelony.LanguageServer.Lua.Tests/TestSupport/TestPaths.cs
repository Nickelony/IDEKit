namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Provides the synthetic paths the Lua test suites use as fixtures. The paths derive from the platform
/// temp directory instead of a hard-coded Windows drive, so a test that only needs a valid absolute path
/// (to build a URI, compare document identity, or open a document) stays independent of the host.
/// </summary>
/// <remarks>
/// Nothing here touches the file system: a fixture is a deterministic path string, not a directory.
/// Tests that need a real directory create their own unique temp root.
/// </remarks>
internal static class TestPaths
{
	/// <summary>The synthetic primary workspace root.</summary>
	internal static string Root { get; } = Path.Combine(Path.GetTempPath(), "LuaFixturePrimary");

	/// <summary>
	/// A second synthetic workspace root that is not nested under <see cref="Root"/>. The two directory
	/// names are deliberately unrelated so neither is a string prefix of the other.
	/// </summary>
	internal static string SecondaryRoot { get; } = Path.Combine(Path.GetTempPath(), "LuaFixtureSecondary");

	/// <summary>Returns the path of <paramref name="fileName"/> inside the synthetic <c>Scripts</c> folder of <see cref="Root"/>.</summary>
	/// <param name="fileName">The Lua file name, for example <c>test.lua</c>.</param>
	/// <returns>An absolute path below <see cref="Root"/>.</returns>
	internal static string Script(string fileName) => Path.Combine(Root, "Scripts", fileName);

	/// <summary>The Lua language server executable name for the current platform.</summary>
	internal static string ServerExecutableFileName { get; } = OperatingSystem.IsWindows() ? "lua-language-server.exe" : "lua-language-server";

	/// <summary>
	/// A plausible path for the Lua language server executable. Tests that never launch the binary only
	/// need a path that is valid on the current platform.
	/// </summary>
	internal static string ServerExecutable { get; } = Path.Combine(Root, "tools", "lua-language-server", ServerExecutableFileName);
}
