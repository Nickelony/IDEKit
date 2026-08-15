using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

/// <summary>
/// Turns the engine-neutral <see cref="MarkdownDocumentModel"/> into an Avalonia control tree for
/// <see cref="MarkdownRenderer"/>.
/// </summary>
/// <remarks>
/// <include file="../../../../shared/docs/MarkdownEmitter.xml" path="doc/members/member[@name='MarkdownEmitter.Remarks']/*"/>
/// </remarks>
internal static partial class MarkdownContentEmitter
{
	private const double InlineCodeHorizontalPadding = 4.0;
	private const double InlineCodeVerticalPadding = 1.0;
	private const double InlineCodeBorderThickness = 1.0;
	private const double InlineCodeCornerRadius = 2.0;
	private const double QuotePaddingHorizontal = 10.0;
	private const double QuotePaddingVertical = 4.0;
	private const double QuoteBarThickness = 3.0;
	private const double ListIndent = 18.0;
	private const double TableCellBorderThickness = 0.5;
	private const double TableCellHorizontalPadding = 6.0;
	private const double TableCellVerticalPadding = 2.0;
	private const double ThematicBreakThickness = 1.0;

	private const string BulletMarker = "\u2022";

	[LoggerMessage(
		EventId = 2001,
		EventName = "HyperlinkOpenFailed",
		Level = LogLevel.Warning,
		Message = "Opening hyperlink '{Uri}' failed."
	)]
	private static partial void LogHyperlinkOpenFailed(ILogger logger, string uri, Exception? exception);

	/// <summary>
	/// Renders the given document model into a new block panel.
	/// </summary>
	/// <param name="model">The document model to render.</param>
	/// <param name="theme">The theme to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <returns>The panel that carries the document's top-level blocks.</returns>
	internal static StackPanel Emit(MarkdownDocumentModel model, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		StackPanel panel = CreateBlockPanel(theme);

		foreach (MarkdownBlock block in model.Blocks)
			AddBlock(panel, block, theme, options);

		return panel;
	}

	/// <summary>
	/// Creates the empty block panel that carries the theme's body font, foreground, and flow direction,
	/// before any block is added.
	/// </summary>
	/// <param name="theme">The theme to render with.</param>
	/// <returns>The configured panel.</returns>
	internal static StackPanel CreateBlockPanel(MarkdownRenderTheme theme)
	{
		return new StackPanel
		{
			Orientation = Orientation.Vertical,
			FlowDirection = theme.FlowDirection
		};
	}

	/// <summary>
	/// Creates a panel that shows <paramref name="content"/> as a single plain-text block, used by the
	/// fallback paths.
	/// </summary>
	/// <param name="content">The text to show.</param>
	/// <param name="theme">The theme to render with.</param>
	/// <returns>The plain-text panel.</returns>
	internal static StackPanel CreatePlainTextPanel(string content, MarkdownRenderTheme theme)
	{
		StackPanel panel = CreateBlockPanel(theme);

		panel.Children.Add(new TextBlock
		{
			Text = content,
			TextWrapping = TextWrapping.Wrap,
			FontFamily = theme.BodyFontFamily,
			FontSize = theme.BodyFontSize,
			Foreground = theme.Foreground,
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing)
		});

