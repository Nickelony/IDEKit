using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceFileSystemDecoratorTests
{
	[TestMethod]
	public void Constructor_NullInner_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TrashFileSystem(null!));
	}

	[TestMethod]
	public async Task DefaultMembers_ForwardToTheInnerFileSystem()
	{
		var inner = new RecordingFileSystem();
		var decorator = new TrashFileSystem(inner);

		WorkspaceFileReadResult read = await decorator.ReadAsync("script.lua", CancellationToken.None);
		FileStamp stamp = await decorator.CaptureStampAsync("script.lua", CancellationToken.None);
		WorkspaceFileReplacementResult written = await decorator.WriteFileAsync(
			"script.lua",
			new byte[] { 1 },
			FileStamp.Missing,
			CancellationToken.None);
		WorkspaceFileMoveResult moved = await decorator.MoveAsync(
			"source.lua",
			"destination.lua",
			FileStamp.Missing,
			CancellationToken.None);
		WorkspaceFileMoveResult directoryMoved = await decorator.MoveDirectoryAsync(
			"source",
			"destination",
			CancellationToken.None);
		WorkspaceFileDeleteResult directoryDeleted = await decorator.DeleteDirectoryAsync(
			"folder",
			CancellationToken.None);

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

		WorkspaceFileDeleteResult deleted = await decorator.DeleteAsync(
			"script.lua",
			FileStamp.Missing,
			CancellationToken.None);

		// A decorator without a delete override forwards the delete to the inner file system.
		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, deleted.Outcome);
		Assert.AreEqual(1, inner.DeleteCount);
	}

	[TestMethod]
	public async Task OverriddenMember_UsesHostPolicyWhileOthersForward()
	{
		var inner = new RecordingFileSystem();
		var decorator = new TrashFileSystem(inner);

		WorkspaceFileDeleteResult deleted = await decorator.DeleteAsync(
			"script.lua",
			FileStamp.Missing,
			CancellationToken.None);
		WorkspaceFileReadResult read = await decorator.ReadAsync("script.lua", CancellationToken.None);

		// The overridden delete routes through the host policy while the remaining members keep
		// forwarding to the inner file system.
		Assert.AreEqual(WorkspaceFileDeleteOutcome.Deleted, deleted.Outcome);
		CollectionAssert.AreEqual(new[] { "script.lua" }, decorator.TrashedPaths.ToArray());
		Assert.AreEqual(0, inner.DeleteCount);
		Assert.AreEqual("content", read.Content);
		Assert.AreEqual(1, inner.ReadCount);
	}

	private sealed class TrashFileSystem : WorkspaceFileSystemDecorator
	{
		public TrashFileSystem(IWorkspaceFileSystem inner)
			: base(inner)
		{ }

		public List<string> TrashedPaths { get; } = [];

		public override Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			TrashedPaths.Add(path);
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		}
	}

	private sealed class PassThroughFileSystem : WorkspaceFileSystemDecorator
	{
		public PassThroughFileSystem(IWorkspaceFileSystem inner)
			: base(inner)
		{ }
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

		public Task<WorkspaceFileReadResult> ReadAsync(
			string path,
			CancellationToken cancellationToken)
		{
			ReadCount++;
			return Task.FromResult(WorkspaceFileReadResult.FromContent("content", default, FileStamp.Missing));
		}

		public Task<FileStamp> CaptureStampAsync(
			string path,
			CancellationToken cancellationToken)
		{
			CaptureCount++;
			return Task.FromResult(FileStamp.Missing);
		}

		public Task<WorkspaceFileReplacementResult> WriteFileAsync(
			string destinationPath,
			ReadOnlyMemory<byte> content,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			WriteCount++;
			return Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced));
		}

		public Task<WorkspaceFileMoveResult> MoveAsync(
			string sourcePath,
			string destinationPath,
			FileStamp expectedSourceStamp,
			CancellationToken cancellationToken)
		{
			MoveCount++;
			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		}

		public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
			string sourcePath,
			string destinationPath,
			CancellationToken cancellationToken)
		{
			MoveDirectoryCount++;
			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		}

		public Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			DeleteCount++;
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		}

		public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
			string path,
			CancellationToken cancellationToken)
		{
			DeleteDirectoryCount++;
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		}
	}
}
