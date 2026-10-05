using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DocumentSymbolKindConversionTests
{
	[TestMethod]
	public void TryFromProtocol_MapsEveryMemberOfBothEnums()
	{
		// The full LSP 3.17 SymbolKind set in protocol order (ordinal values 1-26): every defined
		// protocol value resolves one-to-one.
		DocumentSymbolKind[] expectedKinds =
		[
			DocumentSymbolKind.File,
			DocumentSymbolKind.Module,
			DocumentSymbolKind.Namespace,
			DocumentSymbolKind.Package,
			DocumentSymbolKind.Class,
			DocumentSymbolKind.Method,
			DocumentSymbolKind.Property,
			DocumentSymbolKind.Field,
			DocumentSymbolKind.Constructor,
			DocumentSymbolKind.Enum,
			DocumentSymbolKind.Interface,
			DocumentSymbolKind.Function,
			DocumentSymbolKind.Variable,
			DocumentSymbolKind.Constant,
			DocumentSymbolKind.String,
			DocumentSymbolKind.Number,
			DocumentSymbolKind.Boolean,
			DocumentSymbolKind.Array,
			DocumentSymbolKind.Object,
			DocumentSymbolKind.Key,
			DocumentSymbolKind.Null,
			DocumentSymbolKind.EnumMember,
			DocumentSymbolKind.Struct,
			DocumentSymbolKind.Event,
			DocumentSymbolKind.Operator,
			DocumentSymbolKind.TypeParameter,
		];

		Assert.HasCount(expectedKinds.Length, Enum.GetValues<Lsp.SymbolKind>(), "A protocol member without a row is unmapped.");
		Assert.HasCount(expectedKinds.Length, Enum.GetValues<DocumentSymbolKind>(), "A model member without a row is never produced.");

		for (int protocolValue = 1; protocolValue <= expectedKinds.Length; protocolValue++)
		{
			Assert.IsTrue(DocumentSymbolKindConversion.TryFromProtocol((Lsp.SymbolKind)protocolValue, out DocumentSymbolKind kind));
			Assert.AreEqual(expectedKinds[protocolValue - 1], kind, $"LSP kind {protocolValue} must map one-to-one.");
		}
	}

	[TestMethod]
	[DataRow(-2)]
	[DataRow(0)]
	[DataRow(27)]
	[DataRow(int.MinValue)]
	[DataRow(int.MaxValue)]
	public void TryFromProtocol_OutOfProtocolRange_ReturnsFalse(int protocolValue)
	{
		Assert.IsFalse(DocumentSymbolKindConversion.TryFromProtocol((Lsp.SymbolKind)protocolValue, out DocumentSymbolKind kind));
		Assert.AreEqual(default, kind);
	}
}
