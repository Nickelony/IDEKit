using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Globalization;
using MarkdigBlock = Markdig.Syntax.Block;
using MarkdigInline = Markdig.Syntax.Inlines.Inline;
using MarkdigMarkdown = Markdig.Markdown;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// Parses Markdown and walks the Markdig AST into the engine-neutral <see cref="MarkdownDocumentModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// The walk is defined once for every editor binding: it carries the Markdig-to-model mapping and the
/// decisions that are independent of the toolkit (which heading level scales which way, how a list is
/// ordered, how table columns align, whether a link target is openable). A binding's emitter resolves the
/// remaining toolkit concerns (colors, fonts, element creation) while it walks the model.
/// </para>
/// <para>
/// The pipeline is deliberately narrow so unsupported Markdig constructs render as literal text instead of
/// an approximation with the wrong style; <see cref="MarkdownRenderer.CreateContent"/> documents the enabled
/// extensions. Raw HTML is shown literally rather than interpreted.
/// </para>
/// </remarks>
internal static class MarkdownDocumentModelBuilder
{
	// Deliberately narrow pipeline so unsupported Markdig constructs render as literal text instead of an
	// approximation with the wrong style; MarkdownRenderer.CreateContent documents the enabled extensions.
	private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
		.UsePipeTables()
		.UseAutoLinks()
		.UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
		.Build();

	/// <summary>
	/// Parses the given Markdown and walks it into the neutral document model.
	/// </summary>
	/// <param name="markdown">The Markdown content to parse; line endings are expected to be normalized.</param>
	/// <param name="options">The rendering options that decide, for example, which link targets are openable.</param>
	/// <returns>The document model.</returns>
	internal static MarkdownDocumentModel Build(string markdown, MarkdownRenderOptions options)
	{
		MarkdownDocument document = MarkdigMarkdown.Parse(markdown, s_pipeline);

		var blocks = new List<MarkdownBlock>(document.Count);

		foreach (MarkdigBlock block in document)
		{
			MarkdownBlock? modelBlock = BuildBlock(block, options);

			if (modelBlock is not null)
				blocks.Add(modelBlock);
		}

		return new MarkdownDocumentModel(blocks);
	}

	private static MarkdownBlock? BuildBlock(MarkdigBlock block, MarkdownRenderOptions options)
	{
		switch (block)
		{
			case ParagraphBlock paragraphBlock:
				return BuildParagraph(paragraphBlock.Inline, options);

			case HeadingBlock headingBlock:
				return new MarkdownHeading(headingBlock.Level, BuildInlines(headingBlock.Inline, options));

			case FencedCodeBlock fencedCodeBlock:
				return new MarkdownCodeBlock(fencedCodeBlock.Info?.Trim() ?? string.Empty, fencedCodeBlock.Lines.ToString());

			case CodeBlock codeBlock:
				return new MarkdownCodeBlock(null, codeBlock.Lines.ToString());

			case QuoteBlock quoteBlock:
				return new MarkdownQuote(BuildChildren(quoteBlock, options));

			case ListBlock listBlock:
				return BuildList(listBlock, options);

			case ThematicBreakBlock:
				return new MarkdownThematicBreak();

			case MarkdigTable tableBlock:
				return BuildTable(tableBlock, options);

			case HtmlBlock htmlBlock:
				// Raw HTML is not interpreted; the documented contract is that unsupported constructs render as
				// literal text, and dropping the block would also merge the surrounding paragraphs visually.
				return new MarkdownParagraph([new MarkdownText(htmlBlock.Lines.ToString())]);

			default:
				// Defensive fallback for a leaf block with inline content that the enabled pipeline does not
				// produce; unsupported constructs are handled as literal text before they reach this point.
				return block is LeafBlock { Inline: not null } leafBlock
					? BuildParagraph(leafBlock.Inline, options)
					: null;
		}
	}

	private static List<MarkdownBlock> BuildChildren(ContainerBlock container, MarkdownRenderOptions options)
	{
		var blocks = new List<MarkdownBlock>(container.Count);

		foreach (MarkdigBlock child in container)
		{
			MarkdownBlock? modelBlock = BuildBlock(child, options);

			if (modelBlock is not null)
				blocks.Add(modelBlock);
		}

		return blocks;
	}

	private static MarkdownParagraph BuildParagraph(ContainerInline? inline, MarkdownRenderOptions options)
		=> new(BuildInlines(inline, options));

	private static MarkdownList BuildList(ListBlock listBlock, MarkdownRenderOptions options)
	{
		var items = new List<MarkdownListItem>(listBlock.Count);

		foreach (MarkdigBlock item in listBlock)
		{
			if (item is ListItemBlock listItemBlock)
				items.Add(new MarkdownListItem(BuildChildren(listItemBlock, options)));
		}

		// The text marker styles reject a start index below one, while CommonMark allows an ordered list to
		// start at zero; such a list renders as a number-one list instead of failing the whole document.
		int startIndex = 1;

		if (listBlock.IsOrdered
			&& int.TryParse(listBlock.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out int start))
		{
			startIndex = Math.Max(1, start);
		}

		return new MarkdownList(listBlock.IsOrdered, startIndex, listBlock.IsLoose, items);
	}

