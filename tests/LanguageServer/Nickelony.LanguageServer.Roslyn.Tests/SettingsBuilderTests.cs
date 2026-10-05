using System.Text.Json;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// Pins the settings document the Roslyn profile answers: its sections are keyed by the section names the Roslyn language server requests.
/// </summary>
[TestClass]
public sealed class SettingsBuilderTests
{
	[TestMethod]
	public void Create_KeysTheCompletionSectionByTheServerSectionNames()
	{
		JsonElement enabledSettings = JsonSerializer.SerializeToElement(SettingsBuilder.Build(RoslynLanguageServerOptions.Default));

		// The server requests each per-language option by "{prefix}|{group}.{configName}"; the client resolves a
		// section by splitting it on '.', so the document is keyed by the exact section names.
		Assert.IsTrue(
			enabledSettings.GetProperty("csharp|completion").GetProperty("dotnet_show_completion_items_from_unimported_namespaces").GetBoolean()
		);

		Assert.IsTrue(
			enabledSettings.GetProperty("visual_basic|completion").GetProperty("dotnet_show_completion_items_from_unimported_namespaces").GetBoolean()
		);

		JsonElement disabledSettings = JsonSerializer.SerializeToElement(
			SettingsBuilder.Build(new RoslynLanguageServerOptions { ShowCompletionItemsFromUnimportedNamespaces = false })
		);

		Assert.IsFalse(
			disabledSettings.GetProperty("csharp|completion").GetProperty("dotnet_show_completion_items_from_unimported_namespaces").GetBoolean()
		);

		Assert.IsFalse(
			disabledSettings
				.GetProperty("visual_basic|completion")
				.GetProperty("dotnet_show_completion_items_from_unimported_namespaces")
				.GetBoolean()
		);
	}
}
