using System.Globalization;
using System.Text;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class SidecarLineFileTests
{
	private const string SidecarExtension = ".bkmrk";

	private readonly List<TemporaryDirectory> _directories = [];

	/// <summary>
	/// Restores the sidecar as the tolerant <see cref="SidecarLineFile.TryRestore"/> contract does,
	/// returning an empty list for a missing or unreadable sidecar. The restore scenarios assert the
	/// returned numbers; <see cref="SidecarLineFileAdditionalTests"/> covers the failure
	/// classification itself.
	/// </summary>
	private static IReadOnlyList<int> RestoreLines(string filePath, string sidecarExtension)
		=> SidecarLineFile.TryRestore(filePath, sidecarExtension, out IReadOnlyList<int> lineNumbers)
			? lineNumbers
			: [];

	private string CreateTempPath(out string directory)
	{
		var temp = new TemporaryDirectory("SidecarLineFileTests-");

		_directories.Add(temp);
		directory = temp.Path;
		return temp.GetFilePath("document.txt");
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Best-effort removal of this instance's temporary directories only, so a concurrent test
		// run keeps its own fixtures.
		foreach (TemporaryDirectory directory in _directories)
			directory.Dispose();

		_directories.Clear();
	}

	[TestMethod]
	public void Save_ThenRestore_RoundTripsLineNumbers()
	{
		string filePath = CreateTempPath(out _);

		bool saved = SidecarLineFile.Save(filePath, [1, 5, 9], SidecarExtension);
		Assert.IsTrue(saved);

		IReadOnlyList<int> restored = RestoreLines(filePath, SidecarExtension);
		CollectionAssert.AreEqual(new[] { 1, 5, 9 }, restored.ToArray());
	}

	[TestMethod]
	public void Save_WritesOneLineNumberPerLineWithThePortableNewline()
	{
		// The content is byte-identical on every platform: one invariant-culture line number per
		// line, each terminated by a single \n regardless of the platform newline.
		string filePath = CreateTempPath(out _);

		SidecarLineFile.Save(filePath, [1, 5, 9], SidecarExtension);

		Assert.AreEqual("1\n5\n9\n", File.ReadAllText(SidecarLineFile.GetSidecarPath(filePath, SidecarExtension)));
	}

	[TestMethod]
	public void Save_WritesBomlessUtf8Bytes()
	{
		// The byte-level assertion pins the encoding: UTF-8 without a byte-order mark and LF endings.
		string filePath = CreateTempPath(out _);

		SidecarLineFile.Save(filePath, [1, 5, 9], SidecarExtension);

		byte[] bytes = File.ReadAllBytes(SidecarLineFile.GetSidecarPath(filePath, SidecarExtension));

		CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("1\n5\n9\n"), bytes);
	}

	[TestMethod]
	[DoNotParallelize]
	public void SaveAndRestore_UnderNonInvariantCulture_WritesInvariantDigits()
	{
		// Entries are written and parsed with the invariant culture, so the current culture cannot
		// change the sidecar contents.
		string filePath = CreateTempPath(out _);
		CultureInfo originalCulture = CultureInfo.CurrentCulture;
		CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
			CultureInfo.CurrentUICulture = new CultureInfo("ar-SA");

			Assert.IsTrue(SidecarLineFile.Save(filePath, [1, 5, 9], SidecarExtension));
			CollectionAssert.AreEqual(new[] { 1, 5, 9 }, RestoreLines(filePath, SidecarExtension).ToArray());
			CollectionAssert.AreEqual(new[] { "1", "5", "9" }, File.ReadAllLines(SidecarLineFile.GetSidecarPath(filePath, SidecarExtension)));
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
		}
	}

	[TestMethod]
	public void Save_DuplicatesAndUnsortedInput_AreNormalized()
	{
		string filePath = CreateTempPath(out _);

		SidecarLineFile.Save(filePath, [9, 1, 5, 1], SidecarExtension);

		IReadOnlyList<int> restored = RestoreLines(filePath, SidecarExtension);
		CollectionAssert.AreEqual(new[] { 1, 5, 9 }, restored.ToArray());
	}

	[TestMethod]
	public void Save_EmptySet_DeletesExistingSidecar()
	{
		string filePath = CreateTempPath(out _);
		SidecarLineFile.Save(filePath, [1, 2], SidecarExtension);

		Assert.IsTrue(File.Exists(SidecarLineFile.GetSidecarPath(filePath, SidecarExtension)));

		SidecarLineFile.Save(filePath, [], SidecarExtension);

		Assert.IsFalse(File.Exists(SidecarLineFile.GetSidecarPath(filePath, SidecarExtension)));
		Assert.AreEqual(0, RestoreLines(filePath, SidecarExtension).Count);
	}

	[TestMethod]
	public void Save_EmptyFilePath_ReturnsFalse()
	{
		bool saved = SidecarLineFile.Save(string.Empty, [1], SidecarExtension);

		Assert.IsFalse(saved);
	}

	[TestMethod]
	public void Save_InvalidPath_ReturnsFalseAndRestoresNothing()
	{
		// A path the file system rejects is reported as a failed operation, matching TryRestore.
		Assert.IsFalse(SidecarLineFile.Save("bad\0name.txt", [1], SidecarExtension));
		Assert.IsEmpty(RestoreLines("bad\0name.txt", SidecarExtension));
	}

	[TestMethod]
	public void TryRestore_InvalidEntries_AreSkipped()
	{
		string filePath = CreateTempPath(out _);
		File.WriteAllLines(SidecarLineFile.GetSidecarPath(filePath, SidecarExtension), ["1", "abc", "2", "-3", "0"]);

		IReadOnlyList<int> restored = RestoreLines(filePath, SidecarExtension);

		// Invalid and non-positive entries are ignored.
		CollectionAssert.AreEqual(new[] { 1, 2 }, restored.ToArray());
	}

	[TestMethod]
	public void GetSidecarPath_CustomExtension_IsHonored()
	{
		Assert.AreEqual("doc.txt.bkmrk", SidecarLineFile.GetSidecarPath("doc.txt", ".bkmrk"));
	}

	[TestMethod]
	public void GetSidecarPath_NullExtension_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => SidecarLineFile.GetSidecarPath("doc.txt", null!));
	}

	[TestMethod]
	public void GetSidecarPath_BlankExtension_Throws()
	{
		// A blank extension must not degenerate to the document path, so it is rejected instead of
		// falling back to any default.
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("doc.txt", string.Empty));
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("doc.txt", "   "));
	}

	[TestMethod]
	public void GetSidecarPath_BlankFilePath_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("   ", SidecarExtension));
	}

	[TestMethod]
	public void GetSidecarPath_TrailingDirectorySeparator_Throws()
	{
		// A trailing separator names a directory; concatenating would silently place the sidecar
		// inside that directory instead of next to a document.
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("doc.txt" + Path.DirectorySeparatorChar, SidecarExtension));
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("doc.txt" + Path.AltDirectorySeparatorChar, SidecarExtension));
	}

	[TestMethod]
	public void Save_TrailingDirectorySeparator_ReturnsFalse()
	{
		string directoryPath = CreateTempPath(out _) + Path.DirectorySeparatorChar;

		Assert.IsFalse(SidecarLineFile.Save(directoryPath, [1], SidecarExtension));
	}

	[TestMethod]
	public void TryRestore_TrailingDirectorySeparator_ReturnsEmpty()
	{
		string directoryPath = CreateTempPath(out _) + Path.DirectorySeparatorChar;

		Assert.AreEqual(0, RestoreLines(directoryPath, SidecarExtension).Count);
	}

	[TestMethod]
	public void Save_NullExtension_Throws()
	{
		string filePath = CreateTempPath(out _);

		Assert.ThrowsExactly<ArgumentNullException>(() => SidecarLineFile.Save(filePath, [1], null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => SidecarLineFile.TryRestore(filePath, null!, out _));
	}

	[TestMethod]
	public void Save_BlankExtension_Throws()
	{
		string filePath = CreateTempPath(out _);

		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.Save(filePath, [2], sidecarExtension: string.Empty));
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.TryRestore(filePath, sidecarExtension: string.Empty, out _));
	}

	[TestMethod]
	public void Save_ReplacesSidecarWithoutLeavingTemporaryFile()
	{
		string filePath = CreateTempPath(out string directory);

		SidecarLineFile.Save(filePath, [1], SidecarExtension);
		SidecarLineFile.Save(filePath, [1, 2], SidecarExtension);

		Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
		CollectionAssert.AreEqual(new[] { 1, 2 }, RestoreLines(filePath, SidecarExtension).ToArray());
	}

	[TestMethod]
	public void Save_NonPositiveLineNumbers_AreIgnoredWhileValidNumbersRemain()
	{
		string filePath = CreateTempPath(out _);

		SidecarLineFile.Save(filePath, [1, 0, -3, 2], SidecarExtension);

		CollectionAssert.AreEqual(new[] { 1, 2 }, RestoreLines(filePath, SidecarExtension).ToArray());
	}

	[TestMethod]
	public void Save_DegenerateSet_FailsAndKeepsExistingSidecar()
	{
		string filePath = CreateTempPath(out _);

		SidecarLineFile.Save(filePath, [3, 7], SidecarExtension);

		string sidecarPath = SidecarLineFile.GetSidecarPath(filePath, SidecarExtension);

		// A non-empty set whose entries are all below one is a caller bug: the save fails and the
		// healthy sidecar stays intact instead of being deleted by the degenerate set.
		Assert.IsFalse(SidecarLineFile.Save(filePath, [0, -1], SidecarExtension));
		Assert.IsTrue(File.Exists(sidecarPath));
		CollectionAssert.AreEqual(new[] { 3, 7 }, RestoreLines(filePath, SidecarExtension).ToArray());
	}

	[TestMethod]
	public void GetSidecarPath_ExtensionWithoutLeadingDot_IsNormalized()
	{
		Assert.AreEqual("doc.txt.bkmrk", SidecarLineFile.GetSidecarPath("doc.txt", "bkmrk"));
		Assert.AreEqual("doc.txt.bkmrk", SidecarLineFile.GetSidecarPath("doc.txt", " .bkmrk "));
	}

	[TestMethod]
	public void GetSidecarPath_DegenerateExtension_Throws()
	{
		// A trailing dot is stripped by Windows, so raw "." would make the sidecar path resolve to
		// the document itself; separators would target a different directory.
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("doc.txt", "."));
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("doc.txt", ".."));
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("doc.txt", ".bkmrk."));
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("doc.txt", "bkmrk\\x"));
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.GetSidecarPath("doc.txt", "bkmrk/x"));
	}

	[TestMethod]
	public void ValidateExtension_ValidExtension_DoesNotThrow()
	{
		SidecarLineFile.ValidateExtension(".bkmrk");
		SidecarLineFile.ValidateExtension("bkmrk");
		SidecarLineFile.ValidateExtension(" .bkmrk ");
	}

	[TestMethod]
	public void ValidateExtension_Null_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => SidecarLineFile.ValidateExtension(null!));
	}

	[TestMethod]
	public void ValidateExtension_Blank_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.ValidateExtension(string.Empty));
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.ValidateExtension("   "));
	}

	[TestMethod]
	[DataRow(".bkmrk:stream")]
	[DataRow(".bkmrk*")]
	[DataRow(".bkmrk?")]
	[DataRow(".bkmrk\"x")]
	[DataRow(".bkmrk<x")]
	[DataRow(".bkmrk>x")]
	[DataRow(".bkmrk|x")]
	[DataRow(".bkmrk\tx")]
	[DataRow(".bkmrk\nx")]
	public void ValidateExtension_PortableRejectedCharacters_ThrowOnEveryPlatform(string extension)
	{
		// The rejected set is fixed rather than platform-dependent, so the same extension is
		// accepted or rejected on every operating system.
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.ValidateExtension(extension));
	}

	[TestMethod]
	public void Save_DegenerateExtension_Throws()
	{
		string filePath = CreateTempPath(out _);

		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.Save(filePath, [1], sidecarExtension: "."));
		Assert.ThrowsExactly<ArgumentException>(() => SidecarLineFile.TryRestore(filePath, sidecarExtension: ".", out _));
	}

	[TestMethod]
	public void Save_MissingParentDirectory_ReturnsFalse()
	{
		string filePath = CreateTempPath(out string directory);
		string missingDirectoryPath = Path.Combine(directory, "missing", "document.txt");

		Assert.IsFalse(SidecarLineFile.Save(missingDirectoryPath, [1], SidecarExtension));
		Assert.IsEmpty(RestoreLines(missingDirectoryPath, SidecarExtension));
	}

	[TestMethod]
	public void TryRestore_DuplicateAndUnsortedEntries_AreNormalized()
	{
		string filePath = CreateTempPath(out _);

		File.WriteAllLines(SidecarLineFile.GetSidecarPath(filePath, SidecarExtension), ["5", "1", "1", "2"]);

		IReadOnlyList<int> restored = RestoreLines(filePath, SidecarExtension);

		CollectionAssert.AreEqual(new[] { 1, 2, 5 }, restored.ToArray());
	}

	[TestMethod]
	public void TryRestore_WhitespacePaddedEntries_AreParsed()
	{
		string filePath = CreateTempPath(out _);
		File.WriteAllLines(SidecarLineFile.GetSidecarPath(filePath, SidecarExtension), [" 5 ", "\t2\t"]);

		IReadOnlyList<int> restored = RestoreLines(filePath, SidecarExtension);

		CollectionAssert.AreEqual(new[] { 2, 5 }, restored.ToArray());
	}

	[TestMethod]
	public void Save_ConcurrentSaves_LeaveOneCompleteSidecarWithoutTemporaryFiles()
	{
		string filePath = CreateTempPath(out string directory);
		var results = new bool[8];

		Parallel.For(0, results.Length, i => results[i] = SidecarLineFile.Save(filePath, [i + 1], SidecarExtension));

		// Concurrent saves cannot corrupt the sidecar: at least one save succeeds, the surviving
		// sidecar is one complete save, and no temporary files are left behind. A save that loses a
		// transient replacement race reports false instead of retrying, so not every result must be
		// true.
		Assert.IsTrue(results.Any(result => result));

		IReadOnlyList<int> restored = RestoreLines(filePath, SidecarExtension);

		Assert.AreEqual(1, restored.Count);
		Assert.IsTrue(restored[0] is >= 1 and <= 8);
		Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
	}

	[TestMethod]
	public void Save_WhenTheReplaceFails_ReturnsFalseAndCleansUpTemporaryFile()
	{
		string filePath = CreateTempPath(out string directory);

		SidecarLineFile.Save(filePath, [1], SidecarExtension);

		// The injected failure stands in for a destination another handle holds open without sharing -
		// the way a replace fails on an otherwise healthy machine, and one the file system cannot be
		// made to produce on every platform. The recovery path must report failure, remove the
		// temporary file, and keep the previous content.
		var fileOperations = new FaultingSidecarFileOperations
		{
			MoveFailureMessage = "The destination is held open by another handle.",
		};

		bool saved = SidecarLineFile.SaveWithFileOperations(filePath, [1, 2], SidecarExtension, fileOperations);

		Assert.IsFalse(saved);
		Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
		CollectionAssert.AreEqual(new[] { 1 }, RestoreLines(filePath, SidecarExtension).ToArray());
	}

	[TestMethod]
	public void Save_SidecarPathIsDirectory_ReturnsFalseAndCleansUpTemporaryFile()
	{
		string filePath = CreateTempPath(out string directory);
		string sidecarPath = SidecarLineFile.GetSidecarPath(filePath, SidecarExtension);

		// A directory at the sidecar path makes the replace step fail on every OS, which exercises
		// the same recovery path without relying on platform-specific lock behavior.
		Directory.CreateDirectory(sidecarPath);

		bool saved = SidecarLineFile.Save(filePath, [1, 2], SidecarExtension);

		Assert.IsFalse(saved);
		Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
	}
}
