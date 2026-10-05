using System.Globalization;
using Nickelony.IDEKit.Markdown.Model;

namespace Nickelony.IDEKit.Markdown.Planning;

/// <summary>Turns the Markdown content model into the render plan that an emitter creates toolkit elements from.</summary>
/// <remarks>
/// The planner walks the model once and makes every decision that does not depend on a toolkit: which blocks and inlines render, the block spacing,
/// the heading size, the list markers and indent, which blocks of a tight list or a table cell lose their vertical spacing, the table cell structure,
/// and which links are keyboard stops. It never touches a toolkit type, so an emitter resolves brushes and fonts from the theme and decides
/// only how its elements are created.
/// </remarks>
public static class MarkdownRenderPlanner
{
	private const string BulletMarker = "\u2022";

	/// <summary>Plans the top-level blocks of a document.</summary>
	/// <param name="model">The document model to plan.</param>
	/// <param name="theme">The theme that supplies the block spacing and the heading scales.</param>
	/// <param name="options">The rendering options that decide which links are keyboard stops.</param>
	/// <returns>The plans of the blocks that render, in source order; a block of a kind the planner does not know is left out.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="model"/>, <paramref name="theme"/> or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	public static IReadOnlyList<MarkdownBlockPlan> Plan(MarkdownDocumentModel model, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(theme);
		ArgumentNullException.ThrowIfNull(options);

