using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>Pins the handshake payloads the provider advertises to LuaLS.</summary>
[TestClass]
public sealed class LuaHandshakePayloadTests
{
	[TestMethod]
	public void ClientCapabilities_AdvertiseSnippetSupport()
	{
		JsonElement completionItem = CreateCapabilities().GetProperty("textDocument").GetProperty("completion").GetProperty("completionItem");

		// Snippet support is advertised because the provider consumes snippet insert texts end to end.
		Assert.IsTrue(completionItem.GetProperty("snippetSupport").GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseHierarchicalDocumentSymbols()
	{
		JsonElement documentSymbol = CreateCapabilities().GetProperty("textDocument").GetProperty("documentSymbol");

		// The provider consumes the hierarchical document-symbol shape, so the client advertises it.
		Assert.IsTrue(documentSymbol.GetProperty("hierarchicalDocumentSymbolSupport").GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseFullSemanticTokenRequests()
	{
		JsonElement semanticTokens = CreateCapabilities().GetProperty("textDocument").GetProperty("semanticTokens");

		// The provider consumes full semantic-token requests only; LuaLS does not answer delta
		// requests, so the client does not advertise delta support.
		Assert.IsTrue(semanticTokens.GetProperty("requests").GetProperty("full").GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseSemanticTokenRefreshSupport()
	{
		JsonElement capabilities = CreateCapabilities();

		// LuaLS only sends workspace/semanticTokens/refresh when the client advertises refresh support.
		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("semanticTokens").GetProperty("refreshSupport").GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseTheSharedSemanticTokenLegendAndTheGlobalModifier()
	{
		JsonElement semanticTokens = CreateCapabilities().GetProperty("textDocument").GetProperty("semanticTokens");

		string?[] legend = [.. semanticTokens.GetProperty("tokenTypes").EnumerateArray().Select(tokenType => tokenType.GetString())];
		string?[] modifiers = [.. semanticTokens.GetProperty("tokenModifiers").EnumerateArray().Select(modifier => modifier.GetString())];

		JsonElement sharedSemanticTokens = JsonSerializer
			.SerializeToElement(LanguageServerClientCapabilitiesBuilder.Build(LanguageServerClientCapabilitiesOptions.Default))
			.GetProperty("textDocument")
			.GetProperty("semanticTokens");

		// The Lua legend is the shared legend, which the provider framework tests pin, plus the 'global' modifier that LuaLS declares.
		Assert.AreSequenceEqual(
			[.. sharedSemanticTokens.GetProperty("tokenTypes").EnumerateArray().Select(tokenType => tokenType.GetString())],
			legend
		);

		Assert.AreSequenceEqual(
			[.. sharedSemanticTokens.GetProperty("tokenModifiers").EnumerateArray().Select(modifier => modifier.GetString()), "global"],
			modifiers
		);
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseVersionedDiagnostics()
	{
		JsonElement publishDiagnostics = CreateCapabilities().GetProperty("textDocument").GetProperty("publishDiagnostics");

		// Versioned diagnostics are the precondition for the cached-diagnostics version fences.
		Assert.IsTrue(publishDiagnostics.GetProperty("versionSupport").GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseSignatureHelpParameterLabelOffsets()
	{
		JsonElement signatureInformation = CreateCapabilities()
			.GetProperty("textDocument")
			.GetProperty("signatureHelp")
			.GetProperty("signatureInformation");

		// Label-offset support is the precondition for the offset-pair parameter labels.
		Assert.IsTrue(signatureInformation.GetProperty("parameterInformation").GetProperty("labelOffsetSupport").GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseNoActiveParameterSupport()
	{
		JsonElement signatureInformation = CreateCapabilities()
			.GetProperty("textDocument")
			.GetProperty("signatureHelp")
			.GetProperty("signatureInformation");

		// An explicit null activeParameter in the request context is valid only when the client declares this.
		Assert.IsTrue(signatureInformation.GetProperty("noActiveParameterSupport").GetBoolean());
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseTheCompletionItemFormsTheParserConsumes()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(
			LanguageServerClientCapabilitiesBuilder.Build(LuaLanguageServerProfileFactory.ClientCapabilities)
		);

		JsonElement completion = capabilities.GetProperty("textDocument").GetProperty("completion");
		JsonElement completionItem = completion.GetProperty("completionItem");

		// Every advertised completion capability is a form the completion parser consumes end to end, so
		// a capability-respecting server may use it.
		Assert.IsTrue(completionItem.GetProperty("commitCharactersSupport").GetBoolean());
		Assert.IsTrue(completionItem.GetProperty("preselectSupport").GetBoolean());
		Assert.IsTrue(completionItem.GetProperty("insertReplaceSupport").GetBoolean());

		int[] tags = [.. completionItem.GetProperty("tagSupport").GetProperty("valueSet").EnumerateArray().Select(tag => tag.GetInt32())];

		Assert.AreSequenceEqual([(int)CompletionItemTag.Deprecated], tags);

		string?[] resolveProperties =
		[
			.. completionItem.GetProperty("resolveSupport").GetProperty("properties").EnumerateArray().Select(property => property.GetString()),
		];

		Assert.Contains("detail", resolveProperties);
		Assert.Contains("documentation", resolveProperties);
		Assert.Contains("additionalTextEdits", resolveProperties);

		string?[] itemDefaults =
		[
			.. completion.GetProperty("completionList").GetProperty("itemDefaults").EnumerateArray().Select(defaultName => defaultName.GetString()),
		];

		// The advertised list defaults are exactly the ones the response converter resolves onto items.
		Assert.AreSequenceEqual(["commitCharacters", "editRange", "insertTextFormat", "data"], itemDefaults);
	}

	[TestMethod]
	public void ClientCapabilities_DoNotAdvertiseWorkspaceEditDocumentChanges()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(
			LanguageServerClientCapabilitiesBuilder.Build(LuaLanguageServerProfileFactory.ClientCapabilities)
		);

		// documentChanges stays unadvertised because it can carry resource operations the shared
		// workspace-edit model cannot represent; the parser still accepts the versioned text-edit form
		// defensively when a server sends it anyway.
		Assert.IsFalse(capabilities.GetProperty("workspace").TryGetProperty("workspaceEdit", out _));
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseCodeActionLiteralSupport()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(
			LanguageServerClientCapabilitiesBuilder.Build(LuaLanguageServerProfileFactory.ClientCapabilities)
		);

		JsonElement codeAction = capabilities.GetProperty("textDocument").GetProperty("codeAction");

		// The provider consumes literal CodeAction objects, so the client advertises the literal form;
		// without it a server may answer with bare commands that carry no title or kind.
		JsonElement codeActionKind = codeAction.GetProperty("codeActionLiteralSupport").GetProperty("codeActionKind");

		string?[] kinds = [.. codeActionKind.GetProperty("valueSet").EnumerateArray().Select(kind => kind.GetString())];

		Assert.Contains("quickfix", kinds);
		Assert.Contains("refactor.rewrite", kinds);
		Assert.IsTrue(codeAction.GetProperty("isPreferredSupport").GetBoolean());
	}

	[TestMethod]
	public void InitializationOptions_DisableInteractiveTrustFlows()
	{
		JsonElement options = JsonSerializer.SerializeToElement(InitializationOptionsBuilder.Build());

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
	public void Profile_WiresTheHandshakeHooks()
	{
		LanguageServerProfile profile = LuaLanguageServerProfileFactory.Create();

		// A dropped registration would otherwise only surface in the opt-in integration tests.
		Assert.AreSame(LuaLanguageServerProfileFactory.ClientCapabilities, profile.ClientCapabilities);

		Assert.AreEqual(
			JsonSerializer.SerializeToElement(InitializationOptionsBuilder.Build()).GetRawText(),
			JsonSerializer.SerializeToElement(profile.Hooks.BuildInitializationOptions()).GetRawText()
		);

		Assert.AreEqual(
			JsonSerializer.SerializeToElement(SettingsBuilder.Build(LuaLanguageServerOptions.Default)).GetRawText(),
			JsonSerializer.SerializeToElement(profile.Hooks.BuildSettings()).GetRawText()
		);
	}

	[TestMethod]
	public void Profile_CarriesTheLuaOptionsIntoTheSettings()
	{
		var luaOptions = new LuaLanguageServerOptions { RuntimeVersion = "LuaJIT" };

		LanguageServerProfile profile = LuaLanguageServerProfileFactory.Create(luaOptions);

		Assert.AreEqual(
			JsonSerializer.SerializeToElement(SettingsBuilder.Build(luaOptions)).GetRawText(),
			JsonSerializer.SerializeToElement(profile.Hooks.BuildSettings()).GetRawText()
		);
	}

	private static JsonElement CreateCapabilities() =>
		JsonSerializer.SerializeToElement(LanguageServerClientCapabilitiesBuilder.Build(LuaLanguageServerProfileFactory.ClientCapabilities));
}
