using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>Pins the LuaLS settings document.</summary>
/// <remarks>
/// Settings are serialized through the client's message options so the assertions cover the actual wire shape, including the PascalCase
/// <c>Lua</c> root key, which the document spells exactly.
/// </remarks>
[TestClass]
public sealed class SettingsBuilderTests
{
	private static JsonElement CreateSettings(LuaLanguageServerOptions options) =>
		JsonSerializer.SerializeToElement(SettingsBuilder.Build(options), LanguageServerClient.CreateProtocolSerializerOptions());

	[TestMethod]
	public void Create_PinsTheProviderDefaults()
	{
		JsonElement lua = CreateSettings(LuaLanguageServerOptions.Default).GetProperty("Lua");

		// The defaults mirror LuaLS except for the two documented deviations: third-party checks are
		// disabled because a library cannot answer interactive prompts, and call snippets use
		// "Replace" because the provider consumes snippet insert texts end to end.
		Assert.AreEqual("Lua 5.4", lua.GetProperty("runtime").GetProperty("version").GetString());
		Assert.AreEqual("Disable", lua.GetProperty("workspace").GetProperty("checkThirdParty").GetString());
		Assert.AreEqual(0, lua.GetProperty("workspace").GetProperty("library").GetArrayLength());
		Assert.AreEqual("Replace", lua.GetProperty("completion").GetProperty("callSnippet").GetString());
		Assert.IsTrue(lua.GetProperty("semantic").GetProperty("enable").GetBoolean());
		Assert.IsTrue(lua.GetProperty("semantic").GetProperty("annotation").GetBoolean());
		Assert.IsTrue(lua.GetProperty("semantic").GetProperty("variable").GetBoolean());
		Assert.IsFalse(lua.GetProperty("semantic").GetProperty("keyword").GetBoolean());
		Assert.AreEqual(0, lua.GetProperty("diagnostics").GetProperty("disable").GetArrayLength());
	}

	[TestMethod]
	public void Create_ReflectsCustomOptions()
	{
		var options = new LuaLanguageServerOptions
		{
			RuntimeVersion = "LuaJIT",
			EnableSemanticKeywordHighlighting = true,
			DisabledDiagnostics = ["unused-local"],
			AdditionalLibraryDirectories = [Path.Combine(TestPaths.Root, "Libraries", "Extra")],
		};

		JsonElement lua = CreateSettings(options).GetProperty("Lua");

		Assert.AreEqual("LuaJIT", lua.GetProperty("runtime").GetProperty("version").GetString());
		Assert.IsTrue(lua.GetProperty("semantic").GetProperty("keyword").GetBoolean());

		JsonElement disabledDiagnostics = lua.GetProperty("diagnostics").GetProperty("disable");

		Assert.AreEqual(1, disabledDiagnostics.GetArrayLength());
		Assert.AreEqual("unused-local", disabledDiagnostics[0].GetString());

		JsonElement library = lua.GetProperty("workspace").GetProperty("library");

		Assert.AreSequenceEqual(
			[Path.Combine(TestPaths.Root, "Libraries", "Extra")],
			library.EnumerateArray().Select(entry => entry.GetString()).ToArray()
		);
	}

	[TestMethod]
	public void Create_MapsDisabledSemanticSwitches()
	{
		JsonElement semantic = CreateSettings(
				new LuaLanguageServerOptions
				{
					EnableSemanticHighlighting = false,
					EnableSemanticAnnotationHighlighting = false,
					EnableSemanticVariableHighlighting = false,
					EnableSemanticKeywordHighlighting = false,
				}
			)
			.GetProperty("Lua")
			.GetProperty("semantic");

		Assert.IsFalse(semantic.GetProperty("enable").GetBoolean());
		Assert.IsFalse(semantic.GetProperty("annotation").GetBoolean());
		Assert.IsFalse(semantic.GetProperty("variable").GetBoolean());
		Assert.IsFalse(semantic.GetProperty("keyword").GetBoolean());
	}

	[TestMethod]
	public void Create_ForwardsLibraryFoldersAndDropsBlankEntries()
	{
		JsonElement lua = CreateSettings(
				new LuaLanguageServerOptions
				{
					AdditionalLibraryDirectories =
					[
						Path.Combine(TestPaths.Root, "Libraries", "A"),
						"   ",
						"",
						Path.Combine(TestPaths.Root, "Libraries", "B"),
					],

					DisabledDiagnostics = ["unused-local", "  ", ""],
				}
			)
			.GetProperty("Lua");

		// Blank entries are filtered before forwarding, so the server never receives empty paths.
		JsonElement library = lua.GetProperty("workspace").GetProperty("library");

		Assert.AreSequenceEqual(
			[Path.Combine(TestPaths.Root, "Libraries", "A"), Path.Combine(TestPaths.Root, "Libraries", "B")],
			library.EnumerateArray().Select(entry => entry.GetString()).ToArray()
		);

		JsonElement disabledDiagnostics = lua.GetProperty("diagnostics").GetProperty("disable");
		Assert.AreSequenceEqual(["unused-local"], disabledDiagnostics.EnumerateArray().Select(entry => entry.GetString()).ToArray());
	}
}
