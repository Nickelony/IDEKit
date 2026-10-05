using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Formatting;
using Nickelony.IDEKit.Core.Indentation;
using Nickelony.IDEKit.Core.Scanning;
#if AVALONIAEDIT
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.AvaloniaEdit.Comments;
using Nickelony.IDEKit.AvaloniaEdit.Formatting;
using Nickelony.IDEKit.AvaloniaEdit.Indentation;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.AutoClosing;
using Nickelony.IDEKit.AvalonEdit.Comments;
using Nickelony.IDEKit.AvalonEdit.Formatting;
using Nickelony.IDEKit.AvalonEdit.Indentation;
#endif

namespace Nickelony.Samples;

/// <summary>Wires typing assistance and text commands to one editor: auto-closing pairs, indentation, line comments and formatting.</summary>
internal sealed class SampleEditingFeatures
{
	private static readonly CommentSyntax s_luaComments = new(
		"--",
		new BlockCommentSyntax("--[[", "]]", allowNesting: true),
		StringLiteralStyles.DoubleQuoted | StringLiteralStyles.SingleQuoted | StringLiteralStyles.LongBracketQuoted
	);

	private static readonly CommentSyntax s_cSharpComments = new(
		"//",
		new BlockCommentSyntax("/*", "*/"),

		StringLiteralStyles.DoubleQuoted
			| StringLiteralStyles.SingleQuoted
			| StringLiteralStyles.VerbatimDoubleQuoted
			| StringLiteralStyles.TripleDoubleQuoted
	);

	private static readonly CommentSyntax s_visualBasicComments = new("'", null, StringLiteralStyles.DoubleQuoted);

	private readonly TextEditor _editor;
	private readonly AutoClosingService _autoClosing = new();
	private readonly LineCommentService _lineComments = new();
	private readonly DocumentFormatterService _formatting = new();

	private CommentSyntax _comments = s_luaComments;
	private IndentationDetection _detection;
	private bool _isIndentationChosen;

	private readonly AutoClosingOptions _autoClosingOptions = new()
	{
		Pairs =
		[
			AutoClosingPair.Parentheses,
			AutoClosingPair.Brackets,
			AutoClosingPair.Braces,
			AutoClosingPair.DoubleQuotes,
			AutoClosingPair.SingleQuotes,
		],

		// A closing character skips or deletes with its opener only when this service inserted the pair.
		ClosingTextSkipTracking = AutoClosingTrackingMode.TrackedOnly,
		PairDeletionTracking = AutoClosingTrackingMode.TrackedOnly,
	};

	/// <param name="editor">The editor control the features serve.</param>
	/// <param name="kind">The language of the document, which selects the comment syntax and the indentation rules.</param>
	internal SampleEditingFeatures(TextEditor editor, SampleDocumentKind kind)
	{
		_editor = editor;
		ApplyKind(kind);

		_editor.TextArea.TextEntering += (_, e) => _autoClosing.ApplyTextEntering(_editor, e, _autoClosingOptions);
#if AVALONIAEDIT
		_editor.AddHandler(
			InputElement.KeyDownEvent,
			(_, e) => _autoClosing.TryDeletePair(_editor, e, _autoClosingOptions),
			RoutingStrategies.Tunnel
		);
#else
		_editor.PreviewKeyDown += (_, e) => _autoClosing.TryDeletePair(_editor, e, _autoClosingOptions);
#endif
	}

	/// <summary>
	/// The comment and string syntax of a language, or <see langword="null"/> for a document that holds no code, such as JSON or Markdown.
	/// </summary>
	internal static CommentSyntax? CodeSyntaxOf(SampleDocumentKind kind)
	{
		return kind switch
		{
			SampleDocumentKind.Lua => s_luaComments,
			SampleDocumentKind.CSharp => s_cSharpComments,
			SampleDocumentKind.VisualBasic => s_visualBasicComments,
			_ => null,
		};
	}

	/// <summary>Selects the comment syntax and the indentation rules of a language, which a rename to another extension changes.</summary>
	internal void ApplyKind(SampleDocumentKind kind)
	{
		_comments = CodeSyntaxOf(kind) ?? s_luaComments;

		_editor.TextArea.IndentationStrategy = new PolicyIndentationStrategy(
			_editor.Options,
			kind == SampleDocumentKind.Lua ? new LuaIndentationPolicy() : new PreviousLineIndentationPolicy()
		);
	}

