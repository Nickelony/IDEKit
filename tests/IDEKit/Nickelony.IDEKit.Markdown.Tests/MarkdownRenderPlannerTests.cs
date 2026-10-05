using Nickelony.IDEKit.Markdown.Model;
using Nickelony.IDEKit.Markdown.Planning;

namespace Nickelony.IDEKit.Markdown.Tests;

/// <summary>
/// Pins the layout decisions that both Markdown emitters take from the render plan: which blocks render, the spacing, the heading size,
/// the list markers, the tight-list and table-cell spacing suppression, the table cell structure, and which links are keyboard stops.
/// </summary>
[TestClass]
public sealed class MarkdownRenderPlannerTests
{
	private static readonly MarkdownRenderTheme s_theme = MarkdownRenderTheme.Default with
	{
		BodyFontSize = 10.0,
		BlockSpacing = 7.0,
		HeadingFontSizeScales = [2.0, 1.5],
	};

	[TestMethod]
	public void Plan_EmptyModel_ReturnsNoBlocks() => Assert.IsEmpty(Plan([]));

	[TestMethod]
	public void Plan_Paragraph_SpacesBelowByTheBlockSpacing()
	{
		MarkdownParagraphPlan plan = Single<MarkdownParagraphPlan>(new MarkdownParagraph([new MarkdownText("hello")]));

		Assert.AreEqual(new MarkdownThickness(Left: 0.0, Top: 0.0, Right: 0.0, Bottom: 7.0), plan.Margin);
		Assert.IsFalse(plan.SuppressVerticalSpacing);
		Assert.AreEqual(new MarkdownTextPlan("hello"), plan.Inlines.Single());
	}

	[TestMethod]
	public void Plan_Heading_SpacesAboveAndBelowAndIsBold()
	{
		MarkdownHeadingPlan plan = Single<MarkdownHeadingPlan>(new MarkdownHeading(Level: 1, Inlines: []));

		Assert.AreEqual(new MarkdownThickness(Left: 0.0, Top: 7.0, Right: 0.0, Bottom: 7.0), plan.Margin);
		Assert.IsTrue(plan.IsBold);
	}

	[TestMethod]
	public void Plan_Heading_ScalesTheBodySizeByTheLevelAndReusesTheLastScale()
	{
		Assert.AreEqual(20.0, Single<MarkdownHeadingPlan>(new MarkdownHeading(Level: 1, Inlines: [])).FontSize);
		Assert.AreEqual(15.0, Single<MarkdownHeadingPlan>(new MarkdownHeading(Level: 2, Inlines: [])).FontSize);
		Assert.AreEqual(15.0, Single<MarkdownHeadingPlan>(new MarkdownHeading(Level: 6, Inlines: [])).FontSize);
	}

	[TestMethod]
	public void Plan_Heading_ExtremeScale_IsCappedAtTheFontSizeUpperBound()
	{
		MarkdownRenderTheme theme = s_theme with { HeadingFontSizeScales = [1e9] };

		var plan = (MarkdownHeadingPlan)Plan(theme, new MarkdownHeading(Level: 1, Inlines: [])).Single();

		Assert.AreEqual(MarkdownRenderTheme.FontSizeUpperBound, plan.FontSize);
	}

	[TestMethod]
	public void Plan_BlockOfAnUnknownKind_IsLeftOut()
	{
		IReadOnlyList<MarkdownBlockPlan> plans = Plan(new UnknownBlock(), new MarkdownThematicBreak(), new UnknownBlock());
		Assert.IsInstanceOfType<MarkdownThematicBreakPlan>(plans.Single());
	}

	[TestMethod]
	public void Plan_InlineOfAnUnknownKind_IsLeftOut()
	{
		MarkdownParagraphPlan plan = Single<MarkdownParagraphPlan>(
			new MarkdownParagraph([new UnknownInline(), new MarkdownText("kept"), new UnknownInline()])
		);

		Assert.AreEqual(new MarkdownTextPlan("kept"), plan.Inlines.Single());
	}

	[TestMethod]
	public void Plan_CodeBlock_CarriesTheModelBlock()
	{
		var codeBlock = new MarkdownCodeBlock(Language: "csharp", Code: "var x = 1;");
		Assert.AreSame(codeBlock, Single<MarkdownCodeBlockPlan>(codeBlock).Block);
	}

