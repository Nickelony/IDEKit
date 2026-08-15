using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.IDEKit.IntelliSense.Tests.DocumentSymbols;

[TestClass]
public sealed class TextDocumentSymbolKindTests
{
	[TestMethod]
	public void Members_MatchLspSymbolKindValues()
	{
		// Pin the numeric values so renumbering a member cannot silently break the LSP mapping.
		// MSTEST0032 is suppressed because asserting literal constants is the entire point of this
		// API-stability test.
#pragma warning disable MSTEST0032
		Assert.AreEqual(1, (int)TextDocumentSymbolKind.File);
		Assert.AreEqual(2, (int)TextDocumentSymbolKind.Module);
		Assert.AreEqual(3, (int)TextDocumentSymbolKind.Namespace);
		Assert.AreEqual(4, (int)TextDocumentSymbolKind.Package);
		Assert.AreEqual(5, (int)TextDocumentSymbolKind.Class);
		Assert.AreEqual(6, (int)TextDocumentSymbolKind.Method);
		Assert.AreEqual(7, (int)TextDocumentSymbolKind.Property);
		Assert.AreEqual(8, (int)TextDocumentSymbolKind.Field);
		Assert.AreEqual(9, (int)TextDocumentSymbolKind.Constructor);
		Assert.AreEqual(10, (int)TextDocumentSymbolKind.Enum);
		Assert.AreEqual(11, (int)TextDocumentSymbolKind.Interface);
		Assert.AreEqual(12, (int)TextDocumentSymbolKind.Function);
		Assert.AreEqual(13, (int)TextDocumentSymbolKind.Variable);
		Assert.AreEqual(14, (int)TextDocumentSymbolKind.Constant);
		Assert.AreEqual(15, (int)TextDocumentSymbolKind.String);
		Assert.AreEqual(16, (int)TextDocumentSymbolKind.Number);
		Assert.AreEqual(17, (int)TextDocumentSymbolKind.Boolean);
		Assert.AreEqual(18, (int)TextDocumentSymbolKind.Array);
		Assert.AreEqual(19, (int)TextDocumentSymbolKind.Object);
		Assert.AreEqual(20, (int)TextDocumentSymbolKind.Key);
		Assert.AreEqual(21, (int)TextDocumentSymbolKind.Null);
		Assert.AreEqual(22, (int)TextDocumentSymbolKind.EnumMember);
		Assert.AreEqual(23, (int)TextDocumentSymbolKind.Struct);
		Assert.AreEqual(24, (int)TextDocumentSymbolKind.Event);
		Assert.AreEqual(25, (int)TextDocumentSymbolKind.Operator);
		Assert.AreEqual(26, (int)TextDocumentSymbolKind.TypeParameter);
#pragma warning restore MSTEST0032
	}
}
