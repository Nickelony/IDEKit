using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Pins the handshake payloads the provider advertises to LuaLS: the client capabilities and the
/// initialization options must stay aligned with the features the provider actually implements.
/// </summary>
[TestClass]
public sealed class LuaLanguageServerHandshakeFactoryTests
{
	[TestMethod]
	public void ClientCapabilities_AdvertiseSnippetsDocumentationResolveAndSemanticTokens()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(ClientCapabilitiesFactory.Create());
		JsonElement completionItem = capabilities
			.GetProperty("textDocument")
			.GetProperty("completion")
			.GetProperty("completionItem");

		// Snippet support is advertised because the provider consumes snippet insert texts end to end.
		Assert.IsTrue(completionItem.GetProperty("snippetSupport").GetBoolean());

		string?[] resolveSupportProperties = completionItem
			.GetProperty("resolveSupport")
			.GetProperty("properties")
			.EnumerateArray()
			.Select(property => property.GetString())
			.ToArray();

		CollectionAssert.Contains(resolveSupportProperties, "detail");
		CollectionAssert.Contains(resolveSupportProperties, "documentation");

		JsonElement semanticTokens = capabilities.GetProperty("textDocument").GetProperty("semanticTokens");

		// The provider consumes the hierarchical document-symbol shape, so the client advertises it.
		Assert.IsTrue(capabilities
			.GetProperty("textDocument")
			.GetProperty("documentSymbol")
			.GetProperty("hierarchicalDocumentSymbolSupport")
			.GetBoolean());

		// The provider consumes full semantic-token requests only; LuaLS does not answer delta
		// requests, so the client does not advertise delta support.
		Assert.IsTrue(semanticTokens.GetProperty("requests").GetProperty("full").GetBoolean());

		// LuaLS only sends workspace/semanticTokens/refresh when the client advertises refresh support.
		Assert.IsTrue(capabilities
			.GetProperty("workspace")
			.GetProperty("semanticTokens")
			.GetProperty("refreshSupport")
			.GetBoolean());

		string?[] legend = semanticTokens
			.GetProperty("tokenTypes")
			.EnumerateArray()
			.Select(tokenType => tokenType.GetString())
			.ToArray();

		// The legend mirrors the shared LSP 3.17 + 3.18 vocabulary; the exact list is pinned so a
		// dropped entry cannot silently degrade token colors.
		string?[] expectedLegend =
		[
			"namespace", "type", "class", "enum", "interface", "struct", "typeParameter", "parameter",
			"variable", "property", "enumMember", "event", "function", "method", "macro", "keyword",
			"modifier", "comment", "string", "number", "regexp", "operator", "decorator", "label"
		];

		CollectionAssert.AreEqual(expectedLegend, legend);
		CollectionAssert.AllItemsAreUnique(legend);

		string?[] modifiers = semanticTokens
			.GetProperty("tokenModifiers")
			.EnumerateArray()
			.Select(modifier => modifier.GetString())
			.ToArray();

		// The modifier legend mirrors the shared vocabulary and declares LuaLS's 'global' extension.
		string?[] expectedModifiers =
		[
			"declaration", "definition", "readonly", "static", "deprecated", "abstract", "async",
			"modification", "documentation", "defaultLibrary", ClientCapabilitiesFactory.GlobalSemanticTokenModifier
		];

		CollectionAssert.AreEqual(expectedModifiers, modifiers);
		CollectionAssert.AllItemsAreUnique(modifiers);

		// Versioned diagnostics are the precondition for the cached-diagnostics version fences, and
		// label-offset support is the precondition for the offset-pair parameter labels.
		Assert.IsTrue(capabilities
			.GetProperty("textDocument")
			.GetProperty("publishDiagnostics")
			.GetProperty("versionSupport")
			.GetBoolean());
		Assert.IsTrue(capabilities
			.GetProperty("textDocument")
			.GetProperty("signatureHelp")
			.GetProperty("signatureInformation")
			.GetProperty("parameterInformation")
			.GetProperty("labelOffsetSupport")
			.GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseTheCompletionItemFormsTheParserConsumes()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(ClientCapabilitiesFactory.Create());
		JsonElement completion = capabilities.GetProperty("textDocument").GetProperty("completion");
		JsonElement completionItem = completion.GetProperty("completionItem");

		// Every advertised completion capability is a form the completion parser consumes end to end, so
		// a capability-respecting server may use it.
		Assert.IsTrue(completionItem.GetProperty("commitCharactersSupport").GetBoolean());
		Assert.IsTrue(completionItem.GetProperty("preselectSupport").GetBoolean());
		Assert.IsTrue(completionItem.GetProperty("insertReplaceSupport").GetBoolean());

		int[] tags = completionItem
			.GetProperty("tagSupport")
			.GetProperty("valueSet")
			.EnumerateArray()
			.Select(tag => tag.GetInt32())
			.ToArray();

		CollectionAssert.AreEqual(new[] { (int)CompletionItemTag.Deprecated }, tags);

		string?[] resolveProperties = completionItem
			.GetProperty("resolveSupport")
			.GetProperty("properties")
			.EnumerateArray()
			.Select(property => property.GetString())
			.ToArray();

		CollectionAssert.Contains(resolveProperties, "detail");
		CollectionAssert.Contains(resolveProperties, "documentation");
		CollectionAssert.Contains(resolveProperties, "additionalTextEdits");

		string?[] itemDefaults = completion
			.GetProperty("completionList")
			.GetProperty("itemDefaults")
			.EnumerateArray()
			.Select(defaultName => defaultName.GetString())
			.ToArray();

		// The advertised list defaults are exactly the ones the response converter resolves onto items.
		CollectionAssert.AreEqual(
			new[] { "commitCharacters", "editRange", "insertTextFormat", "data" },
			itemDefaults);
	}