	[TestMethod]
	public void Plan_CodeBlock_CarriesTheSurfaceMetrics()
	{
		MarkdownRenderTheme theme = s_theme with { CodeMaxWidth = 300.0 };
		var plan = (MarkdownCodeBlockPlan)Plan(theme, new MarkdownCodeBlock(Language: null, Code: "x")).Single();

		Assert.AreEqual(MarkdownThickness.Symmetric(horizontal: 0.0, vertical: 7.0), plan.Margin);

		Assert.AreEqual(
			MarkdownThickness.Symmetric(MarkdownLayout.CodeBlockHorizontalPadding, MarkdownLayout.CodeBlockVerticalPadding),
			plan.Padding
		);

		Assert.AreEqual(MarkdownThickness.Uniform(MarkdownLayout.CodeBlockBorderThickness), plan.BorderThickness);
		Assert.AreEqual(MarkdownLayout.CodeBlockCornerRadius, plan.CornerRadius);
		Assert.AreEqual(300.0, plan.MaxWidth);

		// The code wraps inside the surface without its padding and border on both sides.
		Assert.AreEqual(
			MarkdownLayout.GetInnerContentWidth(300.0, MarkdownLayout.CodeBlockHorizontalPadding, MarkdownLayout.CodeBlockBorderThickness),
			plan.WrapWidth
		);
	}

	[TestMethod]
	public void Plan_NullArgument_ThrowsArgumentNullException()
	{
		var model = new MarkdownDocumentModel([]);

		Assert.ThrowsExactly<ArgumentNullException>(() => MarkdownRenderPlanner.Plan(null!, s_theme, MarkdownRenderOptions.Default));
		Assert.ThrowsExactly<ArgumentNullException>(() => MarkdownRenderPlanner.Plan(model, null!, MarkdownRenderOptions.Default));
		Assert.ThrowsExactly<ArgumentNullException>(() => MarkdownRenderPlanner.Plan(model, s_theme, null!));
	}

	[TestMethod]
	public void Plan_ThematicBreak_UsesTheLayoutThicknessAndSpacesAboveAndBelow()
	{
		MarkdownThematicBreakPlan plan = Single<MarkdownThematicBreakPlan>(new MarkdownThematicBreak());

		Assert.AreEqual(MarkdownLayout.ThematicBreakThickness, plan.Thickness);
		Assert.AreEqual(new MarkdownThickness(Left: 0.0, Top: 7.0, Right: 0.0, Bottom: 7.0), plan.Margin);
	}

	[TestMethod]
	public void Plan_Quote_CarriesTheBarAndPaddingAndKeepsTheSpacingOfItsBlocks()
	{
		MarkdownQuotePlan plan = Single<MarkdownQuotePlan>(new MarkdownQuote([new MarkdownParagraph([])]));

		Assert.AreEqual(new MarkdownThickness(Left: 0.0, Top: 0.0, Right: 0.0, Bottom: 7.0), plan.Margin);
		Assert.AreEqual(new MarkdownThickness(Left: MarkdownLayout.QuoteBarThickness, Top: 0.0, Right: 0.0, Bottom: 0.0), plan.BorderThickness);

		Assert.AreEqual(
			new MarkdownThickness(
				Left: MarkdownLayout.QuotePaddingHorizontal,
				Top: MarkdownLayout.QuotePaddingVertical,
				Right: MarkdownLayout.QuotePaddingHorizontal,
				Bottom: MarkdownLayout.QuotePaddingVertical
			),

			plan.Padding
		);

		Assert.IsFalse(plan.Blocks.Single().SuppressVerticalSpacing);
	}

	[TestMethod]
	public void Plan_List_IndentsByTheLayoutIndentAndSpacesBelow()
	{
		MarkdownListPlan plan = Single<MarkdownListPlan>(List(isOrdered: false, startIndex: 1, isLoose: false, itemCount: 1));

		Assert.AreEqual(new MarkdownThickness(Left: MarkdownLayout.ListIndent, Top: 0.0, Right: 0.0, Bottom: 7.0), plan.Margin);
		Assert.AreEqual(MarkdownLayout.ListMarkerGap, plan.MarkerGap);
	}

	[TestMethod]
	public void Plan_BulletedList_GivesEveryItemTheBulletMarker()
	{
		MarkdownListPlan plan = Single<MarkdownListPlan>(List(isOrdered: false, startIndex: 1, isLoose: false, itemCount: 2));

		Assert.IsFalse(plan.IsOrdered);
		Assert.AreSequenceEqual(["\u2022", "\u2022"], [.. plan.Items.Select(item => item.Marker)]);
	}

