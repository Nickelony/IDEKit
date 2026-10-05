namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextIncrementalEditCalculatorHunkTests
{
	private static string Apply(string text, IReadOnlyList<TextIncrementalEdit> edits)
	{
		var builder = new System.Text.StringBuilder(text);

		// The edits ascend, so the last applies first and an insertion beside a replacement stays where it belongs.
		for (int index = edits.Count - 1; index >= 0; index--)
			builder.Remove(edits[index].Range.Offset, edits[index].Range.Length).Insert(edits[index].Range.Offset, edits[index].NewText);

		return builder.ToString();
	}

	private static void AssertAscendingAndDisjoint(IReadOnlyList<TextIncrementalEdit> edits)
	{
		for (int index = 1; index < edits.Count; index++)
			Assert.IsLessThanOrEqualTo(edits[index].Range.Offset, edits[index - 1].Range.EndOffset, "The edits overlap or are out of order.");
	}

	[TestMethod]
	public void ComputeHunkEdits_EqualContents_IsEmpty()
	{
		Assert.IsEmpty(TextIncrementalEditCalculator.ComputeHunkEdits("a\nb", "a\nb"));
		Assert.IsEmpty(TextIncrementalEditCalculator.ComputeHunkEdits(null, null));
	}

	[TestMethod]
	public void ComputeHunkEdits_AChangeInsideOneLine_IsTheSingleRangeOfCompute()
	{
		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits("one\ntwo\nthree", "one\ntXo\nthree");
		Assert.AreEqual(new[] { TextIncrementalEditCalculator.Compute("one\ntwo\nthree", "one\ntXo\nthree") }.Single(), edits.Single());
	}

	[TestMethod]
	[DataRow("\r\n", "\n")]
	[DataRow("\n", "\r\n")]
	[DataRow("\r", "\n")]
	public void ComputeHunkEdits_AConversionOfTheTerminators_HasOneEditPerTerminatorThatTouchesNoOtherCharacter(string from, string to)
	{
		string oldText = "alpha{0}beta{0}gamma{0}delta{0}".Replace("{0}", from, StringComparison.Ordinal);
		string newText = "alpha{0}beta{0}gamma{0}delta{0}".Replace("{0}", to, StringComparison.Ordinal);

		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits(oldText, newText);

		Assert.HasCount(4, edits);

		Assert.IsTrue(
			edits.All(edit => oldText[edit.Range.Offset..edit.Range.EndOffset].All(character => character is '\r' or '\n')),
			"An edit reaches past a terminator."
		);

		Assert.AreEqual(newText, Apply(oldText, edits));
		AssertAscendingAndDisjoint(edits);
	}

	[TestMethod]
	public void ComputeHunkEdits_AConversionOfTheIndentation_HasOneEditPerChangedLineNamingOnlyItsWhitespace()
	{
		const string oldText = "function f()\n    if x then\n        print(y)\n    end\nend\n";
		const string newText = "function f()\n\tif x then\n\t\tprint(y)\n\tend\nend\n";

		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits(oldText, newText);

		Assert.HasCount(3, edits);
		Assert.AreEqual(new TextRange(13, 4), edits[0].Range);
		Assert.AreEqual("\t", edits[0].NewText);
		Assert.AreEqual(newText, Apply(oldText, edits));

		AssertAscendingAndDisjoint(edits);
	}

	[TestMethod]
	public void ComputeHunkEdits_AnInsertedLineInTheMiddle_IsOneInsertionAndKeepsTheLinesAroundIt()
	{
		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits("a\nb\nc\nd\n", "a\nb\nNEW\nc\nd\n");

		Assert.HasCount(1, edits);
		Assert.AreEqual(0, edits[0].Range.Length);
		Assert.AreEqual("NEW\n", edits[0].NewText);
	}

	[TestMethod]
	public void ComputeHunkEdits_TwoSeparatedChanges_AreTwoEdits_AndTheLinesBetweenThemAreLeftAlone()
	{
		const string oldText = "a\nb\nc\nd\ne\nf\ng\n";
		const string newText = "A\nb\nc\nd\ne\nf\nG\n";

		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits(oldText, newText);

		Assert.HasCount(2, edits);
		Assert.AreEqual(new TextRange(0, 1), edits[0].Range);
		Assert.AreEqual(new TextRange(12, 1), edits[1].Range);
		Assert.AreEqual(newText, Apply(oldText, edits));
	}

	[TestMethod]
	public void ComputeHunkEdits_ALineTerminatorAddedAtTheEnd_IsOneInsertion()
	{
		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits("a\nb", "a\nb\n");

		Assert.AreEqual(new TextRange(3, 0), edits.Single().Range);
		Assert.AreEqual("\n", edits.Single().NewText);
	}

	[TestMethod]
	public void ComputeHunkEdits_ARunWithAnotherNumberOfLines_IsOneEditTrimmedToTheCharactersThatDiffer()
	{
		const string oldText = "head\none\ntwo\ntail\n";
		const string newText = "head\nonX\ntwo\nthree\nfour\ntail\n";

		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits(oldText, newText);

		Assert.AreEqual(newText, Apply(oldText, edits));
		AssertAscendingAndDisjoint(edits);

		Assert.IsTrue(edits.All(edit => edit.Range.Offset >= 5), "The head line is not touched.");
		Assert.IsTrue(edits.All(edit => edit.Range.EndOffset <= oldText.Length - 5), "The tail line is not touched.");
	}

	[TestMethod]
	public void ComputeHunkEdits_ARegionBeyondTheBound_IsTheSingleRangeOfCompute()
	{
		string oldText = string.Concat(Enumerable.Repeat("x\r\n", TextIncrementalEditCalculator.MaximumDiffLineCount + 1));
		string newText = oldText.Replace("\r\n", "\n", StringComparison.Ordinal);

		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits(oldText, newText);

		Assert.AreEqual(TextIncrementalEditCalculator.Compute(oldText, newText), edits.Single());
	}

	[TestMethod]
	public void ComputeHunkEdits_AnythingTheEditsDescribe_AppliesToTheNewContent()
	{
		string[] alphabet = ["a", "b", "\n", "\r\n", "\r", " "];
		var random = new Random(1234);

		for (int round = 0; round < 600; round++)
		{
			string oldText = string.Concat(Enumerable.Range(0, random.Next(0, 24)).Select(_ => alphabet[random.Next(alphabet.Length)]));
			string newText = string.Concat(Enumerable.Range(0, random.Next(0, 24)).Select(_ => alphabet[random.Next(alphabet.Length)]));

			IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits(oldText, newText);

			Assert.AreEqual(
				newText,
				Apply(oldText, edits),
				$"{oldText.Replace("\r", "\\r").Replace("\n", "\\n")} -> {newText.Replace("\r", "\\r").Replace("\n", "\\n")}"
			);

			AssertAscendingAndDisjoint(edits);
			Assert.IsTrue(edits.All(edit => edit.Range.EndOffset <= oldText.Length));
		}
	}

	[TestMethod]
	[TestCategory("Performance")]
	public void ComputeHunkEdits_ATwentyThousandLineConversion_IsFast()
	{
		string oldText = string.Join("\r\n", Enumerable.Range(0, 20_000).Select(index => "    line " + index));
		string newText = oldText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("    line", "\tline", StringComparison.Ordinal);

		var watch = System.Diagnostics.Stopwatch.StartNew();
		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits(oldText, newText);
		watch.Stop();

		Assert.AreEqual(newText, Apply(oldText, edits));
		Assert.IsLessThan(2_000L, watch.ElapsedMilliseconds, "The diff took " + watch.ElapsedMilliseconds + " ms.");

		TestContext.WriteLine("ComputeHunkEdits over 20,000 lines: " + watch.ElapsedMilliseconds + " ms, " + edits.Count + " edits.");
	}

	[TestMethod]
	[TestCategory("Performance")]
	public void ComputeHunkEdits_ATwentyThousandLineChangeThatAlsoAddsALine_StaysWithinTheBoundOfTheLineDiff()
	{
		string oldText = string.Join("\n", Enumerable.Range(0, 20_000).Select(index => "    line " + index));
		string newText = string.Join("\n", Enumerable.Range(0, 20_000).Select(index => "\tline " + index).Prepend("added"));

		var watch = System.Diagnostics.Stopwatch.StartNew();
		IReadOnlyList<TextIncrementalEdit> edits = TextIncrementalEditCalculator.ComputeHunkEdits(oldText, newText);
		watch.Stop();

		Assert.AreEqual(newText, Apply(oldText, edits));
		Assert.IsLessThan(3_000L, watch.ElapsedMilliseconds, "The diff took " + watch.ElapsedMilliseconds + " ms.");

		TestContext.WriteLine(
			"ComputeHunkEdits over 20,000 lines with a line added and every line changed: "
				+ watch.ElapsedMilliseconds
				+ " ms, "
				+ edits.Count
				+ " edits."
		);
	}

	public TestContext TestContext { get; set; } = null!;
}
