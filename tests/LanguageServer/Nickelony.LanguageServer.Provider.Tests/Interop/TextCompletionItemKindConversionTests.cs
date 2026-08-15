using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class TextCompletionItemKindConversionTests
{
	[TestMethod]
	public void FromLspKind_MapsEveryProtocolValueByName()
	{
		// The full LSP 3.17 CompletionItemKind set in protocol order (ordinal values 1-25): the numeric
		// mapping and the identifier agreement are checked against one table so the taxonomy has a
		// single numeric pin.
		string[] lspKindIdentifiers =
		[
			"Text", "Method", "Function", "Constructor", "Field", "Variable", "Class", "Interface",
			"Module", "Property", "Unit", "Value", "Enum", "Keyword", "Snippet", "Color", "File",
			"Reference", "Folder", "EnumMember", "Constant", "Struct", "Event", "Operator", "TypeParameter"
		];

		for (int protocolValue = 1; protocolValue <= lspKindIdentifiers.Length; protocolValue++)
		{
			string identifier = lspKindIdentifiers[protocolValue - 1];
			TextCompletionItemKind byNumber = TextCompletionItemKindConversion.FromLspKind(protocolValue);
			TextCompletionItemKind byName = TextCompletionItemKind.FromIdentifier(identifier);

			Assert.AreEqual(identifier, byNumber.Identifier, $"LSP kind {protocolValue} must map one-to-one.");
			Assert.AreEqual(byNumber, byName, $"LSP kind {protocolValue} must resolve by name and by number to the same category.");
		}

		// The presentation fallback and the library-only extensions have no LSP counterpart.
		string[] libraryOnlyIdentifiers = ["Generic", "Array", "Section", "Directive", "Parameter", "Namespace"];

		foreach (string identifier in libraryOnlyIdentifiers)
			CollectionAssert.DoesNotContain(lspKindIdentifiers, identifier, $"'{identifier}' must not be part of the LSP taxonomy.");
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	[DataRow(26)]
	[DataRow(int.MaxValue)]
	public void FromLspKind_OutOfProtocolRange_FallsBackToGeneric(int lspKind)
		=> Assert.AreSame(TextCompletionItemKind.Generic, TextCompletionItemKindConversion.FromLspKind(lspKind));
}