	[TestMethod]
	public void ClientCapabilities_DoNotAdvertiseWorkspaceEditDocumentChanges()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(ClientCapabilitiesFactory.Create());

		// documentChanges stays unadvertised because it can carry resource operations the shared
		// workspace-edit model cannot represent; the parser still accepts the versioned text-edit form
		// defensively when a server sends it anyway.
		Assert.IsFalse(capabilities.GetProperty("workspace").TryGetProperty("workspaceEdit", out _));
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseCodeActionLiteralSupport()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(ClientCapabilitiesFactory.Create());
		JsonElement codeAction = capabilities.GetProperty("textDocument").GetProperty("codeAction");

		// The provider consumes literal CodeAction objects with inline edits, so the client advertises
		// the literal form; without it a server may fall back to the command form it cannot execute.
		JsonElement codeActionKind = codeAction
			.GetProperty("codeActionLiteralSupport")
			.GetProperty("codeActionKind");

		string?[] kinds = codeActionKind
			.GetProperty("valueSet")
			.EnumerateArray()
			.Select(kind => kind.GetString())
			.ToArray();

		CollectionAssert.Contains(kinds, "quickfix");
		CollectionAssert.Contains(kinds, "refactor.rewrite");
		Assert.IsTrue(codeAction.GetProperty("isPreferredSupport").GetBoolean());
	}

	[TestMethod]
	public void InitializationOptions_DisableInteractiveTrustFlows()
	{
		JsonElement options = JsonSerializer.SerializeToElement(InitializationOptionsFactory.Create());

		// A library cannot answer LuaLS's interactive prompts, so trust stays client-disabled; and the
		// client cannot serve LuaLS's configuration-modification or document-reveal flows, so those
		// flags stay disabled too (LuaLS falls back to its own configuration handling).
		Assert.IsFalse(options.GetProperty("changeConfiguration").GetBoolean());
		Assert.IsFalse(options.GetProperty("viewDocument").GetBoolean());
		Assert.IsFalse(options.GetProperty("trustByClient").GetBoolean());

		// Semantic tokens are consumed from full responses; range requests stay disabled.
		Assert.IsFalse(options.GetProperty("useSemanticByRange").GetBoolean());
	}

	[TestMethod]
	public void ProviderConstruction_WiresTheHandshakeFactoriesIntoTheRealClient()
	{
		string workspaceRoot = TestPaths.Root;

		// A configured executable path makes the public constructor build the real client. The wired
		// factories are read reflectively because neither the client nor the provider exposes them, and
		// a dropped registration would otherwise only surface in the opt-in integration tests.
		using var provider = new LuaLanguageServerIntelliSenseProvider(
			[workspaceRoot], TestPaths.ServerExecutable);

		LanguageServerClient client = LuaLanguageServerIntelliSenseProviderTestAccess.GetProviderClient(provider);
		IReadOnlyList<string> normalizedRoots = LanguageServerPaths.NormalizeWorkspaceRoots([workspaceRoot]);

		Assert.AreEqual(
			JsonSerializer.SerializeToElement(ClientCapabilitiesFactory.Create()).GetRawText(),
			JsonSerializer.SerializeToElement(LuaLanguageServerIntelliSenseProviderTestAccess.InvokeClientCapabilitiesProvider(client, normalizedRoots)).GetRawText());
		Assert.AreEqual(
			JsonSerializer.SerializeToElement(InitializationOptionsFactory.Create()).GetRawText(),
			JsonSerializer.SerializeToElement(LuaLanguageServerIntelliSenseProviderTestAccess.InvokeInitializationOptionsProvider(client, normalizedRoots)).GetRawText());
		Assert.AreEqual(
			JsonSerializer.SerializeToElement(SettingsFactory.Create(LuaLanguageServerOptions.Default)).GetRawText(),
			JsonSerializer.SerializeToElement(LuaLanguageServerIntelliSenseProviderTestAccess.InvokeSettingsProvider(client)).GetRawText());
	}

	[TestMethod]
	public void ProviderConstruction_IntegrationProcessSeamResolvesWithoutALiveSession()
	{
		string workspaceRoot = TestPaths.Root;

		// A configured executable path makes the public constructor build the real client. The live
		// integration tier reaches the spawned server process through this seam; resolving it here
		// fails loudly when the internal path chain is renamed, instead of surfacing only when the
		// opt-in archive is configured.
		using var provider = new LuaLanguageServerIntelliSenseProvider(
			[workspaceRoot], TestPaths.ServerExecutable);

		LanguageServerClient client = LuaLanguageServerIntelliSenseProviderTestAccess.GetProviderClient(provider);

		Assert.IsNull(LuaLanguageServerIntelliSenseProviderTestAccess.GetActiveServerProcess(client));
	}
}
