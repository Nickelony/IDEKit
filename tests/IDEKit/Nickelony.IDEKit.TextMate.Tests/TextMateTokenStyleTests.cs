using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.TextMate.Tests;

/// <summary>Pins what counts as formatting in a resolved <see cref="TextMateTokenStyle"/>.</summary>
[TestClass]
public sealed class TextMateTokenStyleTests
{
	[TestMethod]
	public void Empty_RequestsNoFormatting()
	{
		Assert.IsFalse(TextMateTokenStyle.Empty.HasFormatting);
		Assert.IsNull(TextMateTokenStyle.Empty.Foreground);
	}

	[TestMethod]
	public void HasFormatting_ForegroundOnly_IsTrue() =>
		Assert.IsTrue((TextMateTokenStyle.Empty with { Foreground = ArgbColor.Black }).HasFormatting);

	[TestMethod]
	public void HasFormatting_BoldOnly_IsTrue() => Assert.IsTrue((TextMateTokenStyle.Empty with { IsBold = true }).HasFormatting);

	[TestMethod]
	public void HasFormatting_ItalicOnly_IsTrue() => Assert.IsTrue((TextMateTokenStyle.Empty with { IsItalic = true }).HasFormatting);

	[TestMethod]
	public void HasFormatting_UnderlineOnly_IsTrue() => Assert.IsTrue((TextMateTokenStyle.Empty with { IsUnderline = true }).HasFormatting);

	[TestMethod]
	public void HasFormatting_StrikethroughOnly_IsTrue() => Assert.IsTrue((TextMateTokenStyle.Empty with { IsStrikethrough = true }).HasFormatting);
}
