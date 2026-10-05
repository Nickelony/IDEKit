using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nickelony.IDEKit.Core.Media;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;
using List = System.Windows.Documents.List;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	protected override string GetSingleParagraphText(FrameworkElement content) => GetParagraphText(FindAll<Paragraph>(GetDocument(content)).Single());

	protected override (double FontSize, bool IsBold) GetSingleParagraphFont(FrameworkElement content)
	{
		Paragraph paragraph = FindAll<Paragraph>(GetDocument(content)).Single();
		return (paragraph.FontSize, paragraph.FontWeight == FontWeights.Bold);
	}

	protected override int CountInlines(FrameworkElement content, RenderedInlineKind kind)
	{
		FlowDocument document = GetDocument(content);

		return kind switch
		{
			RenderedInlineKind.Bold => FindAll<Bold>(document).Count(),
			RenderedInlineKind.Italic => FindAll<Italic>(document).Count(),
			RenderedInlineKind.LineBreak => FindAll<LineBreak>(document).Count(),
			RenderedInlineKind.Link => FindAll<Hyperlink>(document).Count(),
			RenderedInlineKind.Embedded => FindAll<InlineUIContainer>(document).Count(),

			RenderedInlineKind.Strikethrough => FindAll<Span>(document)
				.Count(span => span.TextDecorations.Count > 0 && span.TextDecorations[0].Location == TextDecorationLocation.Strikethrough),

			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown inline kind."),
		};
	}

	protected override int CountTopLevelBlocks(FrameworkElement content) => GetDocument(content).Blocks.Count;

	protected override Border FindInlineCodeBorder(FrameworkElement content) => GetInlineCodeBorder(GetDocument(content));

	protected override Border FindCodeBlockBorder(FrameworkElement content) => GetCodeBlockBorder(GetDocument(content));

	protected override Border FindThematicBreak(FrameworkElement content) => (Border)FindAll<BlockUIContainer>(GetDocument(content)).Single().Child;

	protected override ArgbColor GetSolidBackground(Border border) => ToArgbColor(((SolidColorBrush)border.Background).Color);

	protected override ArgbColor GetSolidForeground(TextBlock textBlock) => ToArgbColor(((SolidColorBrush)textBlock.Foreground).Color);

	protected override ArgbColor GetBlockquoteBorderColor(FrameworkElement content) =>
		ToArgbColor(((SolidColorBrush)FindAll<Section>(GetDocument(content)).Single().BorderBrush).Color);

	protected override void AssertFontFamilyMatches(string? family, TextBlock textBlock) => Assert.AreEqual(family, textBlock.FontFamily.Source);

	protected override IReadOnlyList<string> GetListMarkers(FrameworkElement content)
	{
		List list = FindAll<List>(GetDocument(content)).Single();
		List<string> markers = [];

		for (int index = 0; index < list.ListItems.Count; index++)
		{
			markers.Add(
				list.MarkerStyle switch
				{
					TextMarkerStyle.Disc => "\u2022",
					TextMarkerStyle.Decimal => (list.StartIndex + index).ToString(CultureInfo.InvariantCulture) + ".",
					_ => throw new InvalidOperationException($"The list uses the unexpected marker style {list.MarkerStyle}."),
				}
			);
		}

		return markers;
	}

	protected override IReadOnlyList<(double Top, double Bottom)> GetListItemMargins(FrameworkElement content)
	{
		List<(double Top, double Bottom)> margins = [];

		foreach (ListItem item in FindAll<ListItem>(GetDocument(content)))
		{
			Thickness margin = ((Paragraph)item.Blocks.FirstBlock!).Margin;
			margins.Add((margin.Top, margin.Bottom));
		}

		return margins;
	}

	protected override (double BarWidth, double TopWidth, int BlockCount) GetBlockquote(FrameworkElement content)
	{
		Section section = FindAll<Section>(GetDocument(content)).Single();
		return (section.BorderThickness.Left, section.BorderThickness.Top, section.Blocks.Count);
	}

	protected override (int Rows, int Columns) GetTableShape(FrameworkElement content)
	{
		Table table = FindAll<Table>(GetDocument(content)).Single();
		return (table.RowGroups[0].Rows.Count, table.RowGroups[0].Rows[0].Cells.Count);
	}

	protected override TextAlignment GetTableCellAlignment(FrameworkElement content, int row, int column)
	{
		Table table = FindAll<Table>(GetDocument(content)).Single();
		return GetFirstCellParagraph(table.RowGroups[0].Rows[row].Cells[column]).TextAlignment;
	}

	protected override IReadOnlyList<double> GetTableCellBottomMargins(FrameworkElement content)
	{
		Table table = FindAll<Table>(GetDocument(content)).Single();
		return [.. table.RowGroups[0].Rows.SelectMany(row => row.Cells).Select(cell => GetFirstCellParagraph(cell).Margin.Bottom)];
	}

	protected override bool IsTableCellBold(FrameworkElement content, int row, int column)
	{
		Table table = FindAll<Table>(GetDocument(content)).Single();
		return GetFirstCellParagraph(table.RowGroups[0].Rows[row].Cells[column]).FontWeight == FontWeights.Bold;
	}

	protected override IReadOnlyList<(double Top, double Bottom)> GetTopLevelTextMargins(FrameworkElement content) =>
		[.. GetDocument(content).Blocks.OfType<Paragraph>().Select(paragraph => (paragraph.Margin.Top, paragraph.Margin.Bottom))];

	protected override (ScrollBarVisibility Vertical, ScrollBarVisibility Horizontal) GetViewerScrollBars(FrameworkElement content)
	{
		var viewer = (FlowDocumentScrollViewer)content;
		return (viewer.VerticalScrollBarVisibility, viewer.HorizontalScrollBarVisibility);
	}

	protected override (bool Focusable, bool IsTabStop, bool IsSelectable) GetViewerInteraction(FrameworkElement content)
	{
		var viewer = (FlowDocumentScrollViewer)content;
		return (viewer.Focusable, viewer.IsTabStop, viewer.IsSelectionEnabled);
	}

	protected override (Uri? OpenableUri, string Text, bool Focusable) GetSingleLink(FrameworkElement content)
	{
		Hyperlink hyperlink = FindAll<Hyperlink>(GetDocument(content)).Single();
		return (hyperlink.NavigateUri, GetHyperlinkText(hyperlink), hyperlink.Focusable);
	}

	protected override bool ActivateSingleLink(FrameworkElement content)
	{
		Hyperlink hyperlink = FindAll<Hyperlink>(GetDocument(content)).Single();

		var clickEvent = new RoutedEventArgs(Hyperlink.ClickEvent);
		hyperlink.RaiseEvent(clickEvent);

		return clickEvent.Handled;
	}

	protected override IReadOnlyList<RenderedLinkLook> GetLinkLooks(FrameworkElement content)
	{
		return
		[
			.. FindAll<Hyperlink>(GetDocument(content))
				.Select(hyperlink => new RenderedLinkLook(
					ShowsHandCursor: Equals(hyperlink.Cursor, Cursors.Hand),
					Foreground: ToArgbColor(((SolidColorBrush)hyperlink.Foreground).Color),
					IsUnderlined: hyperlink.TextDecorations.Any(decoration => decoration.Location == TextDecorationLocation.Underline)
				)),
		];
	}

	protected override bool PressPrimaryButtonOn(EditorControl editor) => RaisePreviewMouseLeftButtonDown(editor);

	private static ArgbColor ToArgbColor(Color color) => new(A: color.A, R: color.R, G: color.G, B: color.B);

	private static FlowDocument GetDocument(FrameworkElement content) => ((FlowDocumentScrollViewer)content).Document;
}
