namespace Nickelony.IDEKit.IntelliSense.Tests;

/// <summary>
/// Pins the declared member values of the shared markup-kind vocabulary so the protocol round-trip
/// mapping cannot drift silently.
/// </summary>
[TestClass]
public sealed class TextMarkupKindTests
{
	[TestMethod]
	[DataRow(TextMarkupKind.PlainText, 0)]
	[DataRow(TextMarkupKind.Markdown, 1)]
	public void MarkupKind_DeclaredValues_AreStable(TextMarkupKind kind, int expectedValue)
		=> Assert.AreEqual(expectedValue, (int)kind);
}
