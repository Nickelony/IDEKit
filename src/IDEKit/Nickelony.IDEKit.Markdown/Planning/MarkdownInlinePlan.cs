using Nickelony.IDEKit.Markdown.Model;

namespace Nickelony.IDEKit.Markdown.Planning;

/// <summary>An inline node of a Markdown render plan, with every layout decision already made.</summary>
/// <remarks>
/// <see cref="MarkdownRenderPlanner"/> builds the tree from the content model, and an emitter creates one toolkit element per node. The nodes carry
/// no toolkit type, brush or font; the emitter resolves those from the theme.
/// </remarks>
public abstract record MarkdownInlinePlan;

/// <summary>Literal text shown with the surrounding block's style.</summary>
/// <param name="Text">The text to show.</param>
public sealed record MarkdownTextPlan(string Text) : MarkdownInlinePlan;

/// <summary>An inline code span, shown in a bordered code surface.</summary>
/// <param name="Text">The code text.</param>
/// <param name="Padding">The padding between the border and the text.</param>
/// <param name="BorderThickness">The border widths.</param>
/// <param name="CornerRadius">The radius of the border's corners.</param>
/// <param name="MaxWidth">The widest the text may grow before it wraps inside the border.</param>
public sealed record MarkdownCodeSpanPlan(
	string Text,
	MarkdownThickness Padding,
	MarkdownThickness BorderThickness,
	double CornerRadius,
	double MaxWidth
) : MarkdownInlinePlan;

/// <summary>A hyperlink, which keeps its link affordances whether or not its target can be opened.</summary>
/// <param name="OpenableUri">The absolute target the renderer may open, or <see langword="null"/> when the link is not activatable.</param>
/// <param name="IsFocusable">
/// Whether the link is a keyboard stop, which only an openable link is and only while content interaction is enabled.
/// </param>
/// <param name="Inlines">The link's displayed content.</param>
public sealed record MarkdownLinkPlan(Uri? OpenableUri, bool IsFocusable, IReadOnlyList<MarkdownInlinePlan> Inlines) : MarkdownInlinePlan;

/// <summary>Bold, italic or strikethrough content.</summary>
/// <param name="Kind">The emphasis kind.</param>
/// <param name="Inlines">The emphasized content.</param>
public sealed record MarkdownEmphasisPlan(MarkdownEmphasisKind Kind, IReadOnlyList<MarkdownInlinePlan> Inlines) : MarkdownInlinePlan;

/// <summary>A hard line break.</summary>
public sealed record MarkdownLineBreakPlan : MarkdownInlinePlan;

/// <summary>The muted, italic placeholder shown for an image that has no alt text.</summary>
/// <param name="Text">The placeholder text: the image target, or a generic marker when the image has none.</param>
public sealed record MarkdownImagePlaceholderPlan(string Text) : MarkdownInlinePlan;

/// <summary>A container that adds no style of its own to the inlines it groups.</summary>
/// <param name="Inlines">The grouped content.</param>
public sealed record MarkdownSpanPlan(IReadOnlyList<MarkdownInlinePlan> Inlines) : MarkdownInlinePlan;
