using Nickelony.IDEKit.Markdown.Model;

namespace Nickelony.IDEKit.Markdown.Planning;

/// <summary>A block node of a Markdown render plan, with every layout decision already made.</summary>
/// <remarks>
/// <para>
/// <see cref="MarkdownRenderPlanner"/> builds the tree from the content model, and an emitter creates one toolkit element per node. A tree holds
/// only the blocks the emitters render, so an emitter has no unknown case to skip.
/// </para>
/// <para>
/// A node carries its own margin before any suppression. The emitter creates the element, then removes the element's vertical margin
/// when <see cref="SuppressVerticalSpacing"/> is set, because the toolkits attach the spacing of a hosted child differently.
/// </para>
/// </remarks>
public abstract record MarkdownBlockPlan
{
	/// <summary>
	/// Whether the element drops its top and bottom margin, because a tight list item or a table cell supplies the spacing around its blocks.
	/// </summary>
	public bool SuppressVerticalSpacing { get; init; }
}

/// <summary>A paragraph of inline content in the theme's body style.</summary>
/// <param name="Margin">The margin around the paragraph.</param>
/// <param name="Inlines">The paragraph's inline content.</param>
public sealed record MarkdownParagraphPlan(MarkdownThickness Margin, IReadOnlyList<MarkdownInlinePlan> Inlines) : MarkdownBlockPlan;

/// <summary>A heading: inline content scaled by its level.</summary>
/// <param name="Margin">The margin around the heading.</param>
/// <param name="FontSize">The heading font size, capped at the theme's font-size upper bound.</param>
/// <param name="IsBold">Whether the heading text is bold.</param>
/// <param name="Inlines">The heading's inline content.</param>
public sealed record MarkdownHeadingPlan(MarkdownThickness Margin, double FontSize, bool IsBold, IReadOnlyList<MarkdownInlinePlan> Inlines)
	: MarkdownBlockPlan;

/// <summary>A code block, which a code-block factory builds from the model block inside a bordered surface.</summary>
/// <param name="Block">The code block to show.</param>
/// <param name="Margin">The margin around the surface.</param>
/// <param name="Padding">The padding between the border and the editor.</param>
/// <param name="BorderThickness">The border widths of the surface.</param>
/// <param name="CornerRadius">The radius of the surface's corners.</param>
/// <param name="MaxWidth">The widest the surface grows, which is the theme's code width.</param>
/// <param name="WrapWidth">The width the code wraps at: the surface width without its padding and border, at least one pixel.</param>
public sealed record MarkdownCodeBlockPlan(
	MarkdownCodeBlock Block,
	MarkdownThickness Margin,
	MarkdownThickness Padding,
	MarkdownThickness BorderThickness,
	double CornerRadius,
	double MaxWidth,
	double WrapWidth
) : MarkdownBlockPlan;

/// <summary>A block quote: its blocks inside a surface with a bar on the leading edge.</summary>
/// <param name="Margin">The margin around the quote.</param>
/// <param name="Padding">The padding between the bar and the quoted blocks.</param>
/// <param name="BorderThickness">The border widths, which draw the bar.</param>
/// <param name="Blocks">The quoted blocks.</param>
public sealed record MarkdownQuotePlan(
	MarkdownThickness Margin,
	MarkdownThickness Padding,
	MarkdownThickness BorderThickness,
	IReadOnlyList<MarkdownBlockPlan> Blocks
) : MarkdownBlockPlan;

/// <summary>An ordered or bulleted list.</summary>
/// <param name="Margin">The margin around the list, whose left edge indents the items.</param>
/// <param name="IsOrdered">Whether the markers are numbers rather than bullets.</param>
/// <param name="StartIndex">The number of the first item of an ordered list.</param>
/// <param name="MarkerGap">The gap between a drawn marker and its item content.</param>
/// <param name="Items">The list's items.</param>
/// <remarks>
/// A toolkit that flows markers natively reads <see cref="IsOrdered"/> and <see cref="StartIndex"/>, and one that draws a marker beside the content
/// reads <see cref="MarkdownListItemPlan.Marker"/> and <see cref="MarkerGap"/>. Both derive from the same list, so the numbering cannot diverge.
/// </remarks>
public sealed record MarkdownListPlan(
	MarkdownThickness Margin,
	bool IsOrdered,
	int StartIndex,
	double MarkerGap,
	IReadOnlyList<MarkdownListItemPlan> Items
) : MarkdownBlockPlan;

/// <summary>One item of a <see cref="MarkdownListPlan"/>.</summary>
/// <param name="Marker">The marker text a toolkit draws beside the content: <c>N.</c> for an ordered list, otherwise a bullet.</param>
/// <param name="Blocks">The item's blocks.</param>
public sealed record MarkdownListItemPlan(string Marker, IReadOnlyList<MarkdownBlockPlan> Blocks);

/// <summary>A thematic break (a horizontal rule).</summary>
/// <param name="Margin">The margin around the rule.</param>
/// <param name="Thickness">The height of the rule.</param>
public sealed record MarkdownThematicBreakPlan(MarkdownThickness Margin, double Thickness) : MarkdownBlockPlan;

/// <summary>A pipe table.</summary>
/// <param name="Margin">The margin around the table.</param>
/// <param name="ColumnCount">
/// The number of columns that the widest row or the alignment list needs. A toolkit that lays cells out by row and cell ignores it.
/// </param>
/// <param name="Rows">The table's rows, the first of which is the header row.</param>
public sealed record MarkdownTablePlan(MarkdownThickness Margin, int ColumnCount, IReadOnlyList<MarkdownTableRowPlan> Rows) : MarkdownBlockPlan;

/// <summary>One row of a <see cref="MarkdownTablePlan"/>.</summary>
/// <param name="Cells">The row's cells.</param>
public sealed record MarkdownTableRowPlan(IReadOnlyList<MarkdownTableCellPlan> Cells);

/// <summary>One cell of a <see cref="MarkdownTableRowPlan"/>.</summary>
/// <param name="Padding">The padding between the cell border and its blocks.</param>
/// <param name="BorderThickness">The border widths of the cell.</param>
/// <param name="IsBold">Whether the cell text is bold, which the cells of the header row are.</param>
/// <param name="Alignment">The column alignment the cell's text takes, or <see langword="null"/> for an unaligned column.</param>
/// <param name="Blocks">The cell's blocks, each with its vertical spacing suppressed because the cell pads itself.</param>
public sealed record MarkdownTableCellPlan(
	MarkdownThickness Padding,
	MarkdownThickness BorderThickness,
	bool IsBold,
	MarkdownColumnAlignment? Alignment,
	IReadOnlyList<MarkdownBlockPlan> Blocks
);
