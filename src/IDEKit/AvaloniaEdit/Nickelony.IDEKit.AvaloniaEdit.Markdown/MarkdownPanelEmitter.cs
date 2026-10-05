using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Nickelony.IDEKit.Markdown;
using Nickelony.IDEKit.Markdown.Model;
using Nickelony.IDEKit.Markdown.Planning;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

/// <summary>
/// Turns the render plan of the engine-neutral <see cref="MarkdownDocumentModel"/> into an Avalonia control tree for <see cref="MarkdownRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <include file="../../../../shared/docs/MarkdownEmitters.xml" path="doc/members/member[@name='MarkdownEmitters.Remarks']/*"/>
/// </para>
/// <para>
/// This emitter draws an ordered list's marker, the plan's <c>"N."</c> text, in its own column, with the gap the plan carries.
/// </para>
/// <para>
/// A table cell is a <c>Border</c> with all four sides at the plan's cell border thickness, so two adjacent cells render a doubled interior border
/// rather than one shared line.
/// </para>
/// </remarks>
internal static class MarkdownPanelEmitter
{
	internal static StackPanel Emit(
		MarkdownDocumentModel model,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		StackPanel panel = CreateBasePanel(resources);

		foreach (MarkdownBlockPlan plan in MarkdownRenderPlanner.Plan(model, resources.Theme, options))
			panel.Children.Add(EmitBlock(plan, resources, options, customHighlightingInstaller));

		return panel;
	}

	/// <summary>Creates the empty block panel that carries the theme's flow direction, before any block is added.</summary>
	internal static StackPanel CreateBasePanel(MarkdownThemeResources resources) =>
		new() { Orientation = Orientation.Vertical, FlowDirection = MarkdownFlowDirectionConversion.ToEngine(resources.Theme.FlowDirection) };

	/// <summary>Creates a panel that shows <paramref name="content"/> as a single plain-text block.</summary>
	internal static StackPanel CreatePlainTextPanel(string content, MarkdownThemeResources resources)
	{
		StackPanel panel = CreateBasePanel(resources);

		panel.Children.Add(
			new TextBlock
			{
				Text = content,
				TextWrapping = TextWrapping.Wrap,
				FontFamily = resources.BodyFontFamily,
				FontSize = resources.Theme.BodyFontSize,
				Foreground = resources.Foreground,
				Margin = new Thickness(left: 0.0, top: 0.0, right: 0.0, bottom: resources.Theme.BlockSpacing),
			}
		);

		return panel;
	}

	private static Control EmitBlock(
		MarkdownBlockPlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		Control control = plan switch
		{
			MarkdownParagraphPlan paragraph => CreateTextBlock(
				inlines: paragraph.Inlines,
				resources,
				options,
				margin: MarkdownThicknessConversion.ToEngine(paragraph.Margin)
			),

			MarkdownHeadingPlan heading => CreateHeading(heading, resources, options),

			MarkdownCodeBlockPlan codeBlock => MarkdownCodeBlockFactory.CreateCodeBlockElement(
				plan: codeBlock,
				resources,
				options,
				customHighlightingInstaller
			),

			MarkdownQuotePlan quote => CreateQuoteBlock(plan: quote, resources, options, customHighlightingInstaller),
			MarkdownListPlan list => CreateListBlock(plan: list, resources, options, customHighlightingInstaller),
			MarkdownThematicBreakPlan thematicBreak => CreateThematicBreak(thematicBreak, resources),
			MarkdownTablePlan table => CreateTableBlock(plan: table, resources, options, customHighlightingInstaller),
			_ => throw new UnreachableException($"The render plan holds a block of the unknown kind {plan.GetType().Name}."),
		};

		if (plan.SuppressVerticalSpacing)
			SuppressVerticalSpacing(control);

		return control;
	}

	private static TextBlock CreateHeading(MarkdownHeadingPlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		TextBlock textBlock = CreateTextBlock(inlines: plan.Inlines, resources, options, margin: MarkdownThicknessConversion.ToEngine(plan.Margin));

		textBlock.FontSize = plan.FontSize;
		textBlock.FontWeight = plan.IsBold ? FontWeight.Bold : FontWeight.Normal;

		return textBlock;
	}

	private static TextBlock CreateTextBlock(
		IReadOnlyList<MarkdownInlinePlan> inlines,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		Thickness margin
	)
	{
		TextBlock textBlock = options.AllowContentInteraction ? new SelectableTextBlock() : new TextBlock();

		// A selectable block stays focusable so it can be selected, but it must not become a dead keyboard tab
		// stop; only an openable link is a stop.
		textBlock.IsTabStop = false;
		textBlock.TextWrapping = TextWrapping.Wrap;
		textBlock.FontFamily = resources.BodyFontFamily;
		textBlock.FontSize = resources.Theme.BodyFontSize;
		textBlock.Foreground = resources.Foreground;
		textBlock.Margin = margin;
		textBlock.Inlines = [.. EmitInlines(inlines, resources, options)];

		return textBlock;
	}

