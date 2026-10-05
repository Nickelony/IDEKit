using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class MarkupContentTests
{
	[TestMethod]
	public void Constructor_PlainText_IsTrimmed()
	{
		var content = new MarkupContent("  text \r\n", MarkupKind.PlainText);

		Assert.AreEqual("text", content.Value);
		Assert.AreEqual(MarkupKind.PlainText, content.Kind);
	}

	[TestMethod]
	public void Constructor_Markdown_KeepsSurroundingWhitespace()
	{
		var content = new MarkupContent("  **text**  ", MarkupKind.Markdown);

		Assert.AreEqual("  **text**  ", content.Value);
		Assert.AreEqual(MarkupKind.Markdown, content.Kind);
	}

	[TestMethod]
	public void Constructor_OmittedKind_IsPlainText() => Assert.AreEqual(MarkupKind.PlainText, new MarkupContent("text").Kind);

	[TestMethod]
	public void Constructor_NullValue_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new MarkupContent(null!));

	[TestMethod]
	public void Constructor_UndefinedKind_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MarkupContent("text", (MarkupKind)42));

	[TestMethod]
	public void IsBlank_ReportsWhitespaceOnlyContent()
	{
		Assert.IsTrue(new MarkupContent("   ").IsBlank);
		Assert.IsTrue(new MarkupContent("  \n ", MarkupKind.Markdown).IsBlank);
		Assert.IsFalse(new MarkupContent("text").IsBlank);
	}

	[TestMethod]
	public void Equality_ComparesTextAndKind()
	{
		Assert.AreEqual(new MarkupContent("text"), new MarkupContent(" text "));
		Assert.AreNotEqual(new MarkupContent("text"), new MarkupContent("text", MarkupKind.Markdown));
		Assert.AreNotEqual(new MarkupContent("text"), new MarkupContent("other"));
	}

	[TestMethod]
	public void ImplicitConversion_FromString_IsPlainText()
	{
		MarkupContent? content = "  text  ";
		Assert.AreEqual(MarkupContent.FromPlainText("text"), content);
	}

	[TestMethod]
	public void ImplicitConversion_FromNull_IsNull()
	{
		string? value = null;
		MarkupContent? content = value;

		Assert.IsNull(content);
	}

	[TestMethod]
	public void FromPlainText_NullValue_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => MarkupContent.FromPlainText(null!));
}
