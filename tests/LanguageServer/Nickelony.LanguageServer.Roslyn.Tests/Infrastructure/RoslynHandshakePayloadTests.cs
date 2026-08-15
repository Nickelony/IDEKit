using System.Text.Json;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// Pins the handshake payloads the Roslyn provider advertises to the language server: the client capabilities,
/// the initialization options, and the settings document must stay aligned with what the shared provider
/// framework consumes and with the section names the Roslyn language server requests.
/// </summary>
[TestClass]
public sealed class RoslynHandshakePayloadTests
{
	[TestMethod]
	public void ClientCapabilities_AdvertisePullDiagnosticsAndTheProfilesTheProviderConsumes()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(ClientCapabilitiesFactory.Create());
		JsonElement textDocument = capabilities.GetProperty("textDocument");

		// The Roslyn language server is pull-oriented: the client advertises textDocument/diagnostic and the
		// workspace-level refresh so the server prefers the pull model and re-queries on a project change.
		JsonElement diagnostic = textDocument.GetProperty("diagnostic");
		Assert.IsFalse(diagnostic.GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsTrue(diagnostic.GetProperty("relatedDocumentSupport").GetBoolean());
		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("diagnostics").GetProperty("refreshSupport").GetBoolean());

		// The semantic-token refresh is what lets the server invalidate cached token colors after a change.
		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("semanticTokens").GetProperty("refreshSupport").GetBoolean());

		// The provider consumes the hierarchical symbol shape and full semantic-token requests only.
		Assert.IsTrue(textDocument.GetProperty("documentSymbol").GetProperty("hierarchicalDocumentSymbolSupport").GetBoolean());
		Assert.IsTrue(textDocument.GetProperty("semanticTokens").GetProperty("requests").GetProperty("full").GetBoolean());
		Assert.IsFalse(textDocument.GetProperty("semanticTokens").GetProperty("requests").GetProperty("range").GetBoolean());

		// The parser consumes snippet insert texts, commit characters, preselect, and the resolve properties.
		JsonElement completionItem = textDocument.GetProperty("completion").GetProperty("completionItem");
		Assert.IsTrue(completionItem.GetProperty("snippetSupport").GetBoolean());
		Assert.IsTrue(completionItem.GetProperty("commitCharactersSupport").GetBoolean());
		Assert.IsTrue(completionItem.GetProperty("preselectSupport").GetBoolean());
		Assert.IsTrue(completionItem.GetProperty("insertReplaceSupport").GetBoolean());

		// Workspace configuration is advertised because the server reads its options through workspace/configuration.
		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("configuration").GetBoolean());
		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("workspaceFolders").GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseTheSharedSemanticTokenVocabulary()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(ClientCapabilitiesFactory.Create());
		JsonElement semanticTokens = capabilities.GetProperty("textDocument").GetProperty("semanticTokens");

		string?[] tokenTypes = semanticTokens
			.GetProperty("tokenTypes")
			.EnumerateArray()
			.Select(tokenType => tokenType.GetString())
			.ToArray();

		CollectionAssert.Contains(tokenTypes, "variable");
		CollectionAssert.Contains(tokenTypes, "keyword");
		CollectionAssert.AllItemsAreUnique(tokenTypes);

		string?[] tokenModifiers = semanticTokens
			.GetProperty("tokenModifiers")
			.EnumerateArray()
			.Select(modifier => modifier.GetString())
			.ToArray();

		CollectionAssert.Contains(tokenModifiers, "declaration");
		CollectionAssert.AllItemsAreUnique(tokenModifiers);
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseTheRoslynCodeActionKindsAndOmitDocumentChanges()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(ClientCapabilitiesFactory.Create());
		JsonElement codeAction = capabilities.GetProperty("textDocument").GetProperty("codeAction");

		string?[] kinds = codeAction
			.GetProperty("codeActionLiteralSupport")
			.GetProperty("codeActionKind")
			.GetProperty("valueSet")
			.EnumerateArray()
			.Select(kind => kind.GetString())
			.ToArray();

		CollectionAssert.Contains(kinds, "quickfix");
		CollectionAssert.Contains(kinds, "refactor.rewrite");
		CollectionAssert.Contains(kinds, "source.organizeImports");
		Assert.IsTrue(codeAction.GetProperty("isPreferredSupport").GetBoolean());

		// documentChanges stays unadvertised because it can carry resource operations the shared workspace-edit
		// model cannot represent; the parser still accepts the versioned text-edit form defensively.
		Assert.IsFalse(capabilities.GetProperty("workspace").TryGetProperty("workspaceEdit", out _));
	}

	[TestMethod]
	public void InitializationOptions_AreEmptyForTheDefaultMaximumAndCarryAnExplicitValueOtherwise()
	{
		// A null maximum defers to the launch command line, so the payload stays empty.
		JsonElement defaultOptions = JsonSerializer.SerializeToElement(InitializationOptionsFactory.Create(RoslynLanguageServerOptions.Default));
		Assert.AreEqual(0, defaultOptions.EnumerateObject().Count());

		JsonElement cappedOptions = JsonSerializer.SerializeToElement(
			InitializationOptionsFactory.Create(new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 200 }));
		Assert.AreEqual(200, cappedOptions.GetProperty("autoLoadProjects").GetInt32());

		// Zero disables automatic project loading for the session.
		JsonElement disabledOptions = JsonSerializer.SerializeToElement(
			InitializationOptionsFactory.Create(new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 0 }));
		Assert.AreEqual(0, disabledOptions.GetProperty("autoLoadProjects").GetInt32());
	}

	[TestMethod]
	public void Settings_KeyTheCompletionSectionByTheServerSectionNames()
	{
		JsonElement enabledSettings = JsonSerializer.SerializeToElement(SettingsFactory.Create(RoslynLanguageServerOptions.Default));

		// The server requests each per-language option by "{prefix}|{group}.{configName}"; the client resolves a
		// section by splitting it on '.', so the document is keyed by the exact section names.
		Assert.IsTrue(enabledSettings
			.GetProperty("csharp|completion")
			.GetProperty("dotnet_show_completion_items_from_unimported_namespaces")
			.GetBoolean());
		Assert.IsTrue(enabledSettings
			.GetProperty("visual_basic|completion")
			.GetProperty("dotnet_show_completion_items_from_unimported_namespaces")
			.GetBoolean());

		JsonElement disabledSettings = JsonSerializer.SerializeToElement(
			SettingsFactory.Create(new RoslynLanguageServerOptions { ShowCompletionItemsFromUnimportedNamespaces = false }));

		Assert.IsFalse(disabledSettings
			.GetProperty("csharp|completion")
			.GetProperty("dotnet_show_completion_items_from_unimported_namespaces")
			.GetBoolean());
		Assert.IsFalse(disabledSettings
			.GetProperty("visual_basic|completion")
			.GetProperty("dotnet_show_completion_items_from_unimported_namespaces")
			.GetBoolean());
	}
}
