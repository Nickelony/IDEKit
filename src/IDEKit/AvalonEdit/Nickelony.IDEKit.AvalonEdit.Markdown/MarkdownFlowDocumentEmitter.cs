using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using List = System.Windows.Documents.List;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Turns the engine-neutral <see cref="MarkdownDocumentModel"/> into the WPF
/// <see cref="FlowDocument"/> representation of Markdown content for <see cref="MarkdownRenderer"/>.
/// </summary>
/// <remarks>
/// <include file="../../../../shared/docs/MarkdownEmitter.xml" path="doc/members/member[@name='MarkdownEmitter.Remarks']/*"/>
/// </remarks>
internal static partial class MarkdownFlowDocumentEmitter
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

	[LoggerMessage(
		EventId = 2001,
		EventName = "HyperlinkOpenFailed",
		Level = LogLevel.Warning,
		Message = "Opening hyperlink '{Uri}' failed."
	)]
	private static partial void LogHyperlinkOpenFailed(ILogger logger, string uri, Exception? exception);

	/// <summary>
	/// Parses and renders the given Markdown into a new flow document.
	/// </summary>
	/// <param name="markdown">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <returns>The rendered document.</returns>
	internal static FlowDocument Render(string markdown, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		MarkdownDocumentModel model = MarkdownDocumentModelBuilder.Build(markdown, options);

		return RenderDocument(model, theme, options);
	}

	/// <summary>
	/// Creates the empty flow document that carries the theme's body font, foreground, flow direction,
	/// transparent background, and zero page padding, before any block is added.
	/// </summary>
	/// <param name="theme">The theme to render with.</param>
	/// <returns>The configured document.</returns>
	internal static FlowDocument CreateBaseFlowDocument(MarkdownRenderTheme theme)
	{
		return new FlowDocument
		{
			FontFamily = theme.BodyFontFamily,
			FontSize = theme.BodyFontSize,
			Foreground = theme.Foreground,
			Background = Brushes.Transparent,
			FlowDirection = theme.FlowDirection,
			PagePadding = new Thickness(0.0)
		};
	}

	/// <summary>
	/// Creates a flow document that shows <paramref name="content"/> as a single plain-text paragraph,
	/// used by the fallback paths.
	/// </summary>
	/// <param name="content">The text to show.</param>
	/// <param name="theme">The theme to render with.</param>
	/// <returns>The plain-text document.</returns>
	internal static FlowDocument CreatePlainTextFlowDocument(string content, MarkdownRenderTheme theme)
	{
		FlowDocument flowDocument = CreateBaseFlowDocument(theme);

		flowDocument.Blocks.Add(new Paragraph(new Run(content))
		{
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing)
		});

		return flowDocument;
	}

	private static FlowDocument RenderDocument(MarkdownDocumentModel model, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		FlowDocument flowDocument = CreateBaseFlowDocument(theme);

		foreach (MarkdownBlock block in model.Blocks)
		{
			Block? renderedBlock = RenderBlock(block, theme, options);

			if (renderedBlock is not null)
				flowDocument.Blocks.Add(renderedBlock);
		}

		return flowDocument;
	}

	private static Block? RenderBlock(MarkdownBlock block, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		switch (block)
		{
			case MarkdownParagraph paragraphBlock:
				return RenderParagraph(paragraphBlock, theme, options);

			case MarkdownHeading headingBlock:
				return RenderHeading(headingBlock, theme, options);

			case MarkdownCodeBlock codeBlock:
				return new BlockUIContainer(MarkdownCodeBlockFactory.CreateCodeBlockElement(codeBlock.Language, codeBlock.Code, theme, options));

			case MarkdownQuote quoteBlock:
				return RenderQuoteBlock(quoteBlock, theme, options);

			case MarkdownList listBlock:
				return RenderListBlock(listBlock, theme, options);

			case MarkdownThematicBreak:
				return RenderThematicBreak(theme);

			case MarkdownTable tableBlock:
				return RenderTableBlock(tableBlock, theme, options);

			default:
				return null;
		}
	}

	private static Paragraph RenderParagraph(MarkdownParagraph paragraphBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var paragraph = new Paragraph
		{
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing)
		};

		RenderInlines(paragraph.Inlines, paragraphBlock.Inlines, theme, options);
		return paragraph;
	}

	private static Paragraph RenderHeading(MarkdownHeading headingBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var paragraph = new Paragraph
		{
			Margin = new Thickness(0.0, theme.BlockSpacing, 0.0, theme.BlockSpacing),
			FontSize = GetHeadingFontSize(theme, headingBlock.Level),
			FontWeight = FontWeights.Bold
		};

		RenderInlines(paragraph.Inlines, headingBlock.Inlines, theme, options);
		return paragraph;
	}

	private static double GetHeadingFontSize(MarkdownRenderTheme theme, int level)
	{
		// WPF rejects font sizes above the theme's font-size upper bound, so an extreme scale would
		// fail the whole rendering; the largest accepted size keeps the document renderable instead.
		return MarkdownHeadingSizes.GetFontSize(
			theme.BodyFontSize,
			theme.HeadingFontSizeScales,
			level,
			MarkdownRenderTheme.FontSizeUpperBound);
	}

	private static Section RenderQuoteBlock(MarkdownQuote quoteBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var section = new Section
		{
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing),
			Padding = new Thickness(QuotePaddingHorizontal, QuotePaddingVertical, QuotePaddingHorizontal, QuotePaddingVertical),
			BorderBrush = MarkdownDerivedBrushCache.GetBorderBrush(theme),
			BorderThickness = new Thickness(QuoteBarThickness, 0.0, 0.0, 0.0)
		};

		foreach (MarkdownBlock child in quoteBlock.Blocks)
		{
			Block? renderedBlock = RenderBlock(child, theme, options);

			if (renderedBlock is not null)
				section.Blocks.Add(renderedBlock);
		}

		return section;
	}

	private static List RenderListBlock(MarkdownList listBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var list = new List
		{
			Margin = new Thickness(ListIndent, 0.0, 0.0, theme.BlockSpacing),
			MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
			StartIndex = listBlock.StartIndex
		};

		foreach (MarkdownListItem item in listBlock.Items)
			list.ListItems.Add(RenderListItemBlock(item, theme, options, listBlock.IsLoose));

		return list;
	}

	private static ListItem RenderListItemBlock(MarkdownListItem listItemBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options, bool isLoose)
	{
		var listItem = new ListItem();

		foreach (MarkdownBlock child in listItemBlock.Blocks)
		{
			Block? renderedBlock = RenderBlock(child, theme, options);

			if (renderedBlock is not null)
			{
				// A tight list renders its items compactly, so the block spacing that separates the items of
				// a loose list is suppressed for an item of a tight list.
				if (!isLoose)
					SuppressVerticalSpacing(renderedBlock);

				listItem.Blocks.Add(renderedBlock);
			}
		}

		return listItem;
	}

	private static BlockUIContainer RenderThematicBreak(MarkdownRenderTheme theme)
	{
		return new BlockUIContainer(new Border
		{
			Height = ThematicBreakThickness,
			Background = MarkdownDerivedBrushCache.GetBorderBrush(theme),
			Margin = new Thickness(0.0, theme.BlockSpacing, 0.0, theme.BlockSpacing)
		});
	}

	private static Table RenderTableBlock(MarkdownTable tableBlock, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var table = new Table
		{
			CellSpacing = 0.0,
			Margin = new Thickness(0.0, 0.0, 0.0, theme.BlockSpacing)
		};

		var rowGroup = new TableRowGroup();
		table.RowGroups.Add(rowGroup);

		foreach (MarkdownTableRow markdownRow in tableBlock.Rows)
		{
			var row = new TableRow();
			rowGroup.Rows.Add(row);

			foreach (MarkdownTableCell markdownCell in markdownRow.Cells)
			{
				var cell = new TableCell
				{
					BorderBrush = MarkdownDerivedBrushCache.GetBorderBrush(theme),
					BorderThickness = new Thickness(TableCellBorderThickness),
					Padding = new Thickness(TableCellHorizontalPadding, TableCellVerticalPadding, TableCellHorizontalPadding, TableCellVerticalPadding),
					ColumnSpan = markdownCell.ColumnSpan,
					RowSpan = markdownCell.RowSpan,
					FontWeight = markdownRow.IsHeader ? FontWeights.Bold : FontWeights.Normal
				};

				foreach (MarkdownBlock child in markdownCell.Blocks)
				{
					Block? renderedBlock = RenderBlock(child, theme, options);

					if (renderedBlock is not null)
					{
						// The cell supplies its own padding, so the vertical block spacing would leave dead space
						// above and below the cell content.
						SuppressVerticalSpacing(renderedBlock);
						cell.Blocks.Add(renderedBlock);
					}
				}

				// GFM column alignment is carried by the table's column definitions, not by the cells.
				if (GetTextAlignment(markdownCell.Alignment) is TextAlignment textAlignment)
					ApplyTextAlignment(cell, textAlignment);

				row.Cells.Add(cell);
			}
		}

		return table;
	}

	private static TextAlignment? GetTextAlignment(MarkdownColumnAlignment? alignment)
		=> alignment switch
		{
			MarkdownColumnAlignment.Left => TextAlignment.Left,
			MarkdownColumnAlignment.Center => TextAlignment.Center,
			MarkdownColumnAlignment.Right => TextAlignment.Right,
			_ => null
		};

	private static void ApplyTextAlignment(TableCell cell, TextAlignment alignment)
	{
		foreach (Block block in cell.Blocks)
		{
			if (block is Paragraph paragraph)
				paragraph.TextAlignment = alignment;
		}
	}

	private static void SuppressVerticalSpacing(Block block)
	{
		Thickness margin = block.Margin;
		block.Margin = new Thickness(margin.Left, 0.0, margin.Right, 0.0);

		// A block that hosts a UI element (a code block or a thematic break) carries the spacing on the
		// hosted element instead of the block itself.
		if (block is BlockUIContainer { Child: FrameworkElement child })
			child.Margin = new Thickness(child.Margin.Left, 0.0, child.Margin.Right, 0.0);
	}

	private static void RenderInlines(InlineCollection target, IReadOnlyList<MarkdownInline> inlines, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		foreach (MarkdownInline inline in inlines)
		{
			Inline? rendered = RenderInline(inline, theme, options);

			if (rendered is not null)
				target.Add(rendered);
		}
	}

	private static Inline? RenderInline(MarkdownInline inline, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		switch (inline)
		{
			case MarkdownText text:
				return new Run(text.Text);

			case MarkdownCodeSpan codeSpan:
				return CreateInlineCodeContainer(codeSpan.Text, theme);

			case MarkdownLink link:
				return RenderLink(link, theme, options);

			case MarkdownEmphasis emphasis:
				return RenderEmphasis(emphasis, theme, options);

			case MarkdownLineBreak:
				return new LineBreak();

			case MarkdownImagePlaceholder placeholder:
				return CreateImagePlaceholder(placeholder.Text, theme);

			case MarkdownSpan span:
				return RenderSpan(span.Inlines, theme, options);

			default:
				return null;
		}
	}

	private static Span RenderSpan(IReadOnlyList<MarkdownInline> inlines, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var span = new Span();

		RenderInlines(span.Inlines, inlines, theme, options);
		return span;
	}

	private static Hyperlink RenderLink(MarkdownLink link, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		Hyperlink hyperlink = CreateHyperlink(link.OpenableUri, theme, options);

		RenderInlines(hyperlink.Inlines, link.Inlines, theme, options);
		return hyperlink;
	}

	// The fallback an image without alt text renders: the image target, or a generic marker when the image
	// has no target either. It is muted and italic so it reads as a placeholder rather than as body text.
	private static Run CreateImagePlaceholder(string text, MarkdownRenderTheme theme)
	{
		return new Run(text)
		{
			FontStyle = FontStyles.Italic,
			Foreground = MarkdownDerivedBrushCache.GetMutedForeground(theme)
		};
	}

	private static Hyperlink CreateHyperlink(Uri? openableUri, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		bool canOpen = openableUri is not null;

		var hyperlink = new Hyperlink
		{
			NavigateUri = openableUri,
			Foreground = theme.LinkForeground,
			TextDecorations = TextDecorations.Underline,
			Cursor = canOpen ? Cursors.Hand : Cursors.Arrow,
			// Only an openable link becomes a keyboard stop when interaction is enabled; a link the
			// renderer cannot open must not add a dead stop.
			Focusable = canOpen && options.AllowContentInteraction
		};

		if (openableUri is not null)
		{
			// The renderer owns activation (the configured opener callbacks) and marks both events as
			// handled: the navigation request so an ambient NavigationService does not navigate twice,
			// and the click so no ambient handler reacts to a link the renderer owns.
			hyperlink.RequestNavigate += (_, e) => e.Handled = true;
			hyperlink.Click += (_, e) =>
			{
				TryOpenHyperlink(openableUri, options);
				e.Handled = true;
			};
		}

		return hyperlink;
	}

	private static Span RenderEmphasis(MarkdownEmphasis emphasis, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		Span span = emphasis.Kind switch
		{
			MarkdownEmphasisKind.Strikethrough => new Span { TextDecorations = TextDecorations.Strikethrough },
			MarkdownEmphasisKind.Bold => new Bold(),
			_ => new Italic()
		};

		RenderInlines(span.Inlines, emphasis.Inlines, theme, options);
		return span;
	}

	private static InlineUIContainer CreateInlineCodeContainer(string text, MarkdownRenderTheme theme)
	{
		return new InlineUIContainer(
			new Border
			{
				Background = MarkdownDerivedBrushCache.GetCodeBackground(theme),
				BorderBrush = MarkdownDerivedBrushCache.GetBorderBrush(theme),
				BorderThickness = new Thickness(InlineCodeBorderThickness),
				CornerRadius = new CornerRadius(InlineCodeCornerRadius),
				Padding = new Thickness(InlineCodeHorizontalPadding, InlineCodeVerticalPadding, InlineCodeHorizontalPadding, InlineCodeVerticalPadding),
				FlowDirection = theme.FlowDirection,
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
			})
		{
			BaselineAlignment = BaselineAlignment.Center
		};
	}

	// The link is already known to be openable when this runs: the click handler is attached only for a
	// supported target, so the support check is not repeated here.
	private static bool TryOpenHyperlink(Uri uri, MarkdownRenderOptions options)
	{
		// A callback result other than true counts as not handled, so the chain falls through to the
		// next callback exactly as it would without the handler. When no callback handles the
		// activation, the click is ignored: the renderer never opens a link on its own.
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