		return panel;
	}

	private static void AddBlock(StackPanel panel, MarkdownBlock block, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		Control? control = EmitBlock(block, theme, options);

		if (control is not null)
			panel.Children.Add(control);
	}

	private static Control? EmitBlock(MarkdownBlock block, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		switch (block)
		{
			case MarkdownParagraph paragraphBlock:
				return CreateTextBlock(paragraphBlock.Inlines, theme, options, new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing));

			case MarkdownHeading headingBlock:
				return CreateHeading(headingBlock, theme, options);

			case MarkdownCodeBlock codeBlock:
				return MarkdownCodeBlockFactory.CreateCodeBlockElement(codeBlock.Language, codeBlock.Code, theme, options);

			case MarkdownQuote quoteBlock:
				return CreateQuoteBlock(quoteBlock, theme, options);

			case MarkdownList listBlock:
				return CreateListBlock(listBlock, theme, options);

			case MarkdownThematicBreak:
				return CreateThematicBreak(theme);

			case MarkdownTable tableBlock:
				return CreateTableBlock(tableBlock, theme, options);

			default:
				return null;
		}
	}

	private static TextBlock CreateHeading(MarkdownHeading headingBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		TextBlock textBlock = CreateTextBlock(
			headingBlock.Inlines,
			theme,
			options,
			new Thickness(0.0, theme.BlockSpacing, 0.0, theme.BlockSpacing));

		textBlock.FontSize = MarkdownHeadingSizes.GetFontSize(
			theme.BodyFontSize,
			theme.HeadingFontSizeScales,
			headingBlock.Level,
			MarkdownRenderTheme.FontSizeUpperBound);
		textBlock.FontWeight = FontWeight.Bold;

		return textBlock;
	}

	private static TextBlock CreateTextBlock(
		IReadOnlyList<MarkdownInline> inlines,
		MarkdownRenderTheme theme,
		MarkdownRenderOptions options,
		Thickness margin)
	{
		TextBlock textBlock = options.AllowContentInteraction ? new SelectableTextBlock() : new TextBlock();

		textBlock.TextWrapping = TextWrapping.Wrap;
		textBlock.FontFamily = theme.BodyFontFamily;
		textBlock.FontSize = theme.BodyFontSize;
		textBlock.Foreground = theme.Foreground;
		textBlock.Margin = margin;
		textBlock.Inlines = BuildInlines(inlines, theme, options);

		return textBlock;
	}

	private static Border CreateQuoteBlock(MarkdownQuote quoteBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var panel = new StackPanel { Orientation = Orientation.Vertical };

		foreach (MarkdownBlock child in quoteBlock.Blocks)
			AddBlock(panel, child, theme, options);

		return new Border
		{
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing),
			Padding = new Thickness(QuotePaddingHorizontal, QuotePaddingVertical, QuotePaddingHorizontal, QuotePaddingVertical),
			BorderBrush = MarkdownDerivedBrushCache.GetBorderBrush(theme),
			BorderThickness = new Thickness(QuoteBarThickness, 0.0, 0.0, 0.0),
			FlowDirection = theme.FlowDirection,
			Child = panel
		};
	}

	private static StackPanel CreateListBlock(MarkdownList listBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var list = new StackPanel
		{
			Orientation = Orientation.Vertical,
			Margin = new Thickness(ListIndent, 0.0, 0.0, theme.BlockSpacing)
		};

		for (int index = 0; index < listBlock.Items.Count; index++)
		{
			string marker = listBlock.IsOrdered
				? (listBlock.StartIndex + index).ToString(CultureInfo.InvariantCulture) + "."
				: BulletMarker;

			list.Children.Add(CreateListItem(listBlock.Items[index], marker, theme, options, listBlock.IsLoose));
		}

		return list;
	}

	private static Grid CreateListItem(MarkdownListItem item, string marker, MarkdownRenderTheme theme, MarkdownRenderOptions options, bool isLoose)
	{
		var grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

		var markerBlock = new TextBlock
		{
			Text = marker,
			Margin = new Thickness(0.0, 0.0, 6.0, 0.0)
		};

		var content = new StackPanel { Orientation = Orientation.Vertical };

		foreach (MarkdownBlock child in item.Blocks)
		{
			Control? control = EmitBlock(child, theme, options);

			if (control is null)
				continue;

			// A tight list renders its items compactly, so the block spacing that separates the items of
			// a loose list is suppressed for an item of a tight list.
			if (!isLoose)
				SuppressVerticalSpacing(control);

			content.Children.Add(control);
		}

		Grid.SetColumn(markerBlock, 0);
		Grid.SetColumn(content, 1);
		grid.Children.Add(markerBlock);
		grid.Children.Add(content);

		return grid;
	}

	private static Border CreateThematicBreak(MarkdownRenderTheme theme)
	{
		return new Border
		{
			Height = ThematicBreakThickness,
			Background = MarkdownDerivedBrushCache.GetBorderBrush(theme),
			Margin = new Thickness(0.0, theme.BlockSpacing, 0.0, theme.BlockSpacing)
		};
	}

	private static Grid CreateTableBlock(MarkdownTable tableBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var grid = new Grid { Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing) };

		int columnCount = Math.Max(tableBlock.ColumnAlignments.Count, GetMaxCellCount(tableBlock));

		for (int column = 0; column < columnCount; column++)
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		for (int rowIndex = 0; rowIndex < tableBlock.Rows.Count; rowIndex++)
		{
			MarkdownTableRow row = tableBlock.Rows[rowIndex];
			grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			int columnIndex = 0;

			foreach (MarkdownTableCell cell in row.Cells)
			{
				Control cellControl = CreateTableCell(cell, row.IsHeader, theme, options);

				Grid.SetRow(cellControl, rowIndex);
				Grid.SetColumn(cellControl, columnIndex);
				Grid.SetColumnSpan(cellControl, cell.ColumnSpan);
				Grid.SetRowSpan(cellControl, cell.RowSpan);
				grid.Children.Add(cellControl);

				columnIndex += cell.ColumnSpan;
			}
		}

		return grid;
	}

	private static int GetMaxCellCount(MarkdownTable tableBlock)
	{
		int count = 0;

		foreach (MarkdownTableRow row in tableBlock.Rows)
		{
			int rowCount = 0;

			foreach (MarkdownTableCell cell in row.Cells)
				rowCount += cell.ColumnSpan;

			count = Math.Max(count, rowCount);
		}

		return count;
	}

	private static Border CreateTableCell(MarkdownTableCell cell, bool isHeader, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var content = new StackPanel { Orientation = Orientation.Vertical };

		foreach (MarkdownBlock child in cell.Blocks)
		{
			Control? control = EmitBlock(child, theme, options);

			if (control is null)
				continue;

			// The cell supplies its own padding, so the vertical block spacing would leave dead space
			// above and below the cell content.
			SuppressVerticalSpacing(control);
			ApplyCellStyle(control, isHeader, cell.Alignment);
			content.Children.Add(control);
		}

		return new Border
		{
			BorderBrush = MarkdownDerivedBrushCache.GetBorderBrush(theme),
			BorderThickness = new Thickness(TableCellBorderThickness),
			Padding = new Thickness(TableCellHorizontalPadding, TableCellVerticalPadding, TableCellHorizontalPadding, TableCellVerticalPadding),
			FlowDirection = theme.FlowDirection,
			Child = content
		};
	}

	private static void ApplyCellStyle(Control control, bool isHeader, MarkdownColumnAlignment? alignment)
	{
		if (control is not TextBlock textBlock)
			return;

		if (isHeader)
			textBlock.FontWeight = FontWeight.Bold;

		textBlock.TextAlignment = alignment switch
		{
			MarkdownColumnAlignment.Left => TextAlignment.Left,
			MarkdownColumnAlignment.Center => TextAlignment.Center,
			MarkdownColumnAlignment.Right => TextAlignment.Right,
			_ => textBlock.TextAlignment
		};
	}

	private static void SuppressVerticalSpacing(Control control)
	{
		Thickness margin = control.Margin;
		control.Margin = new Thickness(margin.Left, 0.0, margin.Right, 0.0);
	}

	private static InlineCollection BuildInlines(IReadOnlyList<MarkdownInline> inlines, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var collection = new InlineCollection();

		foreach (MarkdownInline inline in inlines)
		{
			Inline? rendered = EmitInline(inline, theme, options);

			if (rendered is not null)
				collection.Add(rendered);
		}

		return collection;
	}

	private static Inline? EmitInline(MarkdownInline inline, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		switch (inline)
		{
			case MarkdownText text:
				return new Run(text.Text);

			case MarkdownCodeSpan codeSpan:
				return new InlineUIContainer(CreateInlineCodeBorder(codeSpan.Text, theme));

			case MarkdownLink link:
				return new InlineUIContainer(CreateLinkElement(link, theme, options));

			case MarkdownEmphasis emphasis:
				return CreateEmphasis(emphasis, theme, options);

			case MarkdownLineBreak:
				return new LineBreak();

			case MarkdownImagePlaceholder placeholder:
				return new Run(placeholder.Text)
				{
					FontStyle = FontStyle.Italic,
					Foreground = MarkdownDerivedBrushCache.GetMutedForeground(theme)
				};

			case MarkdownSpan span:
				return new Span { Inlines = BuildInlines(span.Inlines, theme, options) };

			default:
				return null;
		}
	}

	private static Span CreateEmphasis(MarkdownEmphasis emphasis, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		Span span = emphasis.Kind switch
		{
			MarkdownEmphasisKind.Strikethrough => new Span { TextDecorations = TextDecorations.Strikethrough },
			MarkdownEmphasisKind.Bold => new Bold(),
			_ => new Italic()
		};

		span.Inlines = BuildInlines(emphasis.Inlines, theme, options);
		return span;
	}

	private static MarkdownLinkElement CreateLinkElement(MarkdownLink link, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		MarkdownLinkElement element = MarkdownLinkElement.Create(theme, link, options, uri => TryOpenHyperlink(uri, options));

		element.Inlines = BuildInlines(link.Inlines, theme, options);
		return element;
	}

	private static Border CreateInlineCodeBorder(string text, MarkdownRenderTheme theme)
	{
		return new Border
		{
			Background = MarkdownDerivedBrushCache.GetCodeBackground(theme),
			BorderBrush = MarkdownDerivedBrushCache.GetBorderBrush(theme),
			BorderThickness = new Thickness(InlineCodeBorderThickness),
			CornerRadius = new CornerRadius(InlineCodeCornerRadius),
			Padding = new Thickness(InlineCodeHorizontalPadding, InlineCodeVerticalPadding, InlineCodeHorizontalPadding, InlineCodeVerticalPadding),
			FlowDirection = theme.FlowDirection,
			VerticalAlignment = VerticalAlignment.Center,
			Child = new TextBlock
			{
				Text = text,
				Foreground = theme.Foreground,
				FontFamily = theme.CodeFontFamily,
				FontSize = theme.CodeFontSize,

				// Inline code wraps instead of overflowing the rendered width: an identifier wider than
				// the available width cannot be scrolled to horizontally, so it must break inside the
				// border.
				TextWrapping = TextWrapping.Wrap,
				MaxWidth = MarkdownCodeLayout.GetInnerContentWidth(
					theme.CodeMaxWidth,
					InlineCodeHorizontalPadding,
					InlineCodeBorderThickness)
			}
		};
	}

	// The link is already known to be openable when this runs: the open callback is attached only for a
	// supported target, so the support check is not repeated here.
	private static bool TryOpenHyperlink(Uri uri, MarkdownRenderOptions options)
	{
		// A callback result other than true counts as not handled, so the chain falls through to the
		// next callback exactly as it would without the callback. When no callback handles the
		// activation, the press is ignored: the renderer never opens a link on its own.
		if (options.OpenHyperlink is { } openHyperlink && TryInvokeOpener(openHyperlink, uri, options))
			return true;

		return options.OpenExternalUri is { } openExternalUri && TryInvokeOpener(openExternalUri, uri, options);
	}

	private static bool TryInvokeOpener(Func<Uri, bool> opener, Uri uri, MarkdownRenderOptions options)
	{
		try
		{
			return opener(uri);
		}
		catch (Exception exception)
		{
			// Opening a link is a best-effort host action: report the failure and fall through to the
			// next opener in the chain.
			ReportHyperlinkOpenFailure(uri.AbsoluteUri, exception, options);
			return false;
		}
	}

	private static void ReportHyperlinkOpenFailure(string uri, Exception exception, MarkdownRenderOptions options)
	{
		if (options.Logger is { } logger)
			LogHyperlinkOpenFailed(logger, uri, exception);
	}
}
