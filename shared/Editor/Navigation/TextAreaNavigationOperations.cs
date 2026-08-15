#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Documents;
using System.Windows;
#endif
using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Navigation;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Navigation;
#else
namespace Nickelony.IDEKit.AvalonEdit.Navigation;
#endif

/// <summary>
/// Provides helpers for resolving document positions and applying navigation locations.
/// </summary>
/// <remarks>
/// The helpers read and update the text area's document, which is thread-affine, so they must be called on
/// the text area's thread. They extend <see cref="TextArea"/>, which carries the caret, selection, and
/// document state; a host with a <see cref="TextEditor"/> passes <c>editor.TextArea</c>.
/// </remarks>
public static class TextAreaNavigationOperations
{
	/// <summary>
	/// Gets the word at a document offset, where a word is a run of identifier characters.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The walk returns <see langword="null"/> when the probed character cannot be part of a word. It
	/// intentionally does not apply the preceding-token fallback of
	/// <see cref="IdentifierOperations.FindTokenSpan"/>: callers that need the preceding word when
	/// the probe sits on a separator must use the Core helper.
	/// </para>
	/// <para>
	/// An offset at the end of the document belongs to the word that ends there, unless the document ends
	/// with a line terminator (the probe then sits on the terminator and yields <see langword="null"/>).
	/// Because only the policy's <see cref="IdentifierCharacterPolicy.IsPartCharacter(char)"/> rule is
	/// consulted, a run that starts with a digit is returned even though it could not start an identifier
	/// under most language rules.
	/// </para>
	/// </remarks>
	/// <param name="textArea">The text area to inspect.</param>
	/// <param name="offset">
	/// The zero-based document offset to inspect. Values outside the document are clamped.
	/// </param>
	/// <param name="policy">
	/// The identifier-character policy that decides which characters form a word. Defaults to
	/// <see cref="IdentifierCharacterPolicy.Default"/>, which treats letters, digits, and underscores
	/// as word characters.
	/// </param>
	/// <returns>
	/// The word that <paramref name="offset"/> points at, or <see langword="null"/> when the offset is on a
	/// character that cannot be part of a word.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The text area has no document assigned.</exception>
	public static string? GetWordFromOffset(this TextArea textArea, int offset, IdentifierCharacterPolicy? policy = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);

		IdentifierCharacterPolicy effectivePolicy = policy ?? IdentifierCharacterPolicy.Default;
		TextDocument document = GetRequiredDocument(textArea);
		int position = document.ClampOffset(offset);

		// The end of the document holds no character of its own, so a position there belongs to the
		// word that ends at the last character.
		if (position == document.TextLength)
			position--;

		if (position < 0 || !effectivePolicy.IsPartCharacter(document.GetCharAt(position)))
			return null;

		int wordStart = position;
		int wordEnd = position + 1;

		while (wordStart > 0 && effectivePolicy.IsPartCharacter(document.GetCharAt(wordStart - 1)))
			wordStart--;

		while (wordEnd < document.TextLength && effectivePolicy.IsPartCharacter(document.GetCharAt(wordEnd)))
			wordEnd++;