		return PlanBlocks(blocks: model.Blocks, theme, options, suppressVerticalSpacing: false);
	}

	private static List<MarkdownBlockPlan> PlanBlocks(
		IReadOnlyList<MarkdownBlock> blocks,
		MarkdownRenderTheme theme,
		MarkdownRenderOptions options,
		bool suppressVerticalSpacing
	)
	{
		List<MarkdownBlockPlan> plans = [];

		foreach (MarkdownBlock block in blocks)
		{
			MarkdownBlockPlan? plan = PlanBlock(block, theme, options);

			if (plan is not null)
				plans.Add(suppressVerticalSpacing ? plan with { SuppressVerticalSpacing = true } : plan);
		}

		return plans;
	}

	private static MarkdownBlockPlan? PlanBlock(MarkdownBlock block, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		return block switch
		{
			MarkdownParagraph paragraph => new MarkdownParagraphPlan(
				Margin: MarkdownThickness.Below(theme.BlockSpacing),
				Inlines: PlanInlines(paragraph.Inlines, theme, options)
			),

			MarkdownHeading heading => new MarkdownHeadingPlan(
				Margin: MarkdownThickness.Symmetric(horizontal: 0.0, vertical: theme.BlockSpacing),
				FontSize: GetHeadingFontSize(theme, heading.Level),
				IsBold: true,
				Inlines: PlanInlines(heading.Inlines, theme, options)
			),

			MarkdownCodeBlock codeBlock => PlanCodeBlock(codeBlock, theme),
			MarkdownQuote quote => PlanQuote(quote, theme, options),
			MarkdownList list => PlanList(list, theme, options),

			MarkdownThematicBreak => new MarkdownThematicBreakPlan(
				Margin: MarkdownThickness.Symmetric(horizontal: 0.0, vertical: theme.BlockSpacing),
				Thickness: MarkdownLayout.ThematicBreakThickness
			),

			MarkdownTable table => PlanTable(table, theme, options),
			_ => null,
		};
	}

	// The toolkit rejects a font size above its own limit, so an extreme scale would fail the whole rendering;
	// capping at the theme's font-size upper bound keeps the document renderable instead.
	private static double GetHeadingFontSize(MarkdownRenderTheme theme, int level)
	{
		return MarkdownLayout.GetHeadingFontSize(
			bodyFontSize: theme.BodyFontSize,
			scales: theme.HeadingFontSizeScales,
			level,
			upperBound: MarkdownRenderTheme.FontSizeUpperBound
		);
	}

	private static MarkdownCodeBlockPlan PlanCodeBlock(MarkdownCodeBlock codeBlock, MarkdownRenderTheme theme)
	{
		return new MarkdownCodeBlockPlan(
			Block: codeBlock,
			Margin: MarkdownThickness.Symmetric(horizontal: 0.0, vertical: theme.BlockSpacing),
			Padding: MarkdownThickness.Symmetric(MarkdownLayout.CodeBlockHorizontalPadding, MarkdownLayout.CodeBlockVerticalPadding),
			BorderThickness: MarkdownThickness.Uniform(MarkdownLayout.CodeBlockBorderThickness),
			CornerRadius: MarkdownLayout.CodeBlockCornerRadius,
			MaxWidth: theme.CodeMaxWidth,

			// The editor stretches inside the border, so the width the code wraps at is the border's own content width.
			WrapWidth: MarkdownLayout.GetInnerContentWidth(
				theme.CodeMaxWidth,
				MarkdownLayout.CodeBlockHorizontalPadding,
				MarkdownLayout.CodeBlockBorderThickness
			)
		);
	}

	private static MarkdownQuotePlan PlanQuote(MarkdownQuote quote, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		return new MarkdownQuotePlan(
			Margin: MarkdownThickness.Below(theme.BlockSpacing),
			Padding: MarkdownThickness.Symmetric(MarkdownLayout.QuotePaddingHorizontal, MarkdownLayout.QuotePaddingVertical),
			BorderThickness: new MarkdownThickness(Left: MarkdownLayout.QuoteBarThickness, Top: 0.0, Right: 0.0, Bottom: 0.0),
			Blocks: PlanBlocks(blocks: quote.Blocks, theme, options, suppressVerticalSpacing: false)
		);
	}

	private static MarkdownListPlan PlanList(MarkdownList list, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		List<MarkdownListItemPlan> items = new(list.Items.Count);

		for (int index = 0; index < list.Items.Count; index++)
		{
			MarkdownListItem item = list.Items[index];
			string marker = list.IsOrdered ? (list.StartIndex + index).ToString(CultureInfo.InvariantCulture) + "." : BulletMarker;

			// A tight list suppresses the spacing that separates its items; a loose list keeps it.
			items.Add(new MarkdownListItemPlan(marker, PlanBlocks(blocks: item.Blocks, theme, options, suppressVerticalSpacing: !list.IsLoose)));
		}

		return new MarkdownListPlan(
			Margin: new MarkdownThickness(Left: MarkdownLayout.ListIndent, Top: 0.0, Right: 0.0, Bottom: theme.BlockSpacing),
			IsOrdered: list.IsOrdered,
			StartIndex: list.StartIndex,
			MarkerGap: MarkdownLayout.ListMarkerGap,
			items
		);
	}

	private static MarkdownTablePlan PlanTable(MarkdownTable table, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		int columnCount = table.ColumnAlignments.Count;
		List<MarkdownTableRowPlan> rows = new(table.Rows.Count);

		foreach (MarkdownTableRow row in table.Rows)
		{
			columnCount = Math.Max(columnCount, row.Cells.Count);
			List<MarkdownTableCellPlan> cells = new(row.Cells.Count);

			foreach (MarkdownTableCell cell in row.Cells)
			{
				// The cell pads itself, so the spacing between its blocks would leave dead space above and below the content.
				cells.Add(
					new MarkdownTableCellPlan(
						Padding: MarkdownThickness.Symmetric(MarkdownLayout.TableCellHorizontalPadding, MarkdownLayout.TableCellVerticalPadding),
						BorderThickness: MarkdownThickness.Uniform(MarkdownLayout.TableCellBorderThickness),
						IsBold: row.IsHeader,
						Alignment: cell.Alignment,
						Blocks: PlanBlocks(blocks: cell.Blocks, theme, options, suppressVerticalSpacing: true)
					)
				);
			}

			rows.Add(new MarkdownTableRowPlan(cells));
		}

		return new MarkdownTablePlan(Margin: MarkdownThickness.Below(theme.BlockSpacing), columnCount, rows);
	}

	private static List<MarkdownInlinePlan> PlanInlines(
		IReadOnlyList<MarkdownInline> inlines,
		MarkdownRenderTheme theme,
		MarkdownRenderOptions options
	)
	{
		List<MarkdownInlinePlan> plans = new(inlines.Count);

		foreach (MarkdownInline inline in inlines)
		{
			MarkdownInlinePlan? plan = PlanInline(inline, theme, options);

			if (plan is not null)
				plans.Add(plan);
		}

		return plans;
	}

	private static MarkdownInlinePlan? PlanInline(MarkdownInline inline, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		return inline switch
		{
			MarkdownText text => new MarkdownTextPlan(text.Text),
			MarkdownCodeSpan codeSpan => PlanCodeSpan(codeSpan, theme),

			MarkdownLink link => new MarkdownLinkPlan(
				OpenableUri: link.OpenableUri,

				// Only an openable link becomes a keyboard stop when interaction is enabled; a link the
				// renderer cannot open must not add a dead stop.
				IsFocusable: link.OpenableUri is not null && options.AllowContentInteraction,
				Inlines: PlanInlines(link.Inlines, theme, options)
			),

			MarkdownEmphasis emphasis => new MarkdownEmphasisPlan(emphasis.Kind, PlanInlines(emphasis.Inlines, theme, options)),
			MarkdownLineBreak => new MarkdownLineBreakPlan(),
			MarkdownImagePlaceholder placeholder => new MarkdownImagePlaceholderPlan(placeholder.Text),
			MarkdownSpan span => new MarkdownSpanPlan(PlanInlines(span.Inlines, theme, options)),
			_ => null,
		};
	}

	private static MarkdownCodeSpanPlan PlanCodeSpan(MarkdownCodeSpan codeSpan, MarkdownRenderTheme theme)
	{
		return new MarkdownCodeSpanPlan(
			Text: codeSpan.Text,
			Padding: MarkdownThickness.Symmetric(MarkdownLayout.InlineCodeHorizontalPadding, MarkdownLayout.InlineCodeVerticalPadding),
			BorderThickness: MarkdownThickness.Uniform(MarkdownLayout.InlineCodeBorderThickness),
			CornerRadius: MarkdownLayout.InlineCodeCornerRadius,

			MaxWidth: MarkdownLayout.GetInnerContentWidth(
				theme.CodeMaxWidth,
				MarkdownLayout.InlineCodeHorizontalPadding,
				MarkdownLayout.InlineCodeBorderThickness
			)
		);
	}
}
