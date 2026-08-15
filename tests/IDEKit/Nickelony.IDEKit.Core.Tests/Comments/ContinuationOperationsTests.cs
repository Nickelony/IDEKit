namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class ContinuationOperationsTests
{
	private static readonly CommentSyntax s_semicolonSyntax = CommentSyntaxFixtures.SemicolonLine;
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxFixtures.CStyle;
	private static readonly CommentSyntax s_semicolonBlockSyntax = CommentSyntaxFixtures.SemicolonBlock;
	private const char ContinuationMarker = '>';

	[TestMethod]
	public void EndsWithContinuationMarker_WithMarker_ReturnsTrue()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("Legend= 42 >", s_semicolonSyntax, ContinuationMarker);

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_WithMarkerAndTrailingWhitespace_ReturnsTrue()
	{
		// Trailing whitespace does not invalidate the marker; the same holds for a multi-character
		// marker.
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker("Legend= 42 > ", s_semicolonSyntax, ContinuationMarker));
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker("value = 1 + ...   ", s_semicolonSyntax, "..."));
	}

	[TestMethod]
	public void EndsWithContinuationMarker_WithMarkerAndComment_ReturnsTrue()
	{
		// The marker remains after the comment is removed (single- and multi-character markers).
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker("Legend= 42 > ; comment", s_semicolonSyntax, ContinuationMarker));
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker("value = 1 + ... ; comment", s_semicolonSyntax, "..."));
	}

	[TestMethod]
	public void EndsWithContinuationMarker_WithMarkerWhitespaceAndComment_ReturnsTrue()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("Legend= 42 >   ; comment", s_semicolonSyntax, ContinuationMarker);

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_DuplicateMarker_ReturnsTrue()
	{
		// The final marker position determines the result, and a line still ends with a multi-character
		// marker when the extra characters do not break its suffix.
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker("Legend= 42 >>", s_semicolonSyntax, ContinuationMarker));
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker("value = 1 + ....", s_semicolonSyntax, "..."));
	}

	[TestMethod]
	public void EndsWithContinuationMarker_NoMarker_ReturnsFalse()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("Legend= 42", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MarkerInComment_ReturnsFalse()
	{
		// A marker inside a comment does not count.
		bool result = ContinuationOperations.EndsWithContinuationMarker("Legend= 42 ; > not a marker", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_EmptyString_ReturnsFalse()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker(string.Empty, s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_CommentOnlyLine_ReturnsFalse()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("; just a comment", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_WhitespaceOnlyLine_ReturnsFalse()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("   ", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MarkerNotAtEndOfCode_ReturnsFalse()
	{
		// A marker in the middle of the code does not count.
		bool result = ContinuationOperations.EndsWithContinuationMarker("Legend= > 42", s_semicolonSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_OnlyMarker_ReturnsTrue()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker(">", s_semicolonSyntax, ContinuationMarker);

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MultiCharMarker_ReturnsTrue()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + ...", s_semicolonSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_PartialMultiCharMarker_ReturnsFalse()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + ..", s_semicolonSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MultiCharMarkerLongerThanCode_ReturnsFalse()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("..", s_semicolonSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MultiCharMarkerInComment_ReturnsFalse()
	{
		// A marker inside a comment does not count.
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 ; ...", s_semicolonSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_EmptyMarker_ReturnsFalse()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + ...", s_semicolonSyntax, ReadOnlySpan<char>.Empty);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_TrailingMarker_ReturnsTrueForBothOverloads()
	{
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker("Legend= 42 >", s_semicolonSyntax, ContinuationMarker));
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker("Legend= 42 >", s_semicolonSyntax, ">"));
	}

	[TestMethod]
	public void EndsWithContinuationMarker_BlockCommentAfterMarker_ReturnsTrue()
	{
		// The marker remains after the comment is removed.
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + 2 ... /* c */", s_semicolonBlockSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_BlockCommentBeforeMarker_ReturnsTrue()
	{
		// A marker after the block comment counts.
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + 2 /* c */ ...", s_semicolonBlockSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_BlockAndLineComments_ReturnsTrue()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + 2 ... /* c */ // trailing", s_cStyleSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_BlockOpenerInsideLineComment_ReturnsTrue()
	{
		// Text inside a line comment does not affect the marker before it.
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + 2 ... // /* x */", s_cStyleSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_UnclosedBlockCommentAfterMarker_ReturnsTrue()
	{
		// An unclosed comment after the marker does not affect the result.
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + 2 ... /* c", s_semicolonBlockSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_SingleCharacterBlockCommentAfterMarker_ReturnsTrue()
	{
		// The one-character closer is delimiter text, so the marker scan must not look past it; a
		// misclassified closer would hide the marker and report no continuation.
		var syntax = new CommentSyntax(null, new BlockCommentSyntax("{", "}"), StringLiteralStyle.None);

		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + ... { note }", syntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_NoMarkerWithBlockComment_ReturnsFalse()
	{
		bool result = ContinuationOperations.EndsWithContinuationMarker("value = 1 + 2 /* c */", s_semicolonBlockSyntax, ContinuationMarker);

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_BlockMarkerInString_ReturnsFalse()
	{
		// The comment and marker inside the string do not count.
		bool result = ContinuationOperations.EndsWithContinuationMarker("\"value /* c */ ...\"", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MarkerInsideTripleQuotedString_ReturnsFalse()
	{
		// A marker inside the raw string does not count.
		bool result = ContinuationOperations.EndsWithContinuationMarker("\"\"\"a ...\"\"\"", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MarkerAfterTripleQuotedString_ReturnsTrue()
	{
		// A marker after the closed raw string counts.
		bool result = ContinuationOperations.EndsWithContinuationMarker("\"\"\"a\"\"\" ...", s_cStyleSyntax, "...");

		Assert.IsTrue(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_MarkerInsideTripleQuotedMultiline_ReturnsFalse()
	{
		// A marker inside the multi-line raw string does not count.
		bool result = ContinuationOperations.EndsWithContinuationMarker("\"\"\"a\n...\n\"\"\"", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_UnclosedTripleQuotedString_ReturnsFalse()
	{
		// A marker inside an unclosed raw string does not count.
		bool result = ContinuationOperations.EndsWithContinuationMarker("\"\"\"a ...", s_cStyleSyntax, "...");

		Assert.IsFalse(result);
	}

	[TestMethod]
	public void EndsWithContinuationMarker_VbWhitespaceRule_RejectsIdentifierEndingWithMarker()
	{
		CommentSyntax vbSyntax = new("'", null, StringLiteralStyle.None);

		// Without the rule the marker position alone decides, so a VB identifier that ends with '_'
		// is misread as a continuation; the rule requires whitespace before the marker.
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker("Dim foo_", vbSyntax, '_'));
		Assert.IsFalse(ContinuationOperations.EndsWithContinuationMarker(
			"Dim foo_", vbSyntax, "_", markerMustBePrecededByWhitespace: true));
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker(
			"Dim total As Integer = 1 + _", vbSyntax, "_", markerMustBePrecededByWhitespace: true));
	}

	[TestMethod]
	public void EndsWithContinuationMarker_VbWhitespaceRule_RejectsMarkerWithoutPrecedingCharacter()
	{
		CommentSyntax vbSyntax = new("'", null, StringLiteralStyle.None);

		Assert.IsFalse(ContinuationOperations.EndsWithContinuationMarker("_", vbSyntax, "_", markerMustBePrecededByWhitespace: true));
	}

	[TestMethod]
	public void EndsWithContinuationMarker_VbWhitespaceRule_StillIgnoresTrailingComment()
	{
		CommentSyntax vbSyntax = new("'", null, StringLiteralStyle.None);

		// The whitespace before the marker is checked after trailing comments are removed.
		Assert.IsTrue(ContinuationOperations.EndsWithContinuationMarker(
			"Dim total As Integer = 1 + _ ' note", vbSyntax, "_", markerMustBePrecededByWhitespace: true));
	}
}