	private static MarkdownTable BuildTable(MarkdigTable tableBlock, MarkdownRenderOptions options)
	{
		var columnAlignments = new List<MarkdownColumnAlignment?>(tableBlock.ColumnDefinitions.Count);

		foreach (TableColumnDefinition columnDefinition in tableBlock.ColumnDefinitions)
			columnAlignments.Add(GetColumnAlignment(columnDefinition.Alignment));

		var rows = new List<MarkdownTableRow>(tableBlock.Count);

		foreach (MarkdigTableRow markdownRow in tableBlock)
		{
			var cells = new List<MarkdownTableCell>(markdownRow.Count);
			int columnIndex = 0;

			foreach (MarkdigTableCell markdownCell in markdownRow)
			{
				int columnSpan = Math.Max(1, markdownCell.ColumnSpan);

				cells.Add(new MarkdownTableCell(
					BuildChildren(markdownCell, options),
					columnSpan,
					Math.Max(1, markdownCell.RowSpan),
					GetColumnAlignment(tableBlock, columnIndex)));

				// GFM column alignment is carried by the table's column definitions, not by the cells. A
				// spanning cell consumes its covered columns so later cells map to their own definitions.
				columnIndex += columnSpan;
			}

			rows.Add(new MarkdownTableRow(markdownRow.IsHeader, cells));
		}

		return new MarkdownTable(rows, columnAlignments);
	}

	private static MarkdownColumnAlignment? GetColumnAlignment(MarkdigTable table, int columnIndex)
	{
		if (columnIndex < 0 || columnIndex >= table.ColumnDefinitions.Count)
			return null;

		return GetColumnAlignment(table.ColumnDefinitions[columnIndex].Alignment);
	}

	private static MarkdownColumnAlignment? GetColumnAlignment(TableColumnAlign? alignment)
		=> alignment switch
		{
			TableColumnAlign.Left => MarkdownColumnAlignment.Left,
			TableColumnAlign.Center => MarkdownColumnAlignment.Center,
			TableColumnAlign.Right => MarkdownColumnAlignment.Right,
			_ => null
		};

	private static List<MarkdownInline> BuildInlines(ContainerInline? container, MarkdownRenderOptions options)
	{
		if (container is null)
			return [];

		var inlines = new List<MarkdownInline>();
		MarkdigInline? current = container.FirstChild;

		while (current is not null)
		{
			MarkdigInline? next = current.NextSibling;
			MarkdownInline? modelInline = BuildInline(current, options);

			if (modelInline is not null)
				inlines.Add(modelInline);

			current = next;
		}

		return inlines;
	}

	private static MarkdownInline? BuildInline(MarkdigInline inline, MarkdownRenderOptions options)
	{
		switch (inline)
		{
			case LiteralInline literalInline:
				return new MarkdownText(literalInline.Content.ToString());

			case CodeInline codeInline:
				return new MarkdownCodeSpan(codeInline.Content.ToString());

			case HtmlEntityInline htmlEntityInline:
				// CommonMark decodes entity references to their characters; an unhandled node would delete the
				// character from the rendered text instead of showing it.
				return new MarkdownText(htmlEntityInline.Transcoded.ToString());

			case LinkInline linkInline:
				return BuildLink(linkInline, options);

			case AutolinkInline autolinkInline:
				return BuildAutolink(autolinkInline, options);

			case EmphasisInline emphasisInline:
				return new MarkdownEmphasis(GetEmphasisKind(emphasisInline), BuildInlines(emphasisInline, options));

			case LineBreakInline lineBreakInline:
				// CommonMark soft breaks join the lines of a paragraph with a space; only hard breaks
				// (two trailing spaces or a trailing backslash) start a new line.
				return lineBreakInline.IsHard ? new MarkdownLineBreak() : new MarkdownText(" ");

			case HtmlInline htmlInline:
				// Raw inline HTML is shown literally instead of being dropped, matching the contract that
				// unsupported constructs render as literal text.
				return new MarkdownText(htmlInline.Tag ?? string.Empty);

			default:
				return inline is ContainerInline container
					? new MarkdownSpan(BuildInlines(container, options))
					: null;
		}
	}

	private static MarkdownInline BuildLink(LinkInline linkInline, MarkdownRenderOptions options)
	{
		List<MarkdownInline> inlines = BuildInlines(linkInline, options);

		if (linkInline.IsImage)
		{
			// An image whose alt text is empty would otherwise render as nothing at all; the image target is
			// shown as a muted italic placeholder so the image stays visible in the flow.
			return inlines.Count == 0
				? new MarkdownSpan([new MarkdownImagePlaceholder(CreateImagePlaceholderText(linkInline))])
				: new MarkdownSpan(inlines);
		}

		return new MarkdownLink(linkInline.Url, ResolveOpenableUri(linkInline.Url, options), inlines);
	}

	private static MarkdownLink BuildAutolink(AutolinkInline autolinkInline, MarkdownRenderOptions options)
	{
		// An email autolink carries the bare address as its URL, so the mailto target is added here;
		// the display text stays the address, matching CommonMark. A "mailto:" autolink keeps both the
		// target and the display text as written.
		string url = autolinkInline.Url;
		string target = autolinkInline.IsEmail && url.Length > 0 ? "mailto:" + url : url;

		return new MarkdownLink(target, ResolveOpenableUri(target, options), [new MarkdownText(url)]);
	}

	private static Uri? ResolveOpenableUri(string? url, MarkdownRenderOptions options)
		=> Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && options.SupportedHyperlinkSchemes.Contains(uri.Scheme)
			? uri
			: null;

	private static string CreateImagePlaceholderText(LinkInline image)
		=> string.IsNullOrWhiteSpace(image.Url) ? "image" : image.Url!;

	private static MarkdownEmphasisKind GetEmphasisKind(EmphasisInline emphasisInline)
	{
		if (emphasisInline.DelimiterChar == '~')
			return MarkdownEmphasisKind.Strikethrough;

		return emphasisInline.DelimiterCount >= 2
			? MarkdownEmphasisKind.Bold
			: MarkdownEmphasisKind.Italic;
	}
}
