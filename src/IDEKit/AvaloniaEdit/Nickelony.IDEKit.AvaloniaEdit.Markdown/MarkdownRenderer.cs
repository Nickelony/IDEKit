using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Nickelony.IDEKit.Markdown;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

/// <summary>Renders supported Markdown content into Avalonia controls.</summary>
/// <remarks>
/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Remarks.EntryPoints']/*"/>
/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Remarks.Interaction']/*"/>
/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Remarks.Failures']/*"/>
/// </remarks>
public static class MarkdownRenderer
{
	/// <summary>
	/// Creates an Avalonia control that renders the given Markdown content, wrapped in a scroll viewer configured for tooltip hosting.
	/// </summary>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <param name="customHighlightingInstaller">
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Param.HighlightingInstaller']/*"/>
	/// </param>
	/// <returns>A <see cref="ScrollViewer"/> for rendered content, or for the plain-text fallback.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateContent.Remarks.Pipeline']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateContent.Remarks.HtmlAndImages']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateContent.Remarks.Sizing']/*"/>
	/// </para>
	/// <para>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Remarks.Hyperlinks']/*"/>
	/// </para>
	/// </remarks>
	public static Control CreateContent(
		string content,
		MarkdownRenderTheme? theme = null,
		MarkdownRenderOptions? options = null,
		CodeBlockHighlightingInstaller? customHighlightingInstaller = null
	)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		var resources = MarkdownThemeResources.Resolve(theme);

		return MarkdownRenderPipeline.CreateContent<StackPanel, Control>(
			content,
			options,
			emit: model => MarkdownPanelEmitter.Emit(model, resources, options, customHighlightingInstaller),
			createViewer: panel => CreateViewer(panel, theme, options),
			createFallback: text => CreateFallbackContent(text, resources, options)
		);
	}

	/// <summary>Renders the given Markdown content into a standalone block panel.</summary>
	/// <param name="content">The Markdown content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <param name="customHighlightingInstaller">
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Param.HighlightingInstaller']/*"/>
	/// </param>
	/// <returns>The rendered block panel.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// Unlike <see cref="CreateContent"/>, the result is not wrapped in a <see cref="ScrollViewer"/>, so a host can present the same rendering
	/// in its own container and decide the container-level selection, focus, and sizing behavior itself. Hyperlinks are keyboard-reachable
	/// only when <see cref="MarkdownRenderOptions.AllowContentInteraction"/> is enabled, and embedded code
	/// blocks still follow <see cref="MarkdownRenderOptions.AllowCodeBlockSelection"/>, <see cref="MarkdownRenderOptions.AllowScrolling"/>,
	/// and <see cref="MarkdownRenderTheme.MaxVisibleCodeBlockLines"/>; the rendering pipeline and code-block behavior
	/// are the same as in <see cref="CreateContent"/>.
	/// </para>
	/// <para>
	/// Whitespace-only content yields an empty panel. A parsing or rendering failure yields a panel that shows the content as plain text;
	/// see the type remarks for how it is reported.
	/// </para>
	/// </remarks>
	public static Control CreateContentTree(
		string content,
		MarkdownRenderTheme? theme = null,
		MarkdownRenderOptions? options = null,
		CodeBlockHighlightingInstaller? customHighlightingInstaller = null
	)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		var resources = MarkdownThemeResources.Resolve(theme);

		return MarkdownRenderPipeline.CreateContentTree(
			content,
			options,
			emit: model => MarkdownPanelEmitter.Emit(model, resources, options, customHighlightingInstaller),
			createEmpty: () => MarkdownPanelEmitter.CreateBasePanel(resources),
			createPlainText: text => MarkdownPanelEmitter.CreatePlainTextPanel(text, resources)
		);
	}

	/// <summary>Creates a plain-text element that renders the given content.</summary>
	/// <param name="content">The text content to render.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreatePlainTextContent.Remarks']/*"/>
	/// </remarks>
	public static ScrollViewer CreatePlainTextContent(string content, MarkdownRenderTheme? theme = null, MarkdownRenderOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(content);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		var resources = MarkdownThemeResources.Resolve(theme);

		return MarkdownRenderPipeline.CreatePlainTextContent(content, createFallback: text => CreateFallbackContent(text, resources, options));
	}

	/// <summary>Creates a read-only code-block editor with the given language and code.</summary>
	/// <param name="language">The language that resolves syntax highlighting, or <see langword="null"/>.</param>
	/// <param name="code">The code to display.</param>
	/// <param name="theme">The theme to render with, or <see langword="null"/> for the default theme.</param>
	/// <param name="options">The rendering options, or <see langword="null"/> for the default options.</param>
	/// <param name="customHighlightingInstaller">
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.Param.HighlightingInstaller']/*"/>
	/// </param>
	/// <returns>The code-block editor.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateCodeBlockEditor.Remarks.CustomHighlighting']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateCodeBlockEditor.Remarks.Resolution']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateCodeBlockEditor.Remarks.HeightClamp']/*"/>
	/// <include file="../../../../shared/docs/MarkdownRenderer.xml" path="doc/members/member[@name='MarkdownRenderer.CreateCodeBlockEditor.Remarks.WheelRouting']/*"/>
	/// </remarks>
	public static TextEditor CreateCodeBlockEditor(
		string? language,
		string code,
		MarkdownRenderTheme? theme = null,
		MarkdownRenderOptions? options = null,
		CodeBlockHighlightingInstaller? customHighlightingInstaller = null
	)
	{
		ArgumentNullException.ThrowIfNull(code);

		theme ??= MarkdownRenderTheme.Default;
		options ??= MarkdownRenderOptions.Default;

		return MarkdownCodeBlockFactory.CreateStandaloneEditor(
			language,
			code,
			resources: MarkdownThemeResources.Resolve(theme),
			options,
			customHighlightingInstaller
		);
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
			FlowDirection = MarkdownFlowDirectionConversion.ToEngine(theme.FlowDirection),
			VerticalScrollBarVisibility = options.AllowScrolling ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalAlignment = HorizontalAlignment.Left,
			MaxHeight = theme.MaxHeight,
			MaxWidth = theme.MaxWidth,
			Focusable = options.AllowContentInteraction,
			IsTabStop = options.AllowContentInteraction,
		};

		MarkdownScrollChaining.AttachViewer(viewer, options);

		return viewer;
	}

	private static ScrollViewer CreateFallbackContent(string content, MarkdownThemeResources resources, MarkdownRenderOptions options)
	{
		MarkdownRenderTheme theme = resources.Theme;

		// The scroll viewer constrains the text to the theme width, so the text block itself stays
		// unconstrained and wraps at the width it is given. The horizontal scroll bar must stay
		// disabled: re-enabling it would let the text block claim its full ideal width and stop
		// honoring the theme's maximum width.
		var textBlock = new TextBlock
		{
			Text = content,
			TextWrapping = TextWrapping.Wrap,
			FontFamily = resources.BodyFontFamily,
			FontSize = theme.BodyFontSize,
			Foreground = resources.Foreground,
		};

		var scrollViewer = new ScrollViewer
		{
			Content = textBlock,
			FlowDirection = MarkdownFlowDirectionConversion.ToEngine(theme.FlowDirection),
			HorizontalAlignment = HorizontalAlignment.Left,
			MaxHeight = theme.MaxHeight,
			MaxWidth = theme.MaxWidth,
			VerticalScrollBarVisibility = options.AllowScrolling ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Focusable = false,
			IsTabStop = false,
		};

		MarkdownScrollChaining.AttachViewer(scrollViewer, options);

		return scrollViewer;
	}
}
