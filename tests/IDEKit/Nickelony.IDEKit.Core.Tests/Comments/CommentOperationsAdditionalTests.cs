namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Covers the code-range stop rule, the space/tab alphabet of
/// <c>IsBlankOrStartsWithLineComment</c>, the multi-character continuation marker with its
/// whitespace flag, and the planner's configurable delimiter spacing.
/// </summary>
[TestClass]
public sealed class CommentOperationsAdditionalTests
{
	[TestMethod]
	public void GetCodeRange_CodeAfterTheFirstComment_StopsBeforeThatComment()
	{
		var syntax = new CommentSyntax(";", new BlockCommentSyntax("/*", "*/"), StringLiteralStyle.None);
		var snapshot = new StringTextSnapshot("code /* c */ more ; x");

		TextRange range = CommentOperations.GetCodeRange("code /* c */ more ; x", syntax);

		Assert.AreEqual(new TextRange(0, 5), range);
		Assert.AreEqual("code ", range.GetTextFrom(snapshot));
	}

	[TestMethod]
	public void IsBlankOrStartsWithLineComment_NonLayoutWhitespace_IsNeitherBlankNorComment()
	{
		var syntax = new CommentSyntax(";", null, StringLiteralStyle.None);

		// The unified alphabet is space/tab only, so a non-breaking space is content: the line is
		// neither blank nor a comment, matching the line-comment transforms.
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment("\u00A0", syntax));
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment("\u00A0; x", syntax));
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment("\u00A0x", syntax));
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MultiCharacterMarker_RequiresWhitespaceWhenRequested()
	{
		var syntax = new CommentSyntax("%", null, StringLiteralStyle.None);

		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker(
			"value = 1 + ...", syntax, "...", markerMustBePrecededByWhitespace: true));
		Assert.IsFalse(ContinuationOperations.EndsWithContinuationMarker(
			"value=1+...", syntax, "...", markerMustBePrecededByWhitespace: true));
	}

	[TestMethod]
	public void TryCreateEdit_WithoutInsertedSpace_InsertsAndRemovesTheBareDelimiter()
	{
		var syntax = new CommentSyntax("//", null, StringLiteralStyle.None);
		var snapshot = new StringTextSnapshot("value");

		bool created = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				snapshot,
				new TextRange(0, snapshot.TextLength),
				syntax,
				TextLineCommentAction.Comment,
				InsertSpaceAfterDelimiter: false),
			out TextLineCommentEdit edit);

		Assert.IsTrue(created);
		Assert.AreEqual("//value", edit.NewText);

		var commented = new StringTextSnapshot("// value");

		created = TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				commented,
				new TextRange(0, commented.TextLength),
				syntax,
				TextLineCommentAction.Uncomment,
				InsertSpaceAfterDelimiter: false),
			out TextLineCommentEdit uncommentEdit);

		Assert.IsTrue(created);
		Assert.AreEqual("value", uncommentEdit.NewText);
	}
}
