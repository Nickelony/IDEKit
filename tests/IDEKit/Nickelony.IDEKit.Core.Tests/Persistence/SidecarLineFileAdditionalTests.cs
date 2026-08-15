using System.Text;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Covers the failure-reporting and normalization behaviors of <see cref="SidecarLineFile"/>.
/// </summary>
[TestClass]
public sealed class SidecarLineFileAdditionalTests
{
	[TestMethod]
	public void TryRestore_MissingSidecar_SucceedsWithNoLines()
	{
		using var directory = new TemporaryDirectory("sidecar-wave-");
		string documentPath = directory.GetFilePath("doc.txt");

		Assert.IsTrue(SidecarLineFile.TryRestore(documentPath, ".bkmrk", out IReadOnlyList<int> lineNumbers));
		Assert.AreEqual(0, lineNumbers.Count);
	}

	[TestMethod]
	public void TryRestore_BlankPath_ReportsFailure()
	{
		Assert.IsFalse(SidecarLineFile.TryRestore("   ", ".bkmrk", out IReadOnlyList<int> lineNumbers));
		Assert.AreEqual(0, lineNumbers.Count);
	}

	[TestMethod]
	public void TryRestore_DirectoryOccupiesTheSidecarPath_ReportsFailure()
	{
		using var directory = new TemporaryDirectory("sidecar-wave-");
		string documentPath = directory.GetFilePath("doc.txt");

		// A directory at the sidecar path can never hold saved line numbers, so the restore reports
		// a storage failure instead of "nothing was saved".
		Directory.CreateDirectory(SidecarLineFile.GetSidecarPath(documentPath, ".bkmrk"));

		Assert.IsFalse(SidecarLineFile.TryRestore(documentPath, ".bkmrk", out IReadOnlyList<int> lineNumbers));
		Assert.AreEqual(0, lineNumbers.Count);
	}

	[TestMethod]
	public void TryRestore_WhenTheReadFails_ReportsFailure()
	{
		using var directory = new TemporaryDirectory("sidecar-wave-");
		string documentPath = directory.GetFilePath("doc.txt");

		Assert.IsTrue(SidecarLineFile.Save(documentPath, [1, 2], ".bkmrk"));

		// The injected failure stands in for a sidecar another handle holds open without sharing, so the
		// branch is pinned on every platform. An unreadable sidecar is a storage failure, not "nothing
		// was saved".
		var fileOperations = new FaultingSidecarFileOperations
		{
			ReadFailureMessage = "The sidecar is held open by another handle.",
		};

		Assert.IsFalse(SidecarLineFile.TryRestoreWithFileOperations(documentPath, ".bkmrk", out IReadOnlyList<int> lineNumbers, fileOperations));
		Assert.AreEqual(0, lineNumbers.Count);
	}

	[TestMethod]
	public void TryRestore_ByteOrderMarkAndLoneCrTerminator_AreAccepted()
	{
		using var directory = new TemporaryDirectory("sidecar-wave-");
		string documentPath = directory.GetFilePath("doc.txt");

		// The read honors a byte-order mark and accepts any line terminator, including a lone CR.
		File.WriteAllText(
			SidecarLineFile.GetSidecarPath(documentPath, ".bkmrk"),
			"4\r2",
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

		Assert.IsTrue(SidecarLineFile.TryRestore(documentPath, ".bkmrk", out IReadOnlyList<int> restored));

		CollectionAssert.AreEqual(new[] { 2, 4 }, restored.ToArray());
	}
}
