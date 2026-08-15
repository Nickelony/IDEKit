using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Renders supported Markdown content into WPF elements.
/// </summary>
/// <remarks>
/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Remarks.EntryPoints']/*"/>
/// <para>
/// By default the elements returned by <see cref="CreateContent"/> are configured for tooltip
/// hosting: the viewer is not focusable and does not allow text selection, and code blocks suppress
/// mouse selection and drag-and-drop. The plain-text fallback is always passive - never focusable and
/// never selectable - regardless of the interaction options.
/// <see cref="MarkdownRenderOptions.AllowContentInteraction"/> opts the rendered content into text
/// selection and keyboard-reachable hyperlinks, and
/// <see cref="MarkdownRenderOptions.AllowCodeBlockSelection"/> opts code blocks into mouse selection
/// and drag-and-drop. Use <see cref="CreateCodeBlockEditor"/> when an embeddable read-only code
/// editor is needed instead, or <see cref="CreateFlowDocument"/> when the rendering should be hosted
/// in a caller-controlled container.
/// </para>
/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Remarks.Failures']/*"/>
/// </remarks>
public static partial class MarkdownRenderer
{
	[LoggerMessage(
		EventId = 2000,
		EventName = "MarkdownRenderFailed",
		Level = LogLevel.Warning,
		Message = "Markdown rendering failed; showing plain text instead."
	)]
	private static partial void LogRenderFailed(ILogger logger, Exception? exception);

	/// <summary>
	/// Creates a WPF element that renders the given Markdown content.
	/// </summary>
	/// <remarks>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateContent.Remarks.Pipeline']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateContent.Remarks.HtmlAndImages']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateContent.Remarks.Sizing']/*"/>
	/// <para>
	/// Only absolute links whose schemes are listed in the effective options are activatable; see
	/// <see cref="MarkdownRenderOptions.OpenHyperlink"/> for the opener callback chain and the
	/// activation contract. The ambient navigation request is suppressed so an enclosing
	/// <see cref="System.Windows.Navigation.NavigationService"/> cannot activate the link a second
	/// time. A link that cannot be opened keeps the hyperlink color and underline, shows the arrow
	/// instead of the hand cursor, and stays out of the keyboard tab order, so it is distinguishable
	/// from an openable link by its cursor and by its absence from the tab order.
	/// </para>
	/// </remarks>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>
	/// <list type="bullet">
	/// <item>A <see cref="FlowDocumentScrollViewer"/> for rendered content;</item>
	/// <item>a <see cref="ScrollViewer"/> for the plain-text fallback.</item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	public static FrameworkElement CreateContent(string content, MarkdownRenderTheme? theme = null, MarkdownRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		string normalizedContent = LineTerminatorNormalizer.NormalizeToLineFeeds(content);

		if (string.IsNullOrWhiteSpace(normalizedContent))
			return CreateFallbackContent(string.Empty, theme, options);

		FlowDocument? flowDocument = TryRenderDocument(normalizedContent, theme, options);

