using Microsoft.Extensions.Logging;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using AvalonTextEditor = AvaloniaEdit.TextEditor;
#else
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// Creates the read-only code-block editors used by <see cref="MarkdownRenderer"/> and wraps them
/// in their bordered host element.
/// </summary>
/// <remarks>
/// All members create and touch the toolkit's elements and must run on the UI thread that owns the
/// target elements. Language resolution follows the behavior documented on
/// <see cref="MarkdownRenderer.CreateCodeBlockEditor"/>; the element factory measures the
/// editor with the editor's own text view so tabs and word-wrap indentation are accounted
/// for, and it clamps the block to <see cref="MarkdownRenderTheme.MaxVisibleCodeBlockLines"/>.
/// Code-block editors are never focusable; mouse selection and drag-and-drop follow
/// <see cref="MarkdownRenderOptions.AllowCodeBlockSelection"/>.
/// </remarks>
internal static partial class MarkdownCodeBlockFactory
{
	private const double CodeBlockHorizontalPadding = 8.0;
	private const double CodeBlockVerticalPadding = 6.0;
	private const double CodeBlockBorderThickness = 1.0;
	private const double CodeBlockCornerRadius = 3.0;

	// Extra height added to the measured code text so the final line's descenders are not clipped, and
	// the minimum height that keeps a single short line from touching the border.
	private const double CodeBlockMeasuredHeightTolerance = 2.0;
	private const double CodeBlockMinimumHeightTolerance = 4.0;

#if AVALONIAEDIT
	// The height of one text line relative to the font size, used only when the editor's text view cannot
	// report a line height yet.
	private const double FallbackLineHeightRatio = 1.4;
#endif

	// The shared resolver owns the resolution order; this registry adapts the engine's highlighting
	// manager to it.
	private static readonly IMarkdownHighlightingRegistry<IHighlightingDefinition> s_highlightingRegistry = new HighlightingRegistry();

	[LoggerMessage(
		EventId = 2002,
		EventName = "UnresolvedCodeBlockLanguage",
		Level = LogLevel.Debug,
		Message = "No syntax highlighting definition was found for code-block language '{Language}'."
	)]
	private static partial void LogUnresolvedCodeBlockLanguage(ILogger logger, string language);

	/// <summary>
	/// Creates a read-only code-block editor with the given language and code.
	/// </summary>
	/// <param name="language">The language used to resolve syntax highlighting, or <see langword="null"/>.</param>
	/// <param name="code">The code to display.</param>
	/// <param name="theme">The theme to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <returns>The code-block editor.</returns>
	internal static AvalonTextEditor CreateEditor(string? language, string code, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		string normalizedCode = MarkdownCodeText.Normalize(code);

		var editor = new AvalonTextEditor
		{
			Text = normalizedCode,
			IsReadOnly = true,
			Background = Brushes.Transparent,
			Foreground = theme.Foreground,
			BorderThickness = new Thickness(0.0),
			Margin = new Thickness(0.0),
			Padding = new Thickness(0.0),
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			FlowDirection = theme.FlowDirection,
			FontFamily = theme.CodeFontFamily,
			FontSize = theme.CodeFontSize,
			ShowLineNumbers = false,
			WordWrap = true,
			Focusable = false,
			IsTabStop = false
		};

		editor.Options.EnableHyperlinks = false;
		editor.Options.EnableEmailHyperlinks = false;
		editor.Options.EnableTextDragDrop = options.AllowCodeBlockSelection;
		editor.TextArea.Margin = new Thickness(0.0);
		editor.TextArea.Focusable = false;
		editor.TextArea.IsTabStop = false;
#if !AVALONIAEDIT
		KeyboardNavigation.SetIsTabStop(editor, false);
		KeyboardNavigation.SetIsTabStop(editor.TextArea, false);
#endif

		// The editor selects text with the mouse even when it cannot take focus, so the passive contract
		// has to suppress the press that starts a selection; marking it handled also prevents the
		// drag-and-drop an existing selection could initiate.
		if (!options.AllowCodeBlockSelection)
			SuppressPassiveSelection(editor);

		// Code-block editors are passive; let the host provide highlighting before trying built-in resolution.
		if (options.CustomHighlightingInstaller?.Invoke(editor, language) is not true)
		{
			editor.SyntaxHighlighting = MarkdownHighlightingResolver.Resolve(language, options.HighlightingAliases, s_highlightingRegistry);

			if (editor.SyntaxHighlighting is null && !string.IsNullOrWhiteSpace(language) && options.Logger is { } logger)
				LogUnresolvedCodeBlockLanguage(logger, language.Trim());
		}

		return editor;
	}

