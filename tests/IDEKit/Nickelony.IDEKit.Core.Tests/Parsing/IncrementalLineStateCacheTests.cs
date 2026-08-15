namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IncrementalLineStateCacheTests
{
	private int _transitionInvocationCount;

	private int CountBrackets(string lineText, int state)
		=> state + lineText.Count(character => character == '[') - lineText.Count(character => character == ']');

	private int CountingTransition(string lineText, int state)
	{
		_transitionInvocationCount++;
		return CountBrackets(lineText, state);
	}

	[TestMethod]
	public void GetLineStartState_ReferenceStateType_CarriesNullDefaultAndTransitions()
	{
		// TState may be a reference type: the first line's state is null, and the transition builds
		// the following states from it.
		var cache = new IncrementalLineStateCache<string?>(
			new StringTextSnapshot("a\nb"),
			static (lineText, state) => state + lineText);

		Assert.IsNull(cache.GetLineStartState(1));
		Assert.AreEqual("a", cache.GetLineStartState(2));
		Assert.AreEqual("a", cache.GetLineStartState(99));
	}

	[TestMethod]
	public void GetLineStartState_ComputesStatesFromSnapshot()
	{
		var snapshot = new StringTextSnapshot("a[b\nc\nd]e\nf");
		var cache = new IncrementalLineStateCache<int>(snapshot, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(1));
		Assert.AreEqual(1, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(0, cache.GetLineStartState(4));
		Assert.AreEqual(0, cache.GetLineStartState(5));
	}

	[TestMethod]
	public void GetLineStartState_ClampsOutOfRangeLineNumbers()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("[a\n[b"), CountBrackets);

		// Line numbers at or below 1 report the default state, while a number past the end is
		// clamped to the final line, whose state is not the default.
		Assert.AreEqual(0, cache.GetLineStartState(-5));
		Assert.AreEqual(0, cache.GetLineStartState(0));
		Assert.AreEqual(1, cache.GetLineStartState(99));
	}

	[TestMethod]
	public void GetLineStartState_EmptyDocument_ReturnsDefault()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot(string.Empty), CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(5));
	}

	[TestMethod]
	public void ApplyEdit_EditOnFirstLine_RecomputesFollowingState()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("[a\nb"), CountBrackets);
		Assert.AreEqual(1, cache.GetLineStartState(2));

		cache.ApplyEdit(new StringTextSnapshot("[[a\nb"), 0);

		Assert.AreEqual(2, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void ApplyEdit_WithComputedChange_RecomputesTheAffectedState()
	{
		// Piping the calculator's output into the cache keeps the two APIs in lockstep: the change's
		// start offset is the first changed offset the cache documents.
		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute("[a\nb", "[[a\nb");

		Assert.AreEqual(1, change.Range.Offset);
		Assert.AreEqual(0, change.Range.Length);

		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("[a\nb"), CountBrackets);
		Assert.AreEqual(1, cache.GetLineStartState(2));

		cache.ApplyEdit(new StringTextSnapshot("[[a\nb"), change.Range.Offset);

		Assert.AreEqual(2, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void ApplyEdit_EditOnMiddleLine_PreservesPrefixAndRecomputesRemainder()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("a\n[b\nc\nd]"), CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(1, cache.GetLineStartState(4));

		cache.ApplyEdit(new StringTextSnapshot("a\n[b\n[c\nd]"), 5);

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(2, cache.GetLineStartState(4));
	}

	[TestMethod]
	public void ApplyEdit_EditOnLastLine_PreservesTheAffectedLineStartState()
	{
		var snapshot = new StringTextSnapshot("a\nb\n[c\nd\ne");
		var cache = new IncrementalLineStateCache<int>(snapshot, CountingTransition);

		Assert.AreEqual(1, cache.GetLineStartState(5));
		Assert.AreEqual(4, _transitionInvocationCount);

		cache.ApplyEdit(new StringTextSnapshot("a\nb\n[c\nd\ne]"), 10);

		// The state at the start of the edited line depends only on unchanged text, so no further
		// transition is needed.
		Assert.AreEqual(1, cache.GetLineStartState(5));
		Assert.AreEqual(4, _transitionInvocationCount);
	}

	[TestMethod]
	public void ApplyEdit_MultilineReplacement_RecomputesFromFirstAffectedLine()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("a\n[b\nc\nd]\ne"), CountBrackets);

		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(0, cache.GetLineStartState(5));
		Assert.AreEqual(0, cache.GetLineStartState(6));

		cache.ApplyEdit(new StringTextSnapshot("a\n[b\nX\nY\ne"), 5);

		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(1, cache.GetLineStartState(5));
		Assert.AreEqual(1, cache.GetLineStartState(6));
	}

	[TestMethod]
	public void ApplyEdit_EditOnLaterLine_DoesNotRecomputeUnchangedPrefix()
	{
		var snapshot = new StringTextSnapshot("a\n[b\nc\nd]");
		var cache = new IncrementalLineStateCache<int>(snapshot, CountingTransition);

		Assert.AreEqual(1, cache.GetLineStartState(4));
		Assert.AreEqual(3, _transitionInvocationCount);

		cache.ApplyEdit(new StringTextSnapshot("a\n[b\n[c\nd]"), 5);

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(3, _transitionInvocationCount);

		// Only the last line's state is recomputed; the edited line's start state stays valid.
		Assert.AreEqual(2, cache.GetLineStartState(4));
		Assert.AreEqual(4, _transitionInvocationCount);
	}

	[TestMethod]
	public void GetLineStartState_ConcurrentReads_ReturnConsistentStates()
	{
		var snapshot = new StringTextSnapshot("a\n[b\nc\nd]\ne\n[f\ng\nh]");
		var expected = new IncrementalLineStateCache<int>(snapshot, CountBrackets);
		var cache = new IncrementalLineStateCache<int>(snapshot, CountBrackets);
		int expectedState = expected.GetLineStartState(8);
		var observed = new int[256];

		// The state is not cached yet, so the concurrent reads race to fill the same cache.
		Parallel.For(0, observed.Length, i => observed[i] = cache.GetLineStartState(8));

		Assert.AreEqual(1, expectedState);

		for (int i = 0; i < observed.Length; i++)
			Assert.AreEqual(expectedState, observed[i], $"Read {i}.");
	}

	[TestMethod]
	public void GetLineStartState_ConcurrentReadsAndWrites_RemainConsistent()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("[a\nb"), CountBrackets);

		// Reads and an edit race: every reader must observe a valid pre-edit or post-edit state of
		// the second line, and the cache must settle on the post-edit state.
		Parallel.For(0, 256, i =>
		{
			if (i % 64 == 0)
			{
				cache.ApplyEdit(new StringTextSnapshot("[[a\nb"), firstChangedOffset: 0);
			}
			else
			{
				int state = cache.GetLineStartState(2);
				Assert.IsTrue(state is 1 or 2, $"Read {i} observed {state}.");
			}
		});

		Assert.AreEqual(2, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void ApplyEdit_LineMerge_RecomputesStatesFromTheMergedLine()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("a\n[b\nc"), CountingTransition);

		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(2, _transitionInvocationCount);

		// Removing the second line terminator merges the last two lines, so the cached state of the
		// removed line must not survive the edit.
		cache.ApplyEdit(new StringTextSnapshot("a\n[b c"), firstChangedOffset: 4);

		// The removed line's cached state must not survive the edit; the merged line's own start
		// state is unchanged and stays valid.
		Assert.AreEqual(0, cache.GetLineStartState(3));
		Assert.AreEqual(2, _transitionInvocationCount);
	}

	[TestMethod]
	public void ApplyEdit_NegativeFirstChangedOffset_Throws()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("[a\n[b"), CountBrackets);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => cache.ApplyEdit(new StringTextSnapshot("[a\n[b"), -1));
	}

	[TestMethod]
	public void ApplyEdit_FirstChangedOffsetBeyondTheNewTextLength_Throws()
	{
		var snapshot = new StringTextSnapshot("[a\n[b");
		var cache = new IncrementalLineStateCache<int>(snapshot, CountBrackets);

		// An offset past the new text names no position of the edited text - the shape a post-edit
		// offset takes after a deletion shortened the document - so it must be rejected instead of
		// silently preserving every cached state.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			cache.ApplyEdit(new StringTextSnapshot("[a\n[b"), snapshot.TextLength + 1));
	}

	[TestMethod]
	public void ApplyEdit_FirstChangedOffsetAtTheTextEnd_PreservesStates()
	{
		var snapshot = new StringTextSnapshot("[a\n[b");
		var cache = new IncrementalLineStateCache<int>(snapshot, CountingTransition);

		Assert.AreEqual(1, cache.GetLineStartState(2));
		Assert.AreEqual(1, _transitionInvocationCount);

		// An append at the very end leaves all text before the offset unchanged, so the cached
		// states stay valid and no transition is needed.
		cache.ApplyEdit(new StringTextSnapshot("[a\n[bc"), snapshot.TextLength);

		Assert.AreEqual(1, cache.GetLineStartState(2));
		Assert.AreEqual(1, _transitionInvocationCount);
	}

	[TestMethod]
	public void ApplyEdit_BeforeFirstRead_UsesTheNewSnapshot()
	{
		var cache = new IncrementalLineStateCache<int>(new StringTextSnapshot("[a\nb"), CountBrackets);

		// No states are cached yet, so the change is recorded for the first read.
		cache.ApplyEdit(new StringTextSnapshot("[[a\nb"), firstChangedOffset: 0);

		Assert.AreEqual(2, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new IncrementalLineStateCache<string>(
			null!, static (_, state) => state));
		Assert.ThrowsExactly<ArgumentNullException>(() => new IncrementalLineStateCache<string>(
			new StringTextSnapshot("a"), null!));
	}
}
