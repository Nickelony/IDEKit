using System.Text;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the editor-neutral line-comment planner over text snapshots: selection expansion, the
/// toggle decision, the line transforms, and terminator preservation.
/// </summary>
[TestClass]
public sealed class TextLineCommentPlannerTests
{
	private static readonly CommentSyntax s_doubleSlashSyntax = new("//", null, StringLiteralStyle.None);

	[TestMethod]
	public void TryCreateEdit_ToggleOnUncommentedSelection_CommentsEachLine()
	{
		const string text = "first\r\n  second";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		Assert.AreEqual("// first\r\n  // second", result);
	}

	[TestMethod]
	public void TryCreateEdit_ToggleOnCommentedSelection_UncommentsEachLine()
	{
		const string text = "//first\r\n  //second";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		Assert.AreEqual("first\r\n  second", result);
	}

	[TestMethod]
	public void TryCreateEdit_Comment_PreservesWhitespaceOnlyLines()
	{
		const string text = "first\r\n   \r\nthird";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Comment);

		Assert.AreEqual("// first\r\n   \r\n// third", result);
	}

	[TestMethod]
	public void TryCreateEdit_Uncomment_LeavesLinesWithoutPrefixUntouched()
	{
		const string text = "//first\r\nsecond";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Uncomment);

		Assert.AreEqual("first\r\nsecond", result);
	}

	[TestMethod]
	public void TryCreateEdit_NoLineCommentDelimiter_ReturnsFalse()
	{
		var snapshot = new StringTextSnapshot("first");
		var syntaxWithoutLineComments = new CommentSyntax(null, null, StringLiteralStyle.None);

		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(0, snapshot.TextLength),
				syntaxWithoutLineComments,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsFalse(success);
		Assert.AreEqual(default(TextLineCommentEdit), edit);
	}

	[TestMethod]
	public void TryCreateEdit_EmptyDocument_ReturnsFalse()
	{
		var snapshot = new StringTextSnapshot(string.Empty);

		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(0, 0),
				s_doubleSlashSyntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsFalse(success);
		Assert.AreEqual(default(TextLineCommentEdit), edit);
	}

	[TestMethod]
	public void TryCreateEdit_EditDescribesReplaceableRange()
	{
		const string text = "first\r\nsecond";
		var snapshot = new StringTextSnapshot(text);

		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(0, text.Length),
				s_doubleSlashSyntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);

		Assert.AreEqual(0, edit.ReplaceRange.Offset);
		Assert.AreEqual(text.Length, edit.ReplaceRange.Length);
		Assert.AreEqual("// first\r\n// second", edit.NewText);
		Assert.AreEqual(0, edit.Selection.Offset);
		Assert.AreEqual(edit.NewText.Length, edit.Selection.Length);
	}

	[TestMethod]
	public void TryCreateEdit_Comment_PreservesLfOnlyLineEndings()
	{
		const string text = "one\ntwo\nthree";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Comment);

		Assert.AreEqual("// one\n// two\n// three", result);
	}

	[TestMethod]
	public void TryCreateEdit_Comment_PreservesMixedLineEndings()
	{
		const string text = "one\r\ntwo\nthree";
		var snapshot = new StringTextSnapshot(text);

		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(0, text.Length),
				s_doubleSlashSyntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);
		Assert.AreEqual(text.Length, edit.ReplaceRange.Length);

		Assert.AreEqual("// one\r\n// two\n// three", ApplyToText(text, edit));
	}

	[TestMethod]
	public void TryCreateEdit_Comment_DoesNotAddTrailingNewlineToUnterminatedLastLine()
	{
		const string text = "one\r\ntwo";
		var snapshot = new StringTextSnapshot(text);

		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(0, text.Length),
				s_doubleSlashSyntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);
		Assert.IsFalse(edit.NewText.EndsWith('\n'));
		Assert.AreEqual(edit.NewText.Length, edit.Selection.Length);

		Assert.AreEqual("// one\r\n// two", ApplyToText(text, edit));
	}

	[TestMethod]
	public void TryCreateEdit_Comment_TerminatedLastLine_KeepsTrailingNewline()
	{
		const string text = "one\r\ntwo\r\n";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Comment);

		Assert.AreEqual("// one\r\n// two\r\n", result);
	}

	[TestMethod]
	public void TryCreateEdit_EditSelectionExcludesFinalDelimiter_WhenLastLineIsTerminated()
	{
		const string text = "head\r\nfirst\r\nsecond\r\ntail";
		var snapshot = new StringTextSnapshot(text);

		int selectionStart = snapshot.GetLineByNumber(2).Offset;
		int selectionLength = snapshot.GetLineByNumber(3).EndOffset - selectionStart;

		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(selectionStart, selectionLength),
				s_doubleSlashSyntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);
		Assert.AreEqual("// first\r\n// second\r\n", edit.NewText);
		Assert.AreEqual(selectionStart, edit.Selection.Offset);
		Assert.AreEqual(edit.NewText.Length - "\r\n".Length, edit.Selection.Length);
	}

	[TestMethod]
	public void TryCreateEdit_OutOfRangeSelection_IsClampedToTheDocument()
	{
		const string text = "first\r\nsecond";
		var snapshot = new StringTextSnapshot(text);

		bool created = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(text.Length + 10, 5),
				s_doubleSlashSyntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsTrue(created);

		ITextLine lastLine = snapshot.GetLineByNumber(2);

		// The out-of-range selection clamps to the last line, so only that line is commented.
		Assert.AreEqual(lastLine.Offset, edit.ReplaceRange.Offset);
		Assert.AreEqual(lastLine.Length, edit.ReplaceRange.Length);
		Assert.AreEqual("// second", edit.NewText);
	}

	[TestMethod]
	public void TryCreateEdit_SelectionEndingAtLineStart_DoesNotCommentThatLine()
	{
		const string text = "one\r\ntwo\r\nthree";
		var snapshot = new StringTextSnapshot(text);

		int selectionLength = snapshot.GetLineByNumber(2).Offset;

		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(0, selectionLength),
				s_doubleSlashSyntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);
		Assert.AreEqual("// one\r\n", edit.NewText);

		Assert.AreEqual("// one\r\ntwo\r\nthree", ApplyToText(text, edit));
	}

	[TestMethod]
	public void TryCreateEdit_SelectionEndingOnTheLfOfACrLfPair_StaysOnThePrecedingLine()
	{
		const string text = "one\r\ntwo\r\nthree";
		var snapshot = new StringTextSnapshot(text);

		// The selection end sits on the LF character of the first line's CRLF terminator, which
		// belongs to the preceding line, so the selection still covers the first line only.
		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(0, 4),
				s_doubleSlashSyntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);
		Assert.AreEqual("// one\r\n", edit.NewText);

		Assert.AreEqual("// one\r\ntwo\r\nthree", ApplyToText(text, edit));
	}

	[TestMethod]
	public void TryCreateEdit_CollapsedSelectionAtLineStart_CommentsThatLine()
	{
		const string text = "one\r\ntwo\r\nthree";
		var snapshot = new StringTextSnapshot(text);

		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(snapshot.GetLineByNumber(2).Offset, 0),
				s_doubleSlashSyntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);

		Assert.AreEqual("one\r\n// two\r\nthree", ApplyToText(text, edit));
	}

	[TestMethod]
	public void TryCreateEdit_SelectionStartingMidLine_ExpandsToWholeLines()
	{
		const string text = "one\r\ntwo\r\nthree";
		var snapshot = new StringTextSnapshot(text);

		// The selection starts inside the second line and ends inside the third; both whole lines are
		// transformed because a selection expands to the lines it touches.
		int selectionStart = snapshot.GetLineByNumber(2).Offset + 1;
		int selectionEnd = snapshot.GetLineByNumber(3).Offset + 2;

		string result = ApplyEdit(
			text,
			new TextRange(selectionStart, selectionEnd - selectionStart),
			s_doubleSlashSyntax,
			TextLineCommentAction.Comment);

		Assert.AreEqual("one\r\n// two\r\n// three", result);
	}

	[TestMethod]
	public void TryCreateEdit_ToggleOnMixedCommentedAndUncommentedLines_CommentsEveryLine()
	{
		const string text = "//first\r\nsecond\r\n//third";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		// Not every non-whitespace line is commented, so the toggle comments every line.
		Assert.AreEqual("// //first\r\n// second\r\n// //third", result);
	}

	[TestMethod]
	public void TryCreateEdit_ToggleOnBlankAndCommentedLines_Uncomments()
	{
		const string text = "//first\r\n\r\n  \r\n//second";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		// Blank lines are ignored by the toggle decision and stay unchanged.
		Assert.AreEqual("first\r\n\r\n  \r\nsecond", result);
	}

	[TestMethod]
	public void TryCreateEdit_ToggleOnWhitespaceOnlySelection_ProducesNoChange()
	{
		const string text = "   \r\n\t";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		// Whitespace-only lines are left unchanged, so the replacement matches the original text.
		Assert.AreEqual(text, result);
	}

	[TestMethod]
	public void TryCreateEdit_Comment_PreservesTabIndentation()
	{
		const string text = "\tfirst";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Comment);

		Assert.AreEqual("\t// first", result);
	}

	[TestMethod]
	public void TryCreateEdit_Uncomment_RemovesOnlyTheFirstDelimiter()
	{
		const string text = "////first";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Uncomment);

		Assert.AreEqual("//first", result);
	}

	[TestMethod]
	public void TryCreateEdit_DifferentSyntaxes_UsesTheCallersSyntax()
	{
		// The planner is stateless; switching the syntax argument must still apply the delimiter
		// supplied by the caller.
		var dashSyntax = new CommentSyntax("--", null, StringLiteralStyle.None);

		string first = ApplyEdit("//first", new TextRange(0, 7), s_doubleSlashSyntax, TextLineCommentAction.Toggle);
		string second = ApplyEdit("--second", new TextRange(0, 8), dashSyntax, TextLineCommentAction.Toggle);
		string third = ApplyEdit("//third", new TextRange(0, 7), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		Assert.AreEqual("first", first);
		Assert.AreEqual("second", second);
		Assert.AreEqual("third", third);
	}

	[TestMethod]
	public void TryCreateEdit_ToggleOnLineWithUnusualWhitespaceBeforeDelimiter_CommentsInsteadOfDeadEnding()
	{
		// A non-breaking space is content, not indentation, so the line does not start with the
		// delimiter under the transform's rule; the toggle comments instead of dead-ending, and
		// repeating the toggle round-trips.
		const string text = " \u00A0// x";

		string commented = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		Assert.AreEqual(" // \u00A0// x", commented);

		string restored = ApplyEdit(commented, new TextRange(0, commented.Length), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		Assert.AreEqual(text, restored);
	}

	[TestMethod]
	public void TryCreateEdit_ToggleOnTabIndentedComment_Uncomments()
	{
		const string text = " \t// x";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		Assert.AreEqual(" \tx", result);
	}

	[TestMethod]
	public void TryCreateEdit_ToggleOnFormFeedPrefixedLine_CommentsInsteadOfDeadEnding()
	{
		const string text = "\f// x";

		string result = ApplyEdit(text, new TextRange(0, text.Length), s_doubleSlashSyntax, TextLineCommentAction.Toggle);

		Assert.AreEqual("// \f// x", result);
	}

	[TestMethod]
	public void TryCreateEdit_WhitespaceOnlyDelimiter_ReturnsFalse()
	{
		// The syntax constructor normalizes a whitespace-only delimiter to null, so the planner
		// declines instead of treating every space as a comment opener.
		var syntax = new CommentSyntax(" ", null, StringLiteralStyle.None);
		var snapshot = new StringTextSnapshot("a");

		bool created = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(0, snapshot.TextLength),
				syntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: true),
			out _);

		Assert.IsFalse(created);
	}

	/// <summary>
	/// Runs the planner over a snapshot of the supplied text and applies the created edit to the
	/// original text, so tests can assert the transformed document text directly.
	/// </summary>
	/// <param name="text">The source text.</param>
	/// <param name="selection">The selection to transform.</param>
	/// <param name="commentSyntax">The comment syntax to apply.</param>
	/// <param name="action">The transformation to apply.</param>
	/// <returns>The transformed text.</returns>
	private static string ApplyEdit(
		string text,
		TextRange selection,
		CommentSyntax commentSyntax,
		TextLineCommentAction action)
	{
		var snapshot = new StringTextSnapshot(text);

		bool success = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				selection,
				commentSyntax,
				action,
				InsertSpaceAfterDelimiter: true),
			out TextLineCommentEdit edit);

		Assert.IsTrue(success);

		return ApplyToText(text, edit);
	}

	/// <summary>
	/// Splices an edit's replacement text into the original text at its replace range.
	/// </summary>
	/// <param name="text">The original text.</param>
	/// <param name="edit">The edit to apply.</param>
	/// <returns>The transformed text.</returns>
	private static string ApplyToText(string text, TextLineCommentEdit edit)
	{
		var builder = new StringBuilder(text.Length - edit.ReplaceRange.Length + edit.NewText.Length);

		builder.Append(text, 0, edit.ReplaceRange.Offset);
		builder.Append(edit.NewText);
		builder.Append(text, edit.ReplaceRange.EndOffset, text.Length - edit.ReplaceRange.EndOffset);

		return builder.ToString();
	}
}
