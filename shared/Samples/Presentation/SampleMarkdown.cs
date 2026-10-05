using System.IO;
using Nickelony.IDEKit.Markdown;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Markdown;
using PopupContent = Avalonia.Controls.Control;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Markdown;
using PopupContent = System.Windows.FrameworkElement;
#endif

namespace Nickelony.Samples;

/// <summary>
/// Renders Markdown with the theme, and colors its fenced Lua, C# and Visual Basic blocks with the TextMate grammar the editors use.
/// </summary>
internal static class SampleMarkdown
{
	/// <summary>Renders Markdown, such as the content of a hover, into a control the popup shows.</summary>
	internal static PopupContent Render(string markdown, SampleTheme theme)
	{
		return MarkdownRenderer.CreateContent(
			markdown,
			theme.MarkdownTheme,
			customHighlightingInstaller: (editor, language) => Highlight(editor, language, theme)
		);
	}

	/// <summary>Renders a whole Markdown document, such as the preview of a file, as tall and as wide as the place that shows it.</summary>
	/// <remarks>
	/// The preview keeps links and text selectable, which a popup that closes when the pointer leaves has no use for.
	/// </remarks>
	internal static PopupContent RenderDocument(string markdown, SampleTheme theme)
	{
		MarkdownRenderTheme pageTheme = theme.MarkdownTheme with
		{
			MaxWidth = double.PositiveInfinity,
			MaxHeight = double.PositiveInfinity,
			MaxVisibleCodeBlockLines = 40,
		};

		return MarkdownRenderer.CreateContent(
			content: markdown,
			theme: pageTheme,
			options: new MarkdownRenderOptions { AllowContentInteraction = true },
			customHighlightingInstaller: (editor, language) => Highlight(editor, language, theme)
		);
	}

	/// <summary>Returns the fence language of a document's code, such as <c>lua</c>, from the extension of its path.</summary>
	internal static string FenceLanguageOf(string documentId)
	{
		return Path.GetExtension(documentId).ToLowerInvariant() switch
		{
			".lua" => "lua",
			".cs" => "csharp",
			".vb" => "vb",
			".json" => "json",
			_ => string.Empty,
		};
	}

	/// <summary>Wraps code in a fenced block of a language.</summary>
	internal static string Fence(string code, string language) => $"```{language}\n{code}\n```";

	private static bool Highlight(TextEditor editor, string? language, SampleTheme theme)
	{
		string? languageId = language?.ToLowerInvariant() switch
		{
			"lua" => "lua",
			"cs" or "csharp" or "c#" => "csharp",
			"vb" or "vbnet" => "vb",
			_ => null,
		};

		// A block of another language falls back to the highlighting the renderer resolves itself.
		if (languageId is null)
			return false;

		TextMateHighlighter? highlighter = new(editor, languageId, theme);

		// The renderer never disposes what an installer adds, so the highlighting ends when the block leaves the tree. A hidden tab or a theme
		// switch that rebuilds the chrome takes the block out and puts it back, so the highlighting starts again when it returns.
#if AVALONIAEDIT
		editor.AttachedToVisualTree += (_, _) => highlighter ??= new TextMateHighlighter(editor, languageId, theme);

		editor.DetachedFromVisualTree += (_, _) =>
#else
		editor.Loaded += (_, _) => highlighter ??= new TextMateHighlighter(editor, languageId, theme);

		editor.Unloaded += (_, _) =>
#endif
		{
			highlighter?.Dispose();
			highlighter = null;
		};

		return true;
	}
}
