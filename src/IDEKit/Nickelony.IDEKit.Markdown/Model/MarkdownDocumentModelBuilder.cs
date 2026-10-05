using System.Globalization;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigBlock = Markdig.Syntax.Block;
using MarkdigInline = Markdig.Syntax.Inlines.Inline;
using MarkdigMarkdown = Markdig.Markdown;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;

namespace Nickelony.IDEKit.Markdown.Model;

/// <summary>Parses Markdown and walks the Markdig AST into the engine-neutral <see cref="MarkdownDocumentModel"/>.</summary>
/// <remarks>
/// The pipeline is deliberately narrow so unsupported Markdig constructs render as literal text instead of an approximation with the wrong style.
/// Raw HTML is shown literally rather than interpreted.
/// </remarks>
public static class MarkdownDocumentModelBuilder
{
	private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
		.UsePipeTables()
		.UseAutoLinks()
		.UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
		.Build();

	/// <summary>Parses the given Markdown and walks it into the neutral document model.</summary>
	/// <param name="markdown">
	/// The Markdown content to parse; its line terminators should be line feeds, because code text keeps them unchanged.
	/// </param>
	/// <param name="supportedSchemes">
	/// The URI schemes a link target may use to be reported as openable, matched against the lowercase scheme of the target.
	/// </param>
	/// <returns>The document model.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="markdown"/> or <paramref name="supportedSchemes"/> is <see langword="null"/>.
	/// </exception>
	public static MarkdownDocumentModel Build(string markdown, IReadOnlySet<string> supportedSchemes)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		ArgumentNullException.ThrowIfNull(supportedSchemes);

		MarkdownDocument document = MarkdigMarkdown.Parse(markdown, s_pipeline);

		var blocks = new List<MarkdownBlock>(document.Count);

		foreach (MarkdigBlock block in document)
		{
			MarkdownBlock? modelBlock = BuildBlock(block, supportedSchemes);

			if (modelBlock is not null)
				blocks.Add(modelBlock);
		}

