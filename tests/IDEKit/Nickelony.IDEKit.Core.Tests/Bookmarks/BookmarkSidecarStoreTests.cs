namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class BookmarkSidecarStoreTests
{
	private const string Extension = ".bkmrk";

	[TestMethod]
	public void Constructor_NullExtension_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new BookmarkSidecarStore(null!));
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("   ", DisplayName = "WhitespaceOnly")]
	public void Constructor_BlankExtension_Throws(string extension)
	{
		Assert.ThrowsExactly<ArgumentException>(() => new BookmarkSidecarStore(extension));
	}

	[TestMethod]
	[DataRow(".", DisplayName = "DotOnly")]
	[DataRow("a/b", DisplayName = "ForwardSlash")]
	[DataRow("x\\y", DisplayName = "Backslash")]
	public void Constructor_UnusableExtension_Throws(string extension)
	{
		Assert.ThrowsExactly<ArgumentException>(() => new BookmarkSidecarStore(extension));
	}

	[TestMethod]
	public void TrySave_ThenTryLoad_RoundTripsTheStoredLineNumbers()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("script.lua");
		var store = new BookmarkSidecarStore(Extension);

		Assert.IsTrue(store.TrySave(documentPath, [2, 5, 9]));
		Assert.IsTrue(store.TryLoad(documentPath, out IReadOnlyList<int> restored));
		CollectionAssert.AreEqual(new[] { 2, 5, 9 }, restored.ToArray());
	}

	[TestMethod]
	public void TrySave_DuplicateAndUnorderedNumbers_AreStoredOnceInOrder()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("script.lua");
		var store = new BookmarkSidecarStore(Extension);

		Assert.IsTrue(store.TrySave(documentPath, [5, 1, 5]));
		Assert.IsTrue(store.TryLoad(documentPath, out IReadOnlyList<int> restored));
		CollectionAssert.AreEqual(new[] { 1, 5 }, restored.ToArray());
	}

	[TestMethod]
	public void TrySave_WritesTheSidecarNextToTheDocument()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("script.lua");
		var store = new BookmarkSidecarStore(Extension);

		Assert.IsTrue(store.TrySave(documentPath, [1]));
		Assert.IsTrue(File.Exists(documentPath + Extension), "The sidecar must sit next to the document.");
	}

	[TestMethod]
	public void TrySave_EmptyList_DeletesTheSidecar()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("script.lua");
		var store = new BookmarkSidecarStore(Extension);

		Assert.IsTrue(store.TrySave(documentPath, [4]));
		Assert.IsTrue(File.Exists(documentPath + Extension));

		Assert.IsTrue(store.TrySave(documentPath, []));
		Assert.IsFalse(File.Exists(documentPath + Extension), "An empty set deletes the sidecar.");
		Assert.IsTrue(store.TryLoad(documentPath, out IReadOnlyList<int> restored));
		Assert.AreEqual(0, restored.Count);
	}

	[TestMethod]
	public void TrySave_OnlyIgnoredNumbers_FailsAndKeepsTheExistingSidecar()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("script.lua");
		var store = new BookmarkSidecarStore(Extension);

		Assert.IsTrue(store.TrySave(documentPath, [1, 2]));

		// Entries below one are ignored while at least one valid entry remains; a non-empty set whose
		// entries are all ignored is a caller bug that must not delete the saved lines.
		Assert.IsFalse(store.TrySave(documentPath, [0, -3]));
		Assert.IsTrue(File.Exists(documentPath + Extension), "A rejected set must keep the existing sidecar.");
		Assert.IsTrue(store.TryLoad(documentPath, out IReadOnlyList<int> restored));
		CollectionAssert.AreEqual(new[] { 1, 2 }, restored.ToArray());
	}

	[TestMethod]
	public void TrySave_CustomExtension_IsHonored()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("script.lua");
		var store = new BookmarkSidecarStore(".markers");

		Assert.IsTrue(store.TrySave(documentPath, [3]));

		Assert.IsTrue(File.Exists(documentPath + ".markers"));
		Assert.IsFalse(File.Exists(documentPath + Extension));
	}

	[TestMethod]
	public void TrySave_UnwritablePath_FailsAndTryLoadReportsNothingStored()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string blockerPath = directory.GetFilePath("blocker");

		File.WriteAllText(blockerPath, "not a directory");

		// 'blocker' is a file, so a sidecar path below it cannot be written or read.
		string blockedDocumentPath = Path.Combine(blockerPath, "script.lua");
		var store = new BookmarkSidecarStore(Extension);

		Assert.IsFalse(store.TrySave(blockedDocumentPath, [1]));

		// No sidecar exists at the blocked path, so the load reports success with an empty list.
		Assert.IsTrue(store.TryLoad(blockedDocumentPath, out IReadOnlyList<int> restored));
		Assert.AreEqual(0, restored.Count);
	}

	[TestMethod]
	public void TryLoad_MissingSidecar_SucceedsWithNoLineNumbers()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("missing.lua");
		var store = new BookmarkSidecarStore(Extension);

		Assert.IsTrue(store.TryLoad(documentPath, out IReadOnlyList<int> restored));
		Assert.AreEqual(0, restored.Count);
	}

	[TestMethod]
	public void TryLoad_SkipsMalformedEntriesAndNormalizesOrdering()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("script.lua");
		var store = new BookmarkSidecarStore(Extension);

		// The sidecar is written raw so the load normalization is exercised directly: a skipped
		// non-integer, a duplicate, and unordered entries.
		File.WriteAllLines(documentPath + Extension, ["7", "not-a-number", "3", "3", "1", "0"]);

		Assert.IsTrue(store.TryLoad(documentPath, out IReadOnlyList<int> restored));
		CollectionAssert.AreEqual(new[] { 1, 3, 7 }, restored.ToArray());
	}

	[TestMethod]
	public void TryLoad_EmptyFileAndOnlyIgnoredEntries_SucceedWithNoLineNumbers()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("script.lua");
		var store = new BookmarkSidecarStore(Extension);

		File.WriteAllText(documentPath + Extension, string.Empty);

		Assert.IsTrue(store.TryLoad(documentPath, out IReadOnlyList<int> emptyFile));
		Assert.AreEqual(0, emptyFile.Count);

		File.WriteAllLines(documentPath + Extension, ["0", string.Empty, "x"]);

		Assert.IsTrue(store.TryLoad(documentPath, out IReadOnlyList<int> ignoredEntries));
		Assert.AreEqual(0, ignoredEntries.Count);
	}

	[TestMethod]
	public void TryLoad_SidecarPathOccupiedByDirectory_Fails()
	{
		using var directory = new TemporaryDirectory("bookmark-sidecar-");
		string documentPath = directory.GetFilePath("script.lua");

		Directory.CreateDirectory(documentPath + Extension);

		var store = new BookmarkSidecarStore(Extension);

		Assert.IsFalse(store.TryLoad(documentPath, out IReadOnlyList<int> restored));
		Assert.AreEqual(0, restored.Count);
	}

	[TestMethod]
	public void TrySave_NullArguments_Throw()
	{
		var store = new BookmarkSidecarStore(Extension);

		Assert.ThrowsExactly<ArgumentNullException>(() => store.TrySave(null!, [1]));
		Assert.ThrowsExactly<ArgumentNullException>(() => store.TrySave("file.lua", null!));
	}

	[TestMethod]
	public void TryLoad_NullFilePath_Throws()
	{
		var store = new BookmarkSidecarStore(Extension);

		Assert.ThrowsExactly<ArgumentNullException>(() => store.TryLoad(null!, out _));
	}
}
