using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

/// <summary>
/// Renders supported Markdown content into Avalonia controls.
/// </summary>
/// <remarks>
/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Remarks.EntryPoints']/*"/>
/// <para>
/// By default the controls returned by <see cref="CreateContent"/> are configured for tooltip
/// hosting: the viewer is not focusable and does not allow text selection, and code blocks suppress
/// mouse selection and drag-and-drop. The plain-text fallback is always passive - never focusable and
/// never selectable - regardless of the interaction options.
/// <see cref="MarkdownRenderOptions.AllowContentInteraction"/> opts the rendered content into text
/// selection and keyboard-reachable hyperlinks, and
/// <see cref="MarkdownRenderOptions.AllowCodeBlockSelection"/> opts code blocks into mouse selection
/// and drag-and-drop. Use <see cref="CreateCodeBlockEditor"/> when an embeddable read-only code
/// editor is needed instead, or <see cref="CreateContentTree"/> when the rendering should be hosted
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
	/// Creates an Avalonia control that renders the given Markdown content, wrapped in a scroll viewer
	/// configured for tooltip hosting.
	/// </summary>
	/// <remarks>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateContent.Remarks.Pipeline']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateContent.Remarks.HtmlAndImages']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateContent.Remarks.Sizing']/*"/>
	/// <para>
	/// Only absolute links whose schemes are listed in the effective options are activatable; see
	/// <see cref="MarkdownRenderOptions.OpenHyperlink"/> for the opener callback chain and the
	/// activation contract. A link that cannot be opened keeps the hyperlink color and underline, shows
	/// the arrow instead of the hand cursor, and stays out of the keyboard tab order, so it is
	/// distinguishable from an openable link by its cursor and by its absence from the tab order.
	/// </para>
	/// </remarks>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>A <see cref="ScrollViewer"/> for rendered content, or for the plain-text fallback.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	public static Control CreateContent(string content, MarkdownRenderTheme? theme = null, MarkdownRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		string normalizedContent = LineTerminatorNormalizer.NormalizeToLineFeeds(content);

		if (string.IsNullOrWhiteSpace(normalizedContent))
			return CreateFallbackContent(string.Empty, theme, options);

		StackPanel? panel = TryRenderPanel(normalizedContent, theme, options);

		return panel is null
			? CreateFallbackContent(normalizedContent, theme, options)
			: CreateViewer(panel, theme, options);
	}

	/// <summary>
	/// Renders the given Markdown content into a standalone block panel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Unlike <see cref="CreateContent"/>, the result is not wrapped in a tooltip-shaped viewer, so a
	/// host can present the same rendering in its own container and decide the container-level
	/// selection, focus, and sizing behavior itself. Hyperlinks are keyboard-reachable only when
	/// <see cref="MarkdownRenderOptions.AllowContentInteraction"/> is enabled, and embedded code blocks
	/// still follow <see cref="MarkdownRenderOptions.AllowCodeBlockSelection"/>,
	/// <see cref="MarkdownRenderOptions.AllowScrolling"/>, and
	/// <see cref="MarkdownRenderTheme.MaxVisibleCodeBlockLines"/>; the rendering pipeline and
	/// code-block behavior are the same as in tooltip content.
	/// </para>
	/// <para>
	/// Whitespace-only content yields an empty panel. A parsing or rendering failure yields a panel that
	/// shows the content as plain text, and the failure is reported as a warning when a logger is
	/// configured.
	/// </para>
	/// </remarks>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>The rendered block panel.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	public static Control CreateContentTree(string content, MarkdownRenderTheme? theme = null, MarkdownRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		string normalizedContent = LineTerminatorNormalizer.NormalizeToLineFeeds(content);

		if (string.IsNullOrWhiteSpace(normalizedContent))
			return MarkdownContentEmitter.CreateBlockPanel(theme);

		return TryRenderPanel(normalizedContent, theme, options)
			?? MarkdownContentEmitter.CreatePlainTextPanel(normalizedContent, theme);
	}

	/// <summary>
	/// Creates a plain-text control that renders the given content.
	/// </summary>
	/// <remarks>
	/// The content is displayed literally and is not parsed as Markdown. Line endings are normalized to
	/// line feeds like every other entry point. The rendered text is not focusable; see the type
	/// remarks for the contract.
	/// </remarks>
	/// <param name="content">The text content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <returns>A <see cref="ScrollViewer"/> containing the rendered text.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="content"/> is <see langword="null"/>.
	/// </exception>
	public static Control CreatePlainTextContent(string content, MarkdownRenderTheme? theme = null, MarkdownRenderOptions? options = null)
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
	public static TextEditor CreateCodeBlockEditor(string? language, string code, MarkdownRenderTheme? theme = null, MarkdownRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(code);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		TextEditor editor = MarkdownCodeBlockFactory.CreateEditor(language, code, theme, options);

		// A standalone editor scrolls itself and forwards an unconsumable wheel to its parent, exactly like an
		// embedded block; without this the host would have to attach the wheel routing itself.
		MarkdownScrollChaining.AttachCodeBlock(editor, options);

		return editor;
	}

	private static StackPanel? TryRenderPanel(string content, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		try
		{
			MarkdownDocumentModel model = MarkdownDocumentModelBuilder.Build(content, options);

			return MarkdownContentEmitter.Emit(model, theme, options);
		}
		catch (Exception exception)
		{
			// The fallback keeps tooltips resilient, but the failure stays observable.
			ReportRenderFailure(exception, options);
			return null;
		}
	}

	private static ScrollViewer CreateViewer(Control content, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		var viewer = new ScrollViewer
		{
			Content = content,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			Margin = new Thickness(0.0),
			FlowDirection = theme.FlowDirection,
			VerticalScrollBarVisibility = options.AllowScrolling
				? ScrollBarVisibility.Auto
				: ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalAlignment = HorizontalAlignment.Left,
			MaxHeight = theme.MaxHeight,
			MaxWidth = theme.MaxWidth,
			Focusable = options.AllowContentInteraction,
			IsTabStop = options.AllowContentInteraction
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
			Text = content,
			TextWrapping = TextWrapping.Wrap,
			FontFamily = theme.BodyFontFamily,
			FontSize = theme.BodyFontSize,
			Foreground = theme.Foreground
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
