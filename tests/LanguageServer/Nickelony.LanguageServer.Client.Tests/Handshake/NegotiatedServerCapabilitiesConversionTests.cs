namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class NegotiatedServerCapabilitiesConversionTests
{
	private static InitializeResponse Response(string capabilitiesJson) =>
		WireJsonTestHelpers.Deserialize<InitializeResponse>($$"""{ "capabilities": {{capabilitiesJson}} }""");

	[TestMethod]
	public void FromInitializeResponse_NullResponse_ThrowsNotSupportedException()
	{
		Assert.ThrowsExactly<NotSupportedException>(() =>
			NegotiatedServerCapabilitiesConversion.FromInitializeResponse(null, requireTextDocumentSynchronization: false)
		);
	}

	[TestMethod]
	public void FromInitializeResponse_ResponseWithoutCapabilities_ThrowsNotSupportedException()
	{
		Assert.ThrowsExactly<NotSupportedException>(() =>
			NegotiatedServerCapabilitiesConversion.FromInitializeResponse(new InitializeResponse(), requireTextDocumentSynchronization: false)
		);
	}

	[TestMethod]
	[DataRow("utf-8")]
	[DataRow("utf-32")]
	public void FromInitializeResponse_PositionEncodingOtherThanUtf16_ThrowsNotSupportedException(string encoding)
	{
		Assert.ThrowsExactly<NotSupportedException>(() =>
			NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
				Response($$"""{ "positionEncoding": "{{encoding}}", "textDocumentSync": 1 }"""),
				requireTextDocumentSynchronization: false
			)
		);
	}

	[TestMethod]
	[DataRow("utf-16")]
	[DataRow("UTF-16")]
	public void FromInitializeResponse_Utf16PositionEncoding_IsAccepted(string encoding)
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response($$"""{ "positionEncoding": "{{encoding}}", "textDocumentSync": 1 }"""),
			requireTextDocumentSynchronization: true
		);

		Assert.AreEqual(TextDocumentSyncKind.Full, negotiated.TextDocumentSyncKind);
	}

	[TestMethod]
	public void FromInitializeResponse_NoTextSynchronizationWhenRequired_ThrowsNotSupportedException()
	{
		Assert.ThrowsExactly<NotSupportedException>(() =>
			NegotiatedServerCapabilitiesConversion.FromInitializeResponse(Response("{ }"), requireTextDocumentSynchronization: true)
		);
	}

	[TestMethod]
	public void FromInitializeResponse_NoTextSynchronizationWhenNotRequired_ReadsNone()
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response("{ }"),
			requireTextDocumentSynchronization: false
		);

		Assert.AreEqual(TextDocumentSyncKind.None, negotiated.TextDocumentSyncKind);
		Assert.IsFalse(negotiated.SupportsHover);
		Assert.IsEmpty(negotiated.SemanticTokenTypes);
	}

	[TestMethod]
	public void FromInitializeResponse_AdvertisedProviders_SetTheirFlags()
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response(
				"""
				{
				  "textDocumentSync": 2,
				  "hoverProvider": true,
				  "definitionProvider": true,
				  "referencesProvider": true,
				  "renameProvider": true,
				  "documentSymbolProvider": true,
				  "documentFormattingProvider": true,
				  "signatureHelpProvider": { "triggerCharacters": ["("] },
				  "completionProvider": { "resolveProvider": true },
				  "codeActionProvider": { "resolveProvider": true },
				  "diagnosticProvider": { "interFileDependencies": false, "workspaceDiagnostics": false }
				}
				"""
			),

			requireTextDocumentSynchronization: true
		);

		Assert.AreEqual(TextDocumentSyncKind.Incremental, negotiated.TextDocumentSyncKind);
		Assert.IsTrue(negotiated.SupportsHover);
		Assert.IsTrue(negotiated.SupportsDefinition);
		Assert.IsTrue(negotiated.SupportsReferences);
		Assert.IsTrue(negotiated.SupportsRename);
		Assert.IsTrue(negotiated.SupportsDocumentSymbols);
		Assert.IsTrue(negotiated.SupportsFormatting);
		Assert.IsTrue(negotiated.SupportsSignatureHelp);
		Assert.IsTrue(negotiated.SupportsCompletionResolve);
		Assert.IsTrue(negotiated.SupportsCodeActions);
		Assert.IsTrue(negotiated.SupportsCodeActionResolve);
		Assert.IsTrue(negotiated.SupportsPullDiagnostics);
	}

	[TestMethod]
	[DataRow("""{ "textDocumentSync": 2 }""", false, false, DisplayName = "Not advertised")]
	[DataRow("""{ "textDocumentSync": 2, "workspaceSymbolProvider": false }""", false, false, DisplayName = "Declined")]
	[DataRow("""{ "textDocumentSync": 2, "workspaceSymbolProvider": true }""", true, false, DisplayName = "Advertised as a boolean")]
	[DataRow("""{ "textDocumentSync": 2, "workspaceSymbolProvider": {} }""", true, false, DisplayName = "Advertised without resolve")]
	[DataRow(
		"""{ "textDocumentSync": 2, "workspaceSymbolProvider": { "resolveProvider": true } }""",
		true,
		true,
		DisplayName = "Advertised with resolve"
	)]
	public void FromInitializeResponse_WorkspaceSymbolProvider_SetsTheSymbolAndTheResolveFlags(
		string capabilities,
		bool expectedSymbols,
		bool expectedResolve
	)
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response(capabilities),
			requireTextDocumentSynchronization: true
		);

		Assert.AreEqual(expectedSymbols, negotiated.SupportsWorkspaceSymbols);
		Assert.AreEqual(expectedResolve, negotiated.SupportsWorkspaceSymbolResolve);
	}

	[TestMethod]
	public void FromInitializeResponse_TriggerCharacters_AreReadAndAMalformedEntryIsSkipped()
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response(
				"""
				{
				  "textDocumentSync": 1,
				  "completionProvider": { "triggerCharacters": [".", ":", 7] },
				  "signatureHelpProvider": { "triggerCharacters": ["(", ","], "retriggerCharacters": [")"] }
				}
				"""
			),

			requireTextDocumentSynchronization: true
		);

		Assert.AreSequenceEqual([".", ":"], [.. negotiated.CompletionTriggerCharacters]);
		Assert.AreSequenceEqual(["(", ","], [.. negotiated.SignatureHelpTriggerCharacters]);
		Assert.AreSequenceEqual([")"], [.. negotiated.SignatureHelpRetriggerCharacters]);
	}

	[TestMethod]
	public void FromInitializeResponse_ExecuteCommandProvider_ListsTheCommandsAndSkipsAMalformedEntry()
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response("""{ "textDocumentSync": 1, "executeCommandProvider": { "commands": ["lua.setConfig", 7, "lua.solve"] } }"""),
			requireTextDocumentSynchronization: true
		);

		Assert.AreSequenceEqual(["lua.setConfig", "lua.solve"], [.. negotiated.ExecuteCommands]);
	}

	[TestMethod]
	[DataRow("{ }")]
	[DataRow("""{ "executeCommandProvider": { } }""")]
	public void FromInitializeResponse_WithoutExecuteCommands_ListsNone(string extraMembers)
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response(extraMembers),
			requireTextDocumentSynchronization: false
		);

		Assert.IsEmpty(negotiated.ExecuteCommands);
	}

	[TestMethod]
	[DataRow("true")]
	[DataRow("{ }")]
	public void FromInitializeResponse_SignatureHelpWithoutCharacters_IsSupportedWithEmptyLists(string signatureHelpProvider)
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response($$"""{ "textDocumentSync": 1, "signatureHelpProvider": {{signatureHelpProvider}} }"""),
			requireTextDocumentSynchronization: true
		);

		Assert.IsTrue(negotiated.SupportsSignatureHelp);
		Assert.IsEmpty(negotiated.SignatureHelpTriggerCharacters);
		Assert.IsEmpty(negotiated.CompletionTriggerCharacters);
	}

	[TestMethod]
	public void FromInitializeResponse_UnadvertisedProviders_ReadFalse()
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response("""{ "textDocumentSync": 1, "completionProvider": { }, "hoverProvider": false }"""),
			requireTextDocumentSynchronization: true
		);

		Assert.IsFalse(negotiated.SupportsCompletionResolve);
		Assert.IsFalse(negotiated.SupportsHover);
		Assert.IsFalse(negotiated.SupportsCodeActions);
		Assert.IsFalse(negotiated.SupportsSemanticTokensFull);
	}

	[TestMethod]
	public void FromInitializeResponse_SemanticTokensProvider_ReadsTheLegendAndTheFullFlag()
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response(
				"""
				{
				  "textDocumentSync": 1,
				  "semanticTokensProvider": { "legend": { "tokenTypes": ["variable", "function"], "tokenModifiers": ["static"] }, "full": true }
				}
				"""
			),

			requireTextDocumentSynchronization: true
		);

		Assert.IsTrue(negotiated.SupportsSemanticTokensFull);
		Assert.AreSequenceEqual(["variable", "function"], [.. negotiated.SemanticTokenTypes]);
		Assert.AreSequenceEqual(["static"], [.. negotiated.SemanticTokenModifiers]);
	}

	[TestMethod]
	public void FromInitializeResponse_SemanticTokensProviderWithoutLegend_LeavesTheLegendEmpty()
	{
		NegotiatedServerCapabilities negotiated = NegotiatedServerCapabilitiesConversion.FromInitializeResponse(
			Response("""{ "textDocumentSync": 1, "semanticTokensProvider": { "full": { } } }"""),
			requireTextDocumentSynchronization: true
		);

		Assert.IsTrue(negotiated.SupportsSemanticTokensFull);
		Assert.IsEmpty(negotiated.SemanticTokenTypes);
		Assert.IsEmpty(negotiated.SemanticTokenModifiers);
	}
}
