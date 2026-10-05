#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Media;
using Nickelony.IDEKit.Core.Media;
using SharedControl = Avalonia.Controls.Control;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows;
using System.Windows.Controls;
using Nickelony.IDEKit.Core.Media;
using SharedControl = System.Windows.FrameworkElement;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

/// <summary>The renderer scenarios that run identically across the editor bindings, declared once over the hooks below.</summary>
/// <remarks>
/// A binding subclass walks its own content tree and answers each hook with an engine-neutral value, or with a control type both toolkits name alike,
/// such as <c>Border</c> or <c>TextBlock</c>. A scenario whose content tree has no shared shape stays in the binding's own suite.
/// </remarks>
public abstract partial class MarkdownRendererTestsBase
{
	/// <summary>The kind of inline element a scenario counts in the rendered content.</summary>
	protected enum RenderedInlineKind
	{
		Bold,
		Italic,
		Strikethrough,
		LineBreak,

		/// <summary>A hyperlink, in whatever element the binding renders it as.</summary>
		Link,

		/// <summary>An inline that hosts a control other than a link, such as the surface of inline code.</summary>
		Embedded,
	}

	/// <summary>Returns the text of the only paragraph in the content.</summary>
	protected abstract string GetSingleParagraphText(SharedControl content);

	/// <summary>Returns the font size and weight of the only paragraph in the content.</summary>
	protected abstract (double FontSize, bool IsBold) GetSingleParagraphFont(SharedControl content);

	/// <summary>Counts the inlines of one kind anywhere in the content.</summary>
	protected abstract int CountInlines(SharedControl content, RenderedInlineKind kind);

	/// <summary>Counts the blocks directly under the content root.</summary>
	protected abstract int CountTopLevelBlocks(SharedControl content);

	/// <summary>Returns the surface of the only inline code span in the content.</summary>
	protected abstract Border FindInlineCodeBorder(SharedControl content);

	/// <summary>Returns the surface of the only code block in the content.</summary>
	protected abstract Border FindCodeBlockBorder(SharedControl content);

	/// <summary>Returns the thin separator the only thematic break in the content renders as.</summary>
	protected abstract Border FindThematicBreak(SharedControl content);

	/// <summary>Returns the color of a surface whose background is a single solid color.</summary>
	protected abstract ArgbColor GetSolidBackground(Border border);

	/// <summary>Asserts that a text block uses the font family a theme names.</summary>
	protected abstract void AssertFontFamilyMatches(string? family, TextBlock textBlock);

	/// <summary>Returns the marker text of each item of the only list in the content.</summary>
	protected abstract IReadOnlyList<string> GetListMarkers(SharedControl content);

	/// <summary>Returns the top and bottom margin of the first block of each item of the only list in the content.</summary>
	protected abstract IReadOnlyList<(double Top, double Bottom)> GetListItemMargins(SharedControl content);

	/// <summary>Returns the bar width, the top border width, and the block count of the only blockquote in the content.</summary>
	protected abstract (double BarWidth, double TopWidth, int BlockCount) GetBlockquote(SharedControl content);

	/// <summary>Returns the row and column count of the only table in the content.</summary>
	protected abstract (int Rows, int Columns) GetTableShape(SharedControl content);

	/// <summary>Returns the alignment of the text in one cell of the only table in the content.</summary>
	protected abstract TextAlignment GetTableCellAlignment(SharedControl content, int row, int column);

	/// <summary>Returns the bottom margin of the text in each cell of the only table in the content.</summary>
	protected abstract IReadOnlyList<double> GetTableCellBottomMargins(SharedControl content);

	/// <summary>Returns whether the text in one cell of the only table in the content is bold.</summary>
	protected abstract bool IsTableCellBold(SharedControl content, int row, int column);

	/// <summary>Returns whether the viewer takes keyboard focus and a tab stop and whether its text is selectable.</summary>
	protected abstract (bool Focusable, bool IsTabStop, bool IsSelectable) GetViewerInteraction(SharedControl content);

	[TestMethod]
	public void CreateContent_WhitespaceOnlyContent_ReturnsFallbackScrollViewer()
	{
		SharedControl element = MarkdownRenderer.CreateContent("   \n  ");

		Assert.IsInstanceOfType<ScrollViewer>(element);
		Assert.IsInstanceOfType<TextBlock>(((ScrollViewer)element).Content);
	}

	[TestMethod]
	public void CreatePlainTextContent_RendersTextInScrollViewer()
	{
		ScrollViewer viewer = MarkdownRenderer.CreatePlainTextContent("plain text");
		Assert.AreEqual("plain text", ((TextBlock)viewer.Content!).Text);
	}

