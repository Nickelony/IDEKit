using System.Text.Json;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// Pins the handshake payloads the Roslyn profile advertises to the language server: the client capabilities, the initialization options,
/// and the settings document must stay aligned with what the shared provider framework consumes and with the section names the Roslyn language
/// server requests.
/// </summary>
[TestClass]
public sealed class RoslynHandshakePayloadTests
{
	[TestMethod]
	public void ClientCapabilities_AdvertisePullDiagnosticsAndTheCapabilitiesTheProviderConsumes()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(
			LanguageServerClientCapabilitiesBuilder.Build(RoslynLanguageServerProfileFactory.ClientCapabilities)
		);

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
	public void ClientCapabilities_UseTheSharedSemanticTokenLegendUnchanged()
	{
		JsonElement roslyn = SemanticTokensOf(RoslynLanguageServerProfileFactory.ClientCapabilities);
		JsonElement shared = SemanticTokensOf(LanguageServerClientCapabilitiesOptions.Default);

		// The Roslyn profile adds no token type or modifier to the shared legend, which the provider framework tests pin.
		Assert.AreEqual(shared.GetProperty("tokenTypes").GetRawText(), roslyn.GetProperty("tokenTypes").GetRawText());
		Assert.AreEqual(shared.GetProperty("tokenModifiers").GetRawText(), roslyn.GetProperty("tokenModifiers").GetRawText());
	}

	private static JsonElement SemanticTokensOf(LanguageServerClientCapabilitiesOptions options)
	{
		return JsonSerializer
			.SerializeToElement(LanguageServerClientCapabilitiesBuilder.Build(options))
			.GetProperty("textDocument")
			.GetProperty("semanticTokens");
	}

	[TestMethod]
	public void ClientCapabilities_AdvertiseTheRoslynCodeActionKindsAndOmitDocumentChanges()
	{
		JsonElement capabilities = JsonSerializer.SerializeToElement(
			LanguageServerClientCapabilitiesBuilder.Build(RoslynLanguageServerProfileFactory.ClientCapabilities)
		);

		JsonElement codeAction = capabilities.GetProperty("textDocument").GetProperty("codeAction");

		string?[] kinds =
		[
			.. codeAction
				.GetProperty("codeActionLiteralSupport")
				.GetProperty("codeActionKind")
				.GetProperty("valueSet")
				.EnumerateArray()
				.Select(kind => kind.GetString()),
		];

		Assert.Contains("quickfix", kinds);
		Assert.Contains("refactor.rewrite", kinds);
		Assert.Contains("source.organizeImports", kinds);
		Assert.IsTrue(codeAction.GetProperty("isPreferredSupport").GetBoolean());

		// documentChanges stays unadvertised because it can carry resource operations the shared workspace-edit
		// model cannot represent; the parser still accepts the versioned text-edit form defensively.
		Assert.IsFalse(capabilities.GetProperty("workspace").TryGetProperty("workspaceEdit", out _));
	}

	[TestMethod]
	public void InitializationOptions_AreEmptyForTheDefaultMaximumAndCarryAnExplicitValueOtherwise()
	{
		// A null maximum defers to the launch command line, so the payload stays empty.
		JsonElement defaultOptions = JsonSerializer.SerializeToElement(InitializationOptionsBuilder.Build(RoslynLanguageServerOptions.Default));
		Assert.IsEmpty(defaultOptions.EnumerateObject());

		JsonElement cappedOptions = JsonSerializer.SerializeToElement(
			InitializationOptionsBuilder.Build(new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 200 })
		);

		Assert.AreEqual(200, cappedOptions.GetProperty("autoLoadProjects").GetInt32());

		// Zero disables automatic project loading for the session.
		JsonElement disabledOptions = JsonSerializer.SerializeToElement(
			InitializationOptionsBuilder.Build(new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 0 })
		);

		Assert.AreEqual(0, disabledOptions.GetProperty("autoLoadProjects").GetInt32());
	}

	[TestMethod]
	public void Profile_WiresTheLaunchArgumentsAndTheHandshakeHooksFromTheSessionOptions()
	{
		var sessionOptions = new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 200, ShowCompletionItemsFromUnimportedNamespaces = false };

		LanguageServerProfile profile = RoslynLanguageServerProfileFactory.Create(RoslynLanguage.CSharp, sessionOptions);

		// A dropped registration would otherwise only surface in the opt-in integration tests.
		Assert.AreSequenceEqual([.. ServerArgumentsBuilder.Build(sessionOptions)], [.. profile.Launch.ServerArguments]);
		Assert.AreSame(RoslynLanguageServerProfileFactory.ClientCapabilities, profile.ClientCapabilities);

		Assert.AreEqual(
			JsonSerializer.SerializeToElement(InitializationOptionsBuilder.Build(sessionOptions)).GetRawText(),
			JsonSerializer.SerializeToElement(profile.Hooks.BuildInitializationOptions()).GetRawText()
		);

		Assert.AreEqual(
			JsonSerializer.SerializeToElement(SettingsBuilder.Build(sessionOptions)).GetRawText(),
			JsonSerializer.SerializeToElement(profile.Hooks.BuildSettings()).GetRawText()
		);
	}
}