	private static Border CreateQuoteBlock(
		MarkdownQuotePlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		var panel = new StackPanel { Orientation = Orientation.Vertical };

		foreach (MarkdownBlockPlan child in plan.Blocks)
			panel.Children.Add(EmitBlock(plan: child, resources, options, customHighlightingInstaller));

		return new Border
		{
			Margin = MarkdownThicknessConversion.ToEngine(plan.Margin),
			Padding = MarkdownThicknessConversion.ToEngine(plan.Padding),
			BorderBrush = resources.Border,
			BorderThickness = MarkdownThicknessConversion.ToEngine(plan.BorderThickness),
			FlowDirection = MarkdownFlowDirectionConversion.ToEngine(resources.Theme.FlowDirection),
			Child = panel,
		};
	}

	private static StackPanel CreateListBlock(
		MarkdownListPlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		var list = new StackPanel { Orientation = Orientation.Vertical, Margin = MarkdownThicknessConversion.ToEngine(plan.Margin) };

		foreach (MarkdownListItemPlan item in plan.Items)
			list.Children.Add(CreateListItem(plan: item, markerGap: plan.MarkerGap, resources, options, customHighlightingInstaller));

		return list;
	}

	private static Grid CreateListItem(
		MarkdownListItemPlan plan,
		double markerGap,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		var grid = new Grid();

		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

		var markerBlock = new TextBlock
		{
			Text = plan.Marker,
			FontFamily = resources.BodyFontFamily,
			FontSize = resources.Theme.BodyFontSize,
			Foreground = resources.Foreground,
			Margin = new Thickness(left: 0.0, top: 0.0, right: markerGap, bottom: 0.0),
		};

		var content = new StackPanel { Orientation = Orientation.Vertical };

		foreach (MarkdownBlockPlan child in plan.Blocks)
			content.Children.Add(EmitBlock(plan: child, resources, options, customHighlightingInstaller));

		Grid.SetColumn(markerBlock, 0);
		Grid.SetColumn(content, 1);

		grid.Children.Add(markerBlock);
		grid.Children.Add(content);

		return grid;
	}

	private static Border CreateThematicBreak(MarkdownThematicBreakPlan plan, MarkdownThemeResources resources)
	{
		return new Border
		{
			Height = plan.Thickness,
			Background = resources.Border,
			Margin = MarkdownThicknessConversion.ToEngine(plan.Margin),
		};
	}

	private static Grid CreateTableBlock(
		MarkdownTablePlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		var grid = new Grid { Margin = MarkdownThicknessConversion.ToEngine(plan.Margin) };

		// The columns share the render width, as the columns of a flow-document table do, so a long cell wraps instead of clipping the last
		// column. A star column takes its content width when the width is unbounded, so a popup sized to its content keeps that size.
		for (int column = 0; column < plan.ColumnCount; column++)
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

		for (int rowIndex = 0; rowIndex < plan.Rows.Count; rowIndex++)
		{
			grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			int columnIndex = 0;

			foreach (MarkdownTableCellPlan cell in plan.Rows[rowIndex].Cells)
			{
				Control cellControl = CreateTableCell(plan: cell, resources, options, customHighlightingInstaller);

				Grid.SetRow(cellControl, rowIndex);
				Grid.SetColumn(cellControl, columnIndex);
				grid.Children.Add(cellControl);

				columnIndex++;
			}
		}

		return grid;
	}

	private static Border CreateTableCell(
		MarkdownTableCellPlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		var content = new StackPanel { Orientation = Orientation.Vertical };
		TextAlignment? textAlignment = MarkdownTextAlignmentConversion.ToEngine(plan.Alignment);

		foreach (MarkdownBlockPlan child in plan.Blocks)
		{
			Control control = EmitBlock(plan: child, resources, options, customHighlightingInstaller);

			ApplyCellStyle(control, plan.IsBold, textAlignment);
			content.Children.Add(control);
		}

		return new Border
		{
			BorderBrush = resources.Border,
			BorderThickness = MarkdownThicknessConversion.ToEngine(plan.BorderThickness),
			Padding = MarkdownThicknessConversion.ToEngine(plan.Padding),
			FlowDirection = MarkdownFlowDirectionConversion.ToEngine(resources.Theme.FlowDirection),
			Child = content,
		};
	}

