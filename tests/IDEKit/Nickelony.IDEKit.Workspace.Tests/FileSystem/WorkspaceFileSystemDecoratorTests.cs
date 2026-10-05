using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceFileSystemDecoratorTests
{
	[TestMethod]
	public void Constructor_NullInner_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new TrashFileSystem(null!));

	[TestMethod]
	public async Task DefaultMembers_ForwardToTheInnerFileSystem()
	{
		var inner = new RecordingFileSystem();
		var decorator = new TrashFileSystem(inner);

		WorkspaceFileReadResult read = await decorator.ReadAsync("script.lua", CancellationToken.None);
		FileStamp stamp = await decorator.CaptureStampAsync("script.lua", CancellationToken.None);

		WorkspaceFileReplacementResult written = await decorator.WriteFileAsync(
			destinationPath: "script.lua",
			content: new byte[] { 1 },
			expectedStamp: FileStamp.Missing,
			cancellationToken: CancellationToken.None
		);

		WorkspaceFileMoveResult moved = await decorator.MoveAsync(
			sourcePath: "source.lua",
			destinationPath: "destination.lua",
			expectedSourceStamp: FileStamp.Missing,
			cancellationToken: CancellationToken.None
		);

		WorkspaceFileMoveResult directoryMoved = await decorator.MoveDirectoryAsync("source", "destination", CancellationToken.None);

		WorkspaceFileDeleteResult directoryDeleted = await decorator.DeleteDirectoryAsync("folder", CancellationToken.None);

		// Every member the decorator does not override forwards to the inner file system.
		Assert.AreEqual("content", read.Content);
		Assert.AreEqual(FileStamp.Missing, stamp);
		Assert.AreEqual(WorkspaceFileReplacementOutcome.Replaced, written.Outcome);
		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, moved.Outcome);
		Assert.AreEqual(WorkspaceFileMoveOutcome.Moved, directoryMoved.Outcome);
		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, directoryDeleted.Outcome);
		Assert.AreEqual(1, inner.ReadCount);
		Assert.AreEqual(1, inner.CaptureCount);
		Assert.AreEqual(1, inner.WriteCount);
		Assert.AreEqual(1, inner.MoveCount);
		Assert.AreEqual(1, inner.MoveDirectoryCount);
		Assert.AreEqual(1, inner.DeleteDirectoryCount);
	}

	[TestMethod]
	public async Task DefaultDelete_ForwardsToTheInnerFileSystem()
	{
		var inner = new RecordingFileSystem();
		var decorator = new PassThroughFileSystem(inner);

		WorkspaceFileDeleteResult deleted = await decorator.DeleteAsync("script.lua", FileStamp.Missing, CancellationToken.None);

		// A decorator without a delete override forwards the delete to the inner file system.
		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, deleted.Outcome);
		Assert.AreEqual(1, inner.DeleteCount);
	}

	[TestMethod]
	public async Task OverriddenMember_UsesHostPolicyWhileOthersForward()
	{
		var inner = new RecordingFileSystem();
		var decorator = new TrashFileSystem(inner);

		WorkspaceFileDeleteResult deleted = await decorator.DeleteAsync("script.lua", FileStamp.Missing, CancellationToken.None);
		WorkspaceFileReadResult read = await decorator.ReadAsync("script.lua", CancellationToken.None);

		// The overridden delete routes through the host policy while the remaining members keep
		// forwarding to the inner file system.
		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, deleted.Outcome);
		Assert.AreSequenceEqual(["script.lua"], [.. decorator.TrashedPaths]);
		Assert.AreEqual(0, inner.DeleteCount);
		Assert.AreEqual("content", read.Content);
		Assert.AreEqual(1, inner.ReadCount);
	}

	/// <summary>The trash decorator that the Workspace guide shows under "A trash decorator".</summary>
	private sealed class TrashFileSystem(IWorkspaceFileSystem inner) : WorkspaceFileSystemDecorator(inner)
	{
		public List<string> TrashedPaths { get; } = [];

		public override async Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken = default
		)
		{
			FileStamp actual = await Inner.CaptureStampAsync(path, cancellationToken).ConfigureAwait(false);

			if (actual != expectedStamp)
				return new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.ExternalFileConflict, actual);

			TrashedPaths.Add(path); // a host moves the file to its trash or recycle bin here

			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted);
		}
	}

	[TestMethod]
	public async Task OverriddenDelete_WhenTheFileChangedSinceTheStamp_ReportsAConflictAndTrashesNothing()
	{
		var inner = new RecordingFileSystem();
		var decorator = new TrashFileSystem(inner);

		// The inner file system reports the file as missing, so a stamp of an existing file is stale.
		var staleStamp = new FileStamp(Exists: true, Length: 1, LastWriteTimeUtc: DateTime.UnixEpoch, ContentHash: "stale");

		WorkspaceFileDeleteResult deleted = await decorator.DeleteAsync("script.lua", staleStamp, CancellationToken.None);

		Assert.AreEqual(WorkspaceFileDeleteOutcome.ExternalFileConflict, deleted.Outcome);
		Assert.AreEqual(FileStamp.Missing, deleted.ObservedOnDiskStamp);
		Assert.IsEmpty(decorator.TrashedPaths);
		Assert.AreEqual(0, inner.DeleteCount);
	}

	private sealed class PassThroughFileSystem : WorkspaceFileSystemDecorator
	{
		public PassThroughFileSystem(IWorkspaceFileSystem inner)
			: base(inner) { }
	}

	private sealed class RecordingFileSystem : IWorkspaceFileSystem
	{
		public int ReadCount { get; private set; }

		public int CaptureCount { get; private set; }

		public int WriteCount { get; private set; }

		public int MoveCount { get; private set; }

		public int MoveDirectoryCount { get; private set; }

		public int DeleteCount { get; private set; }

		public int DeleteDirectoryCount { get; private set; }

		public Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken)
		{
			ReadCount++;
			return Task.FromResult(WorkspaceFileReadResult.FromContent("content", default, FileStamp.Missing));
		}

		public Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken)
		{
			CaptureCount++;
			return Task.FromResult(FileStamp.Missing);
		}

		public Task<WorkspaceFileReplacementResult> WriteFileAsync(
			string destinationPath,
			ReadOnlyMemory<byte> content,
			FileStamp expectedStamp,
			CancellationToken cancellationToken
		)
		{
			WriteCount++;
			return Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced));
		}

		public Task<WorkspaceFileMoveResult> MoveAsync(
			string sourcePath,
			string destinationPath,
			FileStamp expectedSourceStamp,
			CancellationToken cancellationToken
		)
		{
			MoveCount++;
			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		}

		public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
		{
			MoveDirectoryCount++;
			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		}

		public Task<WorkspaceFileDeleteResult> DeleteAsync(string path, FileStamp expectedStamp, CancellationToken cancellationToken)
		{
			DeleteCount++;
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		}

		public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(string path, CancellationToken cancellationToken)
		{
			DeleteDirectoryCount++;
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		}
	}
}
