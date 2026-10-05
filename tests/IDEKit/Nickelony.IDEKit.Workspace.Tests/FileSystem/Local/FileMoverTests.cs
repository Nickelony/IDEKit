using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Tests.FileSystemTestHelpers;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Covers the mover directly: the stamp it reports, the intermediate path of a case-only move, and what a failed move leaves behind.
/// </summary>
[TestClass]
public sealed class FileMoverTests
{
	public required TestContext TestContext { get; set; }

	private static FileMover CaseSensitiveMover(WorkspaceFileSystemPrimitives? primitives = null) =>
		new(LocalPathComparisonPolicy.CaseSensitive, primitives ?? Primitives());

	private static FileMover CaseInsensitiveMover(WorkspaceFileSystemPrimitives? primitives = null) =>
		new(LocalPathComparisonPolicy.CaseInsensitive, primitives ?? Primitives());

	private async Task<(string Path, FileStamp Stamp)> FileAsync(TemporaryDirectory temp, string name, string text)
	{
		string path = Path.Combine(temp.Path, name);
		await File.WriteAllTextAsync(path, text, TestContext.CancellationToken);

		return (path, await FileStampReader.CaptureAsync(path, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task MoveAsync_ReportsTheContentHashOfTheSourceAndTheLengthAndWriteTimeOfTheDestination()
	{
		using var temp = new TemporaryDirectory();
		(string source, FileStamp expected) = await FileAsync(temp, "source.txt", "content");

		string destination = Path.Combine(temp.Path, "destination.txt");
		DateTime rewritten = new(year: 2001, month: 2, day: 3, hour: 4, minute: 5, second: 6, kind: DateTimeKind.Utc);

		// A move across volumes copies the file, so the destination gets a write time of its own.
		FileMover mover = CaseSensitiveMover(
			Primitives(
				moveFile: (from, to) =>
				{
					File.Move(from, to, overwrite: false);
					File.SetLastWriteTimeUtc(to, rewritten);
				}
			)
		);

		WorkspaceFileMoveResult result = await mover.MoveAsync(
			sourcePath: source,
			destinationPath: destination,
			expectedSourceStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);

		FileStamp reported = result.ObservedOnDiskStamp!.Value;

		Assert.AreEqual(expected.ContentHash, reported.ContentHash);
		Assert.AreEqual(rewritten, reported.LastWriteTimeUtc);
		Assert.AreEqual(new FileInfo(destination).Length, reported.Length);
		Assert.IsTrue(reported.Exists);
	}

	[TestMethod]
	public async Task MoveAsync_ToAnotherName_MovesDirectlyWhateverThePolicy()
	{
		using var temp = new TemporaryDirectory();
		(string source, FileStamp expected) = await FileAsync(temp, "a.txt", "content");

		string destination = Path.Combine(temp.Path, "b.txt");
		List<(string From, string To)> moves = [];

		FileMover mover = CaseInsensitiveMover(
			Primitives(
				moveFile: (from, to) =>
				{
					moves.Add((from, to));
					File.Move(from, to, overwrite: false);
				}
			)
		);

		WorkspaceFileMoveResult result = await mover.MoveAsync(
			sourcePath: source,
			destinationPath: destination,
			expectedSourceStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.AreSequenceEqual([(source, destination)], moves);
	}

	[TestMethod]
	public async Task MoveAsync_OfACaseOnlyRenameOnACaseInsensitiveTarget_GoesThroughAnIntermediatePath()
	{
		using var temp = new TemporaryDirectory();
		(string source, FileStamp expected) = await FileAsync(temp, "Script.lua", "content");

		string destination = Path.Combine(temp.Path, "script.lua");
		List<(string From, string To)> moves = [];

		FileMover mover = CaseInsensitiveMover(
			Primitives(
				moveFile: (from, to) =>
				{
					moves.Add((from, to));
					File.Move(from, to, overwrite: false);
				}
			)
		);

		WorkspaceFileMoveResult result = await mover.MoveAsync(
			sourcePath: source,
			destinationPath: destination,
			expectedSourceStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.HasCount(2, moves);
		Assert.AreEqual(source, moves[0].From);
		Assert.StartsWith(source + ".", moves[0].To);
		Assert.EndsWith(".rename", moves[0].To);
		Assert.AreEqual((moves[0].To, destination), moves[1]);

		string moved = Directory.GetFiles(temp.Path).Single();

		Assert.AreEqual("script.lua", Path.GetFileName(moved));
		Assert.AreEqual("content", await File.ReadAllTextAsync(moved, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task MoveAsync_OfACaseOnlyRenameWithRealPrimitives_EndsWithTheRequestedSpelling()
	{
		using var temp = new TemporaryDirectory();
		(string source, FileStamp expected) = await FileAsync(temp, "Notes.txt", "content");
		string destination = Path.Combine(temp.Path, "notes.txt");

		WorkspaceFileMoveResult result = await CaseInsensitiveMover()
			.MoveAsync(
				sourcePath: source,
				destinationPath: destination,
				expectedSourceStamp: expected,
				cancellationToken: TestContext.CancellationToken
			);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.AreEqual("notes.txt", Path.GetFileName(Directory.GetFiles(temp.Path).Single()));
	}

	[TestMethod]
	public async Task MoveAsync_WhenTheCaseOnlyMoveFailsAfterTheIntermediatePath_RestoresTheSource()
	{
		using var temp = new TemporaryDirectory();
		(string source, FileStamp expected) = await FileAsync(temp, "Script.lua", "content");

		string destination = Path.Combine(temp.Path, "script.lua");
		int calls = 0;

		FileMover mover = CaseInsensitiveMover(
			Primitives(
				moveFile: (from, to) =>
				{
					if (++calls == 2)
						throw new IOException("The destination is locked.", GenericIOFailure);

					File.Move(from, to, overwrite: false);
				}
			)
		);

		WorkspaceFileMoveResult result = await mover.MoveAsync(
			sourcePath: source,
			destinationPath: destination,
			expectedSourceStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileMoveOutcome.MoveFailed, result.Outcome);
		Assert.AreEqual("Script.lua", Path.GetFileName(Directory.GetFiles(temp.Path).Single()));
		Assert.AreEqual("content", await File.ReadAllTextAsync(Directory.GetFiles(temp.Path).Single(), TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task MoveAsync_WhenTheRollbackAlsoFails_ReportsAnUnknownStateAndKeepsTheFile()
	{
		using var temp = new TemporaryDirectory();
		(string source, FileStamp expected) = await FileAsync(temp, "Script.lua", "content");

		string destination = Path.Combine(temp.Path, "script.lua");
		int calls = 0;

		FileMover mover = CaseInsensitiveMover(
			Primitives(
				moveFile: (from, to) =>
				{
					if (++calls > 1)
						throw new IOException("The move failed.", GenericIOFailure);

					File.Move(from, to, overwrite: false);
				}
			)
		);

		WorkspaceFileMoveResult result = await mover.MoveAsync(
			sourcePath: source,
			destinationPath: destination,
			expectedSourceStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileMoveOutcome.MoveStateUnknown, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveStateUnknown, result.Failure!.Code);
		Assert.IsInstanceOfType<AggregateException>(result.Failure.Exception);

		string kept = Directory.GetFiles(temp.Path).Single();

		Assert.EndsWith(".rename", kept);
		Assert.AreEqual("content", await File.ReadAllTextAsync(kept, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task MoveAsync_OfASourceThatChanged_ReportsAConflictAndMovesNothing()
	{
		using var temp = new TemporaryDirectory();
		(string source, FileStamp expected) = await FileAsync(temp, "source.txt", "content");
		await File.WriteAllTextAsync(source, "changed by someone else", TestContext.CancellationToken);
		int moves = 0;

		FileMover mover = CaseSensitiveMover(Primitives(moveFile: (_, _) => moves++));

		WorkspaceFileMoveResult result = await mover.MoveAsync(
			sourcePath: source,
			destinationPath: Path.Combine(temp.Path, "destination.txt"),
			expectedSourceStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileMoveOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(await FileStampReader.CaptureAsync(source, TestContext.CancellationToken), result.ObservedOnDiskStamp);
		Assert.AreEqual(0, moves);
	}

	[TestMethod]
	public async Task MoveAsync_ToAnOccupiedDestination_ReportsItAndMovesNothing()
	{
		using var temp = new TemporaryDirectory();
		(string source, FileStamp expected) = await FileAsync(temp, "source.txt", "content");
		(string destination, _) = await FileAsync(temp, "destination.txt", "occupant");
		int moves = 0;

		FileMover mover = CaseSensitiveMover(Primitives(moveFile: (_, _) => moves++));

		WorkspaceFileMoveResult result = await mover.MoveAsync(
			sourcePath: source,
			destinationPath: destination,
			expectedSourceStamp: expected,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceFileMoveOutcome.DestinationExists, result.Outcome);
		Assert.AreEqual(0, moves);
		Assert.AreEqual("occupant", await File.ReadAllTextAsync(destination, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task MoveAsync_WithACanceledToken_ReportsCanceledAndMovesNothing()
	{
		using var temp = new TemporaryDirectory();
		(string source, FileStamp expected) = await FileAsync(temp, "source.txt", "content");

		WorkspaceFileMoveResult result = await CaseSensitiveMover()
			.MoveAsync(
				sourcePath: source,
				destinationPath: Path.Combine(temp.Path, "destination.txt"),
				expectedSourceStamp: expected,
				cancellationToken: new CancellationToken(canceled: true)
			);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Canceled, result.Outcome);
		Assert.IsTrue(File.Exists(source));
	}

	[TestMethod]
	public async Task MoveDirectoryAsync_MovesTheDirectory()
	{
		using var temp = new TemporaryDirectory();
		string source = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
		await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "content", TestContext.CancellationToken);
		string destination = Path.Combine(temp.Path, "destination");

		WorkspaceFileMoveResult result = await CaseSensitiveMover().MoveDirectoryAsync(source, destination, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.IsFalse(Directory.Exists(source));
		Assert.AreEqual("content", await File.ReadAllTextAsync(Path.Combine(destination, "file.txt"), TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task MoveDirectoryAsync_OfAMissingSourceAndToAnOccupiedDestination_ReportsEachRefusal()
	{
		using var temp = new TemporaryDirectory();

		string occupied = Directory.CreateDirectory(Path.Combine(temp.Path, "occupied")).FullName;
		string source = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;

		WorkspaceFileMoveResult missing = await CaseSensitiveMover()
			.MoveDirectoryAsync(Path.Combine(temp.Path, "absent"), Path.Combine(temp.Path, "destination"), TestContext.CancellationToken);

		WorkspaceFileMoveResult exists = await CaseSensitiveMover().MoveDirectoryAsync(source, occupied, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceFileMoveOutcome.MoveFailed, missing.Outcome);
		Assert.AreEqual(WorkspaceFileMoveOutcome.DestinationExists, exists.Outcome);
		Assert.IsTrue(Directory.Exists(source));
	}

	[TestMethod]
	public async Task MoveDirectoryAsync_OfACaseOnlyRenameOnACaseInsensitiveTarget_GoesThroughAnIntermediatePath()
	{
		using var temp = new TemporaryDirectory();

		string source = Directory.CreateDirectory(Path.Combine(temp.Path, "Folder")).FullName;
		string destination = Path.Combine(temp.Path, "folder");

		List<(string From, string To)> moves = [];

		FileMover mover = CaseInsensitiveMover(
			Primitives(
				moveDirectory: (from, to) =>
				{
					moves.Add((from, to));
					Directory.Move(from, to);
				}
			)
		);

		WorkspaceFileMoveResult result = await mover.MoveDirectoryAsync(source, destination, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, result.Outcome);
		Assert.HasCount(2, moves);
		Assert.EndsWith(".rename", moves[0].To);
		Assert.AreEqual("folder", Path.GetFileName(Directory.GetDirectories(temp.Path).Single()));
	}
}
