namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LineDifferTests
{
	[TestMethod]
	public void GetHunks_IdenticalTexts_ReturnNone() => Assert.IsEmpty(LineDiffer.GetHunks(["a", "b"], ["a", "b"]));

	[TestMethod]
	public void GetHunks_AReplacementAnInsertionAndADeletion_ReturnOneHunkEachInLineOrder()
	{
		IReadOnlyList<LineDiffHunk> hunks = LineDiffer.GetHunks(["a", "b", "c", "d", "e"], ["A", "b", "new", "c", "e"]);

		Assert.AreSequenceEqual(
			[
				new LineDiffHunk(BaselineStart: 0, BaselineCount: 1, CurrentStart: 0, CurrentCount: 1),
				new LineDiffHunk(BaselineStart: 2, BaselineCount: 0, CurrentStart: 2, CurrentCount: 1),
				new LineDiffHunk(BaselineStart: 3, BaselineCount: 1, CurrentStart: 4, CurrentCount: 0),
			],

			[.. hunks]
		);
	}

	[TestMethod]
	public void GetHunks_FromEmptyOrToEmpty_ReturnOneHunkOverEverything()
	{
		Assert.AreSequenceEqual(
			[new LineDiffHunk(BaselineStart: 0, BaselineCount: 0, CurrentStart: 0, CurrentCount: 2)],
			[.. LineDiffer.GetHunks([], ["a", "b"])]
		);

		Assert.AreSequenceEqual(
			[new LineDiffHunk(BaselineStart: 0, BaselineCount: 2, CurrentStart: 0, CurrentCount: 0)],
			[.. LineDiffer.GetHunks(["a", "b"], [])]
		);
	}

	[TestMethod]
	public void GetHunks_MarkTheSameCurrentLinesAsGetChangedLineNumbers()
	{
		string[] baseline = ["x", "a", "b", "a", "c", "d"];
		string[] current = ["a", "a", "b", "q", "c", "e", "d"];

		IEnumerable<int> fromHunks = LineDiffer
			.GetHunks(baseline, current)
			.SelectMany(hunk => Enumerable.Range(hunk.CurrentStart + 1, hunk.CurrentCount));

		Assert.AreSequenceEqual([.. LineDiffer.GetChangedLineNumbers(baseline, current).ChangedLineNumbers.Order()], [.. fromHunks]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_IdenticalTexts_ReturnEmptySet()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a", "b"], ["a", "b"]).ChangedLineNumbers;
		Assert.IsEmpty(changed);
	}

	[TestMethod]
	public void GetChangedLineNumbers_BothEmpty_ReturnsEmptySet()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers([], []).ChangedLineNumbers;
		Assert.IsEmpty(changed);
	}

	[TestMethod]
	public void GetChangedLineNumbers_AmbiguousDuplicateLines_MarkTheResolvedOccurrence()
	{
		// Both current lines could match the baseline's trailing "a", and the diff aligns the common
		// suffix first: the second "a" matches, so the inserted first "a" is the changed line.
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["x", "a"], ["a", "a"]).ChangedLineNumbers;
		Assert.AreSequenceEqual([1], [.. changed]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_ModifiedMiddleLine_ReturnsChangedLineNumber()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a", "b", "c"], ["a", "x", "c"]).ChangedLineNumbers;
		Assert.AreSequenceEqual([2], [.. changed]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_InsertedLine_ReturnsInsertedLineNumber()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a", "c"], ["a", "b", "c"]).ChangedLineNumbers;
		Assert.AreSequenceEqual([2], [.. changed]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_PrependedLine_ReturnsFirstLine()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["b"], ["a", "b"]).ChangedLineNumbers;
		Assert.AreSequenceEqual([1], [.. changed]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_AppendedLine_ReturnsLastLine()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a"], ["a", "b"]).ChangedLineNumbers;
		Assert.AreSequenceEqual([2], [.. changed]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_DeletedLine_MarksNoCurrentLine()
	{
		// A deletion-only edit marks nothing because there is no changed current line.
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a", "b", "c"], ["a", "c"]).ChangedLineNumbers;
		Assert.IsEmpty(changed);
	}

	[TestMethod]
	public void GetChangedLineNumbers_ReplacedLastLine_ReturnsLastLine()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a", "b"], ["a", "x"]).ChangedLineNumbers;
		Assert.AreSequenceEqual([2], [.. changed]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_MultipleSeparateChanges_ReturnsEveryChangedLine()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a", "b", "c", "d", "e"], ["a", "X", "c", "Y", "e"]).ChangedLineNumbers;
		Assert.AreSequenceEqual([2, 4], [.. changed.OrderBy(line => line)]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_ReplacedTailBlock_ReturnsEachReplacedLine()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a", "b", "c"], ["a", "x", "y"]).ChangedLineNumbers;
		Assert.AreSequenceEqual([2, 3], [.. changed.OrderBy(line => line)]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_EmptyBaseline_MarksEveryCurrentLine()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers([], ["a", "b"]).ChangedLineNumbers;
		Assert.AreSequenceEqual([1, 2], [.. changed.OrderBy(line => line)]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_EmptyCurrent_ReturnsEmptySet()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a"], []).ChangedLineNumbers;
		Assert.IsEmpty(changed);
	}

	[TestMethod]
	public void GetChangedLineNumbers_InterleavedInsertionsInMiddle_MarkOnlyInsertedLines()
	{
		IReadOnlySet<int> changed = LineDiffer.GetChangedLineNumbers(["a", "b", "c"], ["a", "x", "b", "y", "c"]).ChangedLineNumbers;

		// "b" is matched, so only the inserted lines 2 and 4 are changed.
		Assert.AreSequenceEqual([2, 4], [.. changed.OrderBy(line => line)]);
	}

	[TestMethod]
	public void GetChangedLineNumbers_HeavilyDivergentInputs_MarkEveryCurrentLine()
	{
		string[] baseline = [.. Enumerable.Range(0, 3000).Select(index => $"baseline {index}")];
		string[] current = [.. Enumerable.Range(0, 3000).Select(index => $"current {index}")];

		LineDiffResult result = LineDiffer.GetChangedLineNumbers(baseline, current);

		Assert.IsTrue(result.ChangedLineNumbers.SetEquals(Enumerable.Range(1, current.Length)));
	}

	[TestMethod]
	public void GetChangedLineNumbers_LargeSparseMiddle_KeepsLocality()
	{
		// Two small changes inside a 700 000-line middle: the linear-space search must resolve them
		// exactly instead of reporting the whole middle as changed.
		const int lineCount = 700_000;
		string[] baseline = new string[lineCount];

		for (int index = 0; index < lineCount; index++)
			baseline[index] = (index % 512).ToString();

		string[] current = (string[])baseline.Clone();
		current[100_000] = "changed one";
		current[500_000] = "changed two";

		LineDiffResult result = LineDiffer.GetChangedLineNumbers(baseline, current);

		Assert.IsFalse(result.IsApproximate);
		Assert.AreSequenceEqual([100_001, 500_001], [.. result.ChangedLineNumbers.OrderBy(line => line)]);

		AssertChangedSetFormsCommonSubsequence(baseline, current, result.ChangedLineNumbers);
	}

	[TestMethod]
	public void GetChangedLineNumbers_DivergentMiddleBeyondWorkBudget_FallsBackAndMarksTheWholeMiddle()
	{
		// A fully rewritten 20 000-line middle exceeds the work budget, so the fallback reports the
		// middle as changed while the common prefix and suffix stay unmarked. The test depends on the
		// budget being exceeded by this input; the exact budget constant is an implementation detail
		// and not part of the contract (the contract is the approximate fallback itself).
		string[] prefix = [.. Enumerable.Range(0, 100).Select(index => $"prefix {index}")];
		string[] middleBaseline = [.. Enumerable.Range(0, 20_000).Select(index => $"baseline {index}")];
		string[] middleCurrent = [.. Enumerable.Range(0, 20_000).Select(index => $"current {index}")];
		string[] suffix = [.. Enumerable.Range(0, 100).Select(index => $"suffix {index}")];

		string[] baseline = [.. prefix, .. middleBaseline, .. suffix];
		string[] current = [.. prefix, .. middleCurrent, .. suffix];

		LineDiffResult result = LineDiffer.GetChangedLineNumbers(baseline, current);

		Assert.IsTrue(result.IsApproximate);
		Assert.HasCount(20_000, result.ChangedLineNumbers);
		Assert.IsTrue(result.ChangedLineNumbers.All(lineNumber => lineNumber is > 100 and <= 20_100));
	}

	[TestMethod]
	public void GetChangedLineNumbers_EmptyStringLines_ReportTheInsertedBlankLine()
	{
		// Empty-string lines are ordinary lines. The diff may align the inserted blank line with any
		// of the duplicates, so the changed line is one of the two blank positions.
		string[] baseline = ["a", string.Empty, "a"];
		string[] current = ["a", string.Empty, string.Empty, "a"];

		LineDiffResult result = LineDiffer.GetChangedLineNumbers(baseline, current);

		Assert.IsFalse(result.IsApproximate);
		Assert.HasCount(1, result.ChangedLineNumbers);
		Assert.IsTrue(result.ChangedLineNumbers.Contains(2) || result.ChangedLineNumbers.Contains(3));

		AssertChangedSetFormsCommonSubsequence(baseline, current, result.ChangedLineNumbers);

		// The reverse direction is a deletion-only edit, which marks no current line.
		LineDiffResult deletion = LineDiffer.GetChangedLineNumbers(current, baseline);

		Assert.IsFalse(deletion.IsApproximate);
		Assert.IsEmpty(deletion.ChangedLineNumbers);
	}

	/// <summary>
	/// Asserts the invariant every exact diff satisfies: the current lines that are not reported as changed appear in the baseline in the same order,
	/// so they form a common subsequence.
	/// </summary>
	private static void AssertChangedSetFormsCommonSubsequence(
		IReadOnlyList<string> baseline,
		IReadOnlyList<string> current,
		IReadOnlySet<int> changed
	)
	{
		int baselineIndex = 0;

		for (int currentIndex = 0; currentIndex < current.Count; currentIndex++)
		{
			if (changed.Contains(currentIndex + 1))
				continue;

			while (baselineIndex < baseline.Count && !string.Equals(baseline[baselineIndex], current[currentIndex], StringComparison.Ordinal))
				baselineIndex++;

			Assert.IsLessThan(baseline.Count, baselineIndex, $"unchanged line {currentIndex + 1} was not found in the baseline");
			baselineIndex++;
		}
	}

	[TestMethod]
	public void GetChangedLineNumbers_SmallInputs_MatchTheMinimalEditDistance()
	{
		// The changed count must equal the number of current lines outside a longest common
		// subsequence, which is the minimal number of inserted or modified current lines. The
		// reference counts the longest common subsequence with a textbook dynamic program.
		var random = new Random(20260914);
		string[] alphabet = ["a", "b", "c"];

		for (int iteration = 0; iteration < 2000; iteration++)
		{
			string[] baseline = [.. Enumerable.Range(0, random.Next(0, 9)).Select(_ => alphabet[random.Next(alphabet.Length)])];
			string[] current = [.. Enumerable.Range(0, random.Next(0, 9)).Select(_ => alphabet[random.Next(alphabet.Length)])];

			LineDiffResult result = LineDiffer.GetChangedLineNumbers(baseline, current);

			Assert.IsFalse(result.IsApproximate);

			Assert.HasCount(
				current.Length - CountLongestCommonSubsequence(baseline, current),
				result.ChangedLineNumbers,
				$"baseline=[{string.Join(',', baseline)}] current=[{string.Join(',', current)}]"
			);

			// The count alone does not prove the reported set is right: the unchanged lines must form a
			// common subsequence and every changed line must be a current line.
			AssertChangedSetFormsCommonSubsequence(baseline, current, result.ChangedLineNumbers);
		}
	}

	/// <summary>Counts the longest common subsequence of two line lists with a textbook dynamic program.</summary>
	private static int CountLongestCommonSubsequence(IReadOnlyList<string> baseline, IReadOnlyList<string> current)
	{
		int[,] lengths = new int[baseline.Count + 1, current.Count + 1];

		for (int baselineIndex = 1; baselineIndex <= baseline.Count; baselineIndex++)
		{
			for (int currentIndex = 1; currentIndex <= current.Count; currentIndex++)
			{
				lengths[baselineIndex, currentIndex] = string.Equals(baseline[baselineIndex - 1], current[currentIndex - 1], StringComparison.Ordinal)
					? lengths[baselineIndex - 1, currentIndex - 1] + 1
					: Math.Max(lengths[baselineIndex - 1, currentIndex], lengths[baselineIndex, currentIndex - 1]);
			}
		}

		return lengths[baseline.Count, current.Count];
	}

	[TestMethod]
	public void GetChangedLineNumbers_RepeatedDiff_ProducesValueEqualResults()
	{
		// The changed set is a FrozenSet, which has no value equality of its own; the result type
		// must provide set-based equality so hosts can cache and compare results.
		string[] baseline = ["a", "b", "c", "d"];
		string[] current = ["a", "x", "c", "y"];

		LineDiffResult first = LineDiffer.GetChangedLineNumbers(baseline, current);
		LineDiffResult second = LineDiffer.GetChangedLineNumbers(baseline, current);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
		Assert.IsTrue(first.ChangedLineNumbers.SetEquals(second.ChangedLineNumbers));
	}
}
