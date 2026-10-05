using System.Text;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class LocalWorkspaceFileSystemTests
{
	private const int SharingViolation = unchecked((int)0x80070020);
	private const int LockViolation = unchecked((int)0x80070021);
	private const int UnableToRemoveReplaced = unchecked((int)0x80070497);
	private const int UnableToMoveReplacement = unchecked((int)0x80070498);
	private const int UnableToMoveReplacement2 = unchecked((int)0x80070499);
	private const int GenericIOFailure = unchecked((int)0x80131620);

	[TestMethod]
	[DataRow(SharingViolation)]
	[DataRow(LockViolation)]
	[DataRow(UnableToRemoveReplaced)]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_ReplaceFailsOnceTransiently_RetriesAndReplaces(int hResult)
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: (sourcePath, targetPath) =>
			{
				if (++calls == 1)
					throw new IOException("The file is in use.", hResult);

				File.Replace(sourcePath, targetPath, null);
			}
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, result.Outcome);
		Assert.AreEqual(2, calls);
		Assert.AreEqual("replacement", File.ReadAllText(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_MoveOfANewFileFailsOnceTransiently_RetriesAndCreatesTheFile()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (sourcePath, targetPath) =>
			{
				if (++calls == 1)
					throw new IOException("The file is in use.", SharingViolation);

				File.Move(sourcePath, targetPath, overwrite: false);
			}
		);

		WorkspaceFileReplacementResult result = await fileSystem.WriteFileAsync(
			destinationPath,
			content: Encoding.UTF8.GetBytes("created"),
			expectedStamp: FileStamp.Missing,
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, result.Outcome);
		Assert.AreEqual(2, calls);
		Assert.AreEqual("created", File.ReadAllText(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_ReplaceKeepsFailingTransiently_ReportsWriteFailedAfterFiveAttempts()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: (_, _) =>
			{
				calls++;
				throw new IOException("The file is in use.", UnableToRemoveReplaced);
			}
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Failed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.WriteFailed, result.Failure!.Code);
		Assert.AreEqual(5, calls);
		Assert.AreEqual("original", File.ReadAllText(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	[DataRow(UnableToMoveReplacement)]
	[DataRow(UnableToMoveReplacement2)]
	[DataRow(GenericIOFailure)]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_ReplaceFailsWithAnotherIOFailure_DoesNotRetry(int hResult)
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: (_, _) =>
			{
				calls++;
				throw new IOException("The replacement failed.", hResult);
			}
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Failed, result.Outcome);
		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_ReplaceIsDenied_DoesNotRetry()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: (_, _) =>
			{
				calls++;
				throw new UnauthorizedAccessException("The destination is read-only.");
			}
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure!.Code);
		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_DestinationChangesBeforeTheRetry_ReportsExternalFileConflict()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		int calls = 0;

		// Another writer changes the destination while the replace fails, so the retry must not overwrite it.
		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: (sourcePath, targetPath) =>
			{
				if (++calls == 1)
				{
					File.WriteAllText(targetPath, "written by another process");
					throw new IOException("The file is in use.", SharingViolation);
				}

				File.Replace(sourcePath, targetPath, null);
			}
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(1, calls);
		Assert.AreEqual("written by another process", File.ReadAllText(destinationPath));
		Assert.AreEqual(await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None), result.ObservedOnDiskStamp);
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_TokenCanceledBeforeTheRetry_ReportsCanceled()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		using var cancellation = new CancellationTokenSource();
		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: (sourcePath, targetPath) =>
			{
				if (++calls == 1)
				{
					cancellation.Cancel();
					throw new IOException("The file is in use.", SharingViolation);
				}

				File.Replace(sourcePath, targetPath, null);
			}
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: cancellation.Token
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Canceled, result.Outcome);
		Assert.AreEqual(1, calls);
		Assert.AreEqual("original", File.ReadAllText(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_MoveFailsOnceTransiently_RetriesAndMoves()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source.txt");
		string destinationPath = Path.Combine(temp.Path, "destination.txt");

		File.WriteAllText(sourcePath, "content");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (source, destination) =>
			{
				if (++calls == 1)
					throw new IOException("The file is in use.", SharingViolation);

				File.Move(source, destination, overwrite: false);
			}
		);

		WorkspaceFileMoveResult result = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.AreEqual(2, calls);
		Assert.IsFalse(File.Exists(sourcePath));
		Assert.AreEqual("content", File.ReadAllText(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_MoveKeepsFailingTransiently_ReportsMoveFailedAfterFiveAttempts()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source.txt");
		string destinationPath = Path.Combine(temp.Path, "destination.txt");

		File.WriteAllText(sourcePath, "content");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (_, _) =>
			{
				calls++;
				throw new IOException("The file is in use.", SharingViolation);
			}
		);

		WorkspaceFileMoveResult result = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.MoveFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveFailed, result.Failure!.Code);
		Assert.AreEqual(5, calls);
		Assert.IsTrue(File.Exists(sourcePath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_MoveFailsWithAnotherFailure_DoesNotRetry()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source.txt");
		string destinationPath = Path.Combine(temp.Path, "destination.txt");

		File.WriteAllText(sourcePath, "content");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (_, _) =>
			{
				throw ++calls == 1
					? (Exception)new IOException("The move failed.", GenericIOFailure)
					: new UnauthorizedAccessException("The source is read-only.");
			}
		);

		WorkspaceFileMoveResult ioFailure = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: CancellationToken.None);
		WorkspaceFileMoveResult denied = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: CancellationToken.None);

		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveFailed, ioFailure.Failure!.Code);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, denied.Failure!.Code);
		Assert.AreEqual(2, calls);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_DestinationAppearsBeforeTheRetry_ReportsDestinationExists()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source.txt");
		string destinationPath = Path.Combine(temp.Path, "destination.txt");

		File.WriteAllText(sourcePath, "content");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (source, destination) =>
			{
				if (++calls == 1)
				{
					File.WriteAllText(destination, "written by another process");
					throw new IOException("The file is in use.", SharingViolation);
				}

				File.Move(source, destination, overwrite: true);
			}
		);

		WorkspaceFileMoveResult result = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.DestinationExists, result.Outcome);
		Assert.AreEqual(1, calls);
		Assert.AreEqual("content", File.ReadAllText(sourcePath));
		Assert.AreEqual("written by another process", File.ReadAllText(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_SourceChangesBeforeTheRetry_ReportsExternalFileConflict()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source.txt");
		string destinationPath = Path.Combine(temp.Path, "destination.txt");

		File.WriteAllText(sourcePath, "content");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (source, destination) =>
			{
				if (++calls == 1)
				{
					File.WriteAllText(source, "changed by another process");
					throw new IOException("The file is in use.", SharingViolation);
				}

				File.Move(source, destination, overwrite: false);
			}
		);

		WorkspaceFileMoveResult result = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(1, calls);
		Assert.AreEqual(await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None), result.ObservedOnDiskStamp);
		Assert.IsFalse(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_TokenCanceledBeforeTheRetry_ReportsCanceled()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source.txt");
		string destinationPath = Path.Combine(temp.Path, "destination.txt");

		File.WriteAllText(sourcePath, "content");

		using var cancellation = new CancellationTokenSource();
		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (source, destination) =>
			{
				if (++calls == 1)
				{
					cancellation.Cancel();
					throw new IOException("The file is in use.", SharingViolation);
				}

				File.Move(source, destination, overwrite: false);
			}
		);

		WorkspaceFileMoveResult result = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: cancellation.Token);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Canceled, result.Outcome);
		Assert.AreEqual(1, calls);
		Assert.IsTrue(File.Exists(sourcePath));
		Assert.IsFalse(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveDirectoryAsync_MoveFailsOnceTransiently_RetriesAndMoves()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source");
		string destinationPath = Path.Combine(temp.Path, "destination");

		Directory.CreateDirectory(sourcePath);

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveDirectory: (source, destination) =>
			{
				if (++calls == 1)
					throw new IOException("A process uses the directory.", SharingViolation);

				Directory.Move(source, destination);
			}
		);

		WorkspaceFileMoveResult result = await fileSystem.MoveDirectoryAsync(sourcePath, destinationPath, CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.AreEqual(2, calls);
		Assert.IsTrue(Directory.Exists(destinationPath));
		Assert.IsFalse(Directory.Exists(sourcePath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveDirectoryAsync_DestinationAppearsBeforeTheRetry_ReportsDestinationExists()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source");
		string destinationPath = Path.Combine(temp.Path, "destination");

		Directory.CreateDirectory(sourcePath);

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveDirectory: (source, destination) =>
			{
				if (++calls == 1)
				{
					Directory.CreateDirectory(destination);
					throw new IOException("A process uses the directory.", SharingViolation);
				}

				Directory.Move(source, destination);
			}
		);

		WorkspaceFileMoveResult result = await fileSystem.MoveDirectoryAsync(sourcePath, destinationPath, CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.DestinationExists, result.Outcome);
		Assert.AreEqual(1, calls);
		Assert.IsTrue(Directory.Exists(sourcePath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveDirectoryAsync_TokenCanceledBeforeTheRetry_ReportsCanceled()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source");
		string destinationPath = Path.Combine(temp.Path, "destination");

		Directory.CreateDirectory(sourcePath);

		using var cancellation = new CancellationTokenSource();
		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveDirectory: (source, destination) =>
			{
				if (++calls == 1)
				{
					cancellation.Cancel();
					throw new IOException("A process uses the directory.", SharingViolation);
				}

				Directory.Move(source, destination);
			}
		);

		WorkspaceFileMoveResult result = await fileSystem.MoveDirectoryAsync(sourcePath, destinationPath, cancellation.Token);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Canceled, result.Outcome);
		Assert.AreEqual(1, calls);
		Assert.IsTrue(Directory.Exists(sourcePath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_DestinationChangesBeforeALaterRetry_ReportsExternalFileConflict()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		int calls = 0;

		// The first retry still finds the expected destination; another writer changes it during the second
		// failure, so the check before the third attempt must see the change.
		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: (sourcePath, targetPath) =>
			{
				calls++;

				if (calls == 2)
					File.WriteAllText(targetPath, "written by another process");

				if (calls <= 2)
					throw new IOException("The file is in use.", SharingViolation);

				File.Replace(sourcePath, targetPath, null);
			}
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(2, calls);
		Assert.AreEqual("written by another process", File.ReadAllText(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_ExclusiveHolderOutlastsTheRetries_ReportsWriteFailed()
	{
		RequireWindows();

		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		var fileSystem = new LocalWorkspaceFileSystem();

		FileStamp expectedStamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);
		WorkspaceFileReplacementResult result;

		// The holder denies reading too, so the evidence capture after the last attempt fails as well; the
		// replace still published nothing.
		using (new FileStream(path: destinationPath, mode: FileMode.Open, access: FileAccess.Read, share: FileShare.None))
		{
			result = await fileSystem.WriteFileAsync(
				destinationPath,
				content: Encoding.UTF8.GetBytes("replacement"),
				expectedStamp,
				cancellationToken: CancellationToken.None
			);
		}

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Failed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.WriteFailed, result.Failure!.Code);
		Assert.AreEqual("original", File.ReadAllText(destinationPath));
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_PreconditionCaptureMeetsASharingViolation_ReportsWriteFailed()
	{
		RequireWindows();

		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		FileStream? holder = null;
		int calls = 0;

		// Another writer changes the destination and keeps it open exclusively, so the check before the retry
		// must capture the destination's content and cannot read it.
		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: (_, targetPath) =>
			{
				calls++;
				File.WriteAllText(targetPath, "written by another process");
				holder = new FileStream(path: targetPath, mode: FileMode.Open, access: FileAccess.Read, share: FileShare.None);
				throw new IOException("The file is in use.", SharingViolation);
			}
		);

		try
		{
			WorkspaceFileReplacementResult result = await WriteAsync(
				fileSystem,
				destinationPath,
				content: "replacement",
				cancellationToken: CancellationToken.None
			);

			Assert.AreEqual(WorkspaceFileReplacementOutcome.Failed, result.Outcome);
			Assert.AreEqual(WorkspaceOperationFailureCodes.WriteFailed, result.Failure!.Code);
			Assert.AreEqual(1, calls);
		}
		finally
		{
			holder?.Dispose();
		}
	}

	[TestMethod]
	[DataRow(UnableToMoveReplacement)]
	[DataRow(UnableToMoveReplacement2)]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_ReplaceLeftTheReplacementUnmoved_MovesItIntoPlace(int hResult)
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		// Without a backup, File.Replace removes the replaced file (0x80070498) or renames it aside
		// (0x80070499) and leaves the new content under the temporary name.
		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: (_, targetPath) =>
			{
				if (hResult == UnableToMoveReplacement)
					File.Delete(targetPath);
				else
					File.Move(targetPath, targetPath + ".aside");

				throw new IOException("The replacement could not be moved.", hResult);
			},

			moveFile: static (source, destination) => File.Move(source, destination, overwrite: false)
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, result.Outcome);
		Assert.AreEqual("replacement", File.ReadAllText(destinationPath));
		Assert.AreEqual(await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None), result.ObservedOnDiskStamp);
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_UnmovedReplacementCannotBeMoved_KeepsTheTemporaryFileAndNamesIt()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: static (_, targetPath) =>
			{
				File.Delete(targetPath);
				throw new IOException("The replacement could not be moved.", UnableToMoveReplacement);
			},

			moveFile: static (_, _) => throw new IOException("The move failed.", GenericIOFailure)
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.ReplacementStateUnknown, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, result.Failure!.Code);

		string temporaryPath = Directory.GetFiles(temp.Path, "*.tmp").Single();

		Assert.AreEqual("replacement", File.ReadAllText(temporaryPath));
		Assert.Contains(temporaryPath, result.Failure.Message);
		Assert.IsFalse(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_WriteFileAsync_TemporaryFileCleanupFailsOnceTransiently_RetriesAndRemovesIt()
	{
		using var temp = new TemporaryDirectory();
		string destinationPath = Path.Combine(temp.Path, "destination.txt");
		File.WriteAllText(destinationPath, "original");

		int deletes = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			replace: static (_, _) => throw new IOException("The replacement failed.", GenericIOFailure),

			deleteFile: path =>
			{
				if (++deletes == 1)
					throw new IOException("A scanner holds the file.", SharingViolation);

				File.Delete(path);
			}
		);

		WorkspaceFileReplacementResult result = await WriteAsync(
			fileSystem,
			destinationPath,
			content: "replacement",
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(WorkspaceFileReplacementOutcome.Failed, result.Outcome);
		Assert.AreEqual(2, deletes);
		Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_CaseOnlyMoveWhoseSecondStepFailsTransiently_RetriesItInPlace()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "Script.lua");
		string destinationPath = Path.Combine(temp.Path, "script.lua");

		File.WriteAllText(sourcePath, "content");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (source, destination) =>
			{
				if (++calls == 2)
					throw new IOException("A scanner holds the intermediate file.", SharingViolation);

				File.Move(source, destination, overwrite: false);
			},

			options: new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive }
		);

		WorkspaceFileMoveResult result = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.AreEqual(3, calls);
		Assert.AreEqual("script.lua", Path.GetFileName(Directory.GetFiles(temp.Path).Single()));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_CaseOnlyMoveWhoseRollbackFailsTransiently_RetriesItAndRestoresTheSource()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "Script.lua");
		string destinationPath = Path.Combine(temp.Path, "script.lua");

		File.WriteAllText(sourcePath, "content");

		int calls = 0;

		// The move to the destination fails for good, and the first rollback attempt meets a scanner.
		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (source, destination) =>
			{
				switch (++calls)
				{
					case 2:
						throw new IOException("The move failed.", GenericIOFailure);

					case 3:
						throw new IOException("A scanner holds the intermediate file.", SharingViolation);

					default:
						File.Move(source, destination, overwrite: false);
						break;
				}
			},

			options: new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive }
		);

		WorkspaceFileMoveResult result = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.MoveFailed, result.Outcome);
		Assert.AreEqual(4, calls);
		Assert.AreEqual("Script.lua", Path.GetFileName(Directory.GetFiles(temp.Path).Single()));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_CaseOnlyMoveWhoseSourceCannotBeRead_ReportsMoveFailed()
	{
		RequireWindows();

		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "Script.lua");
		string destinationPath = Path.Combine(temp.Path, "script.lua");

		File.WriteAllText(sourcePath, "content");

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			options: new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive }
		);

		FileStamp expectedSourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);
		WorkspaceFileMoveResult result;

		// The destination resolves to the source itself, so a failed source capture is a move failure, not an
		// existing destination.
		using (new FileStream(path: sourcePath, mode: FileMode.Open, access: FileAccess.Read, share: FileShare.None))
			result = await fileSystem.MoveAsync(sourcePath, destinationPath, expectedSourceStamp, cancellationToken: CancellationToken.None);

		Assert.AreEqual(WorkspaceFileMoveOutcome.MoveFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveFailed, result.Failure!.Code);
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_DeleteAsync_DeleteFailsOnceTransiently_RetriesAndDeletes()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "file.txt");
		File.WriteAllText(path, "content");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(deleteFile: target =>
		{
			if (++calls == 1)
				throw new IOException("A scanner holds the file.", SharingViolation);

			File.Delete(target);
		});

		FileStamp stamp = await fileSystem.CaptureStampAsync(path, CancellationToken.None);
		WorkspaceFileDeleteResult result = await fileSystem.DeleteAsync(path, stamp, CancellationToken.None);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(2, calls);
		Assert.IsFalse(File.Exists(path));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_DeleteAsync_FileChangesBeforeTheRetry_ReportsExternalFileConflict()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "file.txt");
		File.WriteAllText(path, "content");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(deleteFile: target =>
		{
			if (++calls == 1)
			{
				File.WriteAllText(target, "changed by another process");
				throw new IOException("A scanner holds the file.", SharingViolation);
			}

			File.Delete(target);
		});

		FileStamp stamp = await fileSystem.CaptureStampAsync(path, CancellationToken.None);
		WorkspaceFileDeleteResult result = await fileSystem.DeleteAsync(path, stamp, CancellationToken.None);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(1, calls);
		Assert.AreEqual("changed by another process", File.ReadAllText(path));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_DeleteAsync_DeleteKeepsFailingTransiently_ReportsDeleteFailedAfterFiveAttempts()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "file.txt");
		File.WriteAllText(path, "content");

		int calls = 0;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(deleteFile: _ =>
		{
			calls++;
			throw new IOException("A scanner holds the file.", SharingViolation);
		});

		FileStamp stamp = await fileSystem.CaptureStampAsync(path, CancellationToken.None);
		WorkspaceFileDeleteResult result = await fileSystem.DeleteAsync(path, stamp, CancellationToken.None);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.DeleteFailed, result.Failure!.Code);
		Assert.AreEqual(5, calls);
		Assert.IsTrue(File.Exists(path));
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_MoveAsync_AHolderThatDeniesReadingUntilTheRetry_IsWaitedOut()
	{
		using var temp = new TemporaryDirectory();

		string sourcePath = Path.Combine(temp.Path, "source.txt");
		string destinationPath = Path.Combine(temp.Path, "destination.txt");

		File.WriteAllText(sourcePath, "content");

		int calls = 0;
		FileStream? holder = null;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(
			moveFile: (source, destination) =>
			{
				// The holder shares nothing, so it denies a read of the file and the move until it lets go.
				if (++calls == 1)
				{
					holder = new FileStream(path: source, mode: FileMode.Open, access: FileAccess.Read, share: FileShare.None);
					throw new IOException("An indexer holds the file.", SharingViolation);
				}

				holder?.Dispose();
				File.Move(source, destination, overwrite: false);
			}
		);

		try
		{
			WorkspaceFileMoveResult result = await MoveAsync(fileSystem, sourcePath, destinationPath, cancellationToken: CancellationToken.None);

			Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
			Assert.AreEqual(2, calls);
			Assert.AreEqual("content", File.ReadAllText(destinationPath));
		}
		finally
		{
			holder?.Dispose();
		}
	}

	[TestMethod]
	public async Task LocalWorkspaceFileSystem_DeleteAsync_AHolderThatDeniesReadingUntilTheRetry_IsWaitedOut()
	{
		using var temp = new TemporaryDirectory();
		string path = Path.Combine(temp.Path, "file.txt");
		File.WriteAllText(path, "content");

		int calls = 0;
		FileStream? holder = null;

		LocalWorkspaceFileSystem fileSystem = CreateFileSystem(deleteFile: target =>
		{
			if (++calls == 1)
			{
				holder = new FileStream(path: target, mode: FileMode.Open, access: FileAccess.Read, share: FileShare.None);
				throw new IOException("An indexer holds the file.", SharingViolation);
			}

			holder?.Dispose();
			File.Delete(target);
		});

		try
		{
			FileStamp stamp = await fileSystem.CaptureStampAsync(path, CancellationToken.None);
			WorkspaceFileDeleteResult result = await fileSystem.DeleteAsync(path, stamp, CancellationToken.None);

			Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, result.Outcome);
			Assert.AreEqual(2, calls);
			Assert.IsFalse(File.Exists(path));
		}
		finally
		{
			holder?.Dispose();
		}
	}

	private static LocalWorkspaceFileSystem CreateFileSystem(
		Action<string, string>? replace = null,
		Action<string, string>? moveFile = null,
		Action<string, string>? moveDirectory = null,
		Action<string>? deleteFile = null,
		WorkspaceOptions? options = null
	)
	{
		return new LocalWorkspaceFileSystem(
			options,

			new WorkspaceFileSystemPrimitives(
				Replace: replace ?? (static (_, _) => throw new InvalidOperationException("The test does not replace a file.")),
				MoveFile: moveFile ?? (static (_, _) => throw new InvalidOperationException("The test does not move a file.")),
				MoveDirectory: moveDirectory ?? (static (_, _) => throw new InvalidOperationException("The test does not move a directory.")),
				DeleteFile: deleteFile ?? File.Delete
			)
		);
	}

	private static void RequireWindows()
	{
		if (!OperatingSystem.IsWindows())
			Assert.Inconclusive("The test needs the Windows sharing modes, which other hosts do not enforce.");
	}

	private static async Task<WorkspaceFileReplacementResult> WriteAsync(
		LocalWorkspaceFileSystem fileSystem,
		string destinationPath,
		string content,
		CancellationToken cancellationToken
	)
	{
		FileStamp expectedStamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);
		return await fileSystem.WriteFileAsync(destinationPath, content: Encoding.UTF8.GetBytes(content), expectedStamp, cancellationToken);
	}

	private static async Task<WorkspaceFileMoveResult> MoveAsync(
		LocalWorkspaceFileSystem fileSystem,
		string sourcePath,
		string destinationPath,
		CancellationToken cancellationToken
	)
	{
		FileStamp expectedSourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);
		return await fileSystem.MoveAsync(sourcePath, destinationPath, expectedSourceStamp, cancellationToken);
	}
}
