using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nickelony.IDEKit.Markdown;
using Nickelony.IDEKit.Markdown.Model;
using Nickelony.IDEKit.Markdown.Planning;
using List = System.Windows.Documents.List;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Turns the render plan of the engine-neutral <see cref="MarkdownDocumentModel"/> into the WPF <see cref="FlowDocument"/> representation of Markdown
/// content for <see cref="MarkdownRenderer"/>.
/// </summary>
/// <remarks>
/// <include file="../../../../shared/docs/MarkdownEmitters.xml" path="doc/members/member[@name='MarkdownEmitters.Remarks']/*"/>
/// </remarks>
internal static class MarkdownFlowDocumentEmitter
{
	/// <summary>
	/// Creates the empty flow document that carries the theme's body font, foreground, flow direction, transparent background, and zero page padding,
	/// before any block is added.
	/// </summary>
	/// <param name="resources">The resolved theme resources to render with.</param>
	/// <returns>The configured document.</returns>
	/// <remarks>
	/// The text aligns to the start of each line, because a flow document justifies its text by default, which spreads the words of a wrapped line
	/// in a narrow popup.
	/// </remarks>
	internal static FlowDocument CreateBaseDocument(MarkdownThemeResources resources)
	{
		return new FlowDocument
		{
			FontFamily = resources.BodyFontFamily,
			FontSize = resources.Theme.BodyFontSize,
			Foreground = resources.Foreground,
			Background = Brushes.Transparent,
			FlowDirection = MarkdownFlowDirectionConversion.ToEngine(resources.Theme.FlowDirection),
			PagePadding = new Thickness(0.0),
			TextAlignment = TextAlignment.Left,
		};
	}

	/// <summary>Creates a flow document that shows <paramref name="content"/> as a single plain-text paragraph, used by the fallback paths.</summary>
	/// <param name="content">The text to show.</param>
	/// <param name="resources">The resolved theme resources to render with.</param>
	/// <returns>The plain-text document.</returns>
	internal static FlowDocument CreatePlainTextDocument(string content, MarkdownThemeResources resources)
	{
		FlowDocument flowDocument = CreateBaseDocument(resources);

		flowDocument.Blocks.Add(
			new Paragraph(new Run(content)) { Margin = new Thickness(left: 0.0, top: 0.0, right: 0.0, bottom: resources.Theme.BlockSpacing) }
		);

		return flowDocument;
	}

	/// <summary>Plans the given document model and emits the plan into a new flow document.</summary>
	/// <param name="model">The document model to emit.</param>
	/// <param name="resources">The resolved theme resources to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <param name="customHighlightingInstaller">
	/// The host callback that can claim a code block's highlighting, or <see langword="null"/> for none.
	/// </param>
	/// <returns>The emitted document.</returns>
	internal static FlowDocument Emit(
		MarkdownDocumentModel model,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		FlowDocument flowDocument = CreateBaseDocument(resources);

		foreach (MarkdownBlockPlan plan in MarkdownRenderPlanner.Plan(model, resources.Theme, options))
			flowDocument.Blocks.Add(EmitBlock(plan, resources, options, customHighlightingInstaller));

		return flowDocument;
	}

	private static Block EmitBlock(
		MarkdownBlockPlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		Block block = plan switch
		{
			MarkdownParagraphPlan paragraph => CreateParagraph(paragraph, resources, options),
			MarkdownHeadingPlan heading => CreateHeading(heading, resources, options),

			MarkdownCodeBlockPlan codeBlock => new BlockUIContainer(
				MarkdownCodeBlockFactory.CreateCodeBlockElement(plan: codeBlock, resources, options, customHighlightingInstaller)
			),

			MarkdownQuotePlan quote => CreateQuoteBlock(plan: quote, resources, options, customHighlightingInstaller),
			MarkdownListPlan list => CreateListBlock(plan: list, resources, options, customHighlightingInstaller),
			MarkdownThematicBreakPlan thematicBreak => CreateThematicBreak(thematicBreak, resources),
			MarkdownTablePlan table => CreateTableBlock(plan: table, resources, options, customHighlightingInstaller),
			_ => throw new UnreachableException($"The render plan holds a block of the unknown kind {plan.GetType().Name}."),
		};

		if (plan.SuppressVerticalSpacing)
			SuppressVerticalSpacing(block);

		return block;
	}

	private static Paragraph CreateParagraph(MarkdownParagraphPlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		var paragraph = new Paragraph { Margin = MarkdownThicknessConversion.ToEngine(plan.Margin) };

		EmitInlines(target: paragraph.Inlines, plans: plan.Inlines, resources, options);
		return paragraph;
	}

	private static Paragraph CreateHeading(MarkdownHeadingPlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		var paragraph = new Paragraph
		{
			Margin = MarkdownThicknessConversion.ToEngine(plan.Margin),
			FontSize = plan.FontSize,
			FontWeight = plan.IsBold ? FontWeights.Bold : FontWeights.Normal,
		};

		EmitInlines(target: paragraph.Inlines, plans: plan.Inlines, resources, options);
		return paragraph;
	}