	[TestMethod]
	public void Plan_OrderedList_NumbersTheMarkersFromTheStartIndex()
	{
		MarkdownListPlan plan = Single<MarkdownListPlan>(List(isOrdered: true, startIndex: 9, isLoose: false, itemCount: 3));

		Assert.IsTrue(plan.IsOrdered);
		Assert.AreEqual(9, plan.StartIndex);

		Assert.AreSequenceEqual(["9.", "10.", "11."], [.. plan.Items.Select(item => item.Marker)]);
	}

	[TestMethod]
	public void Plan_TightList_SuppressesTheVerticalSpacingOfItsItemBlocks()
	{
		MarkdownListPlan plan = Single<MarkdownListPlan>(List(isOrdered: false, startIndex: 1, isLoose: false, itemCount: 2));

		Assert.IsTrue(plan.Items.SelectMany(item => item.Blocks).All(block => block.SuppressVerticalSpacing));
		Assert.IsFalse(plan.SuppressVerticalSpacing);
	}

	[TestMethod]
	public void Plan_LooseList_KeepsTheVerticalSpacingOfItsItemBlocks()
	{
		MarkdownListPlan plan = Single<MarkdownListPlan>(List(isOrdered: false, startIndex: 1, isLoose: true, itemCount: 2));
		Assert.DoesNotContain(block => block.SuppressVerticalSpacing, plan.Items.SelectMany(item => item.Blocks));
	}

	[TestMethod]
	public void Plan_TightList_SuppressesTheItemBlockButNotTheBlocksNestedInIt()
	{
		var quote = new MarkdownQuote([new MarkdownParagraph([])]);
		var item = new MarkdownListItem([quote]);

		MarkdownListPlan plan = Single<MarkdownListPlan>(new MarkdownList(IsOrdered: false, StartIndex: 1, IsLoose: false, Items: [item]));
		var quotePlan = (MarkdownQuotePlan)plan.Items.Single().Blocks.Single();

		Assert.IsTrue(quotePlan.SuppressVerticalSpacing);
		Assert.IsFalse(quotePlan.Blocks.Single().SuppressVerticalSpacing);
	}

	[TestMethod]
	public void Plan_ListItemBlockOfAnUnknownKind_IsLeftOut()
	{
		var item = new MarkdownListItem([new UnknownBlock(), new MarkdownThematicBreak()]);
		MarkdownListPlan plan = Single<MarkdownListPlan>(new MarkdownList(IsOrdered: false, StartIndex: 1, IsLoose: true, Items: [item]));

		Assert.IsInstanceOfType<MarkdownThematicBreakPlan>(plan.Items.Single().Blocks.Single());
	}

	[TestMethod]
	public void Plan_Table_CountsTheColumnsOfTheWidestRow()
	{
		MarkdownTable table = Table(alignments: [null], rows: [Row(isHeader: true, cellCount: 2), Row(isHeader: false, cellCount: 3)]);
		Assert.AreEqual(3, Single<MarkdownTablePlan>(table).ColumnCount);
	}

	[TestMethod]
	public void Plan_Table_CountsTheColumnsOfTheAlignmentListWhenItIsWider()
	{
		MarkdownTable table = Table(alignments: [null, null, null, null], rows: [Row(isHeader: true, cellCount: 2)]);
		Assert.AreEqual(4, Single<MarkdownTablePlan>(table).ColumnCount);
	}

	[TestMethod]
	public void Plan_Table_SpacesBelowAndKeepsTheRowAndCellStructure()
	{
		MarkdownTable table = Table(alignments: [null, null], rows: [Row(isHeader: true, cellCount: 2), Row(isHeader: false, cellCount: 2)]);

		MarkdownTablePlan plan = Single<MarkdownTablePlan>(table);

		Assert.AreEqual(new MarkdownThickness(Left: 0.0, Top: 0.0, Right: 0.0, Bottom: 7.0), plan.Margin);
		Assert.AreSequenceEqual([2, 2], [.. plan.Rows.Select(row => row.Cells.Count)]);
	}

