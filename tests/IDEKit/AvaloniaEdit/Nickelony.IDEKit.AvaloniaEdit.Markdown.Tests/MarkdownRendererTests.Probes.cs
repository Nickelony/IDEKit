using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Nickelony.IDEKit.Core.Media;
using EditorControl = AvaloniaEdit.TextEditor;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	protected override string GetSingleParagraphText(Control content) => GetText(SingleParagraphBlock(content).Inlines);

	protected override (double FontSize, bool IsBold) GetSingleParagraphFont(Control content)
	{
		TextBlock paragraph = SingleParagraphBlock(content);
		return (paragraph.FontSize, paragraph.FontWeight == FontWeight.Bold);
	}

	protected override int CountInlines(Control content, RenderedInlineKind kind)
	{
		var inlines = FindInlines(content).ToList();

		return kind switch
		{
			RenderedInlineKind.Bold => inlines.OfType<Bold>().Count(),
			RenderedInlineKind.Italic => inlines.OfType<Italic>().Count(),
			RenderedInlineKind.LineBreak => inlines.OfType<LineBreak>().Count(),
			RenderedInlineKind.Embedded => inlines.OfType<InlineUIContainer>().Count(container => container.Child is not MarkdownLinkElement),
			RenderedInlineKind.Link => inlines.OfType<InlineUIContainer>().Count(container => container.Child is MarkdownLinkElement),

			RenderedInlineKind.Strikethrough => inlines
				.OfType<Span>()
				.Count(span => span.TextDecorations is { Count: > 0 } decorations && decorations[0].Location == TextDecorationLocation.Strikethrough),

			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown inline kind."),
		};
	}

	protected override int CountTopLevelBlocks(Control content) => GetBlockPanel(content).Children.Count;

	protected override Border FindInlineCodeBorder(Control content) => GetInlineCodeBorder(content);

	protected override Border FindCodeBlockBorder(Control content) => GetCodeBlockBorder(content);

	protected override Border FindThematicBreak(Control content) => GetBlockPanel(content).Children.OfType<Border>().Single();

	protected override ArgbColor GetSolidBackground(Border border) => ToArgbColor(((ISolidColorBrush)border.Background!).Color);

	protected override ArgbColor GetSolidForeground(TextBlock textBlock) => ToArgbColor(((ISolidColorBrush)textBlock.Foreground!).Color);

	protected override ArgbColor GetBlockquoteBorderColor(Control content) =>
		ToArgbColor(((ISolidColorBrush)GetBlockPanel(content).Children.OfType<Border>().Single().BorderBrush!).Color);

	protected override void AssertFontFamilyMatches(string? family, TextBlock textBlock)
	{
		Assert.IsNotNull(family);
		Assert.AreEqual(new FontFamily(family).Name, textBlock.FontFamily.Name);
	}

	protected override IReadOnlyList<string> GetListMarkers(Control content) =>
		[.. GetListMarkerBlocks(content).Select(marker => marker.Text ?? string.Empty)];

	protected override IReadOnlyList<(double Top, double Bottom)> GetListItemMargins(Control content) =>
		[.. GetListItemTextBlocks(content).Select(block => (block.Margin.Top, block.Margin.Bottom))];

	protected override (double BarWidth, double TopWidth, int BlockCount) GetBlockquote(Control content)
	{
		Border quote = GetBlockPanel(content).Children.OfType<Border>().Single();
		return (quote.BorderThickness.Left, quote.BorderThickness.Top, ((StackPanel)quote.Child!).Children.Count);
	}

	protected override (int Rows, int Columns) GetTableShape(Control content)
	{
		var grid = (Grid)GetBlockPanel(content).Children.Single();
		return (grid.RowDefinitions.Count, grid.ColumnDefinitions.Count);
	}

	protected override TextAlignment GetTableCellAlignment(Control content, int row, int column)
	{
		var grid = (Grid)GetBlockPanel(content).Children.Single();
		Border cell = grid.Children.OfType<Border>().Single(candidate => Grid.GetRow(candidate) == row && Grid.GetColumn(candidate) == column);

		return ((TextBlock)((StackPanel)cell.Child!).Children[0]).TextAlignment;
	}

	protected override IReadOnlyList<double> GetTableCellBottomMargins(Control content)
	{
		var grid = (Grid)GetBlockPanel(content).Children.Single();
		return [.. grid.Children.OfType<Border>().Select(cell => ((TextBlock)((StackPanel)cell.Child!).Children[0]).Margin.Bottom)];
	}

	protected override bool IsTableCellBold(Control content, int row, int column)
	{
		var grid = (Grid)GetBlockPanel(content).Children.Single();
		Border cell = grid.Children.OfType<Border>().Single(candidate => Grid.GetRow(candidate) == row && Grid.GetColumn(candidate) == column);

		return ((TextBlock)((StackPanel)cell.Child!).Children[0]).FontWeight == FontWeight.Bold;
	}

	protected override IReadOnlyList<(double Top, double Bottom)> GetTopLevelTextMargins(Control content) =>
		[.. GetBlockPanel(content).Children.OfType<TextBlock>().Select(block => (block.Margin.Top, block.Margin.Bottom))];

	protected override (ScrollBarVisibility Vertical, ScrollBarVisibility Horizontal) GetViewerScrollBars(Control content)
	{
		var viewer = (ScrollViewer)content;
		return (viewer.VerticalScrollBarVisibility, viewer.HorizontalScrollBarVisibility);
	}

	protected override (bool Focusable, bool IsTabStop, bool IsSelectable) GetViewerInteraction(Control content) =>
		(content.Focusable, content.IsTabStop, SingleParagraphBlock(content) is SelectableTextBlock);

	protected override (Uri? OpenableUri, string Text, bool Focusable) GetSingleLink(Control content)
	{
		MarkdownLinkElement link = GetLinkElement(content);
		return (link.OpenableUri, GetText(link.Inlines), link.Focusable);
	}

	protected override bool ActivateSingleLink(Control content) =>
		RaisePointerPressed(GetLinkElement(content), PointerUpdateKind.LeftButtonPressed).Handled;

	protected override IReadOnlyList<RenderedLinkLook> GetLinkLooks(Control content)
	{
		return
		[
			.. FindInlines(content)
				.OfType<InlineUIContainer>()
				.Select(container => container.Child)
				.OfType<MarkdownLinkElement>()
				.Select(link => new RenderedLinkLook(
					ShowsHandCursor: !ReferenceEquals(link.Cursor, Cursor.Default),
					Foreground: ToArgbColor(((ISolidColorBrush)link.Foreground!).Color),

					IsUnderlined: link.TextDecorations is { } decorations
						&& decorations.Any(decoration => decoration.Location == TextDecorationLocation.Underline)
				)),
		];
	}

	protected override bool PressPrimaryButtonOn(EditorControl editor) => RaisePointerPressed(editor, PointerUpdateKind.LeftButtonPressed).Handled;

	private static ArgbColor ToArgbColor(Color color) => new(A: color.A, R: color.R, G: color.G, B: color.B);

	private static IEnumerable<TextBlock> GetListMarkerBlocks(Control content)
	{
		var list = (StackPanel)GetBlockPanel(content).Children.Single();

		foreach (Grid item in list.Children.OfType<Grid>())
			yield return item.Children.OfType<TextBlock>().Single();
	}

	private static IEnumerable<TextBlock> GetListItemTextBlocks(Control content)
	{
		var list = (StackPanel)GetBlockPanel(content).Children.Single();

		foreach (Grid item in list.Children.OfType<Grid>())
		{
			var itemContent = (StackPanel)item.Children[1];

			foreach (TextBlock block in itemContent.Children.OfType<TextBlock>())
				yield return block;
		}
	}

	private static TextBlock GetCellText(Grid grid, int childIndex)
	{
		var cell = (Border)grid.Children[childIndex];
		var content = (StackPanel)cell.Child!;

		return (TextBlock)content.Children[0];
	}
}
