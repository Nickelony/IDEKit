using Nickelony.IDEKit.Core.Indentation;
using Nickelony.IDEKit.Core.Text;
using System.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>
/// Creates editor-neutral line-comment edits for the lines touched by a selection: commenting,
/// uncommenting, or toggling them.
/// </summary>
/// <remarks>
/// <para>
/// The planner is the editor-neutral core of line-comment editing and is stateless: an editor
/// binding captures its document into an <see cref="ITextSnapshot"/>, calls the planner, and applies
/// the returned <see cref="TextLineCommentEdit"/> as a document edit, restoring the recorded
/// selection. The edit computation (selection expansion, toggle decision, line transforms, and
/// terminator preservation) is normative here; a binding that applies edits directly (for example by
/// routing them through a host-owned edit target) keeps the application concerns - the no-op skip and
/// the selection restore - outside the planner.
/// </para>
/// <para>
/// The transformation preserves each line's leading whitespace, leaves whitespace-only lines
/// unchanged, and keeps each line's original terminator, so a final unterminated line is not given a
/// new one.
/// </para>
/// <para>
/// Unlike the keystroke-rate resolvers, this planner reads snapshot line metadata, which materializes
/// a snapshot implementation's line table on first access. The cost is accepted here: comment
/// toggling is a user-rate command, not a keystroke-rate path.
/// </para>
/// </remarks>
public static class TextLineCommentPlanner
{
	/// <summary>
	/// Tries to create an edit for the requested line-comment transformation on the selected lines.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The transformation covers every line the selection touches. A non-collapsed selection that ends
	/// exactly at the start of a line does not include that line, while a collapsed selection at the
	/// start of a line does include it. Offsets outside the snapshot are clamped to its bounds.
	/// </para>
	/// <para>
	/// The returned edit can be a no-op when the transformation leaves the selected lines unchanged
	/// (for example, uncommenting lines that carry no delimiter); a caller that applies edits directly
	/// should compare the replacement text with the replaced range and skip identical text, so the
	/// apply does not add a pointless undo entry.
	/// </para>
	/// </remarks>
	/// <param name="request">The transformation inputs.</param>
	/// <param name="edit">
	/// The created edit when the method returns <see langword="true"/>; otherwise, <see langword="default"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the snapshot contains text and
	/// <see cref="TextLineCommentRequest.Syntax"/> provides a non-blank line-comment delimiter;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <see cref="TextLineCommentRequest.Snapshot"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryCreateEdit(in TextLineCommentRequest request, out TextLineCommentEdit edit)
	{
		ITextSnapshot snapshot = request.Snapshot;

		ArgumentNullException.ThrowIfNull(snapshot);

		edit = default;

		string? commentPrefix = request.Syntax.LineCommentDelimiter;

		if (string.IsNullOrWhiteSpace(commentPrefix) || snapshot.TextLength == 0)
			return false;

		int safeSelectionStart = Math.Clamp(request.Selection.Offset, 0, snapshot.TextLength);
		int safeSelectionEnd = Math.Max(safeSelectionStart, Math.Clamp(request.Selection.EndOffset, 0, snapshot.TextLength));

		ITextLine startLine = snapshot.GetLineByOffset(safeSelectionStart);
		ITextLine endLine = snapshot.GetLineByOffset(safeSelectionEnd);

		// A non-collapsed selection that ends at the start of a line does not include that line.
		if (endLine.LineNumber > startLine.LineNumber && endLine.Offset == safeSelectionEnd)
			endLine = snapshot.GetLineByNumber(endLine.LineNumber - 1);

		int lineCount = endLine.LineNumber - startLine.LineNumber + 1;
		var lines = new List<ITextLine>(lineCount);
		var lineTexts = new List<string>(lineCount);

		// The selected lines are materialized once and shared by the toggle decision and the
		// transform, so each line's text is read from the snapshot a single time.
		for (int lineNumber = startLine.LineNumber; lineNumber <= endLine.LineNumber; lineNumber++)
		{
			ITextLine line = snapshot.GetLineByNumber(lineNumber);

			lines.Add(line);
			lineTexts.Add(snapshot.GetText(line.Offset, line.Length));
		}

		TextLineCommentAction effectiveAction = request.Action;

		if (request.Action == TextLineCommentAction.Toggle)
		{
			effectiveAction = ShouldUncommentSelectedLines(lineTexts, commentPrefix)
				? TextLineCommentAction.Uncomment
				: TextLineCommentAction.Comment;
		}

		var builder = new StringBuilder();
		int totalLineLength = 0;
		int lastLineTerminatorLength = 0;

		for (int index = 0; index < lines.Count; index++)
		{
			ITextLine currentLine = lines[index];

			builder.Append(TransformLine(lineTexts[index], commentPrefix, effectiveAction, request.InsertSpaceAfterDelimiter));

			// Preserve the line's original terminator (CRLF, LF, or none for a final unterminated
			// line).
			int terminatorLength = LineTerminators.GetTerminatorLength(snapshot, currentLine.EndOffset);

			builder.Append(snapshot.GetText(currentLine.EndOffset, terminatorLength));

			totalLineLength += currentLine.Length + terminatorLength;
			lastLineTerminatorLength = terminatorLength;
		}

		string newText = builder.ToString();

		// The post-edit selection covers the transformed content but not the final line's terminator.
		edit = new TextLineCommentEdit(
			new TextRange(startLine.Offset, totalLineLength),
			newText,
			new TextRange(startLine.Offset, Math.Max(0, newText.Length - lastLineTerminatorLength)));

		return true;
	}

