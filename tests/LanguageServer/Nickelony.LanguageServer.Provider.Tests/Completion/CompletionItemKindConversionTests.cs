using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class CompletionItemKindConversionTests
{
	[TestMethod]
	public void FromProtocol_MapsEveryProtocolValueByName()
	{
		// The full LSP 3.17 CompletionItemKind set in protocol order (ordinal values 1-25): the numeric
		// mapping and the identifier agreement are checked against one table so the taxonomy has a
		// single numeric pin.
		string[] lspKindIdentifiers =
		[
			"Text",
			"Method",
			"Function",
			"Constructor",
			"Field",
			"Variable",
			"Class",
			"Interface",
			"Module",
			"Property",
			"Unit",
			"Value",
			"Enum",
			"Keyword",
			"Snippet",
			"Color",
			"File",
			"Reference",
			"Folder",
			"EnumMember",
			"Constant",
			"Struct",
			"Event",
			"Operator",
			"TypeParameter",
		];

		for (int protocolValue = 1; protocolValue <= lspKindIdentifiers.Length; protocolValue++)
		{
			string identifier = lspKindIdentifiers[protocolValue - 1];
			CompletionItemKind byNumber = CompletionItemKindConversion.FromProtocol((Lsp.CompletionItemKind)protocolValue);

			var byName = CompletionItemKind.FromIdentifier(identifier);

			Assert.AreEqual(identifier, byNumber.Identifier, $"LSP kind {protocolValue} must map one-to-one.");
			Assert.AreEqual(byNumber, byName, $"LSP kind {protocolValue} must resolve by name and by number to the same category.");
		}

		// The presentation fallback and the library-only extensions have no LSP counterpart.
		string[] libraryOnlyIdentifiers = ["Generic", "Array", "Section", "Directive", "Parameter", "Namespace"];

		foreach (string identifier in libraryOnlyIdentifiers)
			Assert.DoesNotContain(identifier, lspKindIdentifiers, $"'{identifier}' must not be part of the LSP taxonomy.");
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	[DataRow(26)]
	[DataRow(int.MaxValue)]
	public void FromProtocol_OutOfProtocolRange_FallsBackToGeneric(int lspKind) =>
		Assert.AreSame(CompletionItemKind.Generic, CompletionItemKindConversion.FromProtocol((Lsp.CompletionItemKind)lspKind));

	[TestMethod]
	public void FromProtocol_OmittedKind_FallsBackToGeneric() =>
		Assert.AreSame(CompletionItemKind.Generic, CompletionItemKindConversion.FromProtocol(null));
}
