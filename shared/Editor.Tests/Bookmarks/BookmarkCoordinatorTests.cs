#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
using Nickelony.IDEKit.Core.Bookmarks;
using System.Collections.ObjectModel;
using System.IO;
using static Nickelony.IDEKit.AvaloniaEdit.Tests.DocumentTestHelpers;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using Nickelony.IDEKit.Core.Bookmarks;
using System.Collections.ObjectModel;
using System.IO;
using static Nickelony.IDEKit.AvalonEdit.Tests.DocumentTestHelpers;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[TestClass]
public sealed class BookmarkCoordinatorTests
{
	private readonly TemporaryDirectory _tempDirectory = new("BookmarkCoordinatorTests");

	private string CreateTempPath()
		=> _tempDirectory.GetFilePath("document.txt");

	[TestCleanup]
	public void Cleanup()
		=> _tempDirectory.Dispose();

	[TestMethod]
	public void SaveBookmarks_ThenRestoreBookmarks_RoundTripsThroughStore()
	{
		string filePath = CreateTempPath();
		var document = new TextDocument("one\r\ntwo\r\nthree\r\nfour");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new BookmarkSidecarStore(".bookmarks");

		coordinator.ToggleBookmark(GetLineOffset(document, 2));
		coordinator.ToggleBookmark(GetLineOffset(document, 4));

		coordinator.SaveBookmarks(store, filePath);
		coordinator.ClearBookmarks();

		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());

		coordinator.RestoreBookmarks(store, filePath);

		Assert.HasCount(2, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
		Assert.AreEqual(4, coordinator.GetMarkedLineNumbers()[1]);
	}

	[TestMethod]
	public void Bookmarks_FollowEditsAndUndo()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		// An insertion above the bookmark moves it with the text.
		document.Insert(0, "zero\r\n");

		Assert.AreSequenceEqual(new[] { 3 }, coordinator.GetMarkedLineNumbers());

		// Undo shifts the anchors back with the restored text.
		document.UndoStack.Undo();