		return new MarkdownDocumentModel(blocks);
	}

	private static MarkdownBlock? BuildBlock(MarkdigBlock block, IReadOnlySet<string> supportedSchemes)
	{
		return block switch
		{
			ParagraphBlock paragraphBlock => BuildParagraph(paragraphBlock.Inline, supportedSchemes),
			HeadingBlock headingBlock => new MarkdownHeading(headingBlock.Level, BuildInlines(headingBlock.Inline, supportedSchemes)),
			FencedCodeBlock fencedCodeBlock => new MarkdownCodeBlock(fencedCodeBlock.Info?.Trim() ?? string.Empty, LineGroupText(fencedCodeBlock)),
			CodeBlock codeBlock => new MarkdownCodeBlock(null, LineGroupText(codeBlock)),
			QuoteBlock quoteBlock => new MarkdownQuote(BuildChildren(quoteBlock, supportedSchemes)),
			ListBlock listBlock => BuildList(listBlock, supportedSchemes),
			ThematicBreakBlock => new MarkdownThematicBreak(),
			MarkdigTable tableBlock => BuildTable(tableBlock, supportedSchemes),

			// Raw HTML is not interpreted; the documented contract is that unsupported constructs render as literal text, and dropping
			// the block would also merge the surrounding paragraphs visually. The line group is normalized (line feeds, terminator
			// dropped) exactly like a code block's, so the literal text does not carry the terminator that ends its last line.
			HtmlBlock htmlBlock => new MarkdownParagraph([new MarkdownText(MarkdownBlockTextNormalizer.Normalize(LineGroupText(htmlBlock)))]),

			// Defensive fallback for a leaf block with inline content that the enabled pipeline does not produce;
			// unsupported constructs are handled as literal text before they reach this point.
			_ => block is LeafBlock { Inline: not null } leafBlock ? BuildParagraph(leafBlock.Inline, supportedSchemes) : null,
		};
	}

	private static List<MarkdownBlock> BuildChildren(ContainerBlock container, IReadOnlySet<string> supportedSchemes)
	{
		var blocks = new List<MarkdownBlock>(container.Count);

		foreach (MarkdigBlock child in container)
		{
			MarkdownBlock? modelBlock = BuildBlock(child, supportedSchemes);

			if (modelBlock is not null)
				blocks.Add(modelBlock);
		}

		return blocks;
	}

	// Markdig's line group ends without the terminator of its last line. Restoring it keeps an authored trailing blank line, because the
	// normalizer removes one final line feed and would otherwise take the terminator of that blank line for the end of the block.
	private static string LineGroupText(LeafBlock block)
	{
		StringLineGroup lines = block.Lines;
		return lines.Count == 0 ? string.Empty : lines.ToString() + lines.Lines[lines.Count - 1].NewLine.AsString();
	}

	private static MarkdownParagraph BuildParagraph(ContainerInline? inline, IReadOnlySet<string> supportedSchemes) =>
		new(BuildInlines(inline, supportedSchemes));

	private static MarkdownList BuildList(ListBlock listBlock, IReadOnlySet<string> supportedSchemes)
	{
		var items = new List<MarkdownListItem>(listBlock.Count);

		foreach (MarkdigBlock item in listBlock)
		{
			if (item is ListItemBlock listItemBlock)
				items.Add(new MarkdownListItem(BuildChildren(listItemBlock, supportedSchemes)));
		}

		// The text marker styles reject a start index below one, while CommonMark allows an ordered list to
		// start at zero; such a list renders as a number-one list instead of failing the whole document.
		int startIndex = 1;

		if (
			listBlock.IsOrdered
			&& int.TryParse(s: listBlock.OrderedStart, style: NumberStyles.None, provider: CultureInfo.InvariantCulture, result: out int start)
		)
		{
			startIndex = Math.Max(1, start);
		}

		return new MarkdownList(IsOrdered: listBlock.IsOrdered, startIndex, IsLoose: listBlock.IsLoose, items);
	}

	private static MarkdownTable BuildTable(MarkdigTable tableBlock, IReadOnlySet<string> supportedSchemes)
	{
		var columnAlignments = new List<MarkdownColumnAlignment?>(tableBlock.ColumnDefinitions.Count);

		foreach (TableColumnDefinition columnDefinition in tableBlock.ColumnDefinitions)
			columnAlignments.Add(GetColumnAlignment(columnDefinition.Alignment));

		var rows = new List<MarkdownTableRow>(tableBlock.Count);

		foreach (MarkdigTableRow markdownRow in tableBlock.Cast<MarkdigTableRow>())
		{
			var cells = new List<MarkdownTableCell>(markdownRow.Count);
			int columnIndex = 0;

			foreach (MarkdigTableCell markdownCell in markdownRow.Cast<MarkdigTableCell>())
			{
				cells.Add(new MarkdownTableCell(BuildChildren(markdownCell, supportedSchemes), GetColumnAlignment(tableBlock, columnIndex)));

				// GFM column alignment is carried by the table's column definitions, not by the cells, and a
				// pipe table has no spanning cells, so each cell advances exactly one column.
				columnIndex++;
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
	{
		return alignment switch
		{
			TableColumnAlign.Left => MarkdownColumnAlignment.Left,
			TableColumnAlign.Center => MarkdownColumnAlignment.Center,
			TableColumnAlign.Right => MarkdownColumnAlignment.Right,
			_ => null,
		};
	}

	private static List<MarkdownInline> BuildInlines(ContainerInline? container, IReadOnlySet<string> supportedSchemes)
	{
		if (container is null)
			return [];

		var inlines = new List<MarkdownInline>();
		MarkdigInline? current = container.FirstChild;

		while (current is not null)
		{
			MarkdigInline? next = current.NextSibling;
			MarkdownInline? modelInline = BuildInline(current, supportedSchemes);

			if (modelInline is not null)
				inlines.Add(modelInline);

			current = next;
		}

		return inlines;
	}

	private static MarkdownInline? BuildInline(MarkdigInline inline, IReadOnlySet<string> supportedSchemes)
	{
		return inline switch
		{
			LiteralInline literalInline => new MarkdownText(literalInline.Content.ToString()),
			CodeInline codeInline => new MarkdownCodeSpan(codeInline.Content.ToString()),

			// CommonMark decodes entity references to their characters; an unhandled node would delete the character
			// from the rendered text instead of showing it.
			HtmlEntityInline htmlEntityInline => new MarkdownText(htmlEntityInline.Transcoded.ToString()),
			LinkInline linkInline => BuildLink(linkInline, supportedSchemes),
			AutolinkInline autolinkInline => BuildAutolink(autolinkInline, supportedSchemes),
			EmphasisInline emphasisInline => new MarkdownEmphasis(GetEmphasisKind(emphasisInline), BuildInlines(emphasisInline, supportedSchemes)),

			// CommonMark soft breaks join the lines of a paragraph with a space; only hard breaks
			// (two trailing spaces or a trailing backslash) start a new line.
			LineBreakInline lineBreakInline => lineBreakInline.IsHard ? new MarkdownLineBreak() : new MarkdownText(" "),

			// Raw inline HTML is shown literally instead of being dropped, matching the contract that
			// unsupported constructs render as literal text.
			HtmlInline htmlInline => new MarkdownText(htmlInline.Tag ?? string.Empty),
			_ => inline is ContainerInline container ? new MarkdownSpan(BuildInlines(container, supportedSchemes)) : null,
		};
	}

	private static MarkdownInline BuildLink(LinkInline linkInline, IReadOnlySet<string> supportedSchemes)
	{
		List<MarkdownInline> inlines = BuildInlines(linkInline, supportedSchemes);

		if (linkInline.IsImage)
		{
			// An image whose alt text is empty would otherwise render as nothing at all; the image target is
			// shown as a muted italic placeholder so the image stays visible in the flow.
			return inlines.Count == 0
				? new MarkdownSpan([new MarkdownImagePlaceholder(CreateImagePlaceholderText(linkInline))])
				: new MarkdownSpan(inlines);
		}

		return new MarkdownLink(ResolveOpenableUri(linkInline.Url, supportedSchemes), inlines);
	}

	private static MarkdownLink BuildAutolink(AutolinkInline autolinkInline, IReadOnlySet<string> supportedSchemes)
	{
		// An email autolink carries the bare address as its URL, so the mailto target is added here;
		// the display text stays the address, matching CommonMark. A "mailto:" autolink keeps both the
		// target and the display text as written.
		string url = autolinkInline.Url;
		string target = autolinkInline.IsEmail && url.Length > 0 ? "mailto:" + url : url;

		return new MarkdownLink(ResolveOpenableUri(target, supportedSchemes), [new MarkdownText(url)]);
	}

	private static Uri? ResolveOpenableUri(string? url, IReadOnlySet<string> supportedSchemes) =>
		Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && supportedSchemes.Contains(uri.Scheme) ? uri : null;

	private static string CreateImagePlaceholderText(LinkInline image) => string.IsNullOrWhiteSpace(image.Url) ? "image" : image.Url!;

	private static MarkdownEmphasisKind GetEmphasisKind(EmphasisInline emphasisInline)
	{
		if (emphasisInline.DelimiterChar == '~')
			return MarkdownEmphasisKind.Strikethrough;

		return emphasisInline.DelimiterCount >= 2 ? MarkdownEmphasisKind.Bold : MarkdownEmphasisKind.Italic;
	}
}
