namespace Nickelony.IDEKit.Markdown.Model;

/// <summary>A block-level node of the engine-neutral Markdown content model.</summary>
/// <remarks>
/// The model is produced once by <see cref="MarkdownDocumentModelBuilder"/> and turned into toolkit elements by each editor binding's emitter,
/// so the Markdig walk and the mapping decisions it encodes are defined once for every binding. The nodes carry no toolkit types and no resolved
/// brushes or font sizes; a binding's emitter resolves those from its own theme while it walks the model. The nodes are records, but their list
/// members compare by reference, so two separately built models are never equal.
/// </remarks>
public abstract record MarkdownBlock;

/// <summary>A paragraph: inline content rendered with the theme's body style and a trailing block spacing.</summary>
/// <param name="Inlines">The paragraph's inline content.</param>
public sealed record MarkdownParagraph(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>An ATX or Setext heading: inline content rendered bold and scaled by the heading level.</summary>
/// <param name="Level">The heading level, one for the outermost heading.</param>
/// <param name="Inlines">The heading's inline content.</param>
public sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>A fenced or indented code block, shown in a code surface rather than as body text.</summary>
/// <param name="Language">
/// The fence's first info-string token (fence attributes are dropped), an empty string for a fence with no info string,
/// or <see langword="null"/> for an indented block.
/// </param>
/// <param name="Code">
/// The code text as the source holds it: its line terminators are not normalized, and its last line keeps its terminator when the source has one.
/// </param>
public sealed record MarkdownCodeBlock(string? Language, string Code) : MarkdownBlock
{
	/// <summary>The code as a renderer shows it: with line feeds only, without one final line feed, and empty when the block is blank.</summary>
	public string NormalizedCode => MarkdownBlockTextNormalizer.Normalize(Code);
}

/// <summary>A block quote: its child blocks are rendered inside a quoted surface.</summary>
/// <param name="Blocks">The quoted content.</param>
public sealed record MarkdownQuote(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

/// <summary>An ordered or bulleted list.</summary>
/// <param name="IsOrdered">Whether the list is ordered (numbered) rather than bulleted.</param>
/// <param name="StartIndex">
/// The first number of an ordered list; always <c>1</c> for a bulleted list. A CommonMark list that starts at zero is reported as <c>1</c>
/// because the text marker styles reject a lower start.
/// </param>
/// <param name="IsLoose">
/// Whether the list's items are separated by blank lines; a tight list suppresses the block spacing that separates its items.
/// </param>
/// <param name="Items">The list's items.</param>
public sealed record MarkdownList(bool IsOrdered, int StartIndex, bool IsLoose, IReadOnlyList<MarkdownListItem> Items) : MarkdownBlock;

/// <summary>One item of a <see cref="MarkdownList"/>.</summary>
/// <param name="Blocks">The item's block content.</param>
public sealed record MarkdownListItem(IReadOnlyList<MarkdownBlock> Blocks);

/// <summary>A thematic break (a horizontal rule).</summary>
public sealed record MarkdownThematicBreak : MarkdownBlock;

/// <summary>A pipe table.</summary>
/// <param name="Rows">The table's rows, the first of which is the header row.</param>
/// <param name="ColumnAlignments">
/// The GFM column alignment for each column; <see langword="null"/> for an unaligned column. The alignment is a column property,
/// resolved from a cell's starting column into that cell's own <see cref="MarkdownTableCell.Alignment"/>.
/// </param>
public sealed record MarkdownTable(IReadOnlyList<MarkdownTableRow> Rows, IReadOnlyList<MarkdownColumnAlignment?> ColumnAlignments) : MarkdownBlock;

/// <summary>One row of a <see cref="MarkdownTable"/>.</summary>
/// <param name="IsHeader">Whether the row belongs to the table header.</param>
/// <param name="Cells">The row's cells.</param>
public sealed record MarkdownTableRow(bool IsHeader, IReadOnlyList<MarkdownTableCell> Cells);

/// <summary>One cell of a <see cref="MarkdownTableRow"/>.</summary>
/// <param name="Blocks">The cell's block content.</param>
/// <param name="Alignment">The alignment resolved from the cell's starting column, or <see langword="null"/> for an unaligned column.</param>
/// <remarks>
/// A renderer pads the cell itself, so it removes the vertical block spacing from the blocks the cell holds; that spacing would leave dead space
/// above and below the content.
/// </remarks>
public sealed record MarkdownTableCell(IReadOnlyList<MarkdownBlock> Blocks, MarkdownColumnAlignment? Alignment);

/// <summary>The horizontal alignment of a table column.</summary>
public enum MarkdownColumnAlignment
{
	Left,

	Center,

	Right,
}
