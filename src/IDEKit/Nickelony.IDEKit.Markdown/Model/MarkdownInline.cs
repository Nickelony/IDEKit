namespace Nickelony.IDEKit.Markdown.Model;

/// <summary>An inline node of the engine-neutral Markdown content model.</summary>
/// <remarks>
/// The inline tree is produced by <see cref="MarkdownDocumentModelBuilder"/> and turned into toolkit inline elements by each binding's emitter.
/// See <see cref="MarkdownBlock"/> for the model's shared-walk contract.
/// </remarks>
public abstract record MarkdownInline;

/// <summary>Literal text: a CommonMark literal, a decoded HTML entity, or raw inline HTML shown verbatim.</summary>
/// <param name="Text">The text to show.</param>
public sealed record MarkdownText(string Text) : MarkdownInline;

/// <summary>An inline code span, shown in a bordered code surface rather than as body text.</summary>
/// <param name="Text">The code text.</param>
/// <remarks>
/// Each binding wraps the text inside the border once the span would exceed the code surface's maximum width, since an inline span has no horizontal
/// scroll of its own.
/// </remarks>
public sealed record MarkdownCodeSpan(string Text) : MarkdownInline;

/// <summary>A hard line break (two trailing spaces or a trailing backslash).</summary>
public sealed record MarkdownLineBreak : MarkdownInline;

/// <summary>Emphasized content: bold, italic, or strikethrough.</summary>
/// <param name="Kind">The emphasis kind.</param>
/// <param name="Inlines">The emphasized content.</param>
public sealed record MarkdownEmphasis(MarkdownEmphasisKind Kind, IReadOnlyList<MarkdownInline> Inlines) : MarkdownInline;

/// <summary>A hyperlink whose target may or may not be openable.</summary>
/// <param name="OpenableUri">
/// The absolute target when the renderer may open it (the scheme is allowed by the options), otherwise <see langword="null"/>.
/// A link with no openable target keeps its link affordances but is not activatable.
/// </param>
/// <param name="Inlines">The link's displayed content.</param>
public sealed record MarkdownLink(Uri? OpenableUri, IReadOnlyList<MarkdownInline> Inlines) : MarkdownInline;

/// <summary>
/// A generic inline container: an image's alt content, or any container node the narrow pipeline does not name explicitly. The emitter renders
/// it as an unstyled inline span.
/// </summary>
/// <param name="Inlines">The container's content.</param>
public sealed record MarkdownSpan(IReadOnlyList<MarkdownInline> Inlines) : MarkdownInline;

/// <summary>
/// The muted italic placeholder shown for an image whose alt text is empty, so the image stays visible in the flow. The text is the image target,
/// or a generic marker when the image has no target either.
/// </summary>
/// <param name="Text">The placeholder text.</param>
public sealed record MarkdownImagePlaceholder(string Text) : MarkdownInline;

/// <summary>The kind of emphasis a <see cref="MarkdownEmphasis"/> node applies.</summary>
public enum MarkdownEmphasisKind
{
	/// <summary>Bold (a doubled <c>*</c> or <c>_</c> delimiter).</summary>
	Bold,

	/// <summary>Italic (a single <c>*</c> or <c>_</c> delimiter).</summary>
	Italic,

	/// <summary>Strikethrough (a doubled <c>~</c> delimiter).</summary>
	Strikethrough,
}
