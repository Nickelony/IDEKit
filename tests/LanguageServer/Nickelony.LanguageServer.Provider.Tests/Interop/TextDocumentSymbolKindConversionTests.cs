using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class TextDocumentSymbolKindConversionTests
{
	[TestMethod]
	public void FromLspKind_MapsEveryProtocolValue()
	{
		// The full LSP 3.17 SymbolKind set in protocol order (ordinal values 1-26): every defined
		// protocol value resolves one-to-one.
		TextDocumentSymbolKind[] expectedKinds =
		[
			TextDocumentSymbolKind.File,
			TextDocumentSymbolKind.Module,
			TextDocumentSymbolKind.Namespace,
			TextDocumentSymbolKind.Package,
			TextDocumentSymbolKind.Class,
			TextDocumentSymbolKind.Method,
			TextDocumentSymbolKind.Property,
			TextDocumentSymbolKind.Field,
			TextDocumentSymbolKind.Constructor,
			TextDocumentSymbolKind.Enum,
			TextDocumentSymbolKind.Interface,
			TextDocumentSymbolKind.Function,
			TextDocumentSymbolKind.Variable,
			TextDocumentSymbolKind.Constant,
			TextDocumentSymbolKind.String,
			TextDocumentSymbolKind.Number,
			TextDocumentSymbolKind.Boolean,
			TextDocumentSymbolKind.Array,
			TextDocumentSymbolKind.Object,
			TextDocumentSymbolKind.Key,
			TextDocumentSymbolKind.Null,
			TextDocumentSymbolKind.EnumMember,
			TextDocumentSymbolKind.Struct,
			TextDocumentSymbolKind.Event,
			TextDocumentSymbolKind.Operator,
			TextDocumentSymbolKind.TypeParameter
		];

		for (int protocolValue = 1; protocolValue <= expectedKinds.Length; protocolValue++)
		{
			Assert.AreEqual(expectedKinds[protocolValue - 1], TextDocumentSymbolKindConversion.FromLspKind(protocolValue),
				$"LSP kind {protocolValue} must map one-to-one.");
		}
	}

	[TestMethod]
	[DataRow(-2)]
	[DataRow(0)]
	[DataRow(27)]
	[DataRow(int.MinValue)]
	[DataRow(int.MaxValue)]
	public void FromLspKind_OutOfProtocolRange_Throws(int lspKind)
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TextDocumentSymbolKindConversion.FromLspKind(lspKind));

	[TestMethod]
	[DataRow(-2)]
	[DataRow(0)]
	[DataRow(27)]
	[DataRow(int.MinValue)]
	[DataRow(int.MaxValue)]
	public void TryFromLspKind_OutOfProtocolRange_ReturnsFalse(int lspKind)
	{
		Assert.IsFalse(TextDocumentSymbolKindConversion.TryFromLspKind(lspKind, out TextDocumentSymbolKind kind));
		Assert.AreEqual(default(TextDocumentSymbolKind), kind);
	}
}