	// Column alignment and header weight are structural: they are applied to every text block a cell
	// contains, recursing through the nested containers (lists, quotes, nested tables) an emitted block may
	// wrap, so a cell whose content is not a plain paragraph is styled like one.
	private static void ApplyCellStyle(Control control, bool isBold, TextAlignment? textAlignment)
	{
		if (control is TextBlock textBlock)
		{
			if (isBold)
				textBlock.FontWeight = FontWeight.Bold;

			if (textAlignment is TextAlignment alignment)
				textBlock.TextAlignment = alignment;
		}

		foreach (ILogical child in control.GetLogicalChildren())
		{
			if (child is Control childControl)
				ApplyCellStyle(childControl, isBold, textAlignment);
		}
	}

	private static void SuppressVerticalSpacing(Control control)
	{
		Thickness margin = control.Margin;
		control.Margin = new Thickness(left: margin.Left, top: 0.0, right: margin.Right, bottom: 0.0);
	}

	// A nested span fills its own collection with these inlines: a run added to a collection that is assigned to a span afterward does not
	// inherit the span's foreground, so it would render in the toolkit default.
	private static List<Inline> EmitInlines(IReadOnlyList<MarkdownInlinePlan> plans, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		var inlines = new List<Inline>(plans.Count);

		foreach (MarkdownInlinePlan plan in plans)
			inlines.Add(EmitInline(plan, resources, options));

		return inlines;
	}

	private static Inline EmitInline(MarkdownInlinePlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		return plan switch
		{
			MarkdownTextPlan text => new Run(text.Text),

			// A chip aligns its bottom with the bottom of the text: on the baseline its padding would lift it above the line, and centered on the
			// line box it would sit higher than the text, whose glyphs fill the lower part of the box.
			MarkdownCodeSpanPlan codeSpan => new InlineUIContainer(CreateInlineCode(codeSpan, resources))
			{
				BaselineAlignment = BaselineAlignment.TextBottom,
			},

			MarkdownLinkPlan link => new InlineUIContainer(CreateLink(link, resources, options)),
			MarkdownEmphasisPlan emphasis => CreateEmphasis(emphasis, resources, options),
			MarkdownLineBreakPlan => new LineBreak(),

			MarkdownImagePlaceholderPlan placeholder => new Run(placeholder.Text)
			{
				FontStyle = FontStyle.Italic,
				Foreground = resources.MutedForeground,
			},

			MarkdownSpanPlan span => CreateSpan(span: new Span(), plans: span.Inlines, resources, options),
			_ => throw new UnreachableException($"The render plan holds an inline of the unknown kind {plan.GetType().Name}."),
		};
	}

	private static Span CreateEmphasis(MarkdownEmphasisPlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		Span span = plan.Kind switch
		{
			MarkdownEmphasisKind.Strikethrough => new Span { TextDecorations = TextDecorations.Strikethrough },
			MarkdownEmphasisKind.Bold => new Bold(),
			_ => new Italic(),
		};

		return CreateSpan(span, plans: plan.Inlines, resources, options);
	}

	private static Span CreateSpan(
		Span span,
		IReadOnlyList<MarkdownInlinePlan> plans,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options
	)
	{
		span.Inlines.AddRange(EmitInlines(plans, resources, options));
		return span;
	}

	private static MarkdownLinkElement CreateLink(MarkdownLinkPlan plan, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		var element = MarkdownLinkElement.Create(resources, plan, open: uri => MarkdownHyperlinkActivator.TryOpenHyperlink(uri, options));

		element.Inlines = [.. EmitInlines(plan.Inlines, resources, options)];
		return element;
	}

	private static Border CreateInlineCode(MarkdownCodeSpanPlan plan, MarkdownThemeResources resources)
	{
		return new Border
		{
			Background = resources.CodeBackground,
			BorderBrush = resources.Border,
			BorderThickness = MarkdownThicknessConversion.ToEngine(plan.BorderThickness),
			CornerRadius = new CornerRadius(plan.CornerRadius),
			Padding = MarkdownThicknessConversion.ToEngine(plan.Padding),
			FlowDirection = MarkdownFlowDirectionConversion.ToEngine(resources.Theme.FlowDirection),
			VerticalAlignment = VerticalAlignment.Center,

			Child = new TextBlock
			{
				Text = plan.Text,
				Foreground = resources.Foreground,
				FontFamily = resources.CodeFontFamily,
				FontSize = resources.Theme.CodeFontSize,
				TextWrapping = TextWrapping.Wrap,
				MaxWidth = plan.MaxWidth,
			},
		};
	}
}