		Assert.AreSequenceEqual(new[] { 2 }, coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void SaveBookmarks_ProviderReturnsNull_ThrowsAndKeepsTheStoredBookmarks()
	{
		string filePath = CreateTempPath();
		string sidecarPath = filePath + ".bookmarks";
		var coordinator = new BookmarkCoordinator(static () => null!);
		var store = new BookmarkSidecarStore(".bookmarks");

		store.TrySave(filePath, [3]);

		Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.SaveBookmarks(store, filePath));

		// Saving an empty set deletes the sidecar, so the failed save must leave the stored bookmarks alone.
		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> storedAfterFailedSave));
		Assert.AreSequenceEqual([3], storedAfterFailedSave);
		Assert.IsTrue(File.Exists(sidecarPath));
	}

	[TestMethod]
	public void SaveBookmarks_AfterDocumentSwapBeforeRestore_ThrowsAndKeepsTheStoredBookmarks()
	{
		string filePath = CreateTempPath();
		var firstDocument = new TextDocument("one\r\ntwo");
		TextDocument currentDocument = firstDocument;
		var coordinator = new BookmarkCoordinator(() => currentDocument);
		var store = new BookmarkSidecarStore(".bookmarks");

		coordinator.ToggleBookmark(GetLineOffset(firstDocument, 1));
		store.TrySave(filePath, [1]);

		// The provider moves to a new document before the host restores bookmarks for it.
		currentDocument = new TextDocument("one\r\ntwo");

		Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.SaveBookmarks(store, filePath));

		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> storedAfterSwap));
		Assert.AreSequenceEqual([1], storedAfterSwap);
	}

	[TestMethod]
	public void SaveBookmarks_AfterRestoreForCurrentDocument_Succeeds()
	{
		string filePath = CreateTempPath();
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new BookmarkSidecarStore(".bookmarks");

		store.TrySave(filePath, [2]);

		coordinator.RestoreBookmarks(store, filePath);

		Assert.IsTrue(coordinator.SaveBookmarks(store, filePath));
		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> storedAfterRestore));
		Assert.AreSequenceEqual([2], storedAfterRestore);
	}

	[TestMethod]
	public void SaveBookmarks_WhenStoreReportsFailure_ReturnsFalseAndKeepsTheBookmarks()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new RecordingBookmarkStore { SaveResult = false };

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		Assert.IsFalse(coordinator.SaveBookmarks(store, "document.txt"));
		Assert.HasCount(1, store.Saves);

		// A failed save leaves the in-memory bookmarks untouched.
		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void SaveBookmarks_PassesTheAscendingMarkedLinesThroughTheStore()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree\r\nfour");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new RecordingBookmarkStore();

		// Toggled out of order; the coordinator reports bookmarks ascending.
		coordinator.ToggleBookmark(GetLineOffset(document, 4));
		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		Assert.IsTrue(coordinator.SaveBookmarks(store, "document.txt"));
		Assert.HasCount(1, store.Saves);
		Assert.AreEqual("document.txt", store.Saves[0].FilePath);
		Assert.AreSequenceEqual(new[] { 2, 4 }, store.Saves[0].LineNumbers);
	}

	[TestMethod]
	public void SaveBookmarks_WithoutBookmarks_PassesAnEmptyListThroughTheStore()
	{
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new RecordingBookmarkStore();

		// Restoring an empty set binds the (empty) bookmark set to the document; only then can the
		// current, empty set be saved.
		coordinator.RestoreBookmarks([]);

		Assert.IsTrue(coordinator.SaveBookmarks(store, "document.txt"));

		// The store decides what an empty save means; the sidecar store deletes the stored bookmarks.
		Assert.HasCount(1, store.Saves);
		Assert.IsEmpty(store.Saves[0].LineNumbers);
	}

	[TestMethod]
	public void SaveBookmarks_BeforeAnyBinding_ThrowsAndDoesNotInvokeTheStore()
	{
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new RecordingBookmarkStore();

		// A coordinator whose bookmarks were never bound would save an empty set, which deletes the
		// stored bookmarks; the save fails instead of touching the store.
		Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.SaveBookmarks(store, "document.txt"));
		Assert.IsEmpty(store.Saves);
	}

	[TestMethod]
	public void RestoreBookmarks_AppliesTheStoredLinesThroughTheStore()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new RecordingBookmarkStore { LoadResult = [2] };

		coordinator.RestoreBookmarks(store, "document.txt");

		Assert.AreSequenceEqual(new[] { "document.txt" }, store.LoadedPaths);
		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void RestoreBookmarks_OutOfRangeLines_AreIgnored()
	{
		string filePath = CreateTempPath();
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new BookmarkSidecarStore(".bookmarks");

		store.TrySave(filePath, [2, 99]);

		coordinator.RestoreBookmarks(store, filePath);

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void ToggleBookmark_AddsBookmarkForContainingLine()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void ToggleBookmark_ToggleAgain_RemovesBookmark()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));
		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void GetMarkedLineNumbers_ReturnsReadOnlyView()
	{
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));

		IReadOnlyList<int> lines = coordinator.GetMarkedLineNumbers();

		// The view is a read-only wrapper, not a mutable list handed out behind an interface.
		Assert.IsInstanceOfType<ReadOnlyCollection<int>>(lines);

		// Element mutation is rejected through the mutable interface as well.
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<int>)lines).Add(lines[0]));
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<int>)lines)[0] = 99);

		// The rejected mutations leave the coordinator's bookmarks untouched.
		Assert.AreSequenceEqual(new[] { 1 }, coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void GetMarkedLineNumbers_ReturnsAscendingNumbers()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 3));
		coordinator.ToggleBookmark(GetLineOffset(document, 1));

		Assert.AreEqual(1, coordinator.GetMarkedLineNumbers()[0]);
		Assert.AreEqual(3, coordinator.GetMarkedLineNumbers()[1]);
	}

	[TestMethod]
	public void GetNextBookmarkLine_WrapsToFirstBookmark()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 3));

		DocumentLine? next = coordinator.GetNextBookmarkLine(GetLineOffset(document, 3));

		Assert.IsNotNull(next);
		Assert.AreEqual(1, next.LineNumber);
	}

	[TestMethod]
	public void GetPreviousBookmarkLine_WrapsToLastBookmark()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 3));

		DocumentLine? previous = coordinator.GetPreviousBookmarkLine(GetLineOffset(document, 1));

		Assert.IsNotNull(previous);
		Assert.AreEqual(3, previous.LineNumber);
	}

	[TestMethod]
	public void GetNextAndPreviousBookmarkLine_NoBookmarks_ReturnNull()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		Assert.IsNull(coordinator.GetNextBookmarkLine(0));
		Assert.IsNull(coordinator.GetPreviousBookmarkLine(0));
	}

	[TestMethod]
	public void GetNextAndPreviousBookmarkLine_OutOfRangeOffset_IsClamped()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 3));

		DocumentLine? next = coordinator.GetNextBookmarkLine(-100);
		DocumentLine? previous = coordinator.GetPreviousBookmarkLine(document.TextLength + 100);

		Assert.IsNotNull(next);
		Assert.AreEqual(3, next.LineNumber);
		Assert.IsNotNull(previous);
		Assert.AreEqual(1, previous.LineNumber);
	}

	[TestMethod]
	public void GetMarkedLineNumbers_WhenDocumentProviderReturnsNull_ReturnsEmpty()
	{
		var coordinator = new BookmarkCoordinator(() => null!);

		// The render-facing read must not throw.
		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void GetNextAndPreviousBookmarkLine_WhenDocumentProviderReturnsNull_ReturnNull()
	{
		var coordinator = new BookmarkCoordinator(() => null!);

		// Reads report the absence of bookmarks instead of throwing.
		Assert.IsNull(coordinator.GetNextBookmarkLine(0));
		Assert.IsNull(coordinator.GetPreviousBookmarkLine(0));
	}

	[TestMethod]
	public void IsBoundToCurrentDocument_ReflectsTheBindingAndToleratesANullProvider()
	{
		var document = new TextDocument("one\r\ntwo");
		TextDocument? currentDocument = document;
		var coordinator = new BookmarkCoordinator(() => currentDocument!);

		// Never bound yet: a save would delete the stored bookmarks, so the state reports as unbound.
		Assert.IsFalse(coordinator.IsBoundToCurrentDocument);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));

		Assert.IsTrue(coordinator.IsBoundToCurrentDocument);

		// The provider moves to a new document before the host restores bookmarks for it.
		currentDocument = new TextDocument("one\r\ntwo");

		Assert.IsFalse(coordinator.IsBoundToCurrentDocument);

		coordinator.RestoreBookmarks([2]);

		Assert.IsTrue(coordinator.IsBoundToCurrentDocument);

		// The state read tolerates a temporarily missing document instead of throwing.
		currentDocument = null;

		Assert.IsFalse(coordinator.IsBoundToCurrentDocument);
	}

	[TestMethod]
	public void Clear_RemovesAllBookmarksAndRaisesChanged()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		int changedCalls = 0;
		coordinator.Changed += (_, _) => changedCalls++;

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		coordinator.ClearBookmarks();

		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(3, changedCalls);
	}

	[TestMethod]
	public void Clear_WhenNoBookmarksExist_StillRaisesChanged()
	{
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);

		int changedCalls = 0;
		coordinator.Changed += (_, _) => changedCalls++;

		coordinator.ClearBookmarks();

		Assert.AreEqual(1, changedCalls);
	}

	[TestMethod]
	public void Clear_AfterDocumentSwap_BindsToTheCurrentDocumentAndPersistsTheClear()
	{
		string filePath = CreateTempPath();
		string sidecarPath = filePath + ".bookmarks";
		var firstDocument = new TextDocument("one\r\ntwo");
		TextDocument currentDocument = firstDocument;
		var coordinator = new BookmarkCoordinator(() => currentDocument);
		var store = new BookmarkSidecarStore(".bookmarks");

		coordinator.ToggleBookmark(GetLineOffset(firstDocument, 1));
		Assert.IsTrue(coordinator.SaveBookmarks(store, filePath));

		// The provider moves to a new document before the host restores bookmarks for it.
		currentDocument = new TextDocument("one\r\ntwo");

		coordinator.ClearBookmarks();

		// The clear belongs to the document being edited, so saving it succeeds and deletes the sidecar
		// instead of leaving stored bookmarks that a later restore would resurrect.
		Assert.IsTrue(coordinator.SaveBookmarks(store, filePath));
		Assert.IsTrue(store.TryLoad(filePath, out IReadOnlyList<int> linesAfterClear));
		Assert.IsEmpty(linesAfterClear);
		Assert.IsFalse(File.Exists(sidecarPath));
	}

	[TestMethod]
	public void Clear_WhenDocumentProviderReturnsNull_Throws()
	{
		var coordinator = new BookmarkCoordinator(() => null!);

		Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.ClearBookmarks());
	}

	[TestMethod]
	public void ToggleBookmark_RaisesChangedEvent()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		int changedCalls = 0;
		coordinator.Changed += (_, _) => changedCalls++;

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 1));

		Assert.AreEqual(2, changedCalls);
	}

	[TestMethod]
	public void Edits_DoNotRaiseChanged()
	{
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));

		int changedCalls = 0;
		coordinator.Changed += (_, _) => changedCalls++;

		document.Insert(0, "x");

		// The event covers explicit bookmark mutations only; a resolved set that moves with an edit is
		// re-read by the consumer instead.
		Assert.AreEqual(0, changedCalls);
	}

	[TestMethod]
	public void Restore_AddsBookmarksForValidLineNumbers()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.RestoreBookmarks([2, 99, -1]);

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void Restore_DuplicateLineNumbers_AreIgnored()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.RestoreBookmarks([2, 2, 3, 2]);

		Assert.AreSequenceEqual([2, 3], [.. coordinator.GetMarkedLineNumbers()]);
	}

	[TestMethod]
	public void Restore_ReplacesExistingBookmarks()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.RestoreBookmarks([2]);

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void Restore_RaisesChangedEvent()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		int changedCalls = 0;
		coordinator.Changed += (_, _) => changedCalls++;

		coordinator.RestoreBookmarks([1, 2]);

		Assert.AreEqual(1, changedCalls);
	}

	[TestMethod]
	public void Bookmarks_FollowContent_WhenLineBreakInsertedAtLineStart()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		// The line break pushes the marked content down; the bookmark follows the content instead of
		// staying on the inserted blank line.
		document.Insert(GetLineOffset(document, 2), "\r\n");

		Assert.AreSequenceEqual(new[] { 3 }, coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void RestoreBookmarks_WhenStoreReportsFailure_LeavesTheCoordinatorUnchanged()
	{
		var document = new TextDocument("one\r\ntwo");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new RecordingBookmarkStore { TryLoadResult = false, LoadResult = [1] };

		Assert.IsFalse(coordinator.RestoreBookmarks(store, "document.txt"));

		// A failed load must leave the coordinator unbound, so a later save fails instead of
		// persisting an empty set that would delete the stored bookmarks.
		Assert.IsFalse(coordinator.IsBoundToCurrentDocument);
		Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.SaveBookmarks(store, "document.txt"));
	}

	[TestMethod]
	public void RestoreBookmarks_WhenStoreReportsEmptyLoad_StillRebindsAndClears()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var store = new RecordingBookmarkStore();

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		// An empty load is a successful result ("nothing was stored"), so the bookmarks are replaced
		// and the coordinator stays bound.
		Assert.IsTrue(coordinator.RestoreBookmarks(store, "document.txt"));
		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
		Assert.IsTrue(coordinator.IsBoundToCurrentDocument);
	}

	[TestMethod]
	public void GetNextAndPreviousBookmarkLine_SingleBookmark_WrapToItself()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		DocumentLine? next = coordinator.GetNextBookmarkLine(GetLineOffset(document, 2));
		DocumentLine? previous = coordinator.GetPreviousBookmarkLine(GetLineOffset(document, 2));

		Assert.IsNotNull(next);
		Assert.IsNotNull(previous);
		Assert.AreEqual(2, next.LineNumber);
		Assert.AreEqual(2, previous.LineNumber);
	}

	[TestMethod]
	public void GetNextAndPreviousBookmarkLine_BetweenTwoBookmarks_ReturnsAdjacentWithoutWrapping()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree\r\nfour");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 4));

		// The origins sit strictly between the bookmarks, so both reads resolve the adjacent bookmark
		// instead of wrapping around the document ends.
		DocumentLine? next = coordinator.GetNextBookmarkLine(GetLineOffset(document, 2));
		DocumentLine? previous = coordinator.GetPreviousBookmarkLine(GetLineOffset(document, 3));

		Assert.IsNotNull(next);
		Assert.IsNotNull(previous);
		Assert.AreEqual(4, next.LineNumber);
		Assert.AreEqual(1, previous.LineNumber);
	}

	[TestMethod]
	public void MarkedLines_ReflectDocumentEditsAfterCaching()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);

		document.Insert(0, "inserted\r\n");

		// The bookmark follows its line, and the cached result is invalidated by the edit.
		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(3, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void DocumentSwap_DiscardsBookmarksFromPreviousDocument()
	{
		var firstDocument = new TextDocument("one\r\ntwo\r\nthree");
		var secondDocument = new TextDocument("alpha\r\nbeta");
		TextDocument currentDocument = firstDocument;

		var coordinator = new BookmarkCoordinator(() => currentDocument);
		coordinator.ToggleBookmark(GetLineOffset(firstDocument, 2));

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());

		currentDocument = secondDocument;

		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void DocumentSwap_ToggleBookmark_TargetsNewDocumentOnly()
	{
		var firstDocument = new TextDocument("one\r\ntwo\r\nthree");
		var secondDocument = new TextDocument("alpha\r\nbeta");
		TextDocument currentDocument = firstDocument;

		var coordinator = new BookmarkCoordinator(() => currentDocument);
		coordinator.ToggleBookmark(GetLineOffset(firstDocument, 2));

		currentDocument = secondDocument;
		coordinator.ToggleBookmark(GetLineOffset(secondDocument, 1));

		IReadOnlyList<int> bookmarkedLines = coordinator.GetMarkedLineNumbers();

		Assert.HasCount(1, bookmarkedLines);
		Assert.AreEqual(1, bookmarkedLines[0]);
	}

	[TestMethod]
	public void DocumentSwap_Restore_TargetsNewDocument()
	{
		var firstDocument = new TextDocument("one\r\ntwo\r\nthree");
		var secondDocument = new TextDocument("alpha\r\nbeta\r\ngamma");
		TextDocument currentDocument = firstDocument;

		var coordinator = new BookmarkCoordinator(() => currentDocument);
		coordinator.RestoreBookmarks([2]);

		currentDocument = secondDocument;
		coordinator.RestoreBookmarks([3]);

		IReadOnlyList<int> bookmarkedLines = coordinator.GetMarkedLineNumbers();

		Assert.HasCount(1, bookmarkedLines);
		Assert.AreEqual(3, bookmarkedLines[0]);
	}

	[TestMethod]
	public void ToggleBookmark_TextInsertedBeforeIt_KeepsTheMarkedLine()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		document.Insert(0, "X");

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void GetMarkedLineNumbers_CollapsedAnchors_DeduplicatesAndTheNextTogglePrunesTheDuplicate()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 1));
		coordinator.ToggleBookmark(GetLineOffset(document, 2));

		Assert.HasCount(2, coordinator.GetMarkedLineNumbers());

		// Removing the line break merges the two bookmarked lines, so both anchors resolve to one line.
		document.Remove(document.GetLineByNumber(1).EndOffset, document.GetLineByNumber(1).DelimiterLength);

		// Reading de-duplicates without pruning the anchors.
		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());

		// The next mutation prunes the duplicate anchor, so one toggle clears the merged bookmark
		// instead of leaving an orphaned duplicate behind.
		coordinator.ToggleBookmark(GetLineOffset(document, 1));

		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void DocumentSwap_ReadDoesNotDiscardBookmarksOfTheAnchorDocument()
	{
		var firstDocument = new TextDocument("one\r\ntwo\r\nthree");
		var secondDocument = new TextDocument("alpha\r\nbeta");

		TextDocument currentDocument = firstDocument;

		var coordinator = new BookmarkCoordinator(() => currentDocument);
		coordinator.ToggleBookmark(GetLineOffset(firstDocument, 2));

		currentDocument = secondDocument;

		// Reads report no bookmarks for the other document without discarding the anchors.
		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());

		currentDocument = firstDocument;

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(2, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void ToggleBookmark_AfterAnchorsCollapseOntoOneLine_RemovesBookmarkInOneToggle()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(GetLineOffset(document, 2));
		coordinator.ToggleBookmark(GetLineOffset(document, 3));

		// Deleting the content lets both anchors survive and collapse onto the first line.
		document.Remove(0, document.TextLength);

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(1, coordinator.GetMarkedLineNumbers()[0]);

		coordinator.ToggleBookmark(0);

		// A single toggle must clear the collapsed bookmark instead of leaving an orphaned anchor behind.
		Assert.IsEmpty(coordinator.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void Restore_WhenDocumentProviderReturnsNull_PreservesExistingBookmarks()
	{
		var document = new TextDocument("one\r\ntwo");
		TextDocument? current = document;

		var coordinator = new BookmarkCoordinator(() => current!);
		coordinator.ToggleBookmark(GetLineOffset(document, 1));

		current = null;

		Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.RestoreBookmarks([2]));

		// The failed restore must not discard the bookmarks that were already tracked.
		current = document;

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(1, coordinator.GetMarkedLineNumbers()[0]);
	}

	[TestMethod]
	public void ToggleBookmark_EmptyDocument_BookmarksFirstLine()
	{
		var document = new TextDocument();
		var coordinator = new BookmarkCoordinator(() => document);

		coordinator.ToggleBookmark(0);

		Assert.HasCount(1, coordinator.GetMarkedLineNumbers());
		Assert.AreEqual(1, coordinator.GetMarkedLineNumbers()[0]);
	}
}
