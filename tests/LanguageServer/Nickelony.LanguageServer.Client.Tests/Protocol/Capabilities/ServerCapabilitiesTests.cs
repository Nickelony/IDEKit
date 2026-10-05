using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ServerCapabilitiesTests
{
	[TestMethod]
	public void Deserialize_ReadsEveryProviderTheClientModels()
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>(
			"""
			{
			  "positionEncoding": "utf-16",
			  "textDocumentSync": { "change": 2 },
			  "completionProvider": { "resolveProvider": true, "triggerCharacters": ["."] },
			  "hoverProvider": true,
			  "definitionProvider": { "workDoneProgress": true },
			  "documentSymbolProvider": true,
			  "codeActionProvider": { "resolveProvider": false },
			  "referencesProvider": true,
			  "renameProvider": { "prepareProvider": true },
			  "documentFormattingProvider": true,
			  "signatureHelpProvider": { "triggerCharacters": ["("] },
			  "semanticTokensProvider": { "full": { "delta": true }, "legend": { "tokenTypes": ["function"], "tokenModifiers": ["declaration"] } },
			  "diagnosticProvider": { "interFileDependencies": false, "workspaceDiagnostics": false }
			}
			"""
		);

		Assert.AreEqual("utf-16", capabilities.PositionEncoding);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, capabilities.TextDocumentSync?.Kind);
		Assert.IsTrue(capabilities.CompletionProvider?.ResolveProvider);
		Assert.IsTrue(capabilities.HoverProvider?.IsSupported);
		Assert.IsTrue(capabilities.DefinitionProvider?.IsSupported);
		Assert.IsTrue(capabilities.DocumentSymbolProvider?.IsSupported);
		Assert.AreEqual(new CodeActionProviderCapability(IsSupported: true, ResolveProvider: false), capabilities.CodeActionProvider);
		Assert.IsTrue(capabilities.ReferencesProvider?.IsSupported);
		Assert.IsTrue(capabilities.RenameProvider?.IsSupported);
		Assert.IsTrue(capabilities.DocumentFormattingProvider?.IsSupported);
		Assert.IsTrue(capabilities.SignatureHelpProvider?.IsSupported);
		Assert.IsTrue(capabilities.SemanticTokensProvider?.Full?.IsSupported);
		Assert.AreSequenceEqual(["function"], capabilities.SemanticTokensProvider?.Legend?.TokenTypes);
		Assert.AreSequenceEqual(["declaration"], capabilities.SemanticTokensProvider?.Legend?.TokenModifiers);
		Assert.IsTrue(capabilities.DiagnosticProvider?.IsSupported);
	}

	[TestMethod]
	public void Deserialize_LeavesAnAbsentProviderNull()
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>("""{ "hoverProvider": true }""");

		Assert.IsNull(capabilities.PositionEncoding);
		Assert.IsNull(capabilities.TextDocumentSync);
		Assert.IsNull(capabilities.CompletionProvider);
		Assert.IsNull(capabilities.CodeActionProvider);
		Assert.IsNull(capabilities.SemanticTokensProvider);
		Assert.IsNull(capabilities.ReferencesProvider);
	}

	[TestMethod]
	public void Deserialize_IgnoresMembersTheClientDoesNotModel()
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>(
			"""{ "workspace": { "workspaceFolders": { "supported": true } }, "experimental": [1, 2], "hoverProvider": true }"""
		);

		Assert.IsTrue(capabilities.HoverProvider?.IsSupported);
	}

	[TestMethod]
	[DataRow("""{ "resolveProvider": true }""", true, DisplayName = "A resolving provider")]
	[DataRow("""{ "resolveProvider": false }""", false, DisplayName = "A provider that declines to resolve")]
	[DataRow("{}", null, DisplayName = "An options object without a resolve flag")]
	[DataRow("""{ "resolveProvider": null }""", null, DisplayName = "A null resolve flag")]
	public void Deserialize_CompletionProvider_ReadsTheResolveFlag(string optionsJson, bool? expectedResolveProvider)
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>($$"""{ "completionProvider": {{optionsJson}} }""");

		Assert.IsNotNull(capabilities.CompletionProvider);
		Assert.AreEqual(expectedResolveProvider, capabilities.CompletionProvider.Value.ResolveProvider);
	}

	[TestMethod]
	[DataRow("true", DisplayName = "A boolean instead of an options object")]
	[DataRow("""{ "resolveProvider": "yes" }""", DisplayName = "A non-boolean resolve flag")]
	public void Deserialize_CompletionProviderWithAMalformedShape_FailsTheResponse(string optionsJson)
	{
		Assert.ThrowsExactly<JsonException>(() =>
			WireJsonTestHelpers.Deserialize<ServerCapabilities>($$"""{ "completionProvider": {{optionsJson}} }""")
		);
	}

	[TestMethod]
	[DataRow("""{ "legend": { "tokenTypes": ["function"], "tokenModifiers": ["declaration"] } }""", DisplayName = "A legend without a full member")]
	[DataRow(
		"""{ "full": true, "legend": { "tokenTypes": ["function"], "tokenModifiers": ["declaration"] } }""",
		DisplayName = "A legend with a full member"
	)]
	public void Deserialize_SemanticTokensProvider_ReadsTheLegend(string providerJson)
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>($$"""{ "semanticTokensProvider": {{providerJson}} }""");

		Assert.AreSequenceEqual(["function"], capabilities.SemanticTokensProvider?.Legend?.TokenTypes);
		Assert.AreSequenceEqual(["declaration"], capabilities.SemanticTokensProvider?.Legend?.TokenModifiers);
	}

	[TestMethod]
	public void Deserialize_SemanticTokensProviderWithoutALegend_LeavesTheLegendNull()
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>("""{ "semanticTokensProvider": { "full": true } }""");

		Assert.IsTrue(capabilities.SemanticTokensProvider?.Full?.IsSupported);
		Assert.IsNull(capabilities.SemanticTokensProvider?.Legend);
	}

	[TestMethod]
	public void Deserialize_ExplicitNullProviders_LeaveThemNull()
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>(
			"""
			{
			  "textDocumentSync": { "change": 1 },
			  "referencesProvider": null,
			  "renameProvider": null,
			  "documentFormattingProvider": null,
			  "documentSymbolProvider": null,
			  "codeActionProvider": null,
			  "completionProvider": null,
			  "semanticTokensProvider": null
			}
			"""
		);

		Assert.IsNull(capabilities.ReferencesProvider);
		Assert.IsNull(capabilities.RenameProvider);
		Assert.IsNull(capabilities.DocumentFormattingProvider);
		Assert.IsNull(capabilities.DocumentSymbolProvider);
		Assert.IsNull(capabilities.CodeActionProvider);
		Assert.IsNull(capabilities.CompletionProvider);
		Assert.IsNull(capabilities.SemanticTokensProvider);
	}

	[TestMethod]
	public void InitializeResponse_WithNullCapabilities_LeavesThemNull() =>
		Assert.IsNull(WireJsonTestHelpers.Deserialize<InitializeResponse>("""{ "capabilities": null }""").Capabilities);

	[TestMethod]
	public void Serialize_RoundTripsTheNormalizedCapabilityShapes()
	{
		InitializeResponse response = WireJsonTestHelpers.Deserialize<InitializeResponse>(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": 2,
			    "referencesProvider": { "dynamicRegistration": true },
			    "semanticTokensProvider": {
			      "full": { "delta": true },
			      "legend": { "tokenTypes": ["function"], "tokenModifiers": ["declaration"] }
			    }
			  }
			}
			"""
		);

		using var document = JsonDocument.Parse(WireJsonTestHelpers.Serialize(response));

		JsonElement capabilities = document.RootElement.GetProperty("capabilities");

		Assert.AreEqual(2, capabilities.GetProperty("textDocumentSync").GetInt32());
		Assert.IsTrue(capabilities.GetProperty("referencesProvider").GetBoolean());
		Assert.IsTrue(capabilities.GetProperty("semanticTokensProvider").GetProperty("full").GetBoolean());
	}
}