	[TestMethod]
	public void CreatePlainTextContent_Fallback_IsNotFocusable()
	{
		ScrollViewer viewer = MarkdownRenderer.CreatePlainTextContent("plain text");

		Assert.IsFalse(viewer.Focusable);
		Assert.IsFalse(viewer.IsTabStop);
	}

	[TestMethod]
	public void CreateContent_Heading_ScalesFontSizeAndIsBold()
	{
		(double fontSize, bool isBold) = GetSingleParagraphFont(MarkdownRenderer.CreateContent("# Heading"));

		Assert.IsGreaterThan(MarkdownRenderTheme.Default.BodyFontSize, fontSize);
		Assert.IsTrue(isBold);
	}

	[TestMethod]
	public void CreateContent_EmptyHeadingFontSizeScaleList_UsesBodyFontSize()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { HeadingFontSizeScales = [] };

		(double fontSize, bool isBold) = GetSingleParagraphFont(MarkdownRenderer.CreateContent("# Heading", theme));

		Assert.AreEqual(MarkdownRenderTheme.Default.BodyFontSize, fontSize);
		Assert.IsTrue(isBold);
	}

	[TestMethod]
	public void CreateContent_Emphasis_ProducesBoldAndItalic()
	{
		SharedControl element = MarkdownRenderer.CreateContent("**bold** and *italic*");

		Assert.AreEqual(1, CountInlines(element, RenderedInlineKind.Bold));
		Assert.AreEqual(1, CountInlines(element, RenderedInlineKind.Italic));
	}

	[TestMethod]
	public void CreateContent_Strikethrough_ProducesStrikethroughDecoration()
	{
		SharedControl element = MarkdownRenderer.CreateContent("~~gone~~");
		Assert.AreEqual(1, CountInlines(element, RenderedInlineKind.Strikethrough));
	}

	[TestMethod]
	public void CreateContent_SoftLineBreak_RendersAsSpace()
	{
		SharedControl element = MarkdownRenderer.CreateContent("alpha\nbeta");

		Assert.AreEqual("alpha beta", GetSingleParagraphText(element));
		Assert.AreEqual(0, CountInlines(element, RenderedInlineKind.LineBreak));
	}

	[TestMethod]
	public void CreateContent_HardLineBreak_ProducesLineBreak()
	{
		SharedControl element = MarkdownRenderer.CreateContent("alpha  \nbeta");

		Assert.AreEqual(1, CountInlines(element, RenderedInlineKind.LineBreak));
		Assert.AreEqual("alpha\nbeta", GetSingleParagraphText(element));
	}

	[TestMethod]
	public void CreateContent_InlineCode_ProducesStyledInlineSurface()
	{
		Border border = FindInlineCodeBorder(MarkdownRenderer.CreateContent("Use `TextEditor` here."));

		var textBlock = (TextBlock)border.Child!;

		Assert.AreEqual("TextEditor", textBlock.Text);
		Assert.AreEqual(TextWrapping.Wrap, textBlock.TextWrapping);

		AssertFontFamilyMatches(MarkdownRenderTheme.Default.CodeFontFamily, textBlock);
	}

	[DataRow("<div>raw</div>", "<div>raw</div>", DisplayName = "RawHtmlBlock")]
	[DataRow("before <b>bold</b> after", "before <b>bold</b> after", DisplayName = "InlineHtml")]
	[DataRow("AT&amp;T &lt;ok&gt; &#65;", "AT&T <ok> A", DisplayName = "HtmlEntity")]
	[TestMethod]
	public void CreateContent_HtmlAndEntities_RenderAsLiteralText(string markdown, string expectedText) =>
		Assert.AreEqual(expectedText, GetSingleParagraphText(MarkdownRenderer.CreateContent(markdown)));

	[TestMethod]
	public void CreateContent_Image_RendersAltTextWithoutImageElement()
	{
		SharedControl element = MarkdownRenderer.CreateContent("![alt text](https://example.com/image.png)");

		Assert.AreEqual("alt text", GetSingleParagraphText(element));
		Assert.AreEqual(0, CountInlines(element, RenderedInlineKind.Link));
		Assert.AreEqual(0, CountInlines(element, RenderedInlineKind.Embedded));
	}

	[TestMethod]
	public void CreateContent_BulletList_ProducesAMarkerPerItem()
	{
		IReadOnlyList<string> markers = GetListMarkers(MarkdownRenderer.CreateContent("- one\n- two"));
		Assert.AreSequenceEqual(["\u2022", "\u2022"], markers);
	}

	[TestMethod]
	public void CreateContent_OrderedList_StartsAtTheAuthoredNumber()
	{
		IReadOnlyList<string> markers = GetListMarkers(MarkdownRenderer.CreateContent("3. three\n4. four"));
		Assert.AreSequenceEqual(["3.", "4."], markers);
	}

	[TestMethod]
	public void CreateContent_OrderedListStartingAtZero_ClampsToNumberOne()
	{
		IReadOnlyList<string> markers = GetListMarkers(MarkdownRenderer.CreateContent("0. zero\n1. one"));
		Assert.AreSequenceEqual(["1.", "2."], markers);
	}

	[TestMethod]
	public void CreateContent_TightList_SuppressesItemSpacing()
	{
		IReadOnlyList<(double Top, double Bottom)> margins = GetListItemMargins(MarkdownRenderer.CreateContent("- one\n- two"));

		Assert.HasCount(2, margins);

		foreach ((double top, double bottom) in margins)
		{
			Assert.AreEqual(0.0, top);
			Assert.AreEqual(0.0, bottom);
		}
	}

	[TestMethod]
	public void CreateContent_LooseList_KeepsItemSpacing()
	{
		IReadOnlyList<(double Top, double Bottom)> margins = GetListItemMargins(MarkdownRenderer.CreateContent("- one\n\n- two"));

		Assert.HasCount(2, margins);

		foreach ((double Top, double Bottom) margin in margins)
			Assert.AreEqual(MarkdownRenderTheme.Default.BlockSpacing, margin.Bottom);
	}

	[TestMethod]
	public void CreateContent_Blockquote_ProducesLeftBarAroundTheQuotedBlock()
	{
		(double barWidth, double topWidth, int blockCount) = GetBlockquote(MarkdownRenderer.CreateContent("> quoted"));

		Assert.AreEqual(3.0, barWidth);
		Assert.AreEqual(0.0, topWidth);
		Assert.AreEqual(1, blockCount);
	}

	[TestMethod]
	public void CreateContent_ThematicBreak_ProducesOnePixelSeparator()
	{
		Border separator = FindThematicBreak(MarkdownRenderer.CreateContent("before\n\n---\n\nafter"));
		Assert.AreEqual(1.0, separator.Height);
	}

	[TestMethod]
	public void CreateContent_Table_ProducesRowsAndColumns()
	{
		(int rows, int columns) = GetTableShape(MarkdownRenderer.CreateContent("| a | b |\n|---|---|\n| 1 | 2 |"));

		Assert.AreEqual(2, rows);
		Assert.AreEqual(2, columns);
	}

	[TestMethod]
	public void CreateContent_TableColumnAlignment_IsAppliedToCellText()
	{
		SharedControl element = MarkdownRenderer.CreateContent("| left | center | right |\n|:--|:-:|--:|\n| 1 | 2 | 3 |");

		for (int row = 0; row < 2; row++)
		{
			Assert.AreEqual(TextAlignment.Left, GetTableCellAlignment(element, row, column: 0));
			Assert.AreEqual(TextAlignment.Center, GetTableCellAlignment(element, row, column: 1));
			Assert.AreEqual(TextAlignment.Right, GetTableCellAlignment(element, row, column: 2));
		}
	}

	[TestMethod]
	public void CreateContent_DarkSurface_CodeBackgroundContrastsWithSurface()
	{
		var darkSurface = ArgbColor.FromRgb(0x1E, 0x1E, 0x1E);
		MarkdownRenderTheme darkTheme = MarkdownRenderTheme.Default with { SurfaceBackground = darkSurface };

		SharedControl element = MarkdownRenderer.CreateContent("Use `x` here.\n\n```\ncode\n```", darkTheme);

		ArgbColor inlineBackground = GetSolidBackground(FindInlineCodeBorder(element));
		ArgbColor codeBackground = GetSolidBackground(FindCodeBlockBorder(element));

		// Both code surfaces blend toward the contrasting pole, so they are lighter than the dark surface.
		Assert.IsTrue(inlineBackground.R > darkSurface.R && inlineBackground.G > darkSurface.G && inlineBackground.B > darkSurface.B);
		Assert.AreEqual(inlineBackground, codeBackground);
	}

	[TestMethod]
	public void CreateContent_DefaultViewer_IsPassive()
	{
		(bool focusable, bool isTabStop, bool isSelectable) = GetViewerInteraction(MarkdownRenderer.CreateContent("Some text."));

		Assert.IsFalse(focusable);
		Assert.IsFalse(isTabStop);
		Assert.IsFalse(isSelectable);
	}

	[TestMethod]
	public void CreateContent_ContentInteractionEnabled_ViewerAllowsSelectionAndKeyboardFocus()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };

		(bool focusable, bool isTabStop, bool isSelectable) = GetViewerInteraction(
			MarkdownRenderer.CreateContent("Some text.", MarkdownRenderTheme.Default, options)
		);

		Assert.IsTrue(focusable);
		Assert.IsTrue(isTabStop);
		Assert.IsTrue(isSelectable);
	}

	[TestMethod]
	public void CreateContent_HeadingBeyondScaleList_ReusesLastScale()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { HeadingFontSizeScales = [2.0] };

		(double fontSize, _) = GetSingleParagraphFont(MarkdownRenderer.CreateContent("### Heading", theme));

		Assert.AreEqual(MarkdownRenderTheme.Default.BodyFontSize * 2.0, fontSize);
	}

	[TestMethod]
	public void CreateContent_ExtremeHeadingScale_ClampsToTheLargestFontSize()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { HeadingFontSizeScales = [200000.0] };

		(double fontSize, _) = GetSingleParagraphFont(MarkdownRenderer.CreateContent("# Heading", theme));

		Assert.AreEqual(MarkdownRenderTheme.FontSizeUpperBound, fontSize);
	}

	[TestMethod]
	public void CreateContent_TripleEmphasis_ProducesBoldAndItalic()
	{
		SharedControl element = MarkdownRenderer.CreateContent("***strong emphasis***");

		Assert.AreEqual(1, CountInlines(element, RenderedInlineKind.Bold));
		Assert.AreEqual(1, CountInlines(element, RenderedInlineKind.Italic));
	}

	// The narrowed pipeline enables CommonMark emphasis and strikethrough but not the subscript,
	// superscript, marked, and inserted delimiters, so those must stay literal instead of rendering
	// as a different style.
	[DataRow("~sub~")]
	[DataRow("^sup^")]
	[DataRow("==mark==")]
	[DataRow("++ins++")]
	[TestMethod]
	public void CreateContent_UnsupportedEmphasisVariant_RendersAsLiteralText(string variant)
	{
		SharedControl element = MarkdownRenderer.CreateContent(variant);

		Assert.AreEqual(variant, GetSingleParagraphText(element));
		Assert.AreEqual(0, CountInlines(element, RenderedInlineKind.Bold));
		Assert.AreEqual(0, CountInlines(element, RenderedInlineKind.Italic));
		Assert.AreEqual(0, CountInlines(element, RenderedInlineKind.Strikethrough));
	}

	[DataRow("[unclosed link](")]
	[DataRow("```")]
	[DataRow("| a | b |\n|---|---|\n| 1 |")]
	[DataRow("> - item\n>   - nested")]
	[TestMethod]
	public void CreateContent_MalformedMarkdown_RendersWithoutFailing(string content)
	{
		var logger = new RecordingLogger();

		SharedControl element = MarkdownRenderer.CreateContent(content, theme: null, new MarkdownRenderOptions { Logger = logger });

		// A render failure falls back to plain text and is logged, so an empty log means the content rendered as Markdown.
		Assert.IsNotNull(element);
		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	public void CreateContent_ImageWithoutAltText_RendersTheUrlAsPlaceholder()
	{
		SharedControl element = MarkdownRenderer.CreateContent("![](https://example.com/image.png)");
		Assert.AreEqual("https://example.com/image.png", GetSingleParagraphText(element));
	}

	[TestMethod]
	public void CreateContent_TableCellText_DoesNotCarryBlockSpacing()
	{
		IReadOnlyList<double> margins = GetTableCellBottomMargins(MarkdownRenderer.CreateContent("| a | b |\n| - | - |\n| c | d |"));

		Assert.HasCount(4, margins);

		foreach (double bottom in margins)
			Assert.AreEqual(0.0, bottom);
	}

	[TestMethod]
	public void CreateContent_TableHeaderCells_AreBold()
	{
		SharedControl element = MarkdownRenderer.CreateContent("| left | right |\n|:--|--:|\n| 1 | 2 |");

		Assert.IsTrue(IsTableCellBold(element, row: 0, column: 0));
		Assert.IsTrue(IsTableCellBold(element, row: 0, column: 1));
		Assert.IsFalse(IsTableCellBold(element, row: 1, column: 0));
	}
}