	private static Section CreateQuoteBlock(
		MarkdownQuotePlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		var section = new Section
		{
			Margin = MarkdownThicknessConversion.ToEngine(plan.Margin),
			Padding = MarkdownThicknessConversion.ToEngine(plan.Padding),
			BorderBrush = resources.Border,
			BorderThickness = MarkdownThicknessConversion.ToEngine(plan.BorderThickness),
		};

		foreach (MarkdownBlockPlan child in plan.Blocks)
			section.Blocks.Add(EmitBlock(plan: child, resources, options, customHighlightingInstaller));

		return section;
	}

	private static List CreateListBlock(
		MarkdownListPlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		// The list flows its own markers from the ordered flag and the start index. Its default padding indents the content far past the marker,
		// so the content starts after the widest marker text and the plan's gap, as a drawn marker column does.
		var list = new List
		{
			Margin = MarkdownThicknessConversion.ToEngine(plan.Margin),
			MarkerStyle = plan.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
			StartIndex = plan.StartIndex,
			MarkerOffset = plan.MarkerGap,
			Padding = new Thickness(left: MeasureWidestMarker(plan, resources) + plan.MarkerGap, top: 0.0, right: 0.0, bottom: 0.0),
		};

		foreach (MarkdownListItemPlan item in plan.Items)
		{
			var emittedItem = new ListItem();

			foreach (MarkdownBlockPlan child in item.Blocks)
				emittedItem.Blocks.Add(EmitBlock(plan: child, resources, options, customHighlightingInstaller));

			list.ListItems.Add(emittedItem);
		}

		return list;
	}

	private static double MeasureWidestMarker(MarkdownListPlan plan, MarkdownThemeResources resources)
	{
		var typeface = new Typeface(
			fontFamily: resources.BodyFontFamily,
			style: FontStyles.Normal,
			weight: FontWeights.Normal,
			stretch: FontStretches.Normal
		);

		double widest = 0.0;

		foreach (MarkdownListItemPlan item in plan.Items)
		{
			var text = new FormattedText(
				textToFormat: item.Marker,
				culture: CultureInfo.InvariantCulture,
				flowDirection: FlowDirection.LeftToRight,
				typeface,
				emSize: resources.Theme.BodyFontSize,
				foreground: Brushes.Black,
				pixelsPerDip: 1.0
			);

			widest = Math.Max(widest, text.WidthIncludingTrailingWhitespace);
		}

		return widest;
	}

	private static BlockUIContainer CreateThematicBreak(MarkdownThematicBreakPlan plan, MarkdownThemeResources resources)
	{
		return new BlockUIContainer(
			new Border
			{
				Height = plan.Thickness,
				Background = resources.Border,
				Margin = MarkdownThicknessConversion.ToEngine(plan.Margin),
			}
		);
	}

	private static Table CreateTableBlock(
		MarkdownTablePlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		var table = new Table { CellSpacing = 0.0, Margin = MarkdownThicknessConversion.ToEngine(plan.Margin) };

		var rowGroup = new TableRowGroup();
		table.RowGroups.Add(rowGroup);

		foreach (MarkdownTableRowPlan rowPlan in plan.Rows)
		{
			var row = new TableRow();
			rowGroup.Rows.Add(row);

			foreach (MarkdownTableCellPlan cellPlan in rowPlan.Cells)
			{
				var cell = new TableCell
				{
					BorderBrush = resources.Border,
					BorderThickness = MarkdownThicknessConversion.ToEngine(cellPlan.BorderThickness),
					Padding = MarkdownThicknessConversion.ToEngine(cellPlan.Padding),
					FontWeight = cellPlan.IsBold ? FontWeights.Bold : FontWeights.Normal,
				};

				foreach (MarkdownBlockPlan child in cellPlan.Blocks)
					cell.Blocks.Add(EmitBlock(plan: child, resources, options, customHighlightingInstaller));

				// The table is built from rows and cells with no TableColumns, so GFM column alignment is
				// applied per cell instead of through column-level metadata.
				if (MarkdownTextAlignmentConversion.ToEngine(cellPlan.Alignment) is TextAlignment textAlignment)
					ApplyTextAlignment(cell, textAlignment);

				row.Cells.Add(cell);
			}
		}

		return table;
	}

	private static void ApplyTextAlignment(TableCell cell, TextAlignment alignment)
	{
		foreach (Block block in cell.Blocks)
			ApplyTextAlignment(block, alignment);
	}

	// Column alignment is structural: it is applied to every block a cell contains, and to the blocks of the
	// nested containers (sections, lists, nested tables) a cell hosts, so a cell whose content is a list,
	// quote, or nested block is aligned like a plain paragraph.
	private static void ApplyTextAlignment(Block block, TextAlignment alignment)
	{
		block.TextAlignment = alignment;

		switch (block)
		{
			case Section section:
				foreach (Block child in section.Blocks)
					ApplyTextAlignment(child, alignment);

				break;

			case List list:
				foreach (ListItem item in list.ListItems)
				{
					foreach (Block child in item.Blocks)
						ApplyTextAlignment(child, alignment);
				}

				break;

			case Table table:
				foreach (TableRowGroup rowGroup in table.RowGroups)
				{
					foreach (TableRow row in rowGroup.Rows)
					{
						foreach (TableCell nestedCell in row.Cells)
							ApplyTextAlignment(nestedCell, alignment);
					}
				}

				break;
		}
	}

