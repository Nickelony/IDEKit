#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Text;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Text;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

[TestClass]
public sealed class DocumentLineStateCacheTests
{
	private static int CountBrackets(string lineText, int state) =>
		state + lineText.Count(character => character == '[') - lineText.Count(character => character == ']');

	[TestMethod]
	public void GetLineStartState_TracksEditsOnFirstLine()
	{
		var document = new TextDocument("a[b\nc\nd]e");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(1, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));

		document.Insert(0, "[");

		Assert.AreEqual(2, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void GetLineStartState_TracksMultilineReplacement()
	{
		var document = new TextDocument("a\n[b\nc\nd]\ne");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(0, cache.GetLineStartState(5));
		Assert.AreEqual(0, cache.GetLineStartState(6));

		document.Replace(5, 4, "[x\ny");

		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(2, cache.GetLineStartState(4));
		Assert.AreEqual(2, cache.GetLineStartState(5));
		Assert.AreEqual(2, cache.GetLineStartState(6));
	}

	[TestMethod]
	public void GetLineStartState_BatchReplacementWithAnOffsetChangeMap_MatchesAFreshCache()
	{
		const string text = "a\n  [b\n    c\n  d]\ne\n  [f";

		var document = new TextDocument(text);
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(1, cache.GetLineStartState(3));

		// One change over the span of three replaced indentations.
		var map = new OffsetChangeMap { new OffsetChangeMapEntry(13, 2, 1), new OffsetChangeMapEntry(7, 4, 2), new OffsetChangeMapEntry(2, 2, 1) };

		document.Replace(offset: 2, length: 13, text: "\t[b\n\t\tc\n\t", offsetChangeMap: map);

		var fresh = new TextDocument(document.Text);
		using var freshCache = new DocumentLineStateCache<int>(fresh, CountBrackets);

		for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
			Assert.AreEqual(freshCache.GetLineStartState(lineNumber), cache.GetLineStartState(lineNumber), "line " + lineNumber);
	}

	[TestMethod]
	public void GetLineStartState_EditOnLaterLine_PreservesEarlierStates()
	{
		var document = new TextDocument("a\n[b\nc\nd]");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));

