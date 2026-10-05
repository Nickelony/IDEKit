using System.Text;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Comments;

/// <summary>Creates editor-neutral line-comment edits for the lines touched by a selection: commenting, uncommenting, or toggling them.</summary>
/// <remarks>
/// The planner is the editor-neutral, stateless core of line-comment editing: a binding captures its document into a snapshot, calls the planner,
/// and applies the returned edit. The transformation preserves each line's leading whitespace, leaves whitespace-only lines unchanged,
/// and keeps each line's original terminator, so a final unterminated line gains none. Reading snapshot line metadata is a cost the planner accepts
/// because comment toggling is a user-rate command.
/// </remarks>
public static class LineCommentPlanner
{
	/// <summary>Tries to create an edit for the requested line-comment transformation on the selected lines.</summary>
	/// <param name="request">The transformation inputs.</param>
	/// <param name="edit">The created edit when the method returns <see langword="true"/>; otherwise, <see langword="default"/>.</param>
	/// <returns>
	/// <see langword="true"/> when the snapshot contains text and <see cref="LineCommentRequest.Syntax"/> provides
	/// a non-blank line-comment delimiter; otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><see cref="LineCommentRequest.Snapshot"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The transformation covers every line the selection touches: a non-collapsed selection that ends at the start of a line excludes that line,
	/// while a collapsed selection at the start of a line includes it, and offsets outside the snapshot are clamped. The returned edit
	/// can be a no-op, so a caller that applies edits directly should skip identical text to avoid a pointless undo entry.
	/// </remarks>
	public static bool TryCreateEdit(in LineCommentRequest request, out LineCommentEdit edit)
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

		LineCommentActionKind effectiveAction = request.Action;

		if (request.Action == LineCommentActionKind.Toggle)
		{
			effectiveAction = ShouldUncommentSelectedLines(lineTexts, commentPrefix)
				? LineCommentActionKind.Uncomment
				: LineCommentActionKind.Comment;
		}

		var builder = new StringBuilder();

		int totalLineLength = 0;
		int lastLineTerminatorLength = 0;

		for (int index = 0; index < lines.Count; index++)
		{
			ITextLine currentLine = lines[index];

			builder.Append(TransformLine(currentLineText: lineTexts[index], commentPrefix, action: effectiveAction, spacing: request.Spacing));

			// Preserve the line's original terminator (CRLF, LF, or none for a final unterminated
			// line).
			int terminatorLength = LineTerminators.GetTerminatorLength(snapshot, currentLine.EndOffset);

			builder.Append(snapshot.GetText(currentLine.EndOffset, terminatorLength));

			totalLineLength += currentLine.Length + terminatorLength;
			lastLineTerminatorLength = terminatorLength;
		}

		string newText = builder.ToString();

		// The post-edit selection covers the transformed content but not the final line's terminator.
		edit = new LineCommentEdit(
			new TextRange(startLine.Offset, totalLineLength),
			newText,
			new TextRange(startLine.Offset, Math.Max(0, newText.Length - lastLineTerminatorLength))
		);

		return true;
	}

	/// <summary>
	/// Determines whether a toggle should uncomment the selected lines: at least one non-blank line must be present, and every non-blank line
	/// must start with the line-comment delimiter after its leading indentation.
	/// </summary>
	/// <param name="lineTexts">The selected lines' texts without their terminators, in line order.</param>
	/// <param name="commentPrefix">The line-comment delimiter.</param>
	/// <returns><see langword="true"/> when the toggle uncomments rather than comments.</returns>
	/// <remarks>
	/// The decision uses exactly the indentation rule of the uncomment transform (spaces and tabs), so every line it counts as commented is a line
	/// the transform can uncomment; a mismatch would make the toggle a permanent no-op on the affected lines.
	/// </remarks>
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
			if (LineTextOperations.GetLeadingWhitespaceLength(currentLineText) == currentLineText.Length)
				continue;

			foundNonWhitespaceLine = true;

			if (!StartsWithLineComment(currentLineText, commentPrefix))
				return false;
		}

		return foundNonWhitespaceLine;
	}

	/// <summary>
	/// Determines whether the line carries the line-comment delimiter after its leading indentation, under exactly the indentation rule
	/// of the transforms.
	/// </summary>
	/// <param name="currentLineText">The line text without its terminator.</param>
	/// <param name="commentPrefix">The line-comment delimiter.</param>
	/// <returns><see langword="true"/> when the delimiter follows the leading indentation.</returns>
	private static bool StartsWithLineComment(string currentLineText, string commentPrefix)
	{
		int indentationLength = LineTextOperations.GetLeadingWhitespaceLength(currentLineText);
		return currentLineText.AsSpan(indentationLength).StartsWith(commentPrefix, StringComparison.Ordinal);
	}

	/// <summary>Applies the requested transformation to one line of text.</summary>
	/// <param name="currentLineText">The line text without its terminator.</param>
	/// <param name="commentPrefix">The line-comment delimiter.</param>
	/// <param name="action">The effective transformation to apply.</param>
	/// <param name="spacing">How a commented line follows its delimiter.</param>
	/// <returns>The transformed line text.</returns>
	private static string TransformLine(string currentLineText, string commentPrefix, LineCommentActionKind action, LineCommentSpacingStyle spacing)
	{
		return action == LineCommentActionKind.Uncomment
			? UncommentLine(currentLineText, commentPrefix)
			: CommentLine(currentLineText, commentPrefix, spacing);
	}

	/// <summary>
	/// Adds the comment delimiter after the line's leading whitespace, followed by a single space when requested; whitespace that is not a space
	/// or tab is content, so the delimiter is inserted before it.
	/// </summary>
	/// <param name="currentLineText">The line text without its terminator.</param>
	/// <param name="commentPrefix">The line-comment delimiter.</param>
	/// <param name="spacing">How the delimiter is followed.</param>
	/// <returns>The commented line text.</returns>
	private static string CommentLine(string currentLineText, string commentPrefix, LineCommentSpacingStyle spacing)
	{
		// Only spaces and tabs are indentation; other whitespace (for example a non-breaking space)
		// is content. A line that is only spaces and tabs is left as it is.
		if (LineTextOperations.GetLeadingWhitespaceLength(currentLineText) == currentLineText.Length)
			return currentLineText;

		int indentationLength = LineTextOperations.GetLeadingWhitespaceLength(currentLineText);
		string commentedPrefix = spacing == LineCommentSpacingStyle.SpaceAfterDelimiter ? commentPrefix + " " : commentPrefix;

		return currentLineText[..indentationLength] + commentedPrefix + currentLineText[indentationLength..];
	}

	/// <summary>
	/// Removes one leading comment delimiter and the single space that follows it when present; the space is removed regardless
	/// of the insertion setting, so edits round-trip.
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

		int indentationLength = LineTextOperations.GetLeadingWhitespaceLength(currentLineText);
		int delimiterEnd = indentationLength + commentPrefix.Length;

		// The delimiter's own trailing space is removed with it, matching the convention of
		// mainstream desktop editors; a run of spaces loses exactly one space.
		if (delimiterEnd < currentLineText.Length && currentLineText[delimiterEnd] == ' ')
			delimiterEnd++;

		return currentLineText[..indentationLength] + currentLineText[delimiterEnd..];
	}
}
