#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// A block-level node of the engine-neutral Markdown content model.
/// </summary>
/// <remarks>
/// The model is produced once by <see cref="MarkdownDocumentModelBuilder"/> and turned into toolkit
/// elements by each editor binding's emitter, so the Markdig walk and the mapping decisions it encodes
/// (heading levels, list markers, table alignment, link openability) are defined once for every binding.
/// The nodes carry no toolkit types and no resolved brushes or font sizes; a binding's emitter resolves
/// those from its own theme while it walks the model.
/// </remarks>
internal abstract record MarkdownBlock;

/// <summary>
/// A paragraph: inline content rendered with the theme's body style and a trailing block spacing.
/// </summary>
/// <param name="Inlines">The paragraph's inline content.</param>
internal sealed record MarkdownParagraph(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>
/// An ATX or Setext heading: inline content rendered bold and scaled by the heading level.
/// </summary>
/// <param name="Level">The heading level, one for the outermost heading.</param>
/// <param name="Inlines">The heading's inline content.</param>
internal sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>
/// A fenced or indented code block, shown in a code surface rather than as body text.
/// </summary>
/// <param name="Language">
/// The fence's first info-string token (fence attributes are dropped), an empty string for a fence with no
/// info string, or <see langword="null"/> for an indented block.
/// </param>
/// <param name="Code">The raw code text, before <see cref="MarkdownCodeText.Normalize"/> is applied.</param>
internal sealed record MarkdownCodeBlock(string? Language, string Code) : MarkdownBlock;

/// <summary>
/// A block quote: its child blocks are rendered inside a quoted surface.
/// </summary>
/// <param name="Blocks">The quoted content.</param>
internal sealed record MarkdownQuote(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

/// <summary>
/// An ordered or bulleted list.
/// </summary>
/// <param name="IsOrdered">Whether the list is ordered (numbered) rather than bulleted.</param>
/// <param name="StartIndex">
/// The first number of an ordered list; always <c>1</c> for a bulleted list. A CommonMark list that starts
/// at zero is reported as <c>1</c> because the text marker styles reject a lower start.
/// </param>
/// <param name="IsLoose">
/// Whether the list's items are separated by blank lines; a tight list suppresses the block spacing that
/// separates its items.
/// </param>
/// <param name="Items">The list's items.</param>
internal sealed record MarkdownList(bool IsOrdered, int StartIndex, bool IsLoose, IReadOnlyList<MarkdownListItem> Items) : MarkdownBlock;

/// <summary>
/// One item of a <see cref="MarkdownList"/>.
/// </summary>
/// <param name="Blocks">The item's block content.</param>
internal sealed record MarkdownListItem(IReadOnlyList<MarkdownBlock> Blocks);

/// <summary>
/// A thematic break (a horizontal rule).
/// </summary>
internal sealed record MarkdownThematicBreak : MarkdownBlock;

/// <summary>
/// A pipe table.
/// </summary>
/// <param name="Rows">The table's rows, the first of which is the header row.</param>
/// <param name="ColumnAlignments">
/// The GFM column alignment for each column; <see langword="null"/> for an unaligned column. The alignment
/// is a column property, so a spanning cell maps to the column it starts in.
/// </param>
internal sealed record MarkdownTable(IReadOnlyList<MarkdownTableRow> Rows, IReadOnlyList<MarkdownColumnAlignment?> ColumnAlignments) : MarkdownBlock;

/// <summary>
/// One row of a <see cref="MarkdownTable"/>.
/// </summary>
/// <param name="IsHeader">Whether the row belongs to the table header.</param>
/// <param name="Cells">The row's cells.</param>
internal sealed record MarkdownTableRow(bool IsHeader, IReadOnlyList<MarkdownTableCell> Cells);

/// <summary>
/// One cell of a <see cref="MarkdownTableRow"/>.
/// </summary>
/// <param name="Blocks">The cell's block content.</param>
/// <param name="ColumnSpan">The number of columns the cell spans; at least one.</param>
/// <param name="RowSpan">The number of rows the cell spans; at least one.</param>
/// <param name="Alignment">
/// The alignment resolved from the cell's starting column, or <see langword="null"/> for an unaligned column.
/// </param>
internal sealed record MarkdownTableCell(IReadOnlyList<MarkdownBlock> Blocks, int ColumnSpan, int RowSpan, MarkdownColumnAlignment? Alignment);

/// <summary>
/// The horizontal alignment of a table column.
/// </summary>
internal enum MarkdownColumnAlignment
{
	/// <summary>The column is left-aligned.</summary>
	Left,

	/// <summary>The column is centered.</summary>
	Center,

	/// <summary>The column is right-aligned.</summary>
	Right
}
