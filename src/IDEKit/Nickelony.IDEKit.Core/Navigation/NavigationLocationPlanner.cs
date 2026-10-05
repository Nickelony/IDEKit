using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>Plans how an editor applies a navigation location to a document without touching an editor.</summary>
/// <remarks>
/// <para>
/// The caret and selection offsets are clamped to the document, and every boundary is snapped out of the interior of a CRLF terminator,
/// the position between <c>\r</c> and <c>\n</c>, where a later insertion would split the pair. The offset snaps to the start of the terminator.
/// Offsets are not remapped between terminator styles, so a location captured against one style can shift when the target document uses
/// another style.
/// </para>
/// <para>
/// A collapsed selection collapses at the recorded caret. Inside a non-collapsed selection the recorded caret is preserved, and it is clamped
/// to the nearest selection edge when it falls outside the selection.
/// </para>
/// </remarks>
public static class NavigationLocationPlanner
{
	/// <summary>Plans the application of a location to a document.</summary>
	/// <typeparam name="TSource">The text source type; a struct adapter is read without boxing.</typeparam>
	/// <param name="text">The document text the location is applied to.</param>
	/// <param name="lineCount">The number of lines of the document, at least one.</param>
	/// <param name="location">The location to apply.</param>
	/// <returns>The selection, caret and line to reveal.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="lineCount"/> is less than one.</exception>
	public static NavigationLocationPlan Plan<TSource>(TSource text, int lineCount, NavigationLocation location)
		where TSource : ITextCharSource
	{
		// The null test is on the generic type, so a struct source is not boxed.
		if (text is null)
			throw new ArgumentNullException(nameof(text));

		ArgumentOutOfRangeException.ThrowIfLessThan(lineCount, 1);

		int documentLength = text.TextLength;
		int caretOffset = SnapOutOfCrLfInterior(text, Math.Max(0, Math.Min(location.CaretOffset, documentLength)));
		int selectionStart = SnapOutOfCrLfInterior(text, Math.Min(location.Selection.Offset, documentLength));
		int selectionLength = Math.Min(location.Selection.Length, documentLength - selectionStart);
		int selectionEnd = SnapOutOfCrLfInterior(text, selectionStart + selectionLength);

		// Snapping the end out of a CRLF interior can move it in front of the start; the selection
		// collapses in that case.
		selectionLength = Math.Max(0, selectionEnd - selectionStart);

		// A collapsed selection carries no range, so the recorded caret defines the position.
		if (selectionLength == 0)
			selectionStart = caretOffset;

		selectionEnd = selectionStart + selectionLength;

		int safeCaretOffset = Math.Max(selectionStart, Math.Min(caretOffset, selectionEnd));
		int? scrollLine = location.PreferredDocumentLine is int preferredLine ? Math.Max(1, Math.Min(preferredLine, lineCount)) : null;

		return new NavigationLocationPlan(new TextRange(selectionStart, selectionLength), safeCaretOffset, scrollLine);
	}

	private static int SnapOutOfCrLfInterior<TSource>(TSource text, int offset)
		where TSource : ITextCharSource
	{
		return offset > 0 && offset < text.TextLength && LineTerminators.IsCrLfPair(text.GetCharAt(offset - 1), text.GetCharAt(offset))
			? offset - 1
			: offset;
	}
}