	[TestMethod]
	public void Plan_TableCell_PadsAndBordersItselfAndSuppressesTheSpacingOfItsBlocks()
	{
		MarkdownTablePlan plan = Single<MarkdownTablePlan>(Table(alignments: [null], rows: [Row(isHeader: false, cellCount: 1)]));

		MarkdownTableCellPlan cell = plan.Rows.Single().Cells.Single();

		Assert.AreEqual(
			new MarkdownThickness(
				Left: MarkdownLayout.TableCellHorizontalPadding,
				Top: MarkdownLayout.TableCellVerticalPadding,
				Right: MarkdownLayout.TableCellHorizontalPadding,
				Bottom: MarkdownLayout.TableCellVerticalPadding
			),

			cell.Padding
		);

		Assert.AreEqual(MarkdownThickness.Uniform(MarkdownLayout.TableCellBorderThickness), cell.BorderThickness);
		Assert.IsTrue(cell.Blocks.Single().SuppressVerticalSpacing);
	}

	[TestMethod]
	public void Plan_TableCell_OnlyTheHeaderRowIsBold()
	{
		MarkdownTablePlan plan = Single<MarkdownTablePlan>(
			Table(alignments: [null], rows: [Row(isHeader: true, cellCount: 1), Row(isHeader: false, cellCount: 1)])
		);

		Assert.IsTrue(plan.Rows[0].Cells.Single().IsBold);
		Assert.IsFalse(plan.Rows[1].Cells.Single().IsBold);
	}

	[TestMethod]
	public void Plan_TableCell_CarriesTheAlignmentOfItsColumn()
	{
		MarkdownTableCell[] cells =
		[
			new MarkdownTableCell([], MarkdownColumnAlignment.Left),
			new MarkdownTableCell([], MarkdownColumnAlignment.Center),
			new MarkdownTableCell([], MarkdownColumnAlignment.Right),
			new MarkdownTableCell([], Alignment: null),
		];

		var table = new MarkdownTable([new MarkdownTableRow(IsHeader: false, cells)], []);

		MarkdownColumnAlignment?[] alignments = [.. Single<MarkdownTablePlan>(table).Rows.Single().Cells.Select(cell => cell.Alignment)];

		Assert.AreSequenceEqual([MarkdownColumnAlignment.Left, MarkdownColumnAlignment.Center, MarkdownColumnAlignment.Right, null], alignments);
	}

	[TestMethod]
	public void Plan_Link_OpenableTargetWithContentInteraction_IsFocusable()
	{
		var uri = new Uri("https://example.com/");

		MarkdownLinkPlan link = PlanLink(
			new MarkdownLink(uri, [new MarkdownText("docs")]),
			new MarkdownRenderOptions { AllowContentInteraction = true }
		);

		Assert.AreEqual(uri, link.OpenableUri);
		Assert.IsTrue(link.IsFocusable);
		Assert.AreEqual(new MarkdownTextPlan("docs"), link.Inlines.Single());
	}

	[TestMethod]
	public void Plan_Link_OpenableTargetWithoutContentInteraction_IsNotFocusable()
	{
		MarkdownLinkPlan link = PlanLink(new MarkdownLink(new Uri("https://example.com/"), []), MarkdownRenderOptions.Default);
		Assert.IsFalse(link.IsFocusable);
	}

	[TestMethod]
	public void Plan_Link_WithoutOpenableTarget_IsNeverFocusable()
	{
		MarkdownLinkPlan link = PlanLink(
			new MarkdownLink(OpenableUri: null, Inlines: []),
			new MarkdownRenderOptions { AllowContentInteraction = true }
		);

		Assert.IsNull(link.OpenableUri);
		Assert.IsFalse(link.IsFocusable);
	}

	[TestMethod]
	public void Plan_CodeSpan_SizesItsTextBySubtractingThePaddingAndBorderFromTheCodeMaxWidth()
	{
		MarkdownRenderTheme theme = s_theme with { CodeMaxWidth = 300.0 };

		var paragraph = (MarkdownParagraphPlan)Plan(theme, new MarkdownParagraph([new MarkdownCodeSpan("x")])).Single();
		var codeSpan = (MarkdownCodeSpanPlan)paragraph.Inlines.Single();

		Assert.AreEqual("x", codeSpan.Text);

		Assert.AreEqual(
			MarkdownLayout.GetInnerContentWidth(300.0, MarkdownLayout.InlineCodeHorizontalPadding, MarkdownLayout.InlineCodeBorderThickness),
			codeSpan.MaxWidth
		);

		Assert.AreEqual(MarkdownLayout.InlineCodeCornerRadius, codeSpan.CornerRadius);
		Assert.AreEqual(MarkdownThickness.Uniform(MarkdownLayout.InlineCodeBorderThickness), codeSpan.BorderThickness);

		Assert.AreEqual(
			MarkdownThickness.Symmetric(MarkdownLayout.InlineCodeHorizontalPadding, MarkdownLayout.InlineCodeVerticalPadding),
			codeSpan.Padding
		);
	}

