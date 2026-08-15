namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextIncrementalEditCalculatorTests
{
	[TestMethod]
	public void Compute_CollapsesUnchangedPrefixAndSuffixIntoMinimalRangeEdit()
	{
		const string oldText = "local foo = 1\nlocal bar = 2\n";
		const string newText = "local foo = 1\nlocal baz = 2\n";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(oldText, newText);

		Assert.AreEqual(22, change.Range.Offset);
		Assert.AreEqual(1, change.Range.Length);
		Assert.AreEqual("z", change.NewText);
	}

	[TestMethod]
	public void Compute_CrLfAtTheCommonPrefixBoundary_WidensPastTheWholeTerminator()
	{
		const string oldText = "x\r\nA";
		const string newText = "x\rB";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(oldText, newText);

		// The minimal prefix boundary would fall between '\r' and '\n'; the range is widened so the
		// whole terminator is replaced and offsets stay on line boundaries.
		Assert.AreEqual(1, change.Range.Offset);
		Assert.AreEqual(3, change.Range.Length);
		Assert.AreEqual("\rB", change.NewText);
		Assert.AreEqual(newText, oldText[..change.Range.Offset] + change.NewText + oldText[change.Range.EndOffset..]);
	}

	[TestMethod]
	public void Compute_HandlesPureInsertionAtEndOfFile()
	{
		const string oldText = "local foo = 1\n";
		const string newText = "local foo = 1\nlocal bar = 2\n";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(oldText, newText);

		Assert.AreEqual(oldText.Length, change.Range.Offset);
		Assert.AreEqual(0, change.Range.Length);
		Assert.AreEqual("local bar = 2\n", change.NewText);
	}

	[TestMethod]
	public void Compute_NoChange_ProducesEmptyRange()
	{
		const string text = "print('hi')\n";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(text, text);

		Assert.AreEqual(0, change.Range.Length);
		Assert.AreEqual(text.Length, change.Range.Offset);
		Assert.AreEqual(string.Empty, change.NewText);
	}

	[TestMethod]
	public void Compute_NullInputs_TreatBothAsEmpty()
	{
		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(null, null);

		Assert.AreEqual(0, change.Range.Offset);
		Assert.AreEqual(0, change.Range.Length);
		Assert.AreEqual(string.Empty, change.NewText);
	}

	[TestMethod]
	public void Compute_NullOldText_TreatsItAsEmpty()
	{
		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(null, "a");

		Assert.AreEqual(0, change.Range.Offset);
		Assert.AreEqual(0, change.Range.Length);
		Assert.AreEqual("a", change.NewText);
	}

	[TestMethod]
	public void Compute_NullNewText_TreatsItAsEmpty()
	{
		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute("a", null);

		Assert.AreEqual(0, change.Range.Offset);
		Assert.AreEqual(1, change.Range.Length);
		Assert.AreEqual(string.Empty, change.NewText);
	}

	[TestMethod]
	public void Compute_RemovedText_ProducesDeletion()
	{
		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute("abc", "ac");

		Assert.AreEqual(1, change.Range.Offset);
		Assert.AreEqual(1, change.Range.Length);
		Assert.AreEqual(string.Empty, change.NewText);
	}

	[TestMethod]
	public void Compute_CrLfDeletion_WidensTheRangeToTheWholeTerminator()
	{
		const string oldText = "a\r\nb";
		const string newText = "a\nb";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(oldText, newText);

		// The minimal boundary would fall between '\r' and '\n'; the range is widened so both
		// terminator characters are replaced by the new terminator.
		Assert.AreEqual(1, change.Range.Offset);
		Assert.AreEqual(2, change.Range.Length);
		Assert.AreEqual("\n", change.NewText);
		Assert.AreEqual(newText, oldText[..change.Range.Offset] + change.NewText + oldText[change.Range.EndOffset..]);
	}

	[TestMethod]
	public void Compute_CrLfInsertion_KeepsTheMinimalInsertionRange()
	{
		const string oldText = "a\nb";
		const string newText = "a\r\nb";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(oldText, newText);

		// Only the old-content boundaries need to avoid splitting CRLF, and the minimal insertion
		// point before '\n' does not split anything, so the '\r' is inserted on its own.
		Assert.AreEqual(1, change.Range.Offset);
		Assert.AreEqual(0, change.Range.Length);
		Assert.AreEqual("\r", change.NewText);
		Assert.AreEqual(newText, oldText[..change.Range.Offset] + change.NewText + oldText[change.Range.EndOffset..]);
	}

	[TestMethod]
	public void Compute_SurrogatePairAtTheEndBoundary_WidensTheRange()
	{
		const string oldText = "\U0001F600A";
		const string newText = "\uD83DX\U0001F600A";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(oldText, newText);

		// The common suffix starts mid-pair in the old text, so the end boundary is widened to cover
		// the whole surrogate pair as well.
		Assert.AreEqual(0, change.Range.Offset);
		Assert.AreEqual(2, change.Range.Length);
		Assert.AreEqual("\uD83DX\U0001F600", change.NewText);
		Assert.AreEqual(newText, oldText[..change.Range.Offset] + change.NewText + oldText[change.Range.EndOffset..]);
	}

	[TestMethod]
	public void Compute_LoneCrRemoval_KeepsTheRangeOnTheLineBoundary()
	{
		const string oldText = "a\rb";
		const string newText = "ab";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(oldText, newText);

		Assert.AreEqual(1, change.Range.Offset);
		Assert.AreEqual(1, change.Range.Length);
		Assert.AreEqual(string.Empty, change.NewText);
		Assert.AreEqual(newText, oldText[..change.Range.Offset] + change.NewText + oldText[change.Range.EndOffset..]);
	}

	[TestMethod]
	[DataRow("x\r\nA", "x\rB", DisplayName = "CrLfPrefixWidening")]
	[DataRow("a\r\nb", "a\nb", DisplayName = "CrLfToLf")]
	[DataRow("a\nb", "a\r\nb", DisplayName = "LfToCrLf")]
	[DataRow("\U0001F600A", "\uD83DX\U0001F600A", DisplayName = "SurrogateBoundary")]
	[DataRow("local foo = 1\n", "local foo = 1\nlocal bar = 2\n", DisplayName = "InsertionAtEndOfFile")]
	[DataRow("abc", "ac", DisplayName = "Deletion")]
	[DataRow("a\rb", "ab", DisplayName = "LoneCrRemoval")]
	[DataRow("print('hi')\n", "print('hi')\n", DisplayName = "Unchanged")]
	[DataRow("a", "", DisplayName = "DeletedEverything")]
	public void Compute_ChangeAppliedThroughTheEditPipeline_ReproducesTheNewText(string oldText, string newText)
	{
		// Hosts mix the incremental calculator with the batch edit pipeline: the computed change is
		// copied into a TextEditInput and applied as a prepared batch. The round trip must reproduce
		// the new text exactly, including the CRLF and surrogate widenings.
		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(oldText, newText);
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot(oldText),
			[new TextEditInput(change.Range, change.NewText)]);

		Assert.IsTrue(result.IsValid);

		string applied = oldText;

		foreach (TextEditOperation operation in result.Edits.Operations)
		{
			applied = string.Concat(
				applied.AsSpan(0, operation.StartOffset),
				operation.NewText,
				applied.AsSpan(operation.EndOffset));
		}

		Assert.AreEqual(newText, applied);
	}
}