	private static void SuppressVerticalSpacing(Block block)
	{
		Thickness margin = block.Margin;
		block.Margin = new Thickness(left: margin.Left, top: 0.0, right: margin.Right, bottom: 0.0);

		// A block that hosts a UI element (a code block or a thematic break) carries the spacing on the
		// hosted element instead of the block itself.
		if (block is BlockUIContainer { Child: FrameworkElement child })
			child.Margin = new Thickness(left: child.Margin.Left, top: 0.0, right: child.Margin.Right, bottom: 0.0);
	}

	private static void EmitInlines(
		InlineCollection target,
		IReadOnlyList<MarkdownInlinePlan> plans,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options
	)
	{
		foreach (MarkdownInlinePlan plan in plans)
			target.Add(EmitInline(plan, resources, options));
	}

	private static Inline EmitInline(MarkdownInlinePlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		return plan switch
		{
			MarkdownTextPlan text => new Run(text.Text),
			MarkdownCodeSpanPlan codeSpan => CreateInlineCode(codeSpan, resources),
			MarkdownLinkPlan link => CreateLink(link, resources, options),
			MarkdownEmphasisPlan emphasis => CreateEmphasis(emphasis, resources, options),
			MarkdownLineBreakPlan => new LineBreak(),
			MarkdownImagePlaceholderPlan placeholder => CreateImagePlaceholder(placeholder, resources),
			MarkdownSpanPlan spanPlan => CreateSpan(spanPlan, resources, options),
			_ => throw new UnreachableException($"The render plan holds an inline of the unknown kind {plan.GetType().Name}."),
		};
	}

	private static Span CreateSpan(MarkdownSpanPlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		var span = new Span();

		EmitInlines(target: span.Inlines, plans: plan.Inlines, resources, options);
		return span;
	}

	private static Hyperlink CreateLink(MarkdownLinkPlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		Hyperlink hyperlink = CreateHyperlink(plan, resources, options);

		EmitInlines(target: hyperlink.Inlines, plans: plan.Inlines, resources, options);
		return hyperlink;
	}

	// The placeholder is muted and italic so it reads as a placeholder rather than as body text.
	private static Run CreateImagePlaceholder(MarkdownImagePlaceholderPlan plan, MarkdownThemeResources resources) =>
		new(plan.Text) { FontStyle = FontStyles.Italic, Foreground = resources.MutedForeground };

	private static Hyperlink CreateHyperlink(MarkdownLinkPlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		Uri? openableUri = plan.OpenableUri;

		var hyperlink = new Hyperlink
		{
			NavigateUri = openableUri,
			Foreground = resources.LinkForeground,
			TextDecorations = TextDecorations.Underline,
			Cursor = openableUri is not null ? Cursors.Hand : Cursors.Arrow,
			Focusable = plan.IsFocusable,
		};

		if (openableUri is not null)
		{
			// The renderer owns activation (the configured opener callbacks) and marks each event as
			// handled: the navigation request so an ambient NavigationService does not navigate twice,
			// and the click so no ambient handler reacts to a link the renderer owns.
			hyperlink.RequestNavigate += (_, e) => e.Handled = true;

			hyperlink.Click += (_, e) =>
			{
				MarkdownHyperlinkActivator.TryOpenHyperlink(openableUri, options);
				e.Handled = true;
			};
		}

		return hyperlink;
	}

	private static Span CreateEmphasis(MarkdownEmphasisPlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		Span span = plan.Kind switch
		{
			MarkdownEmphasisKind.Strikethrough => new Span { TextDecorations = TextDecorations.Strikethrough },
			MarkdownEmphasisKind.Bold => new Bold(),
			_ => new Italic(),
		};

		EmitInlines(target: span.Inlines, plans: plan.Inlines, resources, options);
		return span;
	}

	private static InlineUIContainer CreateInlineCode(MarkdownCodeSpanPlan plan, MarkdownThemeResources resources)
	{
		return new InlineUIContainer(
			new Border
			{
				Background = resources.CodeBackground,
				BorderBrush = resources.Border,
				BorderThickness = MarkdownThicknessConversion.ToEngine(plan.BorderThickness),
				CornerRadius = new CornerRadius(plan.CornerRadius),
				Padding = MarkdownThicknessConversion.ToEngine(plan.Padding),
				FlowDirection = MarkdownFlowDirectionConversion.ToEngine(resources.Theme.FlowDirection),

				Child = new TextBlock
				{
					Text = plan.Text,
					Foreground = resources.Foreground,
					FontFamily = resources.CodeFontFamily,
					FontSize = resources.Theme.CodeFontSize,
					TextWrapping = TextWrapping.Wrap,
					MaxWidth = plan.MaxWidth,
				},
			}
		)
		{
			BaselineAlignment = BaselineAlignment.Center,
		};
	}
}
