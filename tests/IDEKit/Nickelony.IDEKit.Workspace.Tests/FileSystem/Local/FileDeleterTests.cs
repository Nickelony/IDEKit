using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Tests.FileSystemTestHelpers;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Covers the deleter directly: that a file goes only under its expected stamp, and what a directory and a file do to the other delete.
/// </summary>
[TestClass]
public sealed class FileDeleterTests
{
	public required TestContext TestContext { get; set; }

	private async Task<(string Path, FileStamp Stamp)> FileAsync(TemporaryDirectory temp, string text)
	{
		string path = Path.Combine(temp.Path, "file.txt");
		await File.WriteAllTextAsync(path, text, TestContext.CancellationToken);

		return (path, await FileStampReader.CaptureAsync(path, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task DeleteAsync_UnderTheExpectedStamp_DeletesTheFile()
	{
		using var temp = new TemporaryDirectory();
		(string path, FileStamp expected) = await FileAsync(temp, "content");

		WorkspaceFileDeleteResult result = await new FileDeleter(Primitives()).DeleteAsync(path, expected, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, result.Outcome);
		Assert.IsFalse(File.Exists(path));
	}

	[TestMethod]
	public async Task DeleteAsync_OfAFileThatChanged_ReportsAConflictAndKeepsTheFile()
	{
		using var temp = new TemporaryDirectory();
		(string path, FileStamp expected) = await FileAsync(temp, "content");
		await File.WriteAllTextAsync(path, "changed by someone else", TestContext.CancellationToken);
		int deletes = 0;

		WorkspaceFileDeleteResult result = await new FileDeleter(Primitives(deleteFile: _ => deletes++)).DeleteAsync(
			path,
			expected,
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(await FileStampReader.CaptureAsync(path, TestContext.CancellationToken), result.ObservedOnDiskStamp);
		Assert.AreEqual(0, deletes);
		Assert.IsTrue(File.Exists(path));
	}

	[TestMethod]
	public async Task DeleteAsync_OfAnAbsentFileUnderTheMissingStamp_ReportsDeletedWithoutDeleting()
	{
		using var temp = new TemporaryDirectory();
		int deletes = 0;

		WorkspaceFileDeleteResult result = await new FileDeleter(Primitives(deleteFile: _ => deletes++)).DeleteAsync(
			Path.Combine(temp.Path, "absent.txt"),
			FileStamp.Missing,
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(0, deletes);
	}

	[TestMethod]
	public async Task DeleteAsync_OfADirectory_FailsWithTheDirectoryCode()
	{
		using var temp = new TemporaryDirectory();

		WorkspaceFileDeleteResult result = await new FileDeleter(Primitives()).DeleteAsync(
			temp.Path,
			FileStamp.Directory,
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.IsDirectory, result.Failure!.Code);
		Assert.IsTrue(Directory.Exists(temp.Path));
	}

	[TestMethod]
	public async Task DeleteAsync_WhenTheDeleteFailsOnceTransiently_RunsAgain()
	{
		using var temp = new TemporaryDirectory();
		(string path, FileStamp expected) = await FileAsync(temp, "content");
		int calls = 0;

		var deleter = new FileDeleter(
			Primitives(deleteFile: target =>
			{
				if (++calls == 1)
					throw new IOException("The file is in use.", SharingViolation);

				File.Delete(target);
			})
		);

		WorkspaceFileDeleteResult result = await deleter.DeleteAsync(path, expected, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(2, calls);
		Assert.IsFalse(File.Exists(path));
	}

	[TestMethod]
	public async Task DeleteAsync_WhenAccessIsDenied_FailsWithTheAccessCode()
	{
		using var temp = new TemporaryDirectory();
		(string path, FileStamp expected) = await FileAsync(temp, "content");

		var deleter = new FileDeleter(Primitives(deleteFile: _ => throw new UnauthorizedAccessException("The file is read-only.")));

		WorkspaceFileDeleteResult result = await deleter.DeleteAsync(path, expected, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure!.Code);
		Assert.IsTrue(File.Exists(path));
	}

	[TestMethod]
	public async Task DeleteAsync_WithACanceledToken_ReportsCanceledAndKeepsTheFile()
	{
		using var temp = new TemporaryDirectory();
		(string path, FileStamp expected) = await FileAsync(temp, "content");

		WorkspaceFileDeleteResult result = await new FileDeleter(Primitives()).DeleteAsync(path, expected, new CancellationToken(canceled: true));

		Assert.AreEqual(WorkspaceFileDeleteOutcome.Canceled, result.Outcome);
		Assert.IsTrue(File.Exists(path));
	}

	[TestMethod]
	public async Task DeleteDirectoryAsync_RemovesTheDirectoryWithItsContent()
	{
		using var temp = new TemporaryDirectory();
		string directory = Directory.CreateDirectory(Path.Combine(temp.Path, "folder", "nested")).FullName;
		await File.WriteAllTextAsync(Path.Combine(directory, "file.txt"), "content", TestContext.CancellationToken);

		WorkspaceFileDeleteResult result = await FileDeleter.DeleteDirectoryAsync(Path.Combine(temp.Path, "folder"), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, result.Outcome);
		Assert.IsFalse(Directory.Exists(Path.Combine(temp.Path, "folder")));
	}

	[TestMethod]
	public async Task DeleteDirectoryAsync_OfAnAbsentDirectory_ReportsDeleted()
	{
		using var temp = new TemporaryDirectory();

		WorkspaceFileDeleteResult result = await FileDeleter.DeleteDirectoryAsync(Path.Combine(temp.Path, "absent"), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, result.Outcome);
	}

	[TestMethod]
	public async Task DeleteDirectoryAsync_OfAFile_FailsWithTheFileCodeAndKeepsTheFile()
	{
		using var temp = new TemporaryDirectory();
		(string path, _) = await FileAsync(temp, "content");

		WorkspaceFileDeleteResult result = await FileDeleter.DeleteDirectoryAsync(path, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.NotDirectory, result.Failure!.Code);
		Assert.IsTrue(File.Exists(path));
	}

	[TestMethod]
	public async Task DeleteDirectoryAsync_WithACanceledToken_ReportsCanceledAndKeepsTheDirectory()
	{
		using var temp = new TemporaryDirectory();

		WorkspaceFileDeleteResult result = await FileDeleter.DeleteDirectoryAsync(temp.Path, new CancellationToken(canceled: true));

		Assert.AreEqual(WorkspaceFileDeleteOutcome.Canceled, result.Outcome);
		Assert.IsTrue(Directory.Exists(temp.Path));
	}
}
