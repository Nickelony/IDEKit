using Nickelony.IDEKit.IntelliSense;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Pins the value equality of <see cref="ProtocolMarkupContent"/>: the effective text and the markup kind decide
/// equality, so a <see langword="default"/> instance and an instance built with empty text are equal.
/// </summary>
[TestClass]
public sealed class ProtocolMarkupContentTests
{
	[TestMethod]
	public void Equality_DefaultAndEmptyPlainText_AreEqual()
	{
		var constructed = new ProtocolMarkupContent(string.Empty, TextMarkupKind.PlainText);

		Assert.AreEqual(default(ProtocolMarkupContent), constructed);
		Assert.AreEqual(default(ProtocolMarkupContent).GetHashCode(), constructed.GetHashCode());
	}

	[TestMethod]
	public void Equality_NullAndEmptyText_AreEqual()
	{
		Assert.AreEqual(
			new ProtocolMarkupContent(null, TextMarkupKind.PlainText),
			new ProtocolMarkupContent(string.Empty, TextMarkupKind.PlainText));
	}

	[TestMethod]
	public void Equality_DifferentTextOrKind_AreNotEqual()
	{
		Assert.AreNotEqual(
			new ProtocolMarkupContent("x", TextMarkupKind.PlainText),
			new ProtocolMarkupContent("y", TextMarkupKind.PlainText));
		Assert.AreNotEqual(
			new ProtocolMarkupContent("x", TextMarkupKind.PlainText),
			new ProtocolMarkupContent("x", TextMarkupKind.Markdown));
	}
}
