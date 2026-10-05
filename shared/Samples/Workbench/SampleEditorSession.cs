using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Media;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Media;
#endif

namespace Nickelony.Samples;

/// <summary>Wires the editor features of the Nickelony packages to one editor control that shows one document.</summary>
internal sealed class SampleEditorSession : IDisposable
{
	private readonly Func<WorkspaceEdit, Task> _applyEditAsync;
	private readonly Func<DefinitionLocation, CancellationToken, Task<bool>>? _navigateToDocumentAsync;

	private ISampleHighlighter? _highlighting;
	private readonly ILogger? _logger;

	private SampleTheme _theme;
	private TimeSpan _hoverDelay;

	/// <param name="editor">The editor control the session serves.</param>
	/// <param name="documentId">The identity of the document: its local file path, whose extension selects the highlighting.</param>
	/// <param name="theme">The look of the editor.</param>
	/// <param name="features">The language features that answer the editor's requests.</param>
	/// <param name="hoverDelay">How long the pointer rests over a name before its hover shows.</param>
	/// <param name="applyEditAsync">Applies a code action's edit to every document it names.</param>
	/// <param name="navigateToDocumentAsync">
	/// Shows a definition in another document, or <see langword="null"/> to report such a definition as not navigated.
	/// </param>
	/// <param name="logger">Receives the contained failures of the controllers, or <see langword="null"/> to log nothing.</param>
	internal SampleEditorSession(
		TextEditor editor,
		string documentId,
		SampleTheme theme,
		SampleLanguageFeatures features,
		TimeSpan hoverDelay,
		Func<WorkspaceEdit, Task> applyEditAsync,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? navigateToDocumentAsync = null,
		ILogger? logger = null
	)
	{
		Editor = editor;
		Kind = SampleFiles.KindOf(documentId);
		DocumentId = documentId;
		_applyEditAsync = applyEditAsync;
		_navigateToDocumentAsync = navigateToDocumentAsync;
		_logger = logger;
		_theme = theme;
		_hoverDelay = hoverDelay;
		_highlighting = CreateHighlighter(editor, Kind, theme);

		Editing = new SampleEditingFeatures(editor, Kind);
		Search = new SampleSearch(editor);
		LineStatus = new SampleLineStatus(editor);
		Language = CreateLanguage(features);

		ApplyEditorColors(theme);
	}

	/// <summary>Raised after <see cref="ReplaceLanguage"/> gave the editor another language session.</summary>
	internal event EventHandler? LanguageReplaced;

	internal SampleDocumentKind Kind { get; private set; }

	/// <summary>The identity of the document: its local file path, which a rename changes.</summary>
	internal string DocumentId { get; private set; }

	internal TextEditor Editor { get; }

	internal SampleEditingFeatures Editing { get; }

	internal SampleSearch Search { get; }

	internal SampleLineStatus LineStatus { get; }

	internal SampleLanguageSession Language { get; private set; }

	/// <summary>The features the language session was built from, which a server swap or a settings change builds the next one from.</summary>
	internal SampleLanguageFeatures Features { get; private set; } = SampleLanguageFeatures.None;

	/// <summary>Gives the editor other language features, for example a language server in place of the in-process providers.</summary>
	internal void ReplaceLanguage(SampleLanguageFeatures features)
	{
		Language.Dispose();
		Language = CreateLanguage(features);
		LanguageReplaced?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Follows the document to the path a rename gave it, and the editor keeps its text, undo, scroll and folds.</summary>
	/// <remarks>
	/// The controllers of a language session serve one document id, so the session is rebuilt for the new path; a new extension also takes
	/// its own highlighting and editing rules.
	/// </remarks>
	internal void FollowRename(string documentId, SampleLanguageFeatures features)
	{
		DocumentId = documentId;
		SampleDocumentKind kind = SampleFiles.KindOf(documentId);

		if (kind != Kind)
		{
			Kind = kind;
			_highlighting?.Dispose();
			_highlighting = CreateHighlighter(Editor, kind, _theme);
			Editing.ApplyKind(kind);
		}

		ReplaceLanguage(features);
	}

	/// <summary>Applies a new hover delay to the hover alone, so the other features of the session keep running.</summary>
	internal void ApplyHoverDelay(TimeSpan hoverDelay)
	{
		if (hoverDelay == _hoverDelay)
			return;

		_hoverDelay = hoverDelay;
		Language.ApplyHoverDelay(hoverDelay);
	}

	/// <summary>Switches the editor, its highlighting and its popups to the theme.</summary>
	internal void ApplyTheme(SampleTheme theme)
	{
		_theme = theme;
		ApplyEditorColors(theme);

		_highlighting?.ApplyTheme(theme);
		Language.ApplyTheme(theme);
	}

	public void Dispose()
	{
		Language.Dispose();
		LineStatus.Dispose();
		_highlighting?.Dispose();
	}

	private static ISampleHighlighter? CreateHighlighter(TextEditor editor, SampleDocumentKind kind, SampleTheme theme)
	{
		return kind switch
		{
			SampleDocumentKind.Lua => new TextMateHighlighter(editor, "lua", theme),
			SampleDocumentKind.CSharp => new TextMateHighlighter(editor, "csharp", theme),
			SampleDocumentKind.VisualBasic => new TextMateHighlighter(editor, "vb", theme),
			SampleDocumentKind.Markdown => new TextMateHighlighter(editor, "markdown", theme),
			SampleDocumentKind.Json => new JsonHighlighter(editor, theme),
			_ => null,
		};
	}

	private SampleLanguageSession CreateLanguage(SampleLanguageFeatures features)
	{
		Features = features;

		return new SampleLanguageSession(
			Editor,
			features,
			DocumentId,
			theme: _theme,
			hoverDelay: _hoverDelay,
			applyEditAsync: _applyEditAsync,
			navigateToDocumentAsync: _navigateToDocumentAsync,
			logger: _logger
		);
	}

	private void ApplyEditorColors(SampleTheme theme)
	{
		Editor.Background = theme.Background.ToBrush();
		Editor.Foreground = theme.Foreground.ToBrush();
		Editor.LineNumbersForeground = theme.LineNumber.ToBrush();
		Editor.TextArea.SelectionBrush = theme.Selection.ToBrush();
	}
}
