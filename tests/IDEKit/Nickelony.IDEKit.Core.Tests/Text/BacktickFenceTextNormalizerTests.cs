namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class BacktickFenceTextNormalizerTests
{
	// A fixed terminator keeps the expectations independent of the machine's newline convention.
	private const string NewLine = "\n";

	[TestMethod]
	public void NormalizeForPlainText_PreservesInlineBackticks()
	{
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("Call `value` before `other`.", NewLine);

		Assert.AreEqual("Call `value` before `other`.", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_StripsFenceLinesButPreservesCodeContent()
	{
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText(
			"Summary\n```lua\nlocal value = 1\n```\nTail",
			NewLine);

		Assert.AreEqual(
			$"Summary{NewLine}local value = 1{NewLine}Tail",
			normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_UsesSuppliedNewLine()
	{
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("one\ntwo\nthree", "\r\n");

		Assert.AreEqual("one\r\ntwo\r\nthree", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_TildeFences_AreKept()
	{
		// Only backtick fences are recognized, so a tilde fence stays in the normalized text.
		string? result = BacktickFenceTextNormalizer.NormalizeForPlainText("~~~\ncode\n~~~", "\n");

		Assert.AreEqual("~~~\ncode\n~~~", result);
	}

	[TestMethod]
	public void NormalizeForPlainText_IndentedFence_IsStripped()
	{
		string? result = BacktickFenceTextNormalizer.NormalizeForPlainText("  ```\ncode\n  ```", "\n");

		Assert.AreEqual("code", result);
	}

	[TestMethod]
	public void NormalizeForPlainText_FenceWithTrailingSpaces_IsStripped()
	{
		string? result = BacktickFenceTextNormalizer.NormalizeForPlainText("```   \ncode\n```  ", "\n");

		Assert.AreEqual("code", result);
	}

	[TestMethod]
	public void NormalizeForPlainText_CrLfFences_AreStrippedAndJoined()
	{
		string? result = BacktickFenceTextNormalizer.NormalizeForPlainText("```\r\ncode\r\n```", "\r\n");

		Assert.AreEqual("code", result);
	}

	[TestMethod]
	public void NormalizeForPlainText_NullNewLine_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => BacktickFenceTextNormalizer.NormalizeForPlainText("one", null!));
	}

	[TestMethod]
	public void NormalizeForPlainText_BlankInputReturnsNull()
	{
		Assert.IsNull(BacktickFenceTextNormalizer.NormalizeForPlainText(null, NewLine));
		Assert.IsNull(BacktickFenceTextNormalizer.NormalizeForPlainText("   ", NewLine));
	}

	[TestMethod]
	public void NormalizeForPlainText_OnlyFenceLinesReturnsNull()
	{
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("```\n```", NewLine);

		Assert.IsNull(normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_LoneCr_IsTreatedAsLineBreak()
	{
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("one\rtwo", NewLine);

		Assert.AreEqual($"one{NewLine}two", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_MixedLineEndings_AreNormalized()
	{
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("one\r\ntwo\rthree\nfour", NewLine);

		Assert.AreEqual($"one{NewLine}two{NewLine}three{NewLine}four", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_LongerFence_IsStripped()
	{
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("````\ncode\n````", NewLine);

		Assert.AreEqual("code", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_FenceWithSpacedInfoString_IsStripped()
	{
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("``` csharp\nvar x = 1;\n```", NewLine);

		Assert.AreEqual("var x = 1;", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_LineWithSurroundingBackticks_IsKept()
	{
		const string input = "```inline```";

		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText(input, NewLine);

		Assert.AreEqual(input, normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_IndentedCodeContent_KeepsIndentationExactly()
	{
		// Only blank edge lines are dropped; the first retained line keeps its indentation just like
		// every other retained line.
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText(
			"```\n    if (x)\n        call();\n```",
			NewLine);

		Assert.AreEqual($"    if (x){NewLine}        call();", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_BlankEdgeLines_AreDropped()
	{
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("\n```\ncode\n```\n\n", NewLine);

		Assert.AreEqual("code", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_NestedFenceInsideLongerFence_IsPreservedAsContent()
	{
		// The inner three-backtick block is content of the four-backtick fence (the nested-fence
		// idiom for documenting fences), so only the outer pair is removed.
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText(
			"````\n```lua\nprint(1)\n```\n````",
			NewLine);

		Assert.AreEqual($"```lua{NewLine}print(1){NewLine}```", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_AnnotatedFenceLikeLineInsideFence_IsPreserved()
	{
		// A fence-like line with an info string cannot close the open fence, so it stays content.
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText(
			"```\n```py\ncode\n```",
			NewLine);

		Assert.AreEqual($"```py{NewLine}code", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_ShortBacktickRuns_AreKept()
	{
		// One- and two-backtick lines never open or close a fence.
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("``\ncode\n``", NewLine);

		Assert.AreEqual($"``{NewLine}code{NewLine}``", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_FenceLikeLineWithBacktickInInfoString_IsKept()
	{
		// The first line is not a fence line (its info string contains a backtick), so nothing pairs
		// and the whole text is retained.
		const string Input = "```lang`x\ncode\nmore";

		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText(Input, NewLine);

		Assert.AreEqual(Input, normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_UnclosedFence_RemovesOpenerAndKeepsContent()
	{
		// The opening fence line is removed even when nothing closes it; the remaining lines stay as
		// code content instead of being dropped with the opener.
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("```lua\nprint(1)", NewLine);

		Assert.AreEqual("print(1)", normalized);
	}

	[TestMethod]
	public void NormalizeForPlainText_RetainedLines_LoseTrailingWhitespace()
	{
		// Trailing whitespace on retained lines is removed, while leading indentation is preserved
		// exactly.
		string? normalized = BacktickFenceTextNormalizer.NormalizeForPlainText("```\n  code   \nnext\t", NewLine);

		Assert.AreEqual($"  code{NewLine}next", normalized);
	}
}
