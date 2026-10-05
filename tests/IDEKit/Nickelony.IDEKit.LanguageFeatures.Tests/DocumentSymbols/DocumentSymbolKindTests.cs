using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DocumentSymbolKindTests
{
	[TestMethod]
	public void Members_MatchLspSymbolKindValues()
	{
		// Pin the numeric values so renumbering a member cannot silently break the LSP mapping.
		// MSTEST0032 is suppressed because asserting literal constants is the entire point of this
		// API-stability test.
#pragma warning disable MSTEST0032
		Assert.AreEqual(1, (int)DocumentSymbolKind.File);
		Assert.AreEqual(2, (int)DocumentSymbolKind.Module);
		Assert.AreEqual(3, (int)DocumentSymbolKind.Namespace);
		Assert.AreEqual(4, (int)DocumentSymbolKind.Package);
		Assert.AreEqual(5, (int)DocumentSymbolKind.Class);
		Assert.AreEqual(6, (int)DocumentSymbolKind.Method);
		Assert.AreEqual(7, (int)DocumentSymbolKind.Property);
		Assert.AreEqual(8, (int)DocumentSymbolKind.Field);
		Assert.AreEqual(9, (int)DocumentSymbolKind.Constructor);
		Assert.AreEqual(10, (int)DocumentSymbolKind.Enum);
		Assert.AreEqual(11, (int)DocumentSymbolKind.Interface);
		Assert.AreEqual(12, (int)DocumentSymbolKind.Function);
		Assert.AreEqual(13, (int)DocumentSymbolKind.Variable);
		Assert.AreEqual(14, (int)DocumentSymbolKind.Constant);
		Assert.AreEqual(15, (int)DocumentSymbolKind.String);
		Assert.AreEqual(16, (int)DocumentSymbolKind.Number);
		Assert.AreEqual(17, (int)DocumentSymbolKind.Boolean);
		Assert.AreEqual(18, (int)DocumentSymbolKind.Array);
		Assert.AreEqual(19, (int)DocumentSymbolKind.Object);
		Assert.AreEqual(20, (int)DocumentSymbolKind.Key);
		Assert.AreEqual(21, (int)DocumentSymbolKind.Null);
		Assert.AreEqual(22, (int)DocumentSymbolKind.EnumMember);
		Assert.AreEqual(23, (int)DocumentSymbolKind.Struct);
		Assert.AreEqual(24, (int)DocumentSymbolKind.Event);
		Assert.AreEqual(25, (int)DocumentSymbolKind.Operator);
		Assert.AreEqual(26, (int)DocumentSymbolKind.TypeParameter);
#pragma warning restore MSTEST0032
	}
}
