using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Indentation;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Indentation;

#else

namespace Nickelony.IDEKit.AvalonEdit.Indentation;

#endif

/// <summary>Detects the indentation of an editor's document and converts it between spaces and tabs.</summary>
/// <remarks>
/// <para>
/// Each conversion replaces only the leading whitespace of the lines that change, as one batch in one undo step, so the editor's
/// own anchors keep the caret, the selection (a rectangle too) and the scroll position on their text. A position inside a replaced indentation
/// lands where the engine maps it, at an end of the new indentation. The conversion then sets <see cref="TextEditorOptions.ConvertTabsToSpaces"/>
/// to match the text, and a resize also sets <see cref="TextEditorOptions.IndentationSize"/>. The options are not part of the undo step,
/// so undoing a conversion restores the text and keeps the options.
/// </para>
/// <para>
/// A conversion that changes nothing leaves the document and its undo stack untouched but still sets the options. An optional edit target routes
/// the batch through a host-owned <see cref="ITextEditTarget"/> that holds the same content as the editor.
/// </para>
/// </remarks>
public static class TextEditorIndentationExtensions
{
	/// <summary>Converts the leading tabs of every line to spaces, and makes the editor indent with spaces.</summary>
	/// <param name="textEditor">The editor whose document is converted.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly. A supplied target
	/// must hold the same content as the editor's document.
	/// </param>
	/// <returns><see langword="true"/> when any line changed; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	/// <remarks>
	/// Each tab advances to the next tab stop of <see cref="TextEditorOptions.IndentationSize"/> columns,
	/// so the indentation keeps its width, and a tab after the first other character of a line stays.
	/// The rules are those of <see cref="WhitespaceOperations.ConvertIndentationToSpaces"/>.
	/// </remarks>
	public static bool ConvertIndentationToSpaces(this TextEditor textEditor, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		bool changed = TextEditorEditApplier.ApplyPlan(
			textEditor,
			snapshot => IndentationPlanner.PlanToSpaces(snapshot, textEditor.Options.IndentationSize),
			editTarget
		);

		textEditor.Options.ConvertTabsToSpaces = true;

		return changed;
	}

	/// <summary>Converts the leading groups of spaces that fill a tab stop of every line to tabs, and makes the editor indent with tabs.</summary>
	/// <param name="textEditor">The editor whose document is converted.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly. A supplied target
	/// must hold the same content as the editor's document.
	/// </param>
	/// <returns><see langword="true"/> when any line changed; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	/// <remarks>
	/// The tab stops are <see cref="TextEditorOptions.IndentationSize"/> columns apart, and the rules
	/// are those of <see cref="WhitespaceOperations.ConvertIndentationToTabs"/>.
	/// </remarks>
	public static bool ConvertIndentationToTabs(this TextEditor textEditor, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		bool changed = TextEditorEditApplier.ApplyPlan(
			textEditor,
			snapshot => IndentationPlanner.PlanToTabs(snapshot, textEditor.Options.IndentationSize),
			editTarget
		);

		textEditor.Options.ConvertTabsToSpaces = false;

		return changed;
	}

	/// <summary>
	/// Gives the space indentation of every line a new number of columns per level, and makes the editor indent with that many spaces.
	/// </summary>
	/// <param name="textEditor">The editor whose document is converted.</param>
	/// <param name="currentSize">The number of spaces of one level in the document.</param>
	/// <param name="newSize">The number of spaces of one level after the conversion, which becomes the editor's indentation size.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly. A supplied target
	/// must hold the same content as the editor's document.
	/// </param>
	/// <returns><see langword="true"/> when any line changed; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="currentSize"/> or <paramref name="newSize"/> is less than or equal to zero or greater
	/// than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	/// <remarks>
	/// The rules are those of <see cref="WhitespaceOperations.ResizeIndentation"/>: a line with a tab in its indentation stays, and a remainder
	/// that is not a whole level is kept.
	/// </remarks>
	public static bool ResizeIndentation(this TextEditor textEditor, int currentSize, int newSize, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		bool changed = TextEditorEditApplier.ApplyPlan(
			textEditor,
			snapshot => IndentationPlanner.PlanResize(snapshot, currentSize, newSize),
			editTarget
		);

		textEditor.Options.ConvertTabsToSpaces = true;
		textEditor.Options.IndentationSize = newSize;

		return changed;
	}

	/// <summary>Detects the indentation that the document uses and sets the editor's options to it.</summary>
	/// <param name="textEditor">The editor whose document is inspected and whose options are set.</param>
	/// <param name="defaultInsertSpaces">The style to use when the document does not show one.</param>
	/// <param name="defaultSize">The size to use when the document does not show one.</param>
	/// <returns>What the document showed, with a flag for each half that it did not show and the default supplied.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="defaultSize"/> is less than or equal to zero or greater than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <remarks>
	/// The call copies the lines that <see cref="IndentationDetector"/> reads, up to <see cref="IndentationDetector.MaximumScannedLineCount"/>,
	/// and not the rest of the document. It sets <see cref="TextEditorOptions.ConvertTabsToSpaces"/>
	/// and <see cref="TextEditorOptions.IndentationSize"/> and leaves the text alone.
	/// </remarks>
	public static IndentationDetection DetectIndentation(this TextEditor textEditor, bool defaultInsertSpaces, int defaultSize)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		TextDocument document = TextEditorValidation.RequireDocument(textEditor);

		int end =
			document.LineCount > IndentationDetector.MaximumScannedLineCount
				? document.GetLineByNumber(IndentationDetector.MaximumScannedLineCount + 1).Offset
				: document.TextLength;

		IndentationDetection detection = IndentationDetector.Detect(document.GetText(0, end), defaultInsertSpaces, defaultSize);

		textEditor.Options.ConvertTabsToSpaces = detection.InsertSpaces;
		textEditor.Options.IndentationSize = detection.Size;

		return detection;
	}
}
