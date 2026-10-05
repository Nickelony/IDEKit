using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class LanguageServerClientCapabilitiesBuilderTests
{
	private static JsonElement Build(LanguageServerClientCapabilitiesOptions options) =>
		JsonSerializer.SerializeToElement(LanguageServerClientCapabilitiesBuilder.Build(options));

	private static string?[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(entry => entry.GetString())];

	[TestMethod]
	public void Create_NullOptions_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => LanguageServerClientCapabilitiesBuilder.Build(null!));

	[TestMethod]
	public void Create_DefaultOptions_AdvertiseTheSharedBaselineOnly()
	{
		JsonElement capabilities = Build(LanguageServerClientCapabilitiesOptions.Default);
		JsonElement workspace = capabilities.GetProperty("workspace");
		JsonElement textDocument = capabilities.GetProperty("textDocument");

		Assert.IsTrue(textDocument.GetProperty("completion").GetProperty("completionItem").GetProperty("snippetSupport").GetBoolean());
		Assert.IsTrue(workspace.GetProperty("semanticTokens").GetProperty("refreshSupport").GetBoolean());
		Assert.IsTrue(textDocument.GetProperty("publishDiagnostics").GetProperty("versionSupport").GetBoolean());

		JsonElement kinds = textDocument
			.GetProperty("codeAction")
			.GetProperty("codeActionLiteralSupport")
			.GetProperty("codeActionKind")
			.GetProperty("valueSet");

		Assert.AreEqual(0, kinds.GetArrayLength());
		Assert.IsFalse(textDocument.TryGetProperty("diagnostic", out _));
		Assert.IsFalse(workspace.TryGetProperty("diagnostics", out _));
	}

	[TestMethod]
	public void Create_DefaultOptions_AdvertiseCodeActionDataAndResolveOfEditAndCommand()
	{
		JsonElement codeAction = Build(LanguageServerClientCapabilitiesOptions.Default).GetProperty("textDocument").GetProperty("codeAction");

		Assert.IsTrue(codeAction.GetProperty("dataSupport").GetBoolean());
		Assert.AreSequenceEqual(["edit", "command"], Strings(codeAction.GetProperty("resolveSupport").GetProperty("properties")));
	}

	[TestMethod]
	public void Create_DefaultOptions_AdvertiseWorkspaceSymbolsWithEveryMappedKindAndTheRangeResolve()
	{
		JsonElement symbol = Build(LanguageServerClientCapabilitiesOptions.Default).GetProperty("workspace").GetProperty("symbol");

		Assert.IsFalse(symbol.GetProperty("dynamicRegistration").GetBoolean());

		Assert.AreSequenceEqual(
			[.. Enumerable.Range(1, 26)],
			[.. symbol.GetProperty("symbolKind").GetProperty("valueSet").EnumerateArray().Select(kind => kind.GetInt32())]
		);

		Assert.AreSequenceEqual(["location.range"], Strings(symbol.GetProperty("resolveSupport").GetProperty("properties")));
	}

	[TestMethod]
	public void Create_DefaultOptions_AdvertiseDocumentSymbolsWithTheHierarchyAndEveryMappedKind()
	{
		JsonElement documentSymbol = Build(LanguageServerClientCapabilitiesOptions.Default).GetProperty("textDocument").GetProperty("documentSymbol");

		Assert.IsTrue(documentSymbol.GetProperty("hierarchicalDocumentSymbolSupport").GetBoolean());

		Assert.AreSequenceEqual(
			[.. Enumerable.Range(1, 26)],
			[.. documentSymbol.GetProperty("symbolKind").GetProperty("valueSet").EnumerateArray().Select(kind => kind.GetInt32())]
		);
	}

	[TestMethod]
	public void Create_DefaultOptions_PinTheSharedSemanticTokenLegend()
	{
		JsonElement semanticTokens = Build(LanguageServerClientCapabilitiesOptions.Default).GetProperty("textDocument").GetProperty("semanticTokens");

		string?[] legend = Strings(semanticTokens.GetProperty("tokenTypes"));

		// The legend mirrors the shared LSP 3.17 + 3.18 vocabulary; the exact list is pinned so a
		// dropped entry cannot silently degrade token colors.
		string?[] expectedLegend =
		[
			"namespace",
			"type",
			"class",
			"enum",
			"interface",
			"struct",
			"typeParameter",
			"parameter",
			"variable",
			"property",
			"enumMember",
			"event",
			"function",
			"method",
			"macro",
			"keyword",
			"modifier",
			"comment",
			"string",
			"number",
			"regexp",
			"operator",
			"decorator",
			"label",
		];

		Assert.AreSequenceEqual(expectedLegend, legend);
		Assert.AreAllDistinct(legend);

		string?[] modifiers = Strings(semanticTokens.GetProperty("tokenModifiers"));

		string?[] expectedModifiers =
		[
			"declaration",
			"definition",
			"readonly",
			"static",
			"deprecated",
			"abstract",
			"async",
			"modification",
			"documentation",
			"defaultLibrary",
		];

		Assert.AreSequenceEqual(expectedModifiers, modifiers);
		Assert.AreAllDistinct(modifiers);
	}

	[TestMethod]
	public void Create_CodeActionKinds_ArePublishedInCallerOrder()
	{
		JsonElement capabilities = Build(new LanguageServerClientCapabilitiesOptions { CodeActionKinds = ["", "quickfix", "refactor.extract"] });

		JsonElement kinds = capabilities
			.GetProperty("textDocument")
			.GetProperty("codeAction")
			.GetProperty("codeActionLiteralSupport")
			.GetProperty("codeActionKind")
			.GetProperty("valueSet");

		Assert.AreSequenceEqual(["", "quickfix", "refactor.extract"], Strings(kinds));
	}

	[TestMethod]
	public void Create_PullDiagnostics_AddsThePullCapabilityAndItsWorkspaceRefresh()
	{
		JsonElement capabilities = Build(new LanguageServerClientCapabilitiesOptions { SupportsPullDiagnostics = true });

		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("diagnostics").GetProperty("refreshSupport").GetBoolean());
		Assert.IsTrue(capabilities.GetProperty("textDocument").GetProperty("diagnostic").GetProperty("relatedDocumentSupport").GetBoolean());
	}

	[TestMethod]
	public void Create_ExtraSemanticTokenModifiers_FollowTheSharedModifiers()
	{
		string?[] shared = Strings(
			Build(LanguageServerClientCapabilitiesOptions.Default)
				.GetProperty("textDocument")
				.GetProperty("semanticTokens")
				.GetProperty("tokenModifiers")
		);

		JsonElement capabilities = Build(new LanguageServerClientCapabilitiesOptions { ExtraSemanticTokenModifiers = ["global", "special"] });
		string?[] modifiers = Strings(capabilities.GetProperty("textDocument").GetProperty("semanticTokens").GetProperty("tokenModifiers"));

		Assert.AreSequenceEqual([.. shared, "global", "special"], modifiers);
	}

	[TestMethod]
	public void Create_ReturnsAFreshPayloadEachCall()
	{
		JsonObject first = LanguageServerClientCapabilitiesBuilder.Build(LanguageServerClientCapabilitiesOptions.Default);
		first["workspace"] = null;

		JsonObject second = LanguageServerClientCapabilitiesBuilder.Build(LanguageServerClientCapabilitiesOptions.Default);

		Assert.IsNotNull(second["workspace"]);
	}
}
