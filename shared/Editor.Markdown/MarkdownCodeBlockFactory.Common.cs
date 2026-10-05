using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Validation;
using Nickelony.IDEKit.Markdown;
using Nickelony.IDEKit.Markdown.Model;
using Nickelony.IDEKit.Markdown.Planning;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using EditorControl = AvaloniaEdit.TextEditor;
#else
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

#else

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

#endif

/// <summary>Creates the read-only code-block editors used by <see cref="MarkdownRenderer"/> and wraps them in their bordered host element.</summary>
/// <remarks>
/// Members create and touch toolkit elements, so they must run on the UI thread that owns those elements;
/// the editors are never focusable, and mouse selection and drag-and-drop follow <see cref="MarkdownRenderOptions.AllowCodeBlockSelection"/>.
/// Language resolution follows <see cref="MarkdownRenderer.CreateCodeBlockEditor"/>, and the factory measures with the editor's own text view so tabs
/// and word-wrap indentation count: the block is clamped to <see cref="MarkdownRenderTheme.MaxVisibleCodeBlockLines"/>.
/// </remarks>
internal static partial class MarkdownCodeBlockFactory
{
	// Extra height added to the measured code text so the final line's descenders are not clipped, and
	// the minimum height that keeps a single short line from touching the border.
	private const double CodeBlockMeasuredHeightTolerance = 2.0;
	private const double CodeBlockMinimumHeightTolerance = 4.0;

	// The shared resolver owns the resolution order; this catalog adapts the engine's highlighting
	// manager to it.
	private static readonly IMarkdownHighlightingCatalog<IHighlightingDefinition> s_highlightingCatalog = new HighlightingCatalog();

	[LoggerMessage(
		EventId = 2002,
		EventName = "UnresolvedCodeBlockLanguage",
		Level = LogLevel.Debug,
		Message = "No syntax highlighting definition was found for code-block language '{Language}'."
	)]
	private static partial void LogUnresolvedCodeBlockLanguage(ILogger logger, string language);

	/// <summary>Creates a read-only code-block editor.</summary>
	/// <param name="codeBlock">The code block to display; its language resolves syntax highlighting.</param>
	/// <param name="resources">The resolved theme resources to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <param name="customHighlightingInstaller">
	/// The host callback that can claim the block's highlighting, or <see langword="null"/> for none.
	/// </param>
	/// <returns>The code-block editor.</returns>
	internal static EditorControl CreateEditor(
		MarkdownCodeBlock codeBlock,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		var editor = new EditorControl
		{
			Text = codeBlock.NormalizedCode,
			IsReadOnly = true,
			Background = Brushes.Transparent,
			Foreground = resources.Foreground,
			BorderThickness = new Thickness(0.0),
			Margin = new Thickness(0.0),
			Padding = new Thickness(0.0),
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			FlowDirection = MarkdownFlowDirectionConversion.ToEngine(resources.Theme.FlowDirection),
			FontFamily = resources.CodeFontFamily,
			FontSize = resources.Theme.CodeFontSize,
			ShowLineNumbers = false,
			WordWrap = true,
			Focusable = false,
			IsTabStop = false,
		};

		editor.Options.EnableHyperlinks = false;
		editor.Options.EnableEmailHyperlinks = false;
		editor.Options.EnableTextDragDrop = options.AllowCodeBlockSelection;
		editor.TextArea.Margin = new Thickness(0.0);
		editor.TextArea.Focusable = false;
		editor.TextArea.IsTabStop = false;

		// The editor selects text with the mouse even when it cannot take focus, so the passive contract
		// has to suppress the press that starts a selection; marking it handled also prevents the
		// drag-and-drop an existing selection could initiate. A press on the block's own scroll bar is
		// exempt so an overflowing block stays scrollable.
		if (!options.AllowCodeBlockSelection)
			SuppressPassiveSelection(editor);

		string? language = codeBlock.Language;

		// Code-block editors are passive; let the host provide highlighting before trying built-in resolution.
		if (customHighlightingInstaller?.Invoke(editor, language) is not true)
		{
			editor.SyntaxHighlighting = MarkdownHighlightingResolver.Resolve(language, options.HighlightingAliases, s_highlightingCatalog);

			if (editor.SyntaxHighlighting is null && !string.IsNullOrWhiteSpace(language) && options.Logger is { } logger)
				LogUnresolvedCodeBlockLanguage(logger, language.Trim());
		}

		return editor;
	}

	/// <summary>Creates a read-only code-block editor that follows the scrolling and wheel policy of an embedded block.</summary>
	/// <param name="language">The language that resolves syntax highlighting, or <see langword="null"/>.</param>
	/// <param name="code">The code to display.</param>
	/// <param name="resources">The resolved theme resources to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <param name="customHighlightingInstaller">
	/// The host callback that can claim the block's highlighting, or <see langword="null"/> for none.
	/// </param>
	/// <returns>The code-block editor.</returns>
	internal static EditorControl CreateStandaloneEditor(
		string? language,
		string code,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		EditorControl editor = CreateEditor(codeBlock: new MarkdownCodeBlock(language, code), resources, options, customHighlightingInstaller);

		// A standalone editor follows the scrolling policy like an embedded block, so AllowScrolling = false
		// cannot leave a scroll bar the embedded block would have suppressed.
		editor.VerticalScrollBarVisibility = options.AllowScrolling ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

		// A standalone editor scrolls itself and forwards an unconsumable wheel to an enclosing host scroller, exactly like an
		// embedded block; without this the host would have to attach the wheel routing itself.
		MarkdownScrollChaining.AttachCodeBlock(editor, options);

		return editor;
	}