		document.Insert(5, "[");

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));
		Assert.AreEqual(2, cache.GetLineStartState(4));
	}

	[TestMethod]
	public void GetLineStartState_EditsBeforeFirstRequest_UseTheCurrentDocument()
	{
		var document = new TextDocument("a\n[b");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		document.Insert(0, "[");

		// The first snapshot is taken lazily, so it already reflects the edit.
		Assert.AreEqual(1, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void GetLineStartState_CoalescedEdits_RecomputeInvalidatedLines()
	{
		var document = new TextDocument("a\nb\nc");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(2));

		document.Insert(0, "[");
		document.Insert(3, "[");

		// Both edits are coalesced into one snapshot; states after the earliest edit are recomputed.
		Assert.AreEqual(1, cache.GetLineStartState(2));
		Assert.AreEqual(2, cache.GetLineStartState(3));
	}

	[TestMethod]
	public void GetLineStartState_AfterSeveralEdits_MatchesFreshCache()
	{
		var document = new TextDocument("a\n[b\nc");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(1, cache.GetLineStartState(3));

		document.Insert(0, "[");
		document.Insert(document.TextLength, "]");

		document.Replace(2, 1, "x\n");

		using var freshCache = new DocumentLineStateCache<int>(document, CountBrackets);

		for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
			Assert.AreEqual(freshCache.GetLineStartState(lineNumber), cache.GetLineStartState(lineNumber), $"Line {lineNumber}.");
	}

	[TestMethod]
	public void GetLineStartState_DeletionBurst_MatchesFreshCache()
	{
		var document = new TextDocument("[a\nb\nc\nd\ne]");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		// Prime the cache past the deletion point so the burst must invalidate a suffix of cached states
		// against a document that the deletion shrinks below the primed line index.
		_ = cache.GetLineStartState(5);

		document.Remove(2, 4);

		using var freshCache = new DocumentLineStateCache<int>(document, CountBrackets);

		for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
			Assert.AreEqual(freshCache.GetLineStartState(lineNumber), cache.GetLineStartState(lineNumber), $"Line {lineNumber}.");
	}

	[TestMethod]
	public void GetLineStartState_EditStrictlyInsideChangedLine_IsServedFromTheExistingCache()
	{
		var document = new TextDocument("a\nb\nc");
		int transitionCalls = 0;

		int CountingTransition(string lineText, int state)
		{
			transitionCalls++;
			return CountBrackets(lineText, state);
		}

		using var cache = new DocumentLineStateCache<int>(document, CountingTransition);

		Assert.AreEqual(0, cache.GetLineStartState(2));

		int callsAfterWarmup = transitionCalls;

		// The edit starts past the start of line 2 (at the end of its text), so the start state of line 2 depends only on text
		// that is unaffected by the edit and is served from the existing cache without recomputing.
		document.Insert(3, "x");

		Assert.AreEqual(0, cache.GetLineStartState(2));
		Assert.AreEqual(callsAfterWarmup, transitionCalls, "The changed line's start state must not be recomputed.");

		// A request past the changed line still picks up the coalesced edit.
		Assert.AreEqual(0, cache.GetLineStartState(3));
		Assert.IsGreaterThan(callsAfterWarmup, transitionCalls);
	}

	[TestMethod]
	public void GetLineStartState_NewLineCreatedAtTheEditOffset_UsesTheNewSnapshot()
	{
		var document = new TextDocument("[ab");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(1));

		document.Insert(3, "\n[");

		// The edit starts a new line at the edit offset, so a request for that line must not reuse
		// a state cached for the old line numbering; it is computed from the new snapshot instead.
		Assert.AreEqual(1, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void GetLineStartState_RequestForALineBeforeTheEdit_KeepsTheUnaffectedState()
	{
		var document = new TextDocument("[a\nb\nc");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(1));
		Assert.AreEqual(1, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));

		document.Insert(2, "[");

		// The edit starts past the start of line 1 (at the end of its text), so that line's start state is unaffected, while the
		// states after it move with the new bracket.
		Assert.AreEqual(0, cache.GetLineStartState(1));
		Assert.AreEqual(2, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void GetLineStartState_FullTextReplacement_RecomputesStates()
	{
		var document = new TextDocument("a\nb");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(2));

		document.Text = "a[\nb\nc]";

		// A full-text replacement replaces every line, so the cached states are discarded and the
		// start states are recomputed from the new content.
		Assert.AreEqual(1, cache.GetLineStartState(2));
		Assert.AreEqual(1, cache.GetLineStartState(3));

		// A line number beyond the final line is clamped to the final line's start state.
		Assert.AreEqual(1, cache.GetLineStartState(4));
	}

	[TestMethod]
	public void Dispose_StopsTrackingDocumentEditsAndReportsDefaultStates()
	{
		var document = new TextDocument("a\n[b");
		var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(0, cache.GetLineStartState(2));

		cache.Dispose();

		// A disposed cache reports default states instead of throwing, because its reads run inside a
		// colorizer's render pass.
		Assert.AreEqual(0, cache.GetLineStartState(2));

		// Edits after disposal are not tracked: the instance has stopped tracking the document.
		document.Insert(0, "[");

		Assert.AreEqual(0, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void Dispose_Twice_DoesNotThrow()
	{
		var document = new TextDocument("a\n[b");
		var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		cache.Dispose();
		cache.Dispose();
	}

	[TestMethod]
	public void GetLineStartState_LineAtOrBelowOne_ReturnsDefault()
	{
		var document = new TextDocument("a[b\nc");
		using var cache = new DocumentLineStateCache<int>(document, static (_, _) => 42);

		// The first line has no predecessor, so its start state is the state type's default even though
		// the transition produces another value for every processed line.
		Assert.AreEqual(0, cache.GetLineStartState(1));
		Assert.AreEqual(0, cache.GetLineStartState(0));
		Assert.AreEqual(0, cache.GetLineStartState(-5));

		// The transition result is observable from the second line on, so the default above is not the
		// product of a transition that happens to return zero.
		Assert.AreEqual(42, cache.GetLineStartState(2));
	}

	[TestMethod]
	public void GetLineStartState_LineBeyondDocument_ClampsToFinalLine()
	{
		var document = new TextDocument("a[b\nc]d");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(cache.GetLineStartState(2), cache.GetLineStartState(99));
	}

	[TestMethod]
	public void GetLineStartState_Undo_RestoresThePreEditStates()
	{
		var document = new TextDocument("a\n[b\nc");
		using var cache = new DocumentLineStateCache<int>(document, CountBrackets);

		Assert.AreEqual(1, cache.GetLineStartState(3));

		// The closing bracket on the first line shifts every following state.
		document.Insert(0, "]");

		Assert.AreEqual(0, cache.GetLineStartState(3));

		// An undo raises the same change notifications, so the cache must not serve the states of the
		// undone text.
		document.UndoStack.Undo();

		Assert.AreEqual(1, cache.GetLineStartState(3));
	}
}
