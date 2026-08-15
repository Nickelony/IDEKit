using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using List = System.Windows.Documents.List;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed partial class MarkdownRendererTests
{
	[TestMethod]
	public void CreateContent_WhitespaceOnlyContent_ReturnsFallbackScrollViewer()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("   \n  ");

		Assert.IsInstanceOfType(element, typeof(ScrollViewer));
	}

	[TestMethod]
	public void CreateContent_InlineCode_ProducesStyledInlineContainer()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("Use `TextEditor` here.");
		var viewer = (FlowDocumentScrollViewer)element;

		Border border = GetInlineCodeBorder(viewer.Document);
		var textBlock = (TextBlock)border.Child;

		Assert.AreEqual("TextEditor", textBlock.Text);
		Assert.AreEqual(MarkdownRenderTheme.Default.CodeFontFamily.Source, textBlock.FontFamily.Source);
	}

	[TestMethod]
	public void CreateContent_LongInlineCode_WrapsInsideTheTooltipWidth()
	{
		string identifier = new('x', 240);
		FrameworkElement element = MarkdownRenderer.CreateContent($"Use `{identifier}` here.");
		var viewer = (FlowDocumentScrollViewer)element;

		using HostWindow window = WPFTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		Border border = GetInlineCodeBorder(viewer.Document);
		var textBlock = (TextBlock)border.Child;

		Assert.AreEqual(TextWrapping.Wrap, textBlock.TextWrapping);
		Assert.AreEqual(identifier, textBlock.Text);
		Assert.IsTrue(
			border.ActualWidth <= MarkdownRenderTheme.Default.CodeMaxWidth + 1.0,
			$"Inline code width {border.ActualWidth} must stay inside the code max width and the tooltip.");
		Assert.IsTrue(
			textBlock.ActualHeight > textBlock.FontSize * 2.0,
			"The long identifier must wrap onto more than one line.");
	}

	[TestMethod]
	public void CreateContent_RawHtmlBlock_RendersAsLiteralText()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("<div>raw</div>");
		var viewer = (FlowDocumentScrollViewer)element;

		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual("<div>raw</div>", GetParagraphText(paragraph));
	}

	[TestMethod]
	public void CreateContent_InlineHtml_RendersAsLiteralText()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("before <b>bold</b> after");
		var viewer = (FlowDocumentScrollViewer)element;

		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual("before <b>bold</b> after", GetParagraphText(paragraph));
	}

	[TestMethod]
	public void CreateContent_HtmlEntity_DecodesToCharacter()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("AT&amp;T &lt;ok&gt; &#65;");

		var viewer = (FlowDocumentScrollViewer)element;
		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual("AT&T <ok> A", GetParagraphText(paragraph));
	}

	[TestMethod]
	public void CreateContent_Image_RendersAltTextWithoutImageElement()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("![alt text](https://example.com/image.png)");
		var viewer = (FlowDocumentScrollViewer)element;

		Assert.AreEqual(0, FindAll<Hyperlink>(viewer.Document).Count());
		Assert.AreEqual(0, FindAll<InlineUIContainer>(viewer.Document).Count());
		Assert.AreEqual("alt text", GetParagraphText(FindAll<Paragraph>(viewer.Document).Single()));
	}

	[TestMethod]
	public void CreateContent_Heading_ScalesFontSizeAndIsBold()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("# Heading");
		var viewer = (FlowDocumentScrollViewer)element;

		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.IsTrue(paragraph.FontSize > MarkdownRenderTheme.Default.BodyFontSize);
		Assert.AreEqual(FontWeights.Bold, paragraph.FontWeight);
	}

	[TestMethod]
	public void CreateContent_EmptyHeadingFontSizeScaleList_UsesBodyFontSize()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("# Heading", new MarkdownRenderTheme { HeadingFontSizeScales = [] });
		var viewer = (FlowDocumentScrollViewer)element;

		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual(MarkdownRenderTheme.Default.BodyFontSize, paragraph.FontSize);
		Assert.AreEqual(FontWeights.Bold, paragraph.FontWeight);
	}

	[TestMethod]
	public void CreateContent_List_ProducesWpfList()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("- one\n- two");
		var viewer = (FlowDocumentScrollViewer)element;

		List list = FindAll<List>(viewer.Document).Single();

		Assert.AreEqual(2, list.ListItems.Count);
		Assert.AreEqual(TextMarkerStyle.Disc, list.MarkerStyle);
	}

	[TestMethod]
	public void CreateContent_OrderedList_ProducesDecimalMarker()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("3. three\n4. four");
		var viewer = (FlowDocumentScrollViewer)element;

		List list = FindAll<List>(viewer.Document).Single();

		Assert.AreEqual(TextMarkerStyle.Decimal, list.MarkerStyle);
		Assert.AreEqual(3, list.StartIndex);
	}

	[TestMethod]
	public void CreateContent_OrderedListStartingAtZero_ClampsToNumberOne()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("0. zero\n1. one");

		Assert.IsInstanceOfType(element, typeof(FlowDocumentScrollViewer));

		var viewer = (FlowDocumentScrollViewer)element;
		List list = FindAll<List>(viewer.Document).Single();

		Assert.AreEqual(1, list.StartIndex);
		Assert.AreEqual(2, list.ListItems.Count);
	}

	[TestMethod]
	public void CreateContent_TightList_SuppressesItemSpacing()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("- one\n- two");

		foreach (ListItem item in FindAll<ListItem>(viewer.Document))
			Assert.AreEqual(0.0, ((Paragraph)item.Blocks.FirstBlock!).Margin.Bottom);
	}

	[TestMethod]
	public void CreateContent_LooseList_KeepsItemSpacing()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("- one\n\n- two");

		foreach (ListItem item in FindAll<ListItem>(viewer.Document))
			Assert.AreEqual(MarkdownRenderTheme.Default.BlockSpacing, ((Paragraph)item.Blocks.FirstBlock!).Margin.Bottom);
	}

	[TestMethod]
	public void CreateContent_TightListWithCodeBlock_SuppressesCodeBlockSpacing()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("- one\n  ```\n  code\n  ```");

		Border border = GetCodeBlockBorder(viewer.Document);

		Assert.AreEqual(0.0, border.Margin.Top);
		Assert.AreEqual(0.0, border.Margin.Bottom);
	}

	[TestMethod]
	public void CreateContent_Blockquote_ProducesSection()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("> quoted");
		var viewer = (FlowDocumentScrollViewer)element;

		Section section = FindAll<Section>(viewer.Document).Single();

		Assert.AreEqual(1, section.Blocks.Count);
	}

	[TestMethod]
	public void CreateContent_ThematicBreak_ProducesSeparatorBlock()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("before\n\n---\n\nafter");
		var viewer = (FlowDocumentScrollViewer)element;

		BlockUIContainer container = FindAll<BlockUIContainer>(viewer.Document).Single();
		var border = (Border)container.Child;

		Assert.AreEqual(1.0, border.Height);
	}

	[TestMethod]
	public void CreateContent_Emphasis_ProducesBoldAndItalic()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("**bold** and *italic*");
		var viewer = (FlowDocumentScrollViewer)element;

		Assert.AreEqual(1, FindAll<Bold>(viewer.Document).Count());
		Assert.AreEqual(1, FindAll<Italic>(viewer.Document).Count());
	}

	[TestMethod]
	public void CreateContent_Strikethrough_ProducesStrikethroughDecoration()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("~~gone~~");
		var viewer = (FlowDocumentScrollViewer)element;

		Span span = FindAll<Span>(viewer.Document).Single();

		Assert.IsTrue(span.TextDecorations.Count > 0);
		Assert.AreEqual(TextDecorationLocation.Strikethrough, span.TextDecorations[0].Location);
	}

	// The narrowed pipeline enables only CommonMark emphasis and strikethrough; the subscript,
	// superscript, marked, and inserted delimiters must stay literal instead of rendering
	// as a different style.
	[DataRow("~sub~")]
	[DataRow("^sup^")]
	[DataRow("==mark==")]
	[DataRow("++ins++")]
	[TestMethod]
	public void CreateContent_UnsupportedEmphasisVariant_RendersAsLiteralText(string variant)
	{
		FrameworkElement element = MarkdownRenderer.CreateContent(variant);
		var viewer = (FlowDocumentScrollViewer)element;
		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual(variant, GetParagraphText(paragraph));
		Assert.AreEqual(0, FindAll<Bold>(viewer.Document).Count());
		Assert.AreEqual(0, FindAll<Italic>(viewer.Document).Count());
		Assert.AreEqual(0, FindAll<Span>(viewer.Document).Count());
	}

	[TestMethod]
	public void CreateContent_SoftLineBreak_RendersAsSpace()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("alpha\nbeta");
		var viewer = (FlowDocumentScrollViewer)element;
		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual("alpha beta", GetParagraphText(paragraph));
		Assert.AreEqual(0, FindAll<LineBreak>(viewer.Document).Count());
	}

	[TestMethod]
	public void CreateContent_HardLineBreak_ProducesLineBreak()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("alpha  \nbeta");
		var viewer = (FlowDocumentScrollViewer)element;
		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual(1, FindAll<LineBreak>(viewer.Document).Count());
		Assert.AreEqual("alpha\nbeta", GetParagraphText(paragraph));
	}

	[TestMethod]
	public void CreateContent_Table_ProducesWpfTable()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("| a | b |\n|---|---|\n| 1 | 2 |");
		var viewer = (FlowDocumentScrollViewer)element;

		Table table = FindAll<Table>(viewer.Document).Single();

		Assert.AreEqual(2, table.RowGroups[0].Rows.Count);
		Assert.AreEqual(2, table.RowGroups[0].Rows[0].Cells.Count);
	}

	[TestMethod]
	public void CreateContent_TableColumnAlignment_IsAppliedToCellText()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("| left | center | right |\n|:--|:-:|--:|\n| 1 | 2 | 3 |");
		var viewer = (FlowDocumentScrollViewer)element;

		Table table = FindAll<Table>(viewer.Document).Single();
		TableRow bodyRow = table.RowGroups[0].Rows[1];

		Assert.AreEqual(TextAlignment.Left, GetFirstCellParagraph(bodyRow.Cells[0]).TextAlignment);
		Assert.AreEqual(TextAlignment.Center, GetFirstCellParagraph(bodyRow.Cells[1]).TextAlignment);
		Assert.AreEqual(TextAlignment.Right, GetFirstCellParagraph(bodyRow.Cells[2]).TextAlignment);
	}

	[TestMethod]
	public void CreateContent_HeadingBeyondScaleList_ReusesLastScale()
	{
		var theme = MarkdownRenderTheme.Default with { HeadingFontSizeScales = [2.0] };

		FrameworkElement element = MarkdownRenderer.CreateContent("### Heading", theme);
		var viewer = (FlowDocumentScrollViewer)element;
		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual(MarkdownRenderTheme.Default.BodyFontSize * 2.0, paragraph.FontSize);
	}

	[TestMethod]
	public void CreateContent_ExtremeHeadingScale_ClampsToTheLargestFontSizeWpfAccepts()
	{
		var theme = MarkdownRenderTheme.Default with { HeadingFontSizeScales = [200000.0] };

		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("# Heading", theme);
		Paragraph paragraph = FindAll<Paragraph>(viewer.Document).Single();

		Assert.AreEqual(MarkdownRenderTheme.FontSizeUpperBound, paragraph.FontSize);
	}

	[TestMethod]
	public void CreatePlainTextContent_RendersTextInScrollViewer()
	{
		ScrollViewer scrollViewer = MarkdownRenderer.CreatePlainTextContent("plain text");
		var textBlock = (TextBlock)scrollViewer.Content;

		Assert.AreEqual("plain text", textBlock.Text);
	}

	[TestMethod]
	public void CreatePlainTextContent_Fallback_IsNotFocusable()
	{
		ScrollViewer scrollViewer = MarkdownRenderer.CreatePlainTextContent("plain text");

		Assert.IsFalse(scrollViewer.Focusable);
		Assert.IsFalse(scrollViewer.IsTabStop);
	}

	[TestMethod]
	public void CreateContent_DarkSurface_CodeBackgroundContrastsWithSurface()
	{
		var darkSurface = Color.FromRgb(0x1E, 0x1E, 0x1E);
		var darkTheme = MarkdownRenderTheme.Default with { SurfaceBackground = new SolidColorBrush(darkSurface) };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Use `x` here.\n\n```\ncode\n```", darkTheme);

		Color inlineBackground = ((SolidColorBrush)GetInlineCodeBorder(viewer.Document).Background).Color;
		Color codeBackground = ((SolidColorBrush)GetCodeBlockBorder(viewer.Document).Background).Color;

		// Both code surfaces blend toward the contrasting pole, so they are lighter than the dark surface.
		Assert.IsTrue(inlineBackground.R > darkSurface.R && inlineBackground.G > darkSurface.G && inlineBackground.B > darkSurface.B);
		Assert.AreEqual(inlineBackground, codeBackground);
	}

	[TestMethod]
	public void CreateContent_AfterMutatingTheSurfaceBrushColor_ReDerivesTheCodeBackground()
	{
		var surface = new SolidColorBrush(Colors.White);
		var theme = MarkdownRenderTheme.Default with { SurfaceBackground = surface };

		var lightViewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Use `x` here.", theme);
		Color lightBackground = ((SolidColorBrush)GetInlineCodeBorder(lightViewer.Document).Background).Color;

		// The derived brush cache is keyed by the surface color, so changing the same brush's color must
		// re-derive the code background instead of leaving the cached one in place.
		surface.Color = Colors.Black;

		var darkViewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Use `x` here.", theme);
		Color darkBackground = ((SolidColorBrush)GetInlineCodeBorder(darkViewer.Document).Background).Color;

		Assert.AreNotEqual(lightBackground, darkBackground);
	}

	[TestMethod]
	public void CreateContent_AfterMutatingTheForegroundBrushColor_ReDerivesTheMutedPlaceholderForeground()
	{
		var foreground = new SolidColorBrush(Colors.Black);
		var theme = MarkdownRenderTheme.Default with { Foreground = foreground };
		const string ImageWithoutAltText = "![](https://example.com/image.png)";

		var darkViewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent(ImageWithoutAltText, theme);
		Color darkPlaceholder = ((SolidColorBrush)FindAll<Run>(darkViewer.Document).Single().Foreground).Color;

		// The muted placeholder foreground derives from the body foreground, and the surface color is left
		// unchanged here, so a cache keyed only on the surface color would reuse the stale placeholder
		// brush. Changing the same brush's color must re-derive it.
		foreground.Color = Colors.White;

		var lightViewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent(ImageWithoutAltText, theme);
		Color lightPlaceholder = ((SolidColorBrush)FindAll<Run>(lightViewer.Document).Single().Foreground).Color;

		Assert.AreNotEqual(darkPlaceholder, lightPlaceholder);
	}

	[TestMethod]
	public void CreateContent_TripleEmphasis_ProducesBoldAndItalic()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("***strong emphasis***");
		var viewer = (FlowDocumentScrollViewer)element;

		Bold bold = FindAll<Bold>(viewer.Document).Single();
		Italic italic = FindAll<Italic>(viewer.Document).Single();

		Assert.IsTrue(
			bold.Inlines.OfType<Italic>().Any() || italic.Inlines.OfType<Bold>().Any(),
			"The strong and italic emphasis must be nested.");
	}

	[TestMethod]
	public void CreateContent_TableCellParagraph_DoesNotCarryBlockSpacing()
	{
		FlowDocument document = MarkdownRenderer.CreateFlowDocument("| a | b |\n| - | - |\n| c | d |");
		Table table = document.Blocks.OfType<Table>().Single();

		foreach (TableRow row in table.RowGroups[0].Rows)
		{
			foreach (TableCell cell in row.Cells)
				Assert.AreEqual(0.0, cell.Blocks.FirstBlock!.Margin.Bottom);
		}
	}

	[TestMethod]
	public void CreateFlowDocument_KeepsDefaultColumnWidth()
	{
		FlowDocument document = MarkdownRenderer.CreateFlowDocument("Some paragraph text.");

		Assert.IsTrue(double.IsNaN(document.ColumnWidth), "The renderer must not pin a minimum column width.");
	}

	[TestMethod]
	public void CreateContent_BlockSpacing_DrivesBlockMargins()
	{
		var theme = MarkdownRenderTheme.Default with { BlockSpacing = 20.0 };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("# Heading\n\ntext\n\n---\n\n```lua\nx = 1\n```", theme);
		FlowDocument document = viewer.Document;

		Paragraph heading = document.Blocks.OfType<Paragraph>().First();
		Paragraph paragraph = document.Blocks.OfType<Paragraph>().Last();
		var breakBorder = (Border)document.Blocks.OfType<BlockUIContainer>().First().Child;
		var codeBorder = (Border)document.Blocks.OfType<BlockUIContainer>().Last().Child;

		Assert.AreEqual(20.0, heading.Margin.Top);
		Assert.AreEqual(20.0, heading.Margin.Bottom);
		Assert.AreEqual(20.0, paragraph.Margin.Bottom);
		Assert.AreEqual(20.0, breakBorder.Margin.Top);
		Assert.AreEqual(20.0, breakBorder.Margin.Bottom);
		Assert.AreEqual(20.0, codeBorder.Margin.Top);
		Assert.AreEqual(20.0, codeBorder.Margin.Bottom);
	}

	[TestMethod]
	public void CreateFlowDocument_RendersBlocks()
	{
		FlowDocument document = MarkdownRenderer.CreateFlowDocument("# Heading");

		Assert.AreEqual(1, document.Blocks.Count);
		Assert.IsInstanceOfType(document.Blocks.FirstBlock, typeof(Paragraph));
	}

	[TestMethod]
	public void CreateFlowDocument_WhitespaceOnlyContent_ReturnsEmptyDocument()
	{
		FlowDocument document = MarkdownRenderer.CreateFlowDocument("   \n  ");

		Assert.AreEqual(0, document.Blocks.Count);
	}

	[TestMethod]
	public void CreatePlainTextContent_ThemeValues_FlowIntoTheFallback()
	{
		var theme = MarkdownRenderTheme.Default with
		{
			FlowDirection = FlowDirection.RightToLeft,
			MaxWidth = 123.0,
			MaxHeight = 45.0
		};

		var scrollViewer = (ScrollViewer)MarkdownRenderer.CreatePlainTextContent("plain text", theme);
		var textBlock = (TextBlock)scrollViewer.Content;

		Assert.AreEqual(FlowDirection.RightToLeft, scrollViewer.FlowDirection);
		Assert.AreEqual(FlowDirection.RightToLeft, textBlock.FlowDirection);
		Assert.AreEqual(123.0, scrollViewer.MaxWidth);
		Assert.AreEqual(45.0, scrollViewer.MaxHeight);
	}

	[DataRow("[unclosed link](")]
	[DataRow("```")]
	[DataRow("| a | b |\n|---|---|\n| 1 |")]
	[DataRow("> - item\n>   - nested")]
	[TestMethod]
	public void CreateContent_MalformedMarkdown_RendersWithoutThrowing(string content)
	{
		FrameworkElement element = MarkdownRenderer.CreateContent(content);

		Assert.IsInstanceOfType<FlowDocumentScrollViewer>(element);
	}

	[TestMethod]
	public void CreateContent_NestedList_RendersNestedWpfList()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("- one\n  - nested\n- two");

		List outer = FindAll<List>(viewer.Document).First();
		List inner = FindAll<List>(viewer.Document).Last();

		Assert.AreNotSame(outer, inner);
		Assert.AreEqual(2, outer.ListItems.Count);
		Assert.AreEqual(1, inner.ListItems.Count);
	}

	[TestMethod]
	public void CreateContent_ListInsideBlockquote_RendersListInsideSection()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("> - one\n> - two");

		Section section = FindAll<Section>(viewer.Document).Single();

		Assert.IsInstanceOfType<List>(section.Blocks.FirstBlock);
	}

	[TestMethod]
	public void CreateContent_ScrollingDisabled_ViewerCarriesThemeLimitsAndDisabledScrollBars()
	{
		var theme = MarkdownRenderTheme.Default with { MaxWidth = 321.0, MaxHeight = 123.0 };
		var options = new MarkdownRenderOptions { AllowScrolling = false };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Some text.", theme, options);

		Assert.AreEqual(321.0, viewer.MaxWidth);
		Assert.AreEqual(123.0, viewer.MaxHeight);
		Assert.AreEqual(ScrollBarVisibility.Disabled, viewer.VerticalScrollBarVisibility);
		Assert.AreEqual(ScrollBarVisibility.Disabled, viewer.HorizontalScrollBarVisibility);
	}

	[TestMethod]
	public void CreateFlowDocument_UnboundedSizeLimits_DoNotClampCodeBlocks()
	{
		var theme = MarkdownRenderTheme.Default with
		{
			MaxWidth = double.PositiveInfinity,
			MaxHeight = double.PositiveInfinity,
			CodeMaxWidth = double.PositiveInfinity
		};
		FlowDocument document = MarkdownRenderer.CreateFlowDocument("```lua\nx = 1\n```", theme);

		var border = (Border)FindAll<BlockUIContainer>(document).Single().Child;

		Assert.IsTrue(double.IsPositiveInfinity(border.MaxWidth));
	}

	[TestMethod]
	public void CreatePlainTextContent_MarkdownSyntax_IsDisplayedLiterally()
	{
		var scrollViewer = (ScrollViewer)MarkdownRenderer.CreatePlainTextContent("**bold** and `code`");
		var textBlock = (TextBlock)scrollViewer.Content;

		Assert.AreEqual("**bold** and `code`", textBlock.Text);
	}

	[TestMethod]
	public void CreatePlainTextContent_ThemeAndOptions_FlowIntoTheFallback()
	{
		var theme = MarkdownRenderTheme.Default with
		{
			BodyFontFamily = new FontFamily("Courier New"),
			BodyFontSize = 17.0,
			Foreground = Brushes.Red
		};
		var options = new MarkdownRenderOptions { AllowScrolling = false };
		var scrollViewer = (ScrollViewer)MarkdownRenderer.CreatePlainTextContent("plain text", theme, options);
		var textBlock = (TextBlock)scrollViewer.Content;

		Assert.AreEqual(ScrollBarVisibility.Disabled, scrollViewer.VerticalScrollBarVisibility);
		Assert.AreEqual("Courier New", textBlock.FontFamily.Source);
		Assert.AreEqual(17.0, textBlock.FontSize);
		Assert.AreSame(Brushes.Red, textBlock.Foreground);
		Assert.AreEqual(TextWrapping.Wrap, textBlock.TextWrapping);
	}
}
