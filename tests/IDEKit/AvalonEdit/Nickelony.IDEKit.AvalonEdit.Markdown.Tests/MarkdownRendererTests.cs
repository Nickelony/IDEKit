using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using List = System.Windows.Documents.List;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

/// <summary>
/// The AvalonEdit binding of the shared <see cref="MarkdownRendererTestsBase"/> scenarios; the shared scenarios read the <c>FlowDocument</c>
/// through the hooks in <c>MarkdownRendererTests.Probes.cs</c>, and this suite holds the scenarios that read the <c>FlowDocument</c> itself.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed partial class MarkdownRendererTests : MarkdownRendererTestsBase
{
	[TestMethod]
	public void CreateContent_ShortText_IsAsWideAsItsText()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("A short **hover**.");

		using HostWindow window = WpfTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();

		// A flow document has no width of its own, so without the fitting the viewer would take the whole theme width.
		Assert.IsLessThan(MarkdownRenderTheme.Default.MaxWidth / 2.0, viewer.ActualWidth);
		Assert.IsGreaterThan(20.0, viewer.ActualWidth);
	}

	[TestMethod]
	public void CreateContent_TextThatWraps_TakesTheWholeThemeWidth()
	{
		string paragraph = string.Join(' ', Enumerable.Repeat("words that wrap", 60));
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent(paragraph);

		using HostWindow window = WpfTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();

		// Narrower than this, the wrapped paragraph would need another line.
		Assert.IsGreaterThan(MarkdownRenderTheme.Default.MaxWidth * 0.8, viewer.ActualWidth);
	}

	[TestMethod]
	public void CreateContent_WithoutAMaximumWidth_FillsItsHost()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { MaxWidth = double.PositiveInfinity, MaxHeight = double.PositiveInfinity };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("A short page.", theme);

		using HostWindow window = WpfTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();

		// A page, such as a preview, takes the room of its host rather than the width of its widest line.
		Assert.IsGreaterThan(MarkdownRenderTheme.Default.MaxWidth / 2.0, viewer.ActualWidth);
	}

	[TestMethod]
	public void CreateContentTree_AlignsTheTextToTheStartOfEachLine()
	{
		FlowDocument document = MarkdownRenderer.CreateContentTree("A paragraph.");
		Assert.AreEqual(TextAlignment.Left, document.TextAlignment);
	}

	[TestMethod]
	public void CreateContent_LongInlineCode_WrapsInsideTheTooltipWidth()
	{
		string identifier = new('x', 240);
		FrameworkElement element = MarkdownRenderer.CreateContent($"Use `{identifier}` here.");

		var viewer = (FlowDocumentScrollViewer)element;

		using HostWindow window = WpfTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();
		WpfTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		Border border = GetInlineCodeBorder(viewer.Document);

		var textBlock = (TextBlock)border.Child;

		Assert.AreEqual(TextWrapping.Wrap, textBlock.TextWrapping);
		Assert.AreEqual(identifier, textBlock.Text);

		Assert.IsLessThanOrEqualTo(
			MarkdownRenderTheme.Default.CodeMaxWidth + 1.0,
			border.ActualWidth,
			$"Inline code width {border.ActualWidth} must stay inside the code max width and the tooltip."
		);

		Assert.IsGreaterThan(textBlock.FontSize * 2.0, textBlock.ActualHeight, "The long identifier must wrap onto more than one line.");
	}

	[TestMethod]
	public void CreateContent_TripleEmphasis_NestsTheBoldAndItalicSpans()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("***strong emphasis***");

		var viewer = (FlowDocumentScrollViewer)element;

		Bold bold = FindAll<Bold>(viewer.Document).Single();
		Italic italic = FindAll<Italic>(viewer.Document).Single();

		Assert.IsTrue(bold.Inlines.OfType<Italic>().Any() || italic.Inlines.OfType<Bold>().Any(), "The strong and italic emphasis must be nested.");
	}

	[TestMethod]
	public void CreateContentTree_KeepsDefaultColumnWidth()
	{
		FlowDocument document = MarkdownRenderer.CreateContentTree("Some paragraph text.");
		Assert.IsTrue(double.IsNaN(document.ColumnWidth), "The renderer must not pin a minimum column width.");
	}

	[TestMethod]
	public void CreateContentTree_RendersBlocks()
	{
		FlowDocument document = MarkdownRenderer.CreateContentTree("# Heading");

		Assert.AreEqual(1, document.Blocks.Count);
		Assert.IsInstanceOfType(document.Blocks.FirstBlock, typeof(Paragraph));
	}

	[TestMethod]
	public void CreateContentTree_WhitespaceOnlyContent_ReturnsEmptyDocument()
	{
		FlowDocument document = MarkdownRenderer.CreateContentTree("   \n  ");
		Assert.AreEqual(0, document.Blocks.Count);
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
	public void CreateContent_ListItem_StartsItsTextAfterTheMarkerAndTheGap()
	{
		// MarkdownLayout.ListMarkerGap, the gap the plan leaves after a marker.
		const double markerGap = 6.0;

		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Intro\n\n- item");
		using HostWindow window = WpfTestHost.ShowInHostWindow(viewer);
		viewer.UpdateLayout();

		Paragraph intro = FindAll<Paragraph>(viewer.Document).First();
		List list = FindAll<List>(viewer.Document).Single();
		Paragraph item = list.ListItems.First().Blocks.OfType<Paragraph>().First();

		double indent =
			item.ContentStart.GetCharacterRect(LogicalDirection.Forward).Left - intro.ContentStart.GetCharacterRect(LogicalDirection.Forward).Left;

		var marker = new FormattedText(
			textToFormat: "•",
			culture: CultureInfo.InvariantCulture,
			flowDirection: FlowDirection.LeftToRight,

			typeface: new Typeface(
				fontFamily: viewer.Document.FontFamily,
				style: FontStyles.Normal,
				weight: FontWeights.Normal,
				stretch: FontStretches.Normal
			),

			emSize: viewer.Document.FontSize,
			foreground: Brushes.Black,
			pixelsPerDip: 1.0
		);

		// The Avalonia binding lays the item out the same way: the plan's list margin, the drawn marker, then the gap.
		Assert.AreEqual(list.Margin.Left + marker.WidthIncludingTrailingWhitespace + markerGap, indent, 1.0);
	}

	[TestMethod]
	public void CreateContent_ListInsideBlockquote_RendersListInsideSection()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("> - one\n> - two");

		Section section = FindAll<Section>(viewer.Document).Single();

		Assert.IsInstanceOfType<List>(section.Blocks.FirstBlock);
	}

	[TestMethod]
	public void CreateContent_ThemeFlowDirection_FlowsIntoTheFlowDocument()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { FlowDirection = MarkdownFlowDirection.RightToLeft };

		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Some text.", theme);

		Assert.AreEqual(FlowDirection.RightToLeft, viewer.Document.FlowDirection);
	}
}
