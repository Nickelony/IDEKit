using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Text;
using PointOrigin = Avalonia.Visual;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Text;
using System.Windows;
using PointOrigin = System.Windows.UIElement;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Navigation;

#else

namespace Nickelony.IDEKit.AvalonEdit.Navigation;

#endif

/// <summary>Provides helpers that resolve document positions from offsets and points, and that create and apply navigation locations.</summary>
/// <remarks>
/// The helpers read and update the text area's document, which is thread-affine, so they must be called on the text area's thread.
/// They extend <see cref="TextArea"/>, which carries the caret, selection, and document state; a host with a <see cref="TextEditor"/>
/// passes <c>editor.TextArea</c>.
/// </remarks>
public static class TextAreaExtensions
{
	/// <summary>Returns the word at a document offset, where a word is a run of identifier characters.</summary>
	/// <param name="textArea">The text area to inspect.</param>
	/// <param name="offset">The zero-based document offset to inspect. Values outside the document are clamped.</param>
	/// <param name="policy">
	/// The identifier-character policy that decides which characters form a word. Defaults to <see cref="IdentifierCharacterPolicy.Default"/>,
	/// which treats letters, digits, and underscores as word characters.
	/// </param>
	/// <returns>
	/// The word that <paramref name="offset"/> points at, or <see langword="null"/> when the offset is on a character that cannot be part of a word.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The text area has no document assigned.</exception>
	/// <remarks>
	/// <para>
	/// The word is the span that <see cref="IdentifierOperations.FindTokenSpan"/> resolves in <see cref="IdentifierSpanMode.AtOffset"/> mode,
	/// so the result is <see langword="null"/> when the probed character cannot be part of a word. The preceding-token preference
	/// of <see cref="IdentifierSpanMode.Containing"/> does not apply: callers that need the preceding word when the probe sits on a separator call
	/// the Core helper in that mode.
	/// </para>
	/// <para>
	/// An offset at the end of the document belongs to the word that ends there, unless the document ends with a line terminator (the probe then sits
	/// on the terminator and yields <see langword="null"/>). Because only the policy's <see cref="IdentifierCharacterPolicy.IsPartCharacter(char)"/>
	/// rule is consulted, a run that starts with a digit is returned even though it could not start an identifier under most language rules.
	/// </para>
	/// </remarks>
	public static string? GetWordFromOffset(this TextArea textArea, int offset, IdentifierCharacterPolicy? policy = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);

		var snapshot = new TextDocumentSnapshot(GetRequiredDocument(textArea));

		TextRange? word = IdentifierOperations.FindTokenSpan(
			snapshot,
			offset,
			policy: policy ?? IdentifierCharacterPolicy.Default,
			mode: IdentifierSpanMode.AtOffset
		);