	/// <summary>
	/// Creates the bordered host element for a code block, including the measured editor height, the
	/// theme spacing and flow direction, and the scroll-bar and wheel-chaining behavior configured
	/// through the options.
	/// </summary>
	/// <param name="language">The language used to resolve syntax highlighting, or <see langword="null"/>.</param>
	/// <param name="code">The code to display.</param>
	/// <param name="theme">The theme to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <returns>The bordered code block element.</returns>
	internal static Border CreateCodeBlockElement(string? language, string code, MarkdownRenderTheme theme, MarkdownRenderOptions options)
	{
		AvalonTextEditor editor = CreateEditor(language, code, theme, options);

		// The editor stretches inside the border, so the measured wrap width is the border's own content
		// width. The width is measured without reserving space for the vertical scroll bar that appears
		// when the measured content overflows: the block scrolls in that case, so wrapped lines stay
		// reachable and the drift is accepted.
		double codeBlockWrapWidth = MarkdownCodeLayout.GetInnerContentWidth(
			theme.CodeMaxWidth,
			CodeBlockHorizontalPadding,
			CodeBlockBorderThickness);

		double lineHeight = GetEditorLineHeight(editor);
		double maxVisibleHeight = Math.Max(
			lineHeight + CodeBlockMinimumHeightTolerance,
			Math.Ceiling(theme.MaxVisibleCodeBlockLines * lineHeight) + CodeBlockMeasuredHeightTolerance);
		double measuredHeight = Math.Max(
			lineHeight + CodeBlockMinimumHeightTolerance,
			MeasureEditorHeight(editor, codeBlockWrapWidth, maxVisibleHeight));

		// The block is clamped to the visible-line limit; below the limit the editor keeps an automatic
		// height so the layout that hosts it reports the exact height for the width it is given. The
		// scroll bar is shown only when scrolling is allowed and the content actually overflows, which
		// the editor's own scroll viewer decides.
		editor.MaxHeight = maxVisibleHeight;

		if (measuredHeight > maxVisibleHeight)
			editor.Height = maxVisibleHeight;

		editor.VerticalScrollBarVisibility = options.AllowScrolling
			? ScrollBarVisibility.Auto
			: ScrollBarVisibility.Disabled;

		MarkdownScrollChaining.AttachCodeBlock(editor, options);

		return new Border
		{
			Background = MarkdownDerivedBrushCache.GetCodeBackground(theme),
			BorderBrush = MarkdownDerivedBrushCache.GetBorderBrush(theme),
			BorderThickness = new Thickness(CodeBlockBorderThickness),
			CornerRadius = new CornerRadius(CodeBlockCornerRadius),
			Padding = new Thickness(CodeBlockHorizontalPadding, CodeBlockVerticalPadding, CodeBlockHorizontalPadding, CodeBlockVerticalPadding),
			Margin = new Thickness(0.0, theme.BlockSpacing, 0.0, theme.BlockSpacing),
			FlowDirection = theme.FlowDirection,
			MaxWidth = theme.CodeMaxWidth,
			Child = editor
		};
	}

#if AVALONIAEDIT
	private static void SuppressPassiveSelection(AvalonTextEditor editor)
		=> editor.AddHandler(InputElement.PointerPressedEvent, SuppressPointerPressed, RoutingStrategies.Tunnel);

	private static void SuppressPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
			e.Handled = true;
	}
#else
	private static void SuppressPassiveSelection(AvalonTextEditor editor)
		=> editor.PreviewMouseLeftButtonDown += SuppressMouseLeftButtonDown;

	private static void SuppressMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
	}
#endif

	private static double GetEditorLineHeight(AvalonTextEditor editor)
	{
		double lineHeight = editor.TextArea.TextView.DefaultLineHeight;

		if (double.IsFinite(lineHeight) && lineHeight > 0.0)
			return Math.Ceiling(lineHeight);

#if AVALONIAEDIT
		return Math.Ceiling(Math.Max(1.0, editor.FontSize * FallbackLineHeightRatio));
#else
		var typeface = new Typeface(editor.FontFamily, editor.FontStyle, editor.FontWeight, editor.FontStretch);

		// Measurement follows the UI language and the live DPI so the fallback matches the metrics WPF
		// uses to render the editor.
		var formattedText = new FormattedText(
			"Ag",
			CultureInfo.CurrentUICulture,
			editor.FlowDirection,
			typeface,
			editor.FontSize,
			Brushes.Transparent,
			VisualTreeHelper.GetDpi(editor).PixelsPerDip);

		return Math.Ceiling(Math.Max(1.0, formattedText.Height));
#endif
	}

	private static double MeasureEditorHeight(AvalonTextEditor editor, double width, double maxHeight)
	{
		// The editor itself cannot be measured before it is templated (an unrooted editor reports an
		// empty size), but its text view is available and lays out exactly what the editor will render:
		// word wrap and AvalonEdit's tab stops. Measuring the text view at the block's content width
		// therefore matches the rendered extent.
		TextView textView = editor.TextArea.TextView;
		textView.Measure(new Size(width, maxHeight));

		return Math.Ceiling(textView.DesiredSize.Height) + CodeBlockMeasuredHeightTolerance;
	}

	private sealed class HighlightingRegistry : IMarkdownHighlightingRegistry<IHighlightingDefinition>
	{
		public IHighlightingDefinition? GetDefinition(string name)
			=> HighlightingManager.Instance.GetDefinition(name);

		public IReadOnlyList<IHighlightingDefinition> GetDefinitions()
			=> HighlightingManager.Instance.HighlightingDefinitions;

		public IHighlightingDefinition? GetDefinitionByExtension(string extension)
			=> HighlightingManager.Instance.GetDefinitionByExtension(extension);

		public string GetName(IHighlightingDefinition definition)
			=> definition.Name;
	}
}
