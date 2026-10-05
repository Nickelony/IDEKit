using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

/// <summary>Pins the declared member values of the shared markup-kind vocabulary so the protocol round-trip mapping cannot drift silently.</summary>
[TestClass]
public sealed class MarkupKindTests
{
	[TestMethod]
	[DataRow(MarkupKind.PlainText, 0)]
	[DataRow(MarkupKind.Markdown, 1)]
	public void MarkupKind_DeclaredValues_AreStable(MarkupKind kind, int expectedValue) => Assert.AreEqual(expectedValue, (int)kind);
}