		return word is { } span ? snapshot.GetText(span.Offset, span.Length) : null;
	}

	/// <summary>Tries to map a point in the text area to a zero-based document offset.</summary>
	/// <param name="textArea">The text area to inspect.</param>
	/// <param name="point">The point in the text area's coordinate space to map.</param>
	/// <param name="offset">The mapped document offset when the method returns <see langword="true"/>; otherwise, <c>0</c>.</param>
	/// <returns>
	/// <see langword="true"/> when the point maps to the document; otherwise, <see langword="false"/>. A text area without a document, or whose text
	/// view was never loaded, cannot map the point and yields <see langword="false"/> as well.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The point is translated into the text view and offset by the view's scroll position, so the mapping follows what the view renders.
	/// The column is clamped to the line's bounds, so a point past the end of a line maps to that line's end, and a point outside the rendered
	/// document yields <see langword="false"/>. A loaded view whose visual lines a redraw cleared rebuilds them for the mapping.
	/// </remarks>
	public static bool TryGetOffsetFromPoint(this TextArea textArea, Point point, out int offset)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		return TryMapPointToOffset(textArea, origin: textArea, point, overTextOnly: false, out offset);
	}

	/// <summary>Tries to map a point over the text of a line in the text area to the zero-based document offset under it.</summary>
	/// <param name="textArea">The text area to inspect.</param>
	/// <param name="point">The point in the text area's coordinate space to map.</param>
	/// <param name="offset">The mapped document offset when the method returns <see langword="true"/>; otherwise, <c>0</c>.</param>
	/// <returns>
	/// <see langword="true"/> when the point lies over the text of a line; otherwise, <see langword="false"/>, which includes the blank space
	/// past the end of a row, a margin left of the text, and the failures of <see cref="TryGetOffsetFromPoint"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// Pointer gestures that act on a symbol, such as a hover or a Ctrl+click, use it instead of <see cref="TryGetOffsetFromPoint"/>,
	/// whose clamping places a caret where a click expects it but would pick the last word of a line from the empty space after it. A wrapped line
	/// ends at the width of each of its rows.
	/// </remarks>
	public static bool TryGetTextOffsetFromPoint(this TextArea textArea, Point point, out int offset)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		return TryMapPointToOffset(textArea, origin: textArea, point, overTextOnly: true, out offset);
	}

	/// <summary>Tries to map a point in the coordinate space of <paramref name="origin"/> to a zero-based document offset.</summary>
	/// <param name="textArea">The text area whose text view resolves the point.</param>
	/// <param name="origin">
	/// The element whose coordinate space <paramref name="point"/> is in; the text area itself or an element that contains it.
	/// </param>
	/// <param name="point">The point to map.</param>
	/// <param name="overTextOnly">Whether a point in the blank space past the end of a row fails instead of mapping to the row's end.</param>
	/// <param name="offset">The mapped document offset when the method returns <see langword="true"/>; otherwise, <c>0</c>.</param>
	/// <returns>
	/// <see langword="true"/> when the point maps to the document; otherwise, <see langword="false"/>.
	/// The guards of <see cref="TryGetOffsetFromPoint"/> apply, so a text area without a document, or whose text view was never loaded,
	/// yields <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// The text area and the editor entry points share this body, so both report the same failures for the same view state.
	/// </remarks>
	internal static bool TryMapPointToOffset(TextArea textArea, PointOrigin origin, Point point, bool overTextOnly, out int offset)
	{
		offset = 0;

		TextDocument? document = textArea.Document;
		TextView textView = textArea.TextView;

		if (document is null)
			return false;

		if (!textView.VisualLinesValid)
		{
			// A redraw, such as a semantic-token update, clears the visual lines until the next layout pass, and a hover result can complete in
			// between, so a loaded view rebuilds them on demand. A view that was never loaded has nothing to map against.
			if (!textView.IsLoaded)
				return false;

			textView.EnsureVisualLines();
		}

#if AVALONIAEDIT
		if (origin.TranslatePoint(point, textView) is not { } viewPoint)
			return false;
#else
		Point viewPoint = origin.TranslatePoint(point, textView);
#endif

		Point visualPoint = viewPoint + textView.ScrollOffset;
		TextViewPosition? position = textView.GetPosition(visualPoint);

		if (position is null)
			return false;

		// A wrapped line has a row per wrap, and the point lies over text only up to the width of the row it is in. A point left of the
		// text view lies over a margin, such as the line numbers, which the view maps to the start of the line.
		if (
			overTextOnly
			&& (
				viewPoint.X < 0
				|| textView.GetVisualLineFromVisualTop(visualPoint.Y) is not { } visualLine
				|| visualPoint.X > visualLine.GetTextLineByVisualYPosition(visualPoint.Y).WidthIncludingTrailingWhitespace
			)
		)
		{
			return false;
		}

		// The document clamps the column to the line's bounds, so the offset is always within the document.
		offset = document.GetOffset(position.Value.Line, position.Value.Column);
		return true;
	}

	/// <summary>Creates a zero-length location at a one-based editor position in the text area's document.</summary>
	/// <param name="textArea">The text area whose document provides the offset mapping.</param>
	/// <param name="documentId">
	/// The identifier of the target document, or <see langword="null"/> for a document that has none (for example an unsaved document).
	/// </param>
	/// <param name="position">The one-based editor position to navigate to.</param>
	/// <returns>The created location.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The text area has no document assigned.</exception>
	/// <remarks>
	/// <para>
	/// The position is clamped when the offset is calculated: a column beyond the line end maps to the position before that line's terminator.
	/// </para>
	/// <para>
	/// The original line number is stored as the preferred scroll line and clamped to the document's line range (at least 1) when the location
	/// is applied.
	/// </para>
	/// </remarks>
	public static NavigationLocation CreateCaretLocation(this TextArea textArea, string? documentId, TextLocation position)
	{
		ArgumentNullException.ThrowIfNull(textArea);

		int offset = GetOffset(textArea, position);

		return new NavigationLocation(documentId, CaretOffset: offset, Selection: new TextRange(offset, 0), PreferredDocumentLine: position.Line);
	}

	/// <summary>Creates a location that selects a one-based editor position range.</summary>
	/// <param name="textArea">The text area whose document provides the offset mapping.</param>
	/// <param name="documentId">
	/// The identifier of the target document, or <see langword="null"/> for a document that has none (for example an unsaved document).
	/// </param>
	/// <param name="start">The one-based start position.</param>
	/// <param name="end">The one-based end position.</param>
	/// <returns>The created location.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The text area has no document assigned.</exception>
	/// <remarks>
	/// <para>
	/// Both endpoints are clamped to the document: a column beyond a line's end maps to the position before that line's terminator. If the end
	/// offset precedes the start offset, the selection is empty and collapses at the start position; the caret and the scroll position always follow
	/// the start position.
	/// </para>
	/// <para>
	/// The original start line number is stored as the preferred scroll line and clamped to the document's line range (at least 1) when the location
	/// is applied.
	/// </para>
	/// </remarks>
	public static NavigationLocation CreateRangeLocation(this TextArea textArea, string? documentId, TextLocation start, TextLocation end)
	{
		ArgumentNullException.ThrowIfNull(textArea);

		int startOffset = GetOffset(textArea, start);
		int endOffset = GetOffset(textArea, end);
		int selectionLength = Math.Max(0, endOffset - startOffset);

		return new NavigationLocation(
			documentId,
			CaretOffset: startOffset,
			Selection: new TextRange(startOffset, selectionLength),
			PreferredDocumentLine: start.Line
		);
	}

	/// <summary>Applies a navigation location to the text area and reveals the target line.</summary>
	/// <param name="textArea">The text area to update.</param>
	/// <param name="location">The location to apply.</param>
	/// <param name="focus">
	/// <see cref="EditorFocusMode.Focus"/> to focus the editor before applying the location; <see cref="EditorFocusMode.LeaveUnchanged"/> to leave
	/// focus unchanged.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The text area has no document assigned.</exception>
	/// <remarks>
	/// <para>
	/// The supplied caret and selection offsets are clamped before they are applied, and every boundary is snapped out of the interior of a CRLF
	/// terminator (the position between <c>\r</c> and <c>\n</c>, where a later insertion would split the pair; the offset snaps to the start
	/// of the terminator). Offsets are not remapped between terminator styles, so a location captured against one style can shift when the target
	/// document uses another style; the snapping only keeps applied positions out of interior pair positions.
	/// </para>
	/// <para>
	/// The recorded caret is honored. Inside a non-collapsed selection it is preserved as recorded and only clamped to the nearest selection edge
	/// when it falls outside the selection. A collapsed selection collapses at the recorded caret, so a caret-only location is applied unchanged.
	/// </para>
	/// <para>
	/// Without a preferred line, the line of the applied selection start is scrolled into view, which is the caret line for a location that carries
	/// no selection.
	/// </para>
	/// <para>
	/// <see cref="NavigationLocation.DocumentId"/> is not inspected, so a location that belongs to another document is applied
	/// to <paramref name="textArea"/> nonetheless. Callers that navigate across documents must activate the target document first.
	/// </para>
	/// <para>
	/// The target line is revealed through the owning editor's <see cref="TextEditor.ScrollToLine(int)"/>; a standalone text area
	/// falls back to the text view's own reveal primitive. The reveal leaves the horizontal offset alone and needs a laid-out view: revealing a line
	/// before the editor's template has been applied scrolls nothing.
	/// </para>
	/// </remarks>
	public static void ApplyLocation(this TextArea textArea, NavigationLocation location, EditorFocusMode focus = EditorFocusMode.Focus)
	{
		ArgumentNullException.ThrowIfNull(textArea);

		TextDocument document = GetRequiredDocument(textArea);

		NavigationLocationPlan plan = NavigationLocationPlanner.Plan(new TextDocumentCharSource(document), document.LineCount, location);

		if (focus == EditorFocusMode.Focus)
			textArea.Focus();

		// Assigning the selection does not move the caret, but the selection must exist first: the caret
		// assignment must land inside it, and the editor validates that relationship asynchronously.
		textArea.Selection = Selection.Create(textArea, plan.Selection.Offset, plan.Selection.EndOffset);
		textArea.Caret.Offset = plan.CaretOffset;
		ScrollToLine(textArea, plan.ScrollLine ?? document.GetLineByOffset(plan.Selection.Offset).LineNumber);
	}

	/// <summary>Returns the text area's document, rejecting a text area that has none assigned.</summary>
	private static TextDocument GetRequiredDocument(TextArea textArea) =>
		textArea.Document ?? throw new InvalidOperationException("The text area has no document assigned.");

	/// <summary>Scrolls the target line into view, deferring to the owning editor's scroll when the text area is part of one.</summary>
	/// <remarks>
	/// <para>
	/// Scroll positioning belongs to the editor: <c>TextEditor.ScrollToLine</c> carries the centering and viewing-margin rules,
	/// so an editor-owned text area scrolls exactly as the editor does. A standalone text area has no editor to ask and falls back to the text view's
	/// own reveal primitive, which scrolls the minimum distance and never pans horizontally.
	/// </para>
	/// <para>
	/// The reveal leaves the horizontal offset alone; revealing a column would use the editor's <c>ScrollTo(line, column, ...)</c> overload instead.
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
		textView.MakeVisible(new Rect(x: offset.X + 1, y: visualLine.VisualTop, width: 1, height: visualLine.Height));
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