		return document.GetText(wordStart, wordEnd - wordStart);
	}

	/// <summary>
	/// Creates a zero-length location at a one-based editor position in the text area's document.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The position is clamped when the offset is calculated: a column beyond the line end maps to the
	/// position before that line's terminator.
	/// </para>
	/// <para>
	/// The original line number is stored as the preferred scroll line and
	/// clamped to the document's line range (at least 1) when the location is applied.
	/// </para>
	/// </remarks>
	/// <param name="textArea">The text area whose document provides the offset mapping.</param>
	/// <param name="filePath">
	/// The logical path of the target document, or <see langword="null"/> for a document that is not
	/// associated with a file (for example an unsaved document).
	/// </param>
	/// <param name="position">The one-based editor position to navigate to.</param>
	/// <returns>The created location.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The text area has no document assigned.</exception>
	public static NavigationLocation CreateCaretLocation(
		this TextArea textArea,
		string? filePath,
		TextLocation position)
	{
		ArgumentNullException.ThrowIfNull(textArea);

		int offset = GetOffset(textArea, position);
		return new NavigationLocation(filePath, offset, offset, 0, position.Line);
	}

	/// <summary>
	/// Creates a location that selects a one-based editor position range.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Both endpoints are clamped to the document: a column beyond a line's end maps to the position
	/// before that line's terminator.
	/// If the end offset precedes the start offset, the selection is empty and collapses at the start
	/// position; the caret and the scroll position always follow the start position.
	/// </para>
	/// <para>
	/// The original start line number is stored as the preferred scroll line and
	/// clamped to the document's line range (at least 1) when the location is applied.
	/// </para>
	/// </remarks>
	/// <param name="textArea">The text area whose document provides the offset mapping.</param>
	/// <param name="filePath">
	/// The logical path of the target document, or <see langword="null"/> for a document that is not
	/// associated with a file (for example an unsaved document).
	/// </param>
	/// <param name="start">The one-based start position.</param>
	/// <param name="end">The one-based end position.</param>
	/// <returns>The created location.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The text area has no document assigned.</exception>
	public static NavigationLocation CreateRangeLocation(
		this TextArea textArea,
		string? filePath,
		TextLocation start,
		TextLocation end)
	{
		ArgumentNullException.ThrowIfNull(textArea);

		int startOffset = GetOffset(textArea, start);
		int endOffset = GetOffset(textArea, end);
		int selectionLength = Math.Max(0, endOffset - startOffset);

		return new NavigationLocation(filePath, startOffset, startOffset, selectionLength, start.Line);
	}

	/// <summary>
	/// Applies a navigation location to the text area and reveals the target line.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When <paramref name="focus"/> is <see langword="true"/>, the text area is focused before the location is applied.
	/// </para>
	/// <para>
	/// The supplied caret and selection offsets are clamped before they are applied, and every boundary is
	/// snapped out of the interior of a CRLF terminator (the position between <c>\r</c> and <c>\n</c>,
	/// where a later insertion would split the pair; the offset snaps to the start of the terminator).
	/// Offsets are not remapped between terminator styles, so a location captured against one style can
	/// shift when the target document uses another style; the snapping only keeps applied positions out
	/// of interior pair positions.
	/// </para>
	/// <para>
	/// The recorded caret is honored. Inside a non-collapsed selection it is preserved as recorded and
	/// only clamped to the nearest selection edge when it falls outside the selection. A collapsed
	/// selection collapses at the recorded caret, so a caret-only location is applied unchanged.
	/// </para>
	/// <para>
	/// Without a preferred line, the line of the applied selection start is scrolled into view, which is
	/// the caret line for a location that carries no selection.
	/// </para>
	/// <para>
	/// <see cref="NavigationLocation.FilePath"/> is not inspected, so a location that belongs to another
	/// document is applied to <paramref name="textArea"/> nonetheless. Callers that navigate across
	/// documents must activate the target document first.
	/// </para>
	/// <para>
	/// The target line is revealed through the owning editor, which applies its own centering and
	/// viewing-margin rules, so a target that the editor already considers visible may be left in place;
	/// a standalone text area falls back to the text view's own reveal primitive. Both paths leave the
	/// horizontal offset alone, and both need a laid-out view: revealing a line before the editor's
	/// template has been applied scrolls nothing.
	/// </para>
	/// </remarks>
	/// <param name="textArea">The text area to update.</param>
	/// <param name="location">The location to apply.</param>
	/// <param name="focus">
	/// <see langword="true"/> to focus the editor before applying the location; <see langword="false"/> to leave focus unchanged.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The text area has no document assigned.</exception>
	public static void ApplyLocation(this TextArea textArea, NavigationLocation location, bool focus = true)
	{
		ArgumentNullException.ThrowIfNull(textArea);

		TextDocument document = GetRequiredDocument(textArea);
		int documentLength = document.TextLength;
		int caretOffset = SnapOutOfCrLfInterior(document, Math.Max(0, Math.Min(location.CaretOffset, documentLength)));
		int selectionStart = SnapOutOfCrLfInterior(document, Math.Max(0, Math.Min(location.SelectionStart, documentLength)));
		int selectionLength = Math.Max(0, Math.Min(location.SelectionLength, documentLength - selectionStart));
		int selectionEnd = SnapOutOfCrLfInterior(document, selectionStart + selectionLength);

		// Snapping the end out of a CRLF interior can move it in front of the start; the selection
		// collapses in that case.
		selectionLength = Math.Max(0, selectionEnd - selectionStart);

		// A collapsed selection carries no range, so the recorded caret defines the position.
		// A non-collapsed selection is preserved and the recorded caret is clamped to its bounds.
		if (selectionLength == 0)
			selectionStart = caretOffset;

		selectionEnd = selectionStart + selectionLength;
		int safeCaretOffset = Math.Max(selectionStart, Math.Min(caretOffset, selectionEnd));

		if (focus)
			textArea.Focus();

		// Assigning the selection does not move the caret, but the selection must exist first: the caret
		// assignment must land inside it, and the editor validates that relationship asynchronously.
		textArea.Selection = Selection.Create(textArea, selectionStart, selectionStart + selectionLength);
		textArea.Caret.Offset = safeCaretOffset;
		ScrollToLine(textArea, GetPreferredDocumentLine(document, location, selectionStart));
	}

	/// <summary>
	/// Gets the text area's document, rejecting a text area that has none assigned.
	/// </summary>
	private static TextDocument GetRequiredDocument(TextArea textArea)
		=> textArea.Document
			?? throw new InvalidOperationException("The text area has no document assigned.");

	/// <summary>
	/// Moves an offset out of the interior of a CRLF terminator, the position between <c>\r</c> and
	/// <c>\n</c>, where a later insertion would split the pair. The offset snaps to the start of the
	/// terminator, which is the end of the preceding line.
	/// </summary>
	private static int SnapOutOfCrLfInterior(TextDocument document, int offset)
		=> offset > 0
			&& offset < document.TextLength
			&& document.GetCharAt(offset) == '\n'
			&& document.GetCharAt(offset - 1) == '\r'
				? offset - 1
				: offset;

	/// <summary>
	/// Scrolls the target line into view, deferring to the owning editor's scroll when the text
	/// area is part of one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Scroll positioning belongs to the editor: <c>TextEditor.ScrollToLine</c> carries the
	/// centering and viewing-margin rules, so an editor-owned text area scrolls exactly like the
	/// editor always has. A standalone text area has no editor to ask and falls back to the text
	/// view's own reveal primitive, which scrolls the minimum distance and never pans
	/// horizontally.
	/// </para>
	/// <para>
	/// Both paths leave the horizontal offset alone; revealing a column would use the editor's
	/// <c>ScrollTo(line, column, ...)</c> overload instead.
	/// </para>
	/// </remarks>
	private static void ScrollToLine(TextArea textArea, int line)
	{
		if (textArea.GetService(typeof(TextEditor)) is TextEditor editor)
		{
			editor.ScrollToLine(line);
			return;
		}

		TextDocument document = GetRequiredDocument(textArea);
		int safeLine = Math.Max(1, Math.Min(line, document.LineCount));
		TextView textView = textArea.TextView;
		VisualLine visualLine = textView.GetOrConstructVisualLine(document.GetLineByNumber(safeLine));
		Vector offset = textView.ScrollOffset;

		// The rect stays inside the current horizontal window, so only the vertical offset moves.
		textView.MakeVisible(new Rect(offset.X + 1, visualLine.VisualTop, 1, visualLine.Height));
	}

	private static int GetPreferredDocumentLine(
		TextDocument document,
		NavigationLocation location,
		int selectionStart)
	{
		if (location.PreferredDocumentLine is int preferredLine)
			return Math.Max(1, Math.Min(preferredLine, document.LineCount));

		return document.GetLineByOffset(selectionStart).LineNumber;
	}

	private static int GetOffset(TextArea textArea, TextLocation location)
	{
		// The line is clamped because the document rejects out-of-range line numbers, while
		// TextDocument.GetOffset clamps the column to the line's bounds itself.
		TextDocument document = GetRequiredDocument(textArea);
		int safeLineNumber = Math.Max(1, Math.Min(location.Line, document.LineCount));

		return document.GetOffset(safeLineNumber, location.Column);
	}
}
