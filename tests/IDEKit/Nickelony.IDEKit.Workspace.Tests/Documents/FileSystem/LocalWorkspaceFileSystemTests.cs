using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Text;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class LocalWorkspaceFileSystemTests
{
	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_MissingDestinationDirectory_ReportsFailed()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		var fileSystem = new LocalWorkspaceFileSystem();

		// The staged file cannot be created in a directory that does not exist, and the failure has a
		// known final state: nothing was published.
		WorkspaceFileReplacementResult result = await fileSystem.WriteFileAsync(
			Path.Combine(directory, "missing", "destination.txt"),
			Encoding.UTF8.GetBytes("content"),
			FileStamp.Missing,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Failed, result.Outcome);
		Assert.IsNotNull(result.Failure);
		Assert.AreEqual(WorkspaceOperationFailureCodes.WriteFailed, result.Failure!.Code);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_CaptureStampForMissingPath_ReturnsMissing()
	{
		using var temp = new TemporaryDirectory();
		var fileSystem = new LocalWorkspaceFileSystem();

		FileStamp stamp = await fileSystem.CaptureStampAsync(
			Path.Combine(temp.Path, "missing.txt"),
			CancellationToken.None);

		Assert.AreEqual(FileStamp.Missing, stamp);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_CaptureStampForDirectory_ReturnsDirectoryStamp()
	{
		using var temp = new TemporaryDirectory();
		string directoryPath = Path.Combine(temp.Path, "folder");
		Directory.CreateDirectory(directoryPath);
		var fileSystem = new LocalWorkspaceFileSystem();

		// A directory is not a missing file: the stamp carries the directory flag so a caller rejects the
		// path instead of treating it as absent.
		FileStamp stamp = await fileSystem.CaptureStampAsync(directoryPath, CancellationToken.None);

		Assert.AreEqual(FileStamp.Directory, stamp);
		Assert.AreNotEqual(FileStamp.Missing, stamp);
		Assert.IsTrue(stamp.IsDirectory);
		Assert.IsFalse(stamp.Exists);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MovesAndDeletesOnlyWhenExpectedStampMatches()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "source.lua");
		string destinationPath = Path.Combine(directory, "destination.lua");
		File.WriteAllText(sourcePath, "content");
		var fileSystem = new LocalWorkspaceFileSystem();
		FileStamp sourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);

		WorkspaceFileMoveResult moved = await fileSystem.MoveAsync(
			sourcePath,
			destinationPath,
			sourceStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, moved.Outcome);
		Assert.IsFalse(File.Exists(sourcePath));
		Assert.AreEqual("content", File.ReadAllText(destinationPath));
		File.WriteAllText(destinationPath, "changed");

		WorkspaceFileDeleteResult staleDelete = await fileSystem.DeleteAsync(
			destinationPath,
			sourceStamp,
			CancellationToken.None);
		Assert.AreEqual(WorkspaceFileDeleteOutcome.ExternalFileConflict, staleDelete.Outcome);

		FileStamp destinationStamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);
		WorkspaceFileDeleteResult deleted = await fileSystem.DeleteAsync(
			destinationPath,
			destinationStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, deleted.Outcome);
		Assert.IsFalse(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_ReportsTheResultingDestinationStamp()
	{
		using var temp = new TemporaryDirectory();
		string sourcePath = Path.Combine(temp.Path, "source.lua");
		string destinationPath = Path.Combine(temp.Path, "destination.lua");
		File.WriteAllText(sourcePath, "content");
		var fileSystem = new LocalWorkspaceFileSystem();
		FileStamp sourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);

		WorkspaceFileMoveResult moved = await fileSystem.MoveAsync(
			sourcePath,
			destinationPath,
			sourceStamp,
			CancellationToken.None);

		// The move reports the resulting destination stamp, so the store installs the true baseline
		// instead of echoing the pre-move stamp.
		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, moved.Outcome);
		Assert.IsNotNull(moved.ObservedOnDiskStamp, "A successful move reports the resulting destination stamp.");
		FileStamp destinationStamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);
		Assert.AreEqual(destinationStamp, moved.ObservedOnDiskStamp!.Value, "The reported stamp matches the moved file.");
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_CaseOnlyFileRename_FollowsConfiguredPathComparison()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Script.lua");
		string destinationPath = Path.Combine(directory, "script.lua");
		File.WriteAllText(sourcePath, "content");

		// The case-sensitive policy treats the destination spelling as a distinct path, so the
		// outcome follows the actual volume: a case-insensitive volume still reports the variant as
		// an existing destination, while a case-sensitive volume performs an ordinary rename.
		bool volumeResolvesCaseVariants = File.Exists(Path.Combine(directory, "SCRIPT.lua"));
		var caseSensitive = new LocalWorkspaceFileSystem(
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive });
		FileStamp sourceStamp = await caseSensitive.CaptureStampAsync(sourcePath, CancellationToken.None);
		WorkspaceFileMoveResult sensitive = await caseSensitive.MoveAsync(
			sourcePath,
			destinationPath,
			sourceStamp,
			CancellationToken.None);

		Assert.AreEqual(
			volumeResolvesCaseVariants ? WorkspaceFileMoveOutcome.DestinationExists : WorkspaceFileMoveOutcome.Moved,
			sensitive.Outcome);

		// Reset to the original spelling and verify the case-insensitive policy completes the
		// case-only rename on every platform instead of rejecting the existing variant.
		if (File.Exists(destinationPath))
		{
			File.Delete(destinationPath);
			File.WriteAllText(sourcePath, "content");
		}

		var caseInsensitive = new LocalWorkspaceFileSystem(
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive });
		FileStamp resetStamp = await caseInsensitive.CaptureStampAsync(sourcePath, CancellationToken.None);
		WorkspaceFileMoveResult insensitive = await caseInsensitive.MoveAsync(
			sourcePath,
			destinationPath,
			resetStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, insensitive.Outcome);
		string[] files = Directory.GetFiles(directory);
		Assert.AreEqual(1, files.Length);
		Assert.AreEqual("script.lua", Path.GetFileName(files[0]));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_CaseOnlyDirectoryRename_FollowsConfiguredPathComparison()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Folder");
		Directory.CreateDirectory(sourcePath);
		File.WriteAllText(Path.Combine(sourcePath, "one.lua"), "content");
		string destinationPath = Path.Combine(directory, "folder");
		var caseInsensitive = new LocalWorkspaceFileSystem(
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive });

		WorkspaceFileMoveResult moved = await caseInsensitive.MoveDirectoryAsync(
			sourcePath,
			destinationPath,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, moved.Outcome);
		string[] directories = Directory.GetDirectories(directory);
		Assert.AreEqual(1, directories.Length);
		Assert.AreEqual("folder", Path.GetFileName(directories[0]));
		Assert.IsTrue(File.Exists(Path.Combine(destinationPath, "one.lua")));

		// A missing source is a move failure, and deleting the renamed directory works recursively.
		WorkspaceFileMoveResult missing = await caseInsensitive.MoveDirectoryAsync(
			Path.Combine(directory, "absent"),
			Path.Combine(directory, "other"),
			CancellationToken.None);
		Assert.AreEqual(WorkspaceFileMoveOutcome.MoveFailed, missing.Outcome);

		WorkspaceFileDeleteResult deleted = await caseInsensitive.DeleteDirectoryAsync(destinationPath, CancellationToken.None);
		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, deleted.Outcome);
		Assert.IsFalse(Directory.Exists(destinationPath));

		WorkspaceFileDeleteResult deletedAgain = await caseInsensitive.DeleteDirectoryAsync(destinationPath, CancellationToken.None);
		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, deletedAgain.Outcome);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_ReportsConflictWhenExpectedMissingButDestinationExists()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		var fileSystem = new LocalWorkspaceFileSystem();
		string destinationPath = Path.Combine(directory, "destination.txt");
		File.WriteAllText(destinationPath, "existing");

		// The expected stamp says the destination is missing while the destination exists: the
		// conflict is reported with the observed stamp instead of a publish attempt, and the staged
		// file is removed.
		WorkspaceFileReplacementResult result = await fileSystem.WriteFileAsync(
			destinationPath,
			Encoding.UTF8.GetBytes("content"),
			FileStamp.Missing,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.ExternalFileConflict, result.Outcome);
		Assert.IsTrue(result.ObservedOnDiskStamp!.Value.Exists);
		Assert.AreEqual("existing", File.ReadAllText(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_StaleStampRejectsBeforeStaging()
	{
		using var temp = new TemporaryDirectory();
		var fileSystem = new LocalWorkspaceFileSystem();
		string destinationPath = Path.Combine(temp.Path, "missing", "destination.txt");

		// A file is expected, but neither it nor its directory exists. A staging-first write would try
		// to create the temporary file in the missing directory and report a write failure; validating
		// the expected stamp first reports the external conflict instead, proving that no temporary
		// file was staged.
		FileStamp expectedExisting = new(true, 5, DateTime.UtcNow, "ABCDEF");

		WorkspaceFileReplacementResult result = await fileSystem.WriteFileAsync(
			destinationPath,
			Encoding.UTF8.GetBytes("content"),
			expectedExisting,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.ExternalFileConflict, result.Outcome);
		Assert.IsFalse(result.ObservedOnDiskStamp!.Value.Exists);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_DestinationVanishedBeforeReplace_ReportsExternalFileConflict()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string destinationPath = Path.Combine(directory, "destination.txt");
		File.WriteAllText(destinationPath, "existing");

		// The destination is removed between the stamp check and the publish, so File.Replace reports the
		// vanished destination; the host must be able to re-baseline on the observed conflict.
		var fileSystem = new LocalWorkspaceFileSystem(null, new WorkspaceFileOperations(
			Replace: static (_, targetPath) =>
			{
				File.Delete(targetPath);
				throw new FileNotFoundException("The destination vanished before the replace.");
			},
			MoveFile: static (_, _) => throw new InvalidOperationException("An existing destination must use the replace operation."),
			MoveDirectory: static (_, _) => throw new InvalidOperationException("A file write never moves a directory.")));
		FileStamp destinationStamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);

		WorkspaceFileReplacementResult result = await fileSystem.WriteFileAsync(
			destinationPath,
			Encoding.UTF8.GetBytes("content"),
			destinationStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.ExternalFileConflict, result.Outcome);
		Assert.IsNotNull(result.ObservedOnDiskStamp);
		Assert.IsFalse(result.ObservedOnDiskStamp!.Value.Exists);
		Assert.IsNull(result.Failure);
		Assert.IsFalse(File.Exists(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_ReportsConflictWhenDestinationAppearsDuringMove()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string destinationPath = Path.Combine(directory, "destination.txt");
		string? movedSourcePath = null;

		// The destination appears between the stamp check and the missing-destination move: the
		// substituted move creates the concurrent file and then reports the same IOException the
		// real move raises when the destination was created in the meantime.
		var fileSystem = new LocalWorkspaceFileSystem(null, new WorkspaceFileOperations(
			Replace: static (_, _) => throw new InvalidOperationException("A missing destination must use the move operation."),
			MoveFile: (sourcePath, path) =>
			{
				movedSourcePath = sourcePath;
				File.WriteAllText(path, "concurrent");
				throw new IOException("Cannot create a file when that file already exists.");
			},
			MoveDirectory: static (_, _) => throw new InvalidOperationException("A file write never moves a directory.")));

		WorkspaceFileReplacementResult result = await fileSystem.WriteFileAsync(
			destinationPath,
			Encoding.UTF8.GetBytes("content"),
			FileStamp.Missing,
			CancellationToken.None);

		// The staged file is the one the substituted move received, and it is removed afterwards.
		Assert.IsNotNull(movedSourcePath);
		Assert.AreEqual(directory, Path.GetDirectoryName(movedSourcePath));
		Assert.AreEqual(WorkspaceFileReplacementOutcome.ExternalFileConflict, result.Outcome);
		Assert.IsTrue(result.ObservedOnDiskStamp!.Value.Exists);
		Assert.AreEqual((long)"concurrent".Length, result.ObservedOnDiskStamp!.Value.Length);
		Assert.IsNull(result.Failure);
		Assert.AreEqual("concurrent", File.ReadAllText(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_PreCanceledToken_ReportsCanceledForEveryMutation()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		var fileSystem = new LocalWorkspaceFileSystem();
		string sourcePath = Path.Combine(directory, "source.lua");
		File.WriteAllText(sourcePath, "content");
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		WorkspaceFileMoveResult move = await fileSystem.MoveAsync(
			sourcePath,
			Path.Combine(directory, "moved.lua"),
			FileStamp.Missing,
			cancellation.Token);
		WorkspaceFileDeleteResult delete = await fileSystem.DeleteAsync(sourcePath, FileStamp.Missing, cancellation.Token);
		WorkspaceFileMoveResult directoryMove = await fileSystem.MoveDirectoryAsync(
			directory,
			directory + "-moved",
			cancellation.Token);
		WorkspaceFileDeleteResult directoryDelete = await fileSystem.DeleteDirectoryAsync(directory, cancellation.Token);

		WorkspaceFileReplacementResult replacement = await fileSystem.WriteFileAsync(
			Path.Combine(directory, "replaced.lua"),
			Encoding.UTF8.GetBytes("content"),
			FileStamp.Missing,
			cancellation.Token);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Canceled, move.Outcome);
		Assert.AreEqual(WorkspaceFileDeleteOutcome.Canceled, delete.Outcome);
		Assert.AreEqual(WorkspaceFileMoveOutcome.Canceled, directoryMove.Outcome);
		Assert.AreEqual(WorkspaceFileDeleteOutcome.Canceled, directoryDelete.Outcome);
		Assert.AreEqual(WorkspaceFileReplacementOutcome.Canceled, replacement.Outcome);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_DirectoryDestination_ReportsDestinationExists()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		var fileSystem = new LocalWorkspaceFileSystem();
		string destinationPath = Path.Combine(directory, "destination");
		Directory.CreateDirectory(destinationPath);

		WorkspaceFileReplacementResult result = await fileSystem.WriteFileAsync(
			destinationPath,
			Encoding.UTF8.GetBytes("content"),
			FileStamp.Missing,
			CancellationToken.None);

		// A directory occupies the destination where a missing file was expected: the publish is not
		// attempted and the deterministic conflict is reported with the path-kind code.
		Assert.AreEqual(WorkspaceFileReplacementOutcome.DestinationExists, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.IsDirectory, result.Failure?.Code);
		Assert.IsTrue(Directory.Exists(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_DeleteAsync_Directory_ReportsIsDirectoryFailure()
	{
		using var temp = new TemporaryDirectory();
		var fileSystem = new LocalWorkspaceFileSystem();
		string directoryPath = Path.Combine(temp.Path, "folder");
		Directory.CreateDirectory(directoryPath);

		WorkspaceFileDeleteResult result = await fileSystem.DeleteAsync(
			directoryPath,
			FileStamp.Missing,
			CancellationToken.None);

		// A directory is not a missing file: the delete reports a path-kind failure instead of claiming a
		// completed delete while the directory still occupies the path.
		Assert.AreEqual(WorkspaceFileDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.IsDirectory, result.Failure?.Code);
		Assert.IsTrue(Directory.Exists(directoryPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_DeleteDirectoryAsync_File_ReportsNotDirectoryFailure()
	{
		using var temp = new TemporaryDirectory();
		var fileSystem = new LocalWorkspaceFileSystem();
		string filePath = Path.Combine(temp.Path, "file.txt");
		File.WriteAllText(filePath, "content");

		WorkspaceFileDeleteResult result = await fileSystem.DeleteDirectoryAsync(filePath, CancellationToken.None);

		// A file is not a directory that is already absent: the delete reports a path-kind failure instead
		// of claiming a completed delete while the file still occupies the path.
		Assert.AreEqual(WorkspaceFileDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.NotDirectory, result.Failure?.Code);
		Assert.IsTrue(File.Exists(filePath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_ReadAsync_Directory_ReturnsIsDirectoryResult()
	{
		using var temp = new TemporaryDirectory();
		string directoryPath = Path.Combine(temp.Path, "folder");
		Directory.CreateDirectory(directoryPath);
		var fileSystem = new LocalWorkspaceFileSystem();

		// A directory is not a missing file: the read reports the directory flag so the document
		// store can reject the path instead of tracking a new empty document for it.
		WorkspaceFileReadResult result = await fileSystem.ReadAsync(directoryPath, CancellationToken.None);

		Assert.IsTrue(result.IsDirectory);
		Assert.AreEqual(FileStamp.Missing, result.OnDiskStamp);
		Assert.AreEqual(string.Empty, result.Content);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_LeavesNoStagedFileBehind()
	{
		using var temp = new TemporaryDirectory();
		var fileSystem = new LocalWorkspaceFileSystem();
		string destinationPath = Path.Combine(temp.Path, "notes.txt");
		byte[] content = Encoding.UTF8.GetBytes("temporary");

		// Creating a missing destination publishes the staged file and removes it.
		WorkspaceFileReplacementResult created = await fileSystem.WriteFileAsync(
			destinationPath,
			content,
			FileStamp.Missing,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, created.Outcome);
		Assert.AreEqual((long)content.Length, created.ObservedOnDiskStamp!.Value.Length);
		Assert.AreEqual("temporary", File.ReadAllText(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));

		// Replacing an existing destination publishes the staged file and removes it as well.
		byte[] replacement = Encoding.UTF8.GetBytes("replaced");
		FileStamp existingStamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);
		WorkspaceFileReplacementResult replaced = await fileSystem.WriteFileAsync(
			destinationPath,
			replacement,
			existingStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, replaced.Outcome);
		Assert.AreEqual("replaced", File.ReadAllText(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_ReportsStampConflictAndExistingDestination()
	{
		using var temp = new TemporaryDirectory();
		string sourcePath = Path.Combine(temp.Path, "source.lua");
		string existingPath = Path.Combine(temp.Path, "existing.lua");
		File.WriteAllText(sourcePath, "content");
		File.WriteAllText(existingPath, "existing");
		var fileSystem = new LocalWorkspaceFileSystem();
		FileStamp sourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);

		WorkspaceFileMoveResult stale = await fileSystem.MoveAsync(
			sourcePath,
			Path.Combine(temp.Path, "destination.lua"),
			FileStamp.Missing,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.ExternalFileConflict, stale.Outcome);
		Assert.AreEqual(sourceStamp, stale.ObservedOnDiskStamp);

		WorkspaceFileMoveResult collision = await fileSystem.MoveAsync(
			sourcePath,
			existingPath,
			sourceStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.DestinationExists, collision.Outcome);
		Assert.IsTrue(File.Exists(sourcePath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_UnchangedDestinationAfterFailure_ReportsFailed()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		byte[] replacementBytes = Encoding.UTF8.GetBytes("replacement");

		// An unchanged destination means the failed write did not alter it, so the final state
		// is known to be the expected stamp.
		File.WriteAllText(destinationPath, "original");
		var unchanged = new LocalWorkspaceFileSystem(null, new WorkspaceFileOperations(
			Replace: static (_, _) => throw new IOException("The replacement failed."),
			MoveFile: static (_, _) => throw new IOException("The replacement failed."),
			MoveDirectory: static (_, _) => throw new IOException("The replacement failed.")));
		FileStamp unchangedStamp = await unchanged.CaptureStampAsync(destinationPath, CancellationToken.None);

		WorkspaceFileReplacementResult result = await unchanged.WriteFileAsync(
			destinationPath,
			replacementBytes,
			unchangedStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Failed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.WriteFailed, result.Failure!.Code);
		Assert.AreEqual(unchangedStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual("original", File.ReadAllText(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_IntendedBytesOnDiskAfterFailure_ReportsReplaced()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		byte[] replacementBytes = Encoding.UTF8.GetBytes("replacement");

		// The destination already carries the intended bytes, so the write completed before the
		// reported failure.
		File.WriteAllText(destinationPath, "original");
		var completed = new LocalWorkspaceFileSystem(null, new WorkspaceFileOperations(
			Replace: (_, path) =>
			{
				File.WriteAllBytes(path, replacementBytes);
				throw new IOException("The replacement failed after the write.");
			},
			MoveFile: static (_, _) => throw new IOException("The replacement failed."),
			MoveDirectory: static (_, _) => throw new IOException("The replacement failed.")));
		FileStamp completedStamp = await completed.CaptureStampAsync(destinationPath, CancellationToken.None);

		WorkspaceFileReplacementResult result = await completed.WriteFileAsync(
			destinationPath,
			replacementBytes,
			completedStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, result.Outcome);
		Assert.AreEqual((long)replacementBytes.Length, result.ObservedOnDiskStamp!.Value.Length);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_UnrelatedDestinationAfterFailure_ReportsStateUnknown()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		byte[] replacementBytes = Encoding.UTF8.GetBytes("replacement");

		// The destination carries neither the expected stamp nor the intended bytes, so the final state
		// cannot be established.
		File.WriteAllText(destinationPath, "original");
		var unknown = new LocalWorkspaceFileSystem(null, new WorkspaceFileOperations(
			Replace: (_, path) =>
			{
				File.WriteAllText(path, "unrelated");
				throw new IOException("The replacement failed with an unrelated destination state.");
			},
			MoveFile: static (_, _) => throw new IOException("The replacement failed."),
			MoveDirectory: static (_, _) => throw new IOException("The replacement failed.")));
		FileStamp unknownStamp = await unknown.CaptureStampAsync(destinationPath, CancellationToken.None);

		WorkspaceFileReplacementResult result = await unknown.WriteFileAsync(
			destinationPath,
			replacementBytes,
			unknownStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.ReplacementStateUnknown, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, result.Failure!.Code);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_CaseOnlyRenameWithAlternateSeparatorSpelling_UsesTheIntermediatePath()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Script.lua");
		File.WriteAllText(sourcePath, "content");

		// The destination spells the same path with the alternate directory separator and a trailing
		// separator. The case-only rule normalizes separator spelling and the ending separator before it
		// compares, so the rename is detected as case-only and the move is routed through the
		// intermediate path instead of colliding with the source file. The substituted move records the
		// route and performs no I/O, so the assertion is volume-independent.
		var moves = new List<(string Source, string Destination)>();
		var fileSystem = new LocalWorkspaceFileSystem(
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive },
			new WorkspaceFileOperations(
				Replace: static (_, _) => throw new InvalidOperationException("A rename never replaces a file."),
				MoveFile: (source, destination) => moves.Add((source, destination)),
				MoveDirectory: static (_, _) => throw new InvalidOperationException("A file rename never moves a directory.")));
		string destinationPath = directory.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.AltDirectorySeparatorChar
			+ "script.lua"
			+ Path.AltDirectorySeparatorChar;
		FileStamp sourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);

		WorkspaceFileMoveResult result = await fileSystem.MoveAsync(
			sourcePath,
			destinationPath,
			sourceStamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.AreEqual(2, moves.Count);
		Assert.AreEqual(sourcePath, moves[0].Source);
		Assert.AreNotEqual(destinationPath, moves[0].Destination);
		Assert.AreEqual(moves[0].Destination, moves[1].Source);
		Assert.AreEqual(destinationPath, moves[1].Destination);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveDirectoryAsync_CaseOnlyRenameWithAlternateSeparatorSpelling_UsesTheIntermediatePath()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Folder");
		Directory.CreateDirectory(sourcePath);

		// The same rule applies to a directory move: the alternate separator spelling routes the
		// case-only rename through the intermediate path.
		var moves = new List<(string Source, string Destination)>();
		var fileSystem = new LocalWorkspaceFileSystem(
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive },
			new WorkspaceFileOperations(
				Replace: static (_, _) => throw new InvalidOperationException("A rename never replaces a file."),
				MoveFile: static (_, _) => throw new InvalidOperationException("A directory rename never moves a file."),
				MoveDirectory: (source, destination) => moves.Add((source, destination))));
		string destinationPath = directory.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.AltDirectorySeparatorChar
			+ "folder";

		WorkspaceFileMoveResult result = await fileSystem.MoveDirectoryAsync(
			sourcePath,
			destinationPath,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.AreEqual(2, moves.Count);
		Assert.AreEqual(sourcePath, moves[0].Source);
		Assert.AreNotEqual(destinationPath, moves[0].Destination);
		Assert.AreEqual(moves[0].Destination, moves[1].Source);
		Assert.AreEqual(destinationPath, moves[1].Destination);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_CaseOnlyRenameRollbackFailure_ReportsMoveStateUnknown()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Script.lua");
		File.WriteAllText(sourcePath, "content");
		string destinationPath = Path.Combine(directory, "script.lua");

		// The case-only rename moves the file to its intermediate path, the move to the destination
		// fails, and the rollback move fails as well. The file is left at the intermediate path, so its
		// final location cannot be established and the failure aggregates both causes.
		var moveFailure = new IOException("The move to the destination failed.");
		var rollbackFailure = new IOException("The rollback failed.");
		int moveCount = 0;
		string? intermediatePath = null;
		var fileSystem = new LocalWorkspaceFileSystem(
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive },
			new WorkspaceFileOperations(
				Replace: static (_, _) => throw new InvalidOperationException("A rename never replaces a file."),
				MoveFile: (source, destination) =>
				{
					if (moveCount++ == 0)
					{
						intermediatePath = destination;
						File.Move(source, destination, overwrite: false);
						return;
					}

					throw moveCount == 2 ? moveFailure : rollbackFailure;
				},
				MoveDirectory: static (_, _) => throw new InvalidOperationException("A file rename never moves a directory.")));
		FileStamp sourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);

		WorkspaceFileMoveResult result = await fileSystem.MoveAsync(
			sourcePath,
			destinationPath,
			sourceStamp,
			CancellationToken.None);

		Assert.AreEqual(3, moveCount);
		Assert.AreEqual(WorkspaceFileMoveOutcome.MoveStateUnknown, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveStateUnknown, result.Failure!.Code);
		AggregateException aggregate = (AggregateException)result.Failure.Exception!;
		Assert.AreEqual(2, aggregate.InnerExceptions.Count);
		Assert.AreSame(moveFailure, aggregate.InnerExceptions[0]);
		Assert.AreSame(rollbackFailure, aggregate.InnerExceptions[1]);
		Assert.IsFalse(File.Exists(sourcePath));
		Assert.IsFalse(File.Exists(destinationPath));

		// The file survives at the abandoned intermediate path, which is why the outcome is a state
		// that cannot be established rather than a plain move failure.
		Assert.IsNotNull(intermediatePath);
		Assert.AreEqual("content", File.ReadAllText(intermediatePath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_CaseOnlyRenameThatLosesItsIntermediatePath_ReportsMoveStateUnknown()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Script.lua");
		File.WriteAllText(sourcePath, "content");
		string destinationPath = Path.Combine(directory, "script.lua");

		// The move to the destination succeeds and then throws, and the intermediate path is gone
		// afterwards: the move may have completed, so the final location cannot be established and the
		// failure aggregates the thrown move exception alone.
		var moveFailure = new IOException("The move to the destination failed after it completed.");
		int moveCount = 0;
		string? intermediatePath = null;
		var fileSystem = new LocalWorkspaceFileSystem(
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive },
			new WorkspaceFileOperations(
				Replace: static (_, _) => throw new InvalidOperationException("A rename never replaces a file."),
				MoveFile: (source, destination) =>
				{
					if (moveCount++ == 0)
					{
						intermediatePath = destination;
						File.Move(source, destination, overwrite: false);
						return;
					}

					File.Move(source, destination, overwrite: false);
					throw moveFailure;
				},
				MoveDirectory: static (_, _) => throw new InvalidOperationException("A file rename never moves a directory.")));
		FileStamp sourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);

		WorkspaceFileMoveResult result = await fileSystem.MoveAsync(
			sourcePath,
			destinationPath,
			sourceStamp,
			CancellationToken.None);

		Assert.AreEqual(2, moveCount);
		Assert.AreEqual(WorkspaceFileMoveOutcome.MoveStateUnknown, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveStateUnknown, result.Failure!.Code);
		AggregateException aggregate = (AggregateException)result.Failure.Exception!;
		Assert.AreEqual(1, aggregate.InnerExceptions.Count);
		Assert.AreSame(moveFailure, aggregate.InnerExceptions[0]);

		// The file reached the requested destination, which is exactly what cannot be reported as a
		// completed move after the throw; the intermediate path it passed through is gone.
		Assert.IsNotNull(intermediatePath);
		Assert.IsFalse(File.Exists(intermediatePath));
		Assert.AreEqual("content", File.ReadAllText(destinationPath));
		Assert.AreEqual(1, Directory.GetFiles(directory).Length);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_UnauthorizedAccess_ReportsAccessDenied()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string destinationPath = Path.Combine(directory, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		// A permission failure is a host-actionable outcome, so it carries the AccessDenied code instead
		// of the generic write-failure code.
		var fileSystem = new LocalWorkspaceFileSystem(null, new WorkspaceFileOperations(
			Replace: static (_, _) => throw new UnauthorizedAccessException("The destination is read-only."),
			MoveFile: static (_, _) => throw new UnauthorizedAccessException("The destination is read-only."),
			MoveDirectory: static (_, _) => throw new UnauthorizedAccessException("The destination is read-only.")));
		FileStamp stamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);

		WorkspaceFileReplacementResult result = await fileSystem.WriteFileAsync(
			destinationPath,
			Encoding.UTF8.GetBytes("content"),
			stamp,
			CancellationToken.None);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Failed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure!.Code);
	}
}