	/// <summary>
	/// Creates the bordered host element for a code block, including the measured editor height, the theme spacing and flow direction,
	/// and the scroll-bar and wheel-chaining behavior configured through the options.
	/// </summary>
	/// <param name="plan">The code block plan: the block to display, whose language resolves syntax highlighting, and the surface metrics.</param>
	/// <param name="resources">The resolved theme resources to render with.</param>
	/// <param name="options">The rendering options.</param>
	/// <param name="customHighlightingInstaller">
	/// The host callback that can claim the block's highlighting, or <see langword="null"/> for none.
	/// </param>
	/// <returns>The bordered code block element.</returns>
	internal static Border CreateCodeBlockElement(
		MarkdownCodeBlockPlan plan,
		MarkdownThemeResources resources,
		MarkdownRenderOptions options,
		CodeBlockHighlightingInstaller? customHighlightingInstaller
	)
	{
		EditorControl editor = CreateEditor(codeBlock: plan.Block, resources, options, customHighlightingInstaller);

		// The plan's wrap width is the border's own content width. The width is measured without reserving
		// space for the vertical scroll bar that appears when the measured content overflows: the block
		// scrolls in that case, so wrapped lines stay reachable and the drift is accepted.
		double codeBlockWrapWidth = plan.WrapWidth;

		double lineHeight = GetEditorLineHeight(editor);

		double maxVisibleHeight = Math.Max(
			lineHeight + CodeBlockMinimumHeightTolerance,
			Math.Ceiling(resources.Theme.MaxVisibleCodeBlockLines * lineHeight) + CodeBlockMeasuredHeightTolerance
		);

		double measuredHeight = Math.Max(
			lineHeight + CodeBlockMinimumHeightTolerance,
			MeasureEditorHeight(editor, codeBlockWrapWidth, maxVisibleHeight)
		);

		// The block is clamped to the visible-line limit; below the limit the editor keeps an automatic
		// height so the layout that hosts it reports the exact height for the width it is given. The
		// scroll bar is shown only when scrolling is allowed and the content actually overflows, which
		// the editor's own scroll viewer decides.
		editor.MaxHeight = maxVisibleHeight;

		if (measuredHeight > maxVisibleHeight)
			editor.Height = maxVisibleHeight;

		editor.VerticalScrollBarVisibility = options.AllowScrolling ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

		MarkdownScrollChaining.AttachCodeBlock(editor, options);

		return new Border
		{
			Background = resources.CodeBackground,
			BorderBrush = resources.Border,
			BorderThickness = MarkdownThicknessConversion.ToEngine(plan.BorderThickness),
			CornerRadius = new CornerRadius(plan.CornerRadius),
			Padding = MarkdownThicknessConversion.ToEngine(plan.Padding),
			Margin = MarkdownThicknessConversion.ToEngine(plan.Margin),
			FlowDirection = MarkdownFlowDirectionConversion.ToEngine(resources.Theme.FlowDirection),
			MaxWidth = plan.MaxWidth,
			Child = editor,
		};
	}

	private static double GetEditorLineHeight(EditorControl editor)
	{
		double lineHeight = editor.TextArea.TextView.DefaultLineHeight;
		return NumericValidation.IsFinitePositive(lineHeight) ? Math.Ceiling(lineHeight) : GetFallbackLineHeight(editor);
	}

	private static double MeasureEditorHeight(EditorControl editor, double width, double maxHeight)
	{
		// The editor itself cannot be measured before it is templated (an unrooted editor reports an
		// empty size), but its text view is available and lays out exactly what the editor will render:
		// word wrap and the editor's tab stops. Measuring the text view at the block's content width
		// therefore matches the rendered extent.
		TextView textView = editor.TextArea.TextView;
		textView.Measure(new Size(width, maxHeight));

		return Math.Ceiling(textView.DesiredSize.Height) + CodeBlockMeasuredHeightTolerance;
	}

	private sealed class HighlightingCatalog : IMarkdownHighlightingCatalog<IHighlightingDefinition>
	{
		public IHighlightingDefinition? GetDefinition(string name) => HighlightingManager.Instance.GetDefinition(name);

		public IReadOnlyList<IHighlightingDefinition> GetDefinitions() => HighlightingManager.Instance.HighlightingDefinitions;

		public IHighlightingDefinition? GetDefinitionByExtension(string extension) =>
			HighlightingManager.Instance.GetDefinitionByExtension(extension);

		public string GetName(IHighlightingDefinition definition) => definition.Name;
	}
}
