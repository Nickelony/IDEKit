using static Nickelony.IDEKit.TextMate.Tests.TextMateThemeTestHelpers;

namespace Nickelony.IDEKit.TextMate.Tests;

/// <summary>
/// Pins the <c>fontStyle</c> extraction into <see cref="TextMateFontTraits"/>: each recognized trait word enables exactly its own trait,
/// and unmentioned traits stay disabled.
/// </summary>
[TestClass]
public sealed class TextMateFontTraitsTests
{
	[DataRow("bold", true, false, false, false, DisplayName = "Bold")]
	[DataRow("italic", false, true, false, false, DisplayName = "Italic")]
	[DataRow("underline", false, false, true, false, DisplayName = "Underline")]
	[DataRow("strikethrough", false, false, false, true, DisplayName = "Strikethrough")]
	[DataRow("bold italic", true, true, false, false, DisplayName = "BoldItalic")]
	[DataRow("bold italic underline strikethrough", true, true, true, true, DisplayName = "AllTraits")]
	[TestMethod]
	public void Extract_FontStyleWord_EnablesOnlyItsOwnTrait(string fontStyle, bool bold, bool italic, bool underline, bool strikethrough)
	{
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = fontStyle }));

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(bold, style.IsBold, $"'{fontStyle}' must set bold to {bold}.");
		Assert.AreEqual(italic, style.IsItalic, $"'{fontStyle}' must set italic to {italic}.");
		Assert.AreEqual(underline, style.IsUnderline, $"'{fontStyle}' underline.");
		Assert.AreEqual(strikethrough, style.IsStrikethrough, $"'{fontStyle}' strikethrough.");
	}
}