		return flowDocument is null
			? CreateFallbackContent(normalizedContent, theme, options)
			: CreateViewer(flowDocument, theme, options);
	}

	/// <summary>
	/// Renders the given Markdown content into a standalone <see cref="FlowDocument"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Unlike <see cref="CreateContent"/>, the result is not wrapped in a tooltip-shaped viewer, so a
	/// host can present the same rendering in its own container and decide the document-level
	/// selection, focus, and sizing behavior itself. Hyperlinks are keyboard-reachable only when
	/// <see cref="MarkdownRenderOptions.AllowContentInteraction"/> is enabled, and embedded code blocks
	/// still follow <see cref="MarkdownRenderOptions.AllowCodeBlockSelection"/>,
	/// <see cref="MarkdownRenderOptions.AllowScrolling"/>, and
	/// <see cref="MarkdownRenderTheme.MaxVisibleCodeBlockLines"/>; the rendering pipeline and
	/// code-block behavior are the same as in tooltip content.
	/// </para>
	/// <para>
	/// The document keeps WPF's default single-column layout metrics, which the tooltip viewer renders
	/// as one continuously flowing column. A host that paginates the document itself (for example a
	/// <c>FlowDocumentReader</c> or a print paginator) controls the column layout through
	/// <c>ColumnWidth</c> and the page size. Code blocks are atomic block-level elements, so a code
	/// block taller than a page or column cannot split across a page or column break.
	/// </para>
	/// <para>
	/// Whitespace-only content yields an empty document. A parsing or rendering failure yields a document
	/// that shows the content as plain text, and the failure is reported as a warning when a logger is
	/// configured.
	/// </para>
	/// </remarks>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>The rendered document.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	public static FlowDocument CreateFlowDocument(string content, MarkdownRenderTheme? theme = null, MarkdownRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		string normalizedContent = LineTerminatorNormalizer.NormalizeToLineFeeds(content);

		if (string.IsNullOrWhiteSpace(normalizedContent))
			return MarkdownFlowDocumentEmitter.CreateBaseFlowDocument(theme);

		return TryRenderDocument(normalizedContent, theme, options)
			?? MarkdownFlowDocumentEmitter.CreatePlainTextFlowDocument(normalizedContent, theme);
	}

	/// <summary>
	/// Creates a plain-text element that renders the given content.
	/// </summary>
	/// <remarks>
	/// The content is displayed literally and is not parsed as Markdown. Line endings are normalized to
	/// line feeds like every other entry point; WPF renders both <c>CRLF</c> and <c>LF</c> as a line
	/// break, so the visible result is unchanged. The rendered text is not focusable; see the type
	/// remarks for the contract.
	/// </remarks>
	/// <param name="content">The text content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>A <see cref="ScrollViewer"/> containing the rendered text.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	public static ScrollViewer CreatePlainTextContent(string content, MarkdownRenderTheme? theme = null, MarkdownRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		string normalizedContent = LineTerminatorNormalizer.NormalizeToLineFeeds(content);

		return CreateFallbackContent(normalizedContent, theme ?? MarkdownRenderTheme.Default, options ?? MarkdownRenderOptions.Default);
	}

	/// <summary>
	/// Creates a read-only code-block editor with the given language and code.
	/// </summary>
	/// <remarks>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateCodeBlockEditor.Remarks.CustomHighlighting']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateCodeBlockEditor.Remarks.Resolution']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateCodeBlockEditor.Remarks.HeightClamp']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateCodeBlockEditor.Remarks.WheelRouting']/*"/>
	/// </remarks>
	/// <param name="language">The language used to resolve syntax highlighting, or <see langword="null"/>.</param>
	/// <param name="code">The code to display.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>The code-block editor.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="code"/> is <see langword="null"/>.
	/// </exception>
	public static AvalonTextEditor CreateCodeBlockEditor(string? language, string code, MarkdownRenderTheme? theme = null, MarkdownRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(code);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		AvalonTextEditor editor = MarkdownCodeBlockFactory.CreateEditor(language, code, theme, options);

		// A standalone editor scrolls itself and forwards an unconsumable wheel to its parent, exactly like an
		// embedded block; without this the host would have to attach the wheel routing itself.
		MarkdownScrollChaining.AttachCodeBlock(editor, options);

		return editor;
	}

	private static FlowDocument? TryRenderDocument(string content, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		try
		{
			return MarkdownFlowDocumentEmitter.Render(content, theme, options);
		}
		catch (Exception exception)
		{
			// The fallback keeps tooltips resilient, but the failure stays observable.
			ReportRenderFailure(exception, options);
			return null;
		}
	}

	private static FlowDocumentScrollViewer CreateViewer(FlowDocument document, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var viewer = new FlowDocumentScrollViewer
		{
			Document = document,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			Margin = new Thickness(0.0),
			FlowDirection = theme.FlowDirection,
			IsToolBarVisible = false,
			VerticalScrollBarVisibility = options.AllowScrolling
				? ScrollBarVisibility.Auto
				: ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalAlignment = HorizontalAlignment.Left,
			IsSelectionEnabled = options.AllowContentInteraction,
			Focusable = options.AllowContentInteraction,
			IsTabStop = options.AllowContentInteraction,
			MaxHeight = theme.MaxHeight,
			MaxWidth = theme.MaxWidth
		};

		MarkdownScrollChaining.AttachViewer(viewer, options);

		return viewer;
	}

	private static ScrollViewer CreateFallbackContent(string content, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		// The scroll viewer constrains the text to the theme width, so the text block itself stays
		// unconstrained and wraps at the width it is given. The horizontal scroll bar must stay
		// disabled: re-enabling it would let the text block claim its full ideal width and stop
		// honoring the theme's maximum width.
		var textBlock = new TextBlock
		{
			Foreground = theme.Foreground,
			Text = content,
			TextWrapping = TextWrapping.Wrap,
			FlowDirection = theme.FlowDirection,
			FontFamily = theme.BodyFontFamily,
			FontSize = theme.BodyFontSize
		};

		var scrollViewer = new ScrollViewer
		{
			Content = textBlock,
			FlowDirection = theme.FlowDirection,
			HorizontalAlignment = HorizontalAlignment.Left,
			MaxHeight = theme.MaxHeight,
			MaxWidth = theme.MaxWidth,
			VerticalScrollBarVisibility = options.AllowScrolling
				? ScrollBarVisibility.Auto
				: ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Focusable = false,
			IsTabStop = false
		};

		MarkdownScrollChaining.AttachViewer(scrollViewer, options);

		return scrollViewer;
	}

	private static void ReportRenderFailure(Exception exception, MarkdownRenderOptions options)
	{
		if (options.Logger is { } logger)
			LogRenderFailed(logger, exception);
	}
}