	/// <summary>
	/// Determines whether a toggle should uncomment the selected lines: at least one non-blank line
	/// must be present, and every non-blank line must start with the line-comment delimiter after its
	/// leading indentation.
	/// </summary>
	/// <remarks>
	/// The decision uses exactly the indentation rule of the uncomment transform (spaces and tabs),
	/// so every line it counts as commented is a line the transform can uncomment; a mismatch would
	/// make the toggle a permanent no-op on the affected lines.
	/// </remarks>
	/// <param name="lineTexts">The selected lines' texts without their terminators, in line order.</param>
	/// <param name="commentPrefix">The line-comment delimiter.</param>
	/// <returns><see langword="true"/> when the toggle uncomments rather than comments.</returns>
	private static bool ShouldUncommentSelectedLines(List<string> lineTexts, string commentPrefix)
	{
		bool foundNonWhitespaceLine = false;

		for (int index = 0; index < lineTexts.Count; index++)
		{
			string currentLineText = lineTexts[index];

			// Lines that are only spaces and tabs are ignored; at least one non-indentation-only line
			// must be present for the toggle to switch to uncommenting. The space/tab alphabet matches
			// the transforms and CommentOperations.IsBlankOrStartsWithLineComment, so a line whose only
			// whitespace is some other character is content.
			if (IndentationOperations.GetLeadingWhitespaceLength(currentLineText) == currentLineText.Length)
				continue;

			foundNonWhitespaceLine = true;

			if (!StartsWithLineComment(currentLineText, commentPrefix))
				return false;
		}

		return foundNonWhitespaceLine;
	}

	/// <summary>
	/// Determines whether the line carries the line-comment delimiter after its leading indentation,
	/// under exactly the indentation rule of the transforms.
	/// </summary>
	/// <param name="currentLineText">The line text without its terminator.</param>
	/// <param name="commentPrefix">The line-comment delimiter.</param>
	/// <returns><see langword="true"/> when the delimiter follows the leading indentation.</returns>
	private static bool StartsWithLineComment(string currentLineText, string commentPrefix)
	{
		int indentationLength = IndentationOperations.GetLeadingWhitespaceLength(currentLineText);
		return currentLineText.AsSpan(indentationLength).StartsWith(commentPrefix, StringComparison.Ordinal);
	}

	/// <summary>
	/// Applies the requested transformation to one line of text.
	/// </summary>
	/// <param name="currentLineText">The line text without its terminator.</param>
	/// <param name="commentPrefix">The line-comment delimiter.</param>
	/// <param name="action">The effective transformation to apply.</param>
	/// <param name="insertSpaceAfterDelimiter">Whether a commented line follows its delimiter with a single space.</param>
	/// <returns>The transformed line text.</returns>
	private static string TransformLine(
		string currentLineText,
		string commentPrefix,
		TextLineCommentAction action,
		bool insertSpaceAfterDelimiter)
	{
		return action == TextLineCommentAction.Uncomment
			? UncommentLine(currentLineText, commentPrefix)
			: CommentLine(currentLineText, commentPrefix, insertSpaceAfterDelimiter);
	}

	/// <summary>
	/// Adds the comment delimiter after the line's leading whitespace, followed by a single space
	/// when requested (the convention of mainstream desktop editors). Whitespace that is not a space
	/// or tab (for example a non-breaking space) is content, so the delimiter is inserted before it.
	/// </summary>
	/// <param name="currentLineText">The line text without its terminator.</param>
	/// <param name="commentPrefix">The line-comment delimiter.</param>
	/// <param name="insertSpaceAfterDelimiter">Whether the delimiter is followed by a single space.</param>
	/// <returns>The commented line text.</returns>
	private static string CommentLine(string currentLineText, string commentPrefix, bool insertSpaceAfterDelimiter)
	{
		// Only spaces and tabs are indentation; other whitespace (for example a non-breaking space)
		// is content. A line that is only spaces and tabs is left as it is.
		if (IndentationOperations.GetLeadingWhitespaceLength(currentLineText) == currentLineText.Length)
			return currentLineText;

		int indentationLength = IndentationOperations.GetLeadingWhitespaceLength(currentLineText);
		string commentedPrefix = insertSpaceAfterDelimiter ? commentPrefix + " " : commentPrefix;

		return currentLineText[..indentationLength] + commentedPrefix + currentLineText[indentationLength..];
	}

	/// <summary>
	/// Removes one leading comment delimiter, together with the single space that follows it when
	/// present, from a line that carries the delimiter after its leading whitespace. The space is
	/// always removed when present, independent of the insertion setting, so edits round-trip.
	/// </summary>
	/// <param name="currentLineText">The line text without its terminator.</param>
	/// <param name="commentPrefix">The line-comment delimiter.</param>
	/// <returns>The uncommented line text, or the original text when it has no leading delimiter.</returns>
	private static string UncommentLine(string currentLineText, string commentPrefix)
	{
		// Only spaces and tabs are indentation, so whitespace such as a non-breaking space counts as
		// content and keeps the line from starting with the comment marker.
		if (!StartsWithLineComment(currentLineText, commentPrefix))
			return currentLineText;

		int indentationLength = IndentationOperations.GetLeadingWhitespaceLength(currentLineText);
		int delimiterEnd = indentationLength + commentPrefix.Length;

		// The delimiter's own trailing space is removed with it, matching the convention of
		// mainstream desktop editors; a run of spaces loses exactly one space.
		if (delimiterEnd < currentLineText.Length && currentLineText[delimiterEnd] == ' ')
			delimiterEnd++;

		return currentLineText[..indentationLength] + currentLineText[delimiterEnd..];
	}
}
