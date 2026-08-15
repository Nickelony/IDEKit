#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.ChangeMarkers;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.ChangeMarkers;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[TestClass]
public sealed class UnsavedChangesTrackerTests
{
	[TestMethod]
	public void NoChanges_ReturnsNoMarkedLines()
	{
		var document = new TextDocument("alpha\r\nbeta\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void ModifiedLine_IsMarked()
	{
		var document = new TextDocument("alpha\r\nBETA\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, [2]);
	}

	[TestMethod]
	public void InsertedLines_AreMarked_AndFollowingLinesAreNot()
	{
		var document = new TextDocument("alpha\r\ninserted\r\nanother\r\nbeta\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, [2, 3]);
	}

	[TestMethod]
	public void AppendedLines_AreMarked()
	{
		var document = new TextDocument("alpha\r\nbeta\r\ngamma\r\ndelta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta");

		AssertLineNumbers(tracker, [3, 4]);
	}

	[TestMethod]
	public void DeletedLine_LeavesRemainingLinesUnmarked()
	{
		var document = new TextDocument("alpha\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void DeletedLastLine_LeavesRemainingLinesUnmarked()
	{
		var document = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void EmptyBaseline_MarksEveryLine()
	{
		var document = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("");

		AssertLineNumbers(tracker, [1, 2]);
	}

	[TestMethod]
	public void SetBaseline_ToCurrentText_ClearsMarkedLines()
	{
		var document = new TextDocument("alpha\r\nBETA\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");
		tracker.SetBaseline(document.Text);

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void ReplacedLines_InTheMiddle_AreMarked()
	{
		var document = new TextDocument("alpha\r\nfirst\r\nsecond\r\ndelta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma\r\ndelta");

		AssertLineNumbers(tracker, [2, 3]);
	}

	[TestMethod]
	public void LineEndings_AreNormalized_BeforeComparing()
	{
		var document = new TextDocument("alpha\nbeta\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void CarriageReturnOnlyLineEndings_AreNormalized_BeforeComparing()
	{
		var document = new TextDocument("alpha\rbeta\rgamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void SetBaseline_TreatsALeadingByteOrderMarkAsLineContent()
	{
		const string mark = "\uFEFF";

		// The comparison is ordinal, so a byte-order mark the caller supplies is ordinary content of
		// line 1. The workspace decode strips a file's mark before its content reaches a document, so
		// in that flow neither side carries one; this pins the tracker's own contract for a caller
		// that passes raw text instead.
		var documentWithMark = new TextDocument(mark + "alpha\r\nbeta");
		UnsavedChangesTracker matching = CreateTracker(documentWithMark);

		matching.SetBaseline(mark + "alpha\r\nbeta");

		AssertLineNumbers(matching, []);

		var documentWithoutMark = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker mismatched = CreateTracker(documentWithoutMark);

		mismatched.SetBaseline(mark + "alpha\r\nbeta");

		// Only line 1 differs: the mark is not normalized away, and the rest of the content matches.
		AssertLineNumbers(mismatched, [1]);
	}

	[TestMethod]
	public void MarkedLines_AreSortedByLineNumber()
	{
		var document = new TextDocument("one\r\nchanged\r\nthree\r\nchanged2\r\nfive");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("one\r\ntwo\r\nthree\r\nfour\r\nfive");

		IReadOnlyList<int> lines = tracker.GetMarkedLineNumbers();

		Assert.HasCount(2, lines);
		Assert.AreEqual(2, lines[0]);
		Assert.AreEqual(4, lines[1]);
	}

	[TestMethod]
	public void EmptyBaseline_WithSingleLineDocument_MarksThatLine()
	{
		var document = new TextDocument("alpha");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("");

		AssertLineNumbers(tracker, [1]);
	}

	[TestMethod]
	public void SetBaseline_RaisesChangedEvent()
	{
		var document = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		int changedCalls = 0;
		tracker.Changed += (_, _) => changedCalls++;

		tracker.SetBaseline("alpha");

		Assert.AreEqual(1, changedCalls);
	}

	[TestMethod]
	public void DocumentSwap_ReturnsLinesFromCurrentDocument()
	{
		var firstDocument = new TextDocument("alpha\r\nBETA");
		var secondDocument = new TextDocument("alpha\r\nBETA\r\ngamma");

		TextDocument currentDocument = firstDocument;
		var tracker = new UnsavedChangesTracker(() => currentDocument);

		tracker.SetBaseline("alpha\r\nbeta");

		AssertLineNumbers(tracker, [2]);

		currentDocument = secondDocument;

		// The read follows the provider, so it reports the new document's marked lines.
		AssertLineNumbers(tracker, [2, 3]);
	}

	[TestMethod]
	public void GetMarkedLineNumbers_WhenDocumentProviderReturnsNull_ReturnsEmptyList()
	{
		var tracker = new UnsavedChangesTracker(() => null!);

		// The read runs inside a margin's render pass, so a temporarily missing document yields no marks.
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void GetMarkedLineNumbers_ReturnsReadOnlyView()
	{
		var document = new TextDocument("alpha\r\nBETA");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta");

		IReadOnlyList<int> lines = tracker.GetMarkedLineNumbers();

		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<int>)lines).Add(lines[0]));
	}

	[TestMethod]
	public void TrailingTerminatorAdded_MarksTheTrailingEmptyLine()
	{
		var document = new TextDocument("alpha\r\n");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha");

		AssertLineNumbers(tracker, [2]);
	}

	[TestMethod]
	public void TrailingTerminatorRemoved_LeavesNoMarkedLine()
	{
		var document = new TextDocument("alpha");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\n");

		// Removing a terminator is a deletion-only edit, which is never marked.
		AssertLineNumbers(tracker, []);
	}

	[TestMethod]
	public void EditAfterCachedRead_InvalidatesTheCachedResult()
	{
		var document = new TextDocument("alpha\r\nbeta\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");

		AssertLineNumbers(tracker, []);

		document.Insert(0, "inserted\r\n");

		AssertLineNumbers(tracker, [1]);
	}

	[TestMethod]
	public void SetBaseline_FromBackgroundThread_IsReflectedByNextRead()
	{
		var document = new TextDocument("alpha\r\nbeta\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");
		AssertLineNumbers(tracker, []);

		// SetBaseline is documented as callable from any thread; a baseline replaced off the owner
		// thread must be picked up by the next read instead of being served from the cache. The wait
		// is blocking so every document read stays on the owner thread, as GetMarkedLines requires.
		Task.Run(() => tracker.SetBaseline("alpha\r\nBETA\r\ngamma")).GetAwaiter().GetResult();

		AssertLineNumbers(tracker, [2]);
	}

	[TestMethod]
	public void SetBaseline_ConcurrentWithAResultRead_ConvergesToTheFinalBaseline()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("one\r\ntwo\r\nthree");

		// A baseline replaced while a read computes its result must not poison the cached result:
		// readers may observe either baseline mid-flight, but after the writer stops, the final
		// baseline is authoritative on the next read even though the document never changed. The
		// reads run on the owner thread; only the baseline writer runs on a background thread.
		Task writer = Task.Run(() =>
		{
			for (int index = 0; index < 512; index++)
				tracker.SetBaseline(index % 2 == 0 ? "one\r\nCHANGED\r\nthree" : "one\r\ntwo\r\nthree");
		});

		for (int index = 0; index < 512; index++)
			_ = tracker.GetMarkedLineNumbers();

		writer.GetAwaiter().GetResult();

		tracker.SetBaseline("one\r\nCHANGED\r\nthree");

		AssertLineNumbers(tracker, [2]);
	}

	[TestMethod]
	public void EditsBetweenReads_AreTrackedIncrementally_WithExactMarks()
	{
		var document = new TextDocument("alpha\r\nbeta\r\ngamma");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta\r\ngamma");
		AssertLineNumbers(tracker, []);

		document.Insert(0, "X");
		AssertLineNumbers(tracker, [1]);

		document.Insert(document.TextLength, "\r\ndelta");
		AssertLineNumbers(tracker, [1, 4]);
	}

	[TestMethod]
	public void IncrementalEdits_MatchAFullRebuild()
	{
		const string baseline = "one\r\ntwo\r\nthree\r\nfour";
		var document = new TextDocument(baseline);
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline(baseline);
		AssertLineNumbers(tracker, []);

		// The sequence exercises an insertion inside a line, an append, a prefix insertion, a
		// multi-line join, and a line split; each result must equal a rebuild from the full text.
		document.Insert(0, "zero ");
		AssertMatchesFullRebuild(document, baseline, tracker);

		document.Insert(document.TextLength, "\r\nfive");
		AssertMatchesFullRebuild(document, baseline, tracker);

		document.Insert(document.GetLineByNumber(2).Offset, "in this line ");
		AssertMatchesFullRebuild(document, baseline, tracker);

		DocumentLine thirdLine = document.GetLineByNumber(3);
		document.Replace(thirdLine.Offset, thirdLine.Length + thirdLine.DelimiterLength, "merged");
		AssertMatchesFullRebuild(document, baseline, tracker);

		document.Insert(4, "\r\n\r\n");
		AssertMatchesFullRebuild(document, baseline, tracker);
	}

	[TestMethod]
	public void CrlfBoundaryEdits_MatchAFullRebuild()
	{
		const string baseline = "a\r\nb";

		// Deleting the LF of the CRLF pair: the removed fragment is only half of a line break.
		AssertEditAndUndoMatchFullRebuild(baseline, static document => document.Remove(2, 1));

		// Deleting the CR of the pair: the unchanged LF becomes a break on its own.
		AssertEditAndUndoMatchFullRebuild(baseline, static document => document.Remove(1, 1));

		// A replacement that starts mid-pair and removes the LF plus the rest of the line.
		AssertEditAndUndoMatchFullRebuild(baseline, static document => document.Replace(2, 2, "X"));

		// Inserting text whose trailing CR pairs with the unchanged LF.
		AssertEditAndUndoMatchFullRebuild(baseline, static document => document.Insert(1, "x\r"));

		// Inserting a lone CR before the LF completes a CRLF pair by insertion.
		AssertEditAndUndoMatchFullRebuild(baseline, static document => document.Insert(1, "\r"));

		// Deleting the entire pair joins the lines.
		AssertEditAndUndoMatchFullRebuild(baseline, static document => document.Remove(1, 2));
	}

	[TestMethod]
	public void RandomizedEdits_MatchAFullRebuild()
	{
		string[] seedTexts =
		[
			"",
			"a",
			"\r\n",
			"\n\r",
			"a\r\nb",
			"one\r\ntwo\rthree\nfour\r\n",
			"x\r\r\ny\n\nz\r",
		];

		string[] fragments = ["", "x", "\r", "\n", "\r\n", "ab\r\n", "\nx", "\r\r", "z\r\nw"];

		foreach (string seedText in seedTexts)
		{
			var document = new TextDocument(seedText);
			UnsavedChangesTracker tracker = CreateTracker(document);

			tracker.SetBaseline(seedText);

			// A fixed seed keeps the differential test deterministic while covering edit shapes beyond
			// the hand-written cases, including repeated CR/LF boundary edits that reuse the maintained
			// line view and its line-start offsets across many document versions.
			var random = new Random(20260916);

			for (int step = 0; step < 200; step++)
			{
				int offset = random.Next(document.TextLength + 1);
				int removalLength = random.Next(Math.Min(3, document.TextLength - offset) + 1);
				string insertion = fragments[random.Next(fragments.Length)];

				document.Replace(offset, removalLength, insertion);

				AssertMatchesFullRebuild(document, seedText, tracker);
			}
		}
	}

	[TestMethod]
	public void Changed_IsRaisedForDocumentEdits()
	{
		var document = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta");

		// The subscription attaches on the first read; edits can change the marked lines, so the
		// tracker raises Changed for them as well.
		_ = tracker.GetMarkedLineNumbers();

		int changedCalls = 0;
		tracker.Changed += (_, _) => changedCalls++;

		document.Insert(0, "X");

		Assert.AreEqual(1, changedCalls);
	}

	[TestMethod]
	public void Dispose_StopsTracking_AndRenderReadYieldsNoLines()
	{
		var document = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta");
		AssertLineNumbers(tracker, []);

		int changedCalls = 0;
		tracker.Changed += (_, _) => changedCalls++;

		tracker.Dispose();
		tracker.Dispose();

		// The read runs inside a margin's render pass, so it must not throw after disposal; a disposed
		// tracker reports no marked lines.
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());

		// Mutations still reject use after disposal.
		Assert.ThrowsExactly<ObjectDisposedException>(() => tracker.SetBaseline("alpha\r\nbeta"));

		// A disposed tracker is detached, so later edits must not reach it.
		document.Insert(0, "changed\r\n");

		Assert.AreEqual(0, changedCalls);
	}

	[TestMethod]
	public void SetBaseline_FromBackgroundThread_RaisesChangedOnTheCallingThread()
	{
		var document = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		int handlerThreadId = 0;
		tracker.Changed += (_, _) => handlerThreadId = Environment.CurrentManagedThreadId;

		int callingThreadId = 0;

		// SetBaseline is documented as callable from any thread and raises Changed on the calling thread,
#if AVALONIAEDIT
		// so the notification must not be marshalled to the owner thread. A dedicated thread pins the
		// off-thread call: Task.Run can run inline on the headless session's dispatcher thread, which would
		// make the "another thread" premise vacuous.
		var thread = new Thread(() =>
#else
		// so the notification must not be marshalled to the owner thread. The wait is blocking so the
		// capture is complete before the assertions run.
		Task.Run(() =>
#endif
		{
			callingThreadId = Environment.CurrentManagedThreadId;
			tracker.SetBaseline("alpha\r\nBETA");
#if AVALONIAEDIT
		})
		{
			IsBackground = true
		};

		thread.Start();

		Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "The baseline call did not complete.");
#else
		}).GetAwaiter().GetResult();
#endif

		Assert.AreNotEqual(Environment.CurrentManagedThreadId, callingThreadId, "The premise: the call must run on another thread.");
		Assert.AreEqual(callingThreadId, handlerThreadId);
	}

	[TestMethod]
	public void DocumentChangeHandlerFault_IsContainedAndTheTrackerKeepsWorking()
	{
		var document = new TextDocument("alpha\r\nbeta");
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline("alpha\r\nbeta");
		AssertLineNumbers(tracker, []);

		// The line-view update runs inside the document's change notification, which is documented to
		// contain a defect instead of letting it escape into the user's edit. A real edit always carries
		// valid change arguments, so the internal handler is invoked directly with a change whose offset
		// lies outside the document to fault the update.
		int changedCalls = 0;
		tracker.Changed += (_, _) => changedCalls++;

		var faultyChange = new DocumentChangeEventArgs(document.TextLength + 1, string.Empty, "x");

		// The fault must not escape the handler, and the subscriber is still notified.
		tracker.Document_Changed(document, faultyChange);

		Assert.AreEqual(1, changedCalls);

		// The faulted line view is rebuilt on its next read, and later edits stay tracked.
		AssertLineNumbers(tracker, []);

		document.Insert(0, "X");

		AssertLineNumbers(tracker, [1]);
	}

	/// <summary>
	/// Asserts that the tracker's incrementally maintained result matches a fresh tracker that reads
	/// the full document text.
	/// </summary>
	private static void AssertMatchesFullRebuild(TextDocument document, string baseline, UnsavedChangesTracker tracker)
	{
		UnsavedChangesTracker rebuilt = CreateTracker(document);
		rebuilt.SetBaseline(baseline);

		int[] incremental = [.. tracker.GetMarkedLineNumbers()];
		int[] full = [.. rebuilt.GetMarkedLineNumbers()];

		Assert.AreSequenceEqual(full, incremental);
	}

	/// <summary>
	/// Applies one edit to a fresh document and asserts that the incrementally tracked result matches a
	/// full rebuild, before and after the edit is undone (the reverse edit must be survived as well).
	/// </summary>
	private static void AssertEditAndUndoMatchFullRebuild(string baseline, Action<TextDocument> edit)
	{
		var document = new TextDocument(baseline);
		UnsavedChangesTracker tracker = CreateTracker(document);

		tracker.SetBaseline(baseline);
		AssertLineNumbers(tracker, []);

		edit(document);

		AssertMatchesFullRebuild(document, baseline, tracker);

		document.UndoStack.Undo();

		AssertMatchesFullRebuild(document, baseline, tracker);
	}

	private static UnsavedChangesTracker CreateTracker(TextDocument document)
		=> new(() => document);

	private static void AssertLineNumbers(UnsavedChangesTracker tracker, int[] expected)
	{
		int[] actual = [.. tracker.GetMarkedLineNumbers()];
		Assert.AreSequenceEqual(expected, actual);
	}
}