	/// <summary>Comments the selected lines, or uncomments them when every one is already commented.</summary>
	internal void ToggleLineComment() => _lineComments.ApplyEdit(_editor, _comments, LineCommentActionKind.Toggle);

	/// <summary>Trims the trailing whitespace of every line as one undo step.</summary>
	internal void TrimTrailingWhitespace() => _formatting.FormatDocument(_editor, TrimTrailingWhitespaceFormatter.Instance);

	/// <summary>
	/// Sets the editor's indentation to the one the text shows, and to spaces of <paramref name="defaultSize"/> columns for what it does not show.
	/// </summary>
	/// <param name="defaultSize">The number of spaces of one level in a text that shows no size.</param>
	internal void DetectIndentation(int defaultSize)
	{
		_detection = _editor.DetectIndentation(defaultInsertSpaces: true, defaultSize);
		_isIndentationChosen = false;
	}

	/// <summary>Describes the indentation of the editor for the status bar: the number of spaces of one level, or the width of a tab.</summary>
	internal string DescribeIndentation() =>
		_editor.Options.ConvertTabsToSpaces ? $"Spaces: {_editor.Options.IndentationSize}" : $"Tab Size: {_editor.Options.IndentationSize}";

	/// <summary>Says where the indentation of the editor comes from: the file, the defaults of the settings, or a choice made for this tab.</summary>
	internal string DescribeIndentationSource()
	{
		if (_isIndentationChosen)
			return "Chosen for this tab";

		return (_detection.InsertSpacesDetected, _detection.SizeDetected) switch
		{
			(true, true) => "Detected from the file: the style and the size",
			(true, false) => "Detected from the file: the style; the size is the tab size of the settings",
			(false, true) => "Detected from the file: the size; the style is the default, spaces",
			_ => "The file shows no indentation, so the defaults apply: spaces, with the tab size of the settings",
		};
	}

	/// <summary>Whether the editor indents with spaces or with a tab as given, and with the given number of columns per level.</summary>
	internal bool UsesIndentation(bool insertSpaces, int size) =>
		_editor.Options.ConvertTabsToSpaces == insertSpaces && _editor.Options.IndentationSize == size;

	/// <summary>Sets what the Tab key inserts and how wide a level is, without touching the text.</summary>
	internal void UseIndentation(bool insertSpaces, int size)
	{
		_editor.Options.ConvertTabsToSpaces = insertSpaces;
		_editor.Options.IndentationSize = size;
		_isIndentationChosen = true;
	}

	/// <summary>Gives this editor the indentation of another editor of the same document, which received the same text.</summary>
	internal void FollowIndentation(SampleEditingFeatures other)
	{
		UseIndentation(other._editor.Options.ConvertTabsToSpaces, other._editor.Options.IndentationSize);
		_isIndentationChosen = other._isIndentationChosen;
		_detection = other._detection;
	}

	/// <summary>Converts the leading tabs to spaces as one undo step and makes the editor insert spaces.</summary>
	/// <returns>Whether any line changed.</returns>
	internal bool ConvertIndentationToSpaces()
	{
		bool changed = _editor.ConvertIndentationToSpaces();
		_isIndentationChosen = true;

		return changed;
	}

	/// <summary>Converts the leading runs of spaces that fill a tab stop to tabs as one undo step and makes the editor insert tabs.</summary>
	/// <returns>Whether any line changed.</returns>
	internal bool ConvertIndentationToTabs()
	{
		bool changed = _editor.ConvertIndentationToTabs();
		_isIndentationChosen = true;

		return changed;
	}

	/// <summary>Gives the space indentation the text shows the number of columns per level that the editor uses, as one undo step.</summary>
	/// <returns>Whether any line changed: it does not when the text shows no space indentation, or shows the size the editor uses.</returns>
	internal bool ReindentSpaces()
	{
		int newSize = _editor.Options.IndentationSize;
		IndentationDetection shown = IndentationDetector.Detect(_editor.Text, defaultInsertSpaces: true, newSize);

		if (!shown.SizeDetected || shown.Size == newSize)
			return false;

		bool changed = _editor.ResizeIndentation(shown.Size, newSize);
		_isIndentationChosen = true;

		return changed;
	}
}