	[TestMethod]
	public void Plan_InlineContainers_KeepTheirNestedContentInOrder()
	{
		var inlines = new MarkdownInline[]
		{
			new MarkdownEmphasis(MarkdownEmphasisKind.Bold, [new MarkdownText("b")]),
			new MarkdownLineBreak(),
			new MarkdownImagePlaceholder("img"),
			new MarkdownSpan([new MarkdownText("s"), new UnknownInline()]),
		};

		MarkdownParagraphPlan paragraph = Single<MarkdownParagraphPlan>(new MarkdownParagraph(inlines));

		var emphasis = (MarkdownEmphasisPlan)paragraph.Inlines[0];
		var span = (MarkdownSpanPlan)paragraph.Inlines[3];

		Assert.AreEqual(MarkdownEmphasisKind.Bold, emphasis.Kind);
		Assert.AreEqual(new MarkdownTextPlan("b"), emphasis.Inlines.Single());
		Assert.IsInstanceOfType<MarkdownLineBreakPlan>(paragraph.Inlines[1]);
		Assert.AreEqual(new MarkdownImagePlaceholderPlan("img"), paragraph.Inlines[2]);
		Assert.AreEqual(new MarkdownTextPlan("s"), span.Inlines.Single());
	}

	[TestMethod]
	public void Plan_ParsedMarkdown_PlansTheBlocksInSourceOrder()
	{
		MarkdownDocumentModel model = MarkdownDocumentModelBuilder.Build(
			"# Title\n\ntext\n\n1. one\n2. two\n\n---\n",
			MarkdownRenderOptions.Default.SupportedHyperlinkSchemes
		);

		IReadOnlyList<MarkdownBlockPlan> plans = MarkdownRenderPlanner.Plan(model, s_theme, MarkdownRenderOptions.Default);

		Assert.IsInstanceOfType<MarkdownHeadingPlan>(plans[0]);
		Assert.IsInstanceOfType<MarkdownParagraphPlan>(plans[1]);
		Assert.IsInstanceOfType<MarkdownListPlan>(plans[2]);
		Assert.IsInstanceOfType<MarkdownThematicBreakPlan>(plans[3]);
		Assert.HasCount(4, plans);
	}

	private static IReadOnlyList<MarkdownBlockPlan> Plan(params MarkdownBlock[] blocks) => Plan(s_theme, blocks);

	private static IReadOnlyList<MarkdownBlockPlan> Plan(MarkdownRenderTheme theme, params MarkdownBlock[] blocks) =>
		MarkdownRenderPlanner.Plan(new MarkdownDocumentModel(blocks), theme, MarkdownRenderOptions.Default);

	private static TPlan Single<TPlan>(MarkdownBlock block)
		where TPlan : MarkdownBlockPlan => (TPlan)Plan(block).Single();

	private static MarkdownLinkPlan PlanLink(MarkdownLink link, MarkdownRenderOptions options)
	{
		IReadOnlyList<MarkdownBlockPlan> plans = MarkdownRenderPlanner.Plan(
			new MarkdownDocumentModel([new MarkdownParagraph([link])]),
			s_theme,
			options
		);

		return (MarkdownLinkPlan)((MarkdownParagraphPlan)plans.Single()).Inlines.Single();
	}

	private static MarkdownList List(bool isOrdered, int startIndex, bool isLoose, int itemCount)
	{
		MarkdownListItem[] items = [.. Enumerable.Range(0, itemCount).Select(_ => new MarkdownListItem([new MarkdownParagraph([])]))];
		return new MarkdownList(isOrdered, startIndex, isLoose, items);
	}

	private static MarkdownTable Table(IReadOnlyList<MarkdownColumnAlignment?> alignments, IReadOnlyList<MarkdownTableRow> rows) =>
		new(rows, alignments);

	private static MarkdownTableRow Row(bool isHeader, int cellCount)
	{
		MarkdownTableCell[] cells =
		[
			.. Enumerable.Range(0, cellCount).Select(_ => new MarkdownTableCell([new MarkdownParagraph([])], Alignment: null)),
		];

		return new MarkdownTableRow(isHeader, cells);
	}

	private sealed record UnknownBlock : MarkdownBlock;

	private sealed record UnknownInline : MarkdownInline;
}
