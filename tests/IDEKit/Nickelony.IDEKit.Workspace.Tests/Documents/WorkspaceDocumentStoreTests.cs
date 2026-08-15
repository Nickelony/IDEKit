using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed partial class WorkspaceDocumentStoreTests
{
	private static readonly TextFileFormat s_defaultFormat = TestSnapshots.FileFormat;

	private static readonly WorkspaceDocumentOpenOptions s_openOptions = new(
		TextEncodingKind.Utf8,
		s_defaultFormat);

	// Store paths must be fully qualified; the scripted file-system double never touches disk, so a
	// stable per-run root is enough to give every test an absolute identity path.
	private static readonly string s_testRoot = Path.Combine(
		Path.GetTempPath(),
		$"idekit-workspace-store-tests-{Guid.NewGuid():N}");

	private static string TestPath(params string[] segments) => Path.Combine([s_testRoot, .. segments]);

	private static WorkspaceDocumentCommitRequest CreateCommitRequest(WorkspaceDocumentSnapshot snapshot)
	{
		return new WorkspaceDocumentCommitRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp);
	}

	private static WorkspaceDocumentSnapshot RequireSnapshot(WorkspaceDocumentMutationResult result)
	{
		return result.Snapshot ?? throw new AssertFailedException("Expected a mutation snapshot.");
	}

	[TestMethod]
	public async Task Open_TrailingSeparatorSpelling_ResolvesToTheSameDocumentIdentity()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		string filePath = TestPath("script.lua");

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(filePath, s_openOptions)).Snapshot!;
		WorkspaceDocumentOpenResult again = await store.OpenAsync(
			filePath + Path.DirectorySeparatorChar,
			s_openOptions);

		// The trailing separator is canonicalized away, so both spellings identify one document.
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.AlreadyOpen, again.Outcome);
		Assert.AreEqual(opened.DocumentId, again.Snapshot!.DocumentId);
	}

	[TestMethod]
	public async Task Open_UndefinedOptionFormats_AreRejectedAsArgumentErrors()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => store.OpenAsync(
			TestPath("script.lua"),
			new WorkspaceDocumentOpenOptions((TextEncodingKind)42, s_defaultFormat)));
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.OpenAsync(
			TestPath("script.lua"),
			new WorkspaceDocumentOpenOptions(
				TextEncodingKind.Utf8,
				new TextFileFormat(TextEncodingKind.Windows1252, true, TextNewlineStyle.None))));
	}

	[TestMethod]
	public async Task Constructor_NullFileSystem_ThrowsAndDefaultComparisonFollowsThePlatform()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceDocumentStore(null!));

		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		// The default comparison is the operating-system assumption the document identity uses when the
		// caller does not supply one.
		Assert.AreEqual(LocalPathComparisonPolicy.ForCurrentPlatform.Comparison, store.PathComparison.Comparison);
	}

	[TestMethod]
	public async Task Requests_NullRequestArguments_AreArgumentErrors()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		Assert.ThrowsExactly<ArgumentNullException>(() => store.Replace(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => store.Discard(null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.CommitAsync(null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.ReloadAsync(null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.ResolveExternalConflictAsync(null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.RenameAsync(null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.SaveAsAsync(null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.DeleteAsync(null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.RenameDirectoryAsync(null!));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.DeleteDirectoryAsync(null!));
	}

	[TestMethod]
	public async Task InvalidPaths_AreRejectedWithoutReading()
	{
		var fileSystem = new FakeFileSystem("unused", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentOpenResult nullPath = await store.OpenAsync(null, s_openOptions);
		WorkspaceDocumentOpenResult whitespacePath = await store.OpenAsync("  ", s_openOptions);
		bool found = store.TryGetSnapshot(string.Empty, out WorkspaceDocumentSnapshot? snapshot);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.InvalidPath, nullPath.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.InvalidPath, whitespacePath.Outcome);
		Assert.IsFalse(found);
		Assert.IsNull(snapshot);
		Assert.AreEqual(0, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task GetSnapshotsUnderDirectory_NullOrBlankPathOnALiveStore_ReturnsEmpty()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("script.lua"), s_openOptions);

		// Null and blank directory paths are invalid input and return no snapshots instead of throwing,
		// even with a tracked document present.
		Assert.IsEmpty(store.GetSnapshotsUnderDirectory(null));
		Assert.IsEmpty(store.GetSnapshotsUnderDirectory("   "));
	}

	[TestMethod]
	public async Task GetSnapshotsUnderDirectory_ReturnsSnapshotsSortedByDocumentId()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(
			fileSystem,
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive });
		await store.OpenAsync(TestPath("folder", "b.lua"), s_openOptions);
		await store.OpenAsync(TestPath("folder", "a.lua"), s_openOptions);
		await store.OpenAsync(TestPath("folder", "C.lua"), s_openOptions);

		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots = store.GetSnapshotsUnderDirectory(TestPath("folder"));

		// Case-sensitive ordinal order: an uppercase 'C' sorts before the lowercase names.
		CollectionAssert.AreEqual(
			new[] { TestPath("folder", "C.lua"), TestPath("folder", "a.lua"), TestPath("folder", "b.lua") },
			snapshots.Select(snapshot => snapshot.DocumentId).ToArray());
	}

	[TestMethod]
	public void LifetimeLinkedCancellation_AfterLifetimeDisposal_ReportsNull()
	{
		var lifetime = new CancellationTokenSource();
		lifetime.Dispose();

		// The dirty-reload branch runs without an active-operation registration, so the lifetime source
		// can already be disposed when it links its token; the helper reports that state instead of
		// letting ObjectDisposedException escape a member documented to return Canceled.
		Assert.IsNull(WorkspaceDocumentStore.TryCreateLifetimeLinkedCancellation(lifetime, CancellationToken.None));
	}

	[TestMethod]
	public async Task Dispose_IsIdempotentAndRejectsSubsequentOperations()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		await store.DisposeAsync();
		await store.DisposeAsync();

		ObjectDisposedException disposed = Assert.ThrowsExactly<ObjectDisposedException>(
			() => store.TryGetSnapshot(TestPath("script.lua"), out _));
		Assert.AreEqual(typeof(WorkspaceDocumentStore).FullName, disposed.ObjectName);
		Assert.ThrowsExactly<ObjectDisposedException>(() => store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"after disposal",
			snapshot.FileFormat)));
		Assert.ThrowsExactly<ObjectDisposedException>(() => store.Discard(new WorkspaceDocumentDiscardRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version))));
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => store.OpenAsync(TestPath("other.lua"), s_openOptions));

		// Every reader member follows the disposed contract regardless of the supplied path.
		Assert.ThrowsExactly<ObjectDisposedException>(() => store.TryGetSnapshot(null, out _));
		Assert.ThrowsExactly<ObjectDisposedException>(() => store.GetSnapshotsUnderDirectory("   "));
	}

	[TestMethod]
	public async Task UnnormalizablePaths_AreReportedAsInvalidAcrossMembers()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		// A fully qualified path with an embedded null character cannot be normalized by
		// Path.GetFullPath on any supported platform, so every path-accepting member reports invalid
		// input instead of throwing.
		string invalidPath = TestPath("invalid\0path.lua");

		WorkspaceDocumentOpenResult opened = await store.OpenAsync(invalidPath, s_openOptions);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.InvalidPath, opened.Outcome);
		Assert.IsFalse(store.TryGetSnapshot(invalidPath, out _));
		Assert.AreEqual(0, store.GetSnapshotsUnderDirectory(invalidPath).Count);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentRenameResult renamed = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			invalidPath));
		WorkspaceDocumentSaveAsResult savedAs = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			invalidPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.InvalidPath, renamed.Outcome);
		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.InvalidPath, savedAs.Outcome);

		WorkspaceDocumentDirectoryRenameResult directoryRenamed = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(invalidPath, "destination"));
		WorkspaceDocumentDirectoryDeleteResult directoryDeleted = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(invalidPath));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.InvalidPath, directoryRenamed.Outcome);
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.InvalidPath, directoryDeleted.Outcome);
	}

	[TestMethod]
	public async Task RelativePaths_AreReportedAsInvalidWithoutResolvingThem()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		// Document identity is an absolute path; a relative path is rejected instead of being
		// resolved against the process current directory, so no read reaches the file system.
		WorkspaceDocumentOpenResult opened = await store.OpenAsync("script.lua", s_openOptions);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.InvalidPath, opened.Outcome);
		Assert.IsFalse(store.TryGetSnapshot("script.lua", out _));

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentRenameResult renamed = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			"renamed.lua"));
		WorkspaceDocumentSaveAsResult savedAs = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			"copy.lua"));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.InvalidPath, renamed.Outcome);
		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.InvalidPath, savedAs.Outcome);
		Assert.AreEqual(1, fileSystem.ReadCount);
		Assert.AreEqual(0, fileSystem.ReplaceCount);

		WorkspaceDocumentDirectoryRenameResult directoryRenamed = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest("folder", TestPath("moved")));
		WorkspaceDocumentDirectoryDeleteResult directoryDeleted = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest("folder"));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.InvalidPath, directoryRenamed.Outcome);
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.InvalidPath, directoryDeleted.Outcome);
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
	}

	[TestMethod]
	public async Task BlankIdentity_IsAnArgumentErrorAcrossMembers()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// A blank document id must not reach the dictionary lookup, which would throw with the misleading
		// parameter name "key"; request identities are normalized ids from a snapshot, so a blank id means
		// the caller built the identity incorrectly and every member reports it as an argument error.
		ArgumentException commitError = await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.CommitAsync(
			new WorkspaceDocumentCommitRequest(new(initial.DocumentKey, "   ", initial.Version), initial.OnDiskStamp)));
		StringAssert.Contains(commitError.ParamName, "DocumentId");
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, "   ", initial.Version))));
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, "   ", initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk)));
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(initial.DocumentKey, "   ", initial.Version),
				initial.OnDiskStamp,
				TestPath("renamed.lua"))));
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(initial.DocumentKey, "   ", initial.Version),
				initial.OnDiskStamp,
				TestPath("copy.lua"))));
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(initial.DocumentKey, "   ", initial.Version), initial.OnDiskStamp)));
		Assert.ThrowsExactly<ArgumentException>(() => store.Discard(
			new WorkspaceDocumentDiscardRequest(new(initial.DocumentKey, "   ", initial.Version))));

		// The identity is validated before the destination path, so the argument error is reported even
		// when the destination is also unnormalizable.
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(initial.DocumentKey, "   ", initial.Version),
				initial.OnDiskStamp,
				"invalid\0path.lua")));
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(initial.DocumentKey, "   ", initial.Version),
				initial.OnDiskStamp,
				"invalid\0path.lua")));

		// The guards run before any state or file-system work, so the tracked document is untouched.
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out WorkspaceDocumentSnapshot? snapshot));
		Assert.AreEqual(initial.Version, snapshot!.Version);
		Assert.AreEqual(1, fileSystem.ReadCount);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task Dispose_DuringPendingDirtyReload_ReturnsCanceled()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		var pendingStamp = new TaskCompletionSource<FileStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingCapturedStamp = pendingStamp.Task;
		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)));
		await fileSystem.Captures.WhenReachedAsync(1);

		await store.DisposeAsync();
		pendingStamp.SetResult(new FileStamp(true, 9, DateTime.UnixEpoch.AddMinutes(3), "external"));

		WorkspaceDocumentReloadResult result = await reload;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Canceled, result.Outcome);
	}

	// Shared plumbing for the two file-system doubles in this class: the content and file format they
	// both model, the read-result shape built from them, and the members a double deliberately does not
	// implement. The two dialects stay separate: FakeFileSystem scripts results through queues and
	// knobs, ControllableFileSystem holds the replacement/read rendezvous open.
	private abstract class StoreFileSystemDouble(string content, TextFileFormat fileFormat) : IWorkspaceFileSystem
	{
		protected string Content { get; } = content;

		protected TextFileFormat FileFormat { get; } = fileFormat;

		protected WorkspaceFileReadResult CreateReadResult(FileStamp stamp)
			=> WorkspaceFileReadResult.FromContent(Content, FileFormat, stamp);

		public virtual Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken)
			=> throw new NotSupportedException("This double does not script reads.");

		public virtual Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken)
			=> throw new NotSupportedException("This double does not script stamp captures.");

		public virtual Task<WorkspaceFileReplacementResult> WriteFileAsync(
			string destinationPath,
			ReadOnlyMemory<byte> content,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("This double does not script replacements.");

		public virtual Task<WorkspaceFileMoveResult> MoveAsync(
			string sourcePath,
			string destinationPath,
			FileStamp expectedSourceStamp,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("This double does not script file moves.");

		public virtual Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
			string sourcePath,
			string destinationPath,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("This double does not script directory moves.");

		public virtual Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("This double does not script deletes.");

		public virtual Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
			string path,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("This double does not script directory deletes.");
	}

	// Scripted file-system double for the store tests: every operation returns a queued or
	// knob-selected result instead of touching disk, and TestSignal ordinals expose when an
	// operation starts. The file-system-only tests use ControllableFileSystem, which holds the
	// replacement/read rendezvous open instead of scripting results; the two doubles stay separate
	// because those two dialects are genuinely different (results vs. rendezvous).
	private sealed class FakeFileSystem : StoreFileSystemDouble
	{
		public FakeFileSystem(string content, TextFileFormat fileFormat)
			: base(content, fileFormat)
		{
		}

		public int ReadCount => Reads.Count;

		public string? LastReadPath { get; private set; }

		public Task<WorkspaceFileReadResult>? PendingRead { get; set; }

		/// <summary>
		/// When set, returned as the read without observing the cancellation token, so a test can complete a
		/// read after the store was disposed instead of the read ending by cancellation.
		/// </summary>
		public Task<WorkspaceFileReadResult>? PendingReadIgnoringCancellation { get; set; }

		/// <summary>
		/// Signals that a read started; waiters name the one-based read ordinal.
		/// </summary>
		public TestSignal Reads { get; } = new();

		public Queue<Task<WorkspaceFileReadResult>> ReadResults { get; } = new();

		public Queue<FileStamp> CapturedStamps { get; } = new();

		/// <summary>
		/// Captures that need a faulted or externally completed task instead of a queued stamp.
		/// Checked before <see cref="CapturedStamps"/>.
		/// </summary>
		public Queue<Task<FileStamp>> CaptureTasks { get; } = new();

		public bool OmitReplacementStamp { get; set; }

		public Task<WorkspaceFileReplacementResult>? PendingReplacement { get; set; }

		/// <summary>
		/// Signals that a replacement (commit or save-as) started; waiters name the one-based ordinal.
		/// </summary>
		public TestSignal Replacements { get; } = new();

		public WorkspaceFileReplacementOutcome ReplacementOutcome { get; set; } =
			WorkspaceFileReplacementOutcome.Replaced;

		public int ReplaceCount => Replacements.Count;

		public FileStamp? ReplacementStamp { get; set; }

		public FileStamp? LastReplacementExpectedStamp { get; private set; }

		public override async Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken)
		{
			Reads.Advance();
			LastReadPath = path;
			cancellationToken.ThrowIfCancellationRequested();

			if (PendingReadIgnoringCancellation is not null)
				return await PendingReadIgnoringCancellation.ConfigureAwait(false);

			if (PendingRead is not null)
				return await PendingRead.WaitAsync(cancellationToken);

			if (ReadResults.Count > 0)
				return await ReadResults.Dequeue();

			return CreateReadResult();
		}

		/// <summary>
		/// Signals that a stamp capture started; waiters name the one-based ordinal.
		/// </summary>
		public TestSignal Captures { get; } = new();

		public Task<FileStamp>? PendingCapturedStamp { get; set; }

		public override Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken)
		{
			Captures.Advance();
			cancellationToken.ThrowIfCancellationRequested();

			// The pending-stamp trap deliberately returns the raw task without observing the
			// cancellation token so disposal cannot shortcut the path under test.
			if (PendingCapturedStamp is not null)
				return PendingCapturedStamp;

			if (CaptureTasks.Count > 0)
				return CaptureTasks.Dequeue();

			if (CapturedStamps.Count > 0)
				return Task.FromResult(CapturedStamps.Dequeue());

			return Task.FromResult(CreateReadResult().OnDiskStamp);
		}

		public override Task<WorkspaceFileReplacementResult> WriteFileAsync(
			string destinationPath,
			ReadOnlyMemory<byte> content,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			Replacements.Advance();
			LastWriteDestination = destinationPath;
			LastReplacementExpectedStamp = expectedStamp;
			cancellationToken.ThrowIfCancellationRequested();

			if (PendingReplacement is not null)
				return PendingReplacement.WaitAsync(cancellationToken);

			return Task.FromResult(new WorkspaceFileReplacementResult(
				ReplacementOutcome,
				OmitReplacementStamp
					? null
					: ReplacementStamp ?? new FileStamp(true, content.Length, DateTime.UnixEpoch, "logical")));
		}

		public Task<WorkspaceFileDeleteResult>? PendingDelete { get; set; }

		/// <summary>
		/// Signals that a delete started; waiters name the one-based ordinal.
		/// </summary>
		public TestSignal Deletes { get; } = new();

		/// <summary>
		/// Gets the expected stamp forwarded by the most recent delete.
		/// </summary>
		public FileStamp? LastDeleteExpectedStamp { get; private set; }

		public string? LastWriteDestination { get; private set; }

		public WorkspaceFileMoveResult MoveResult { get; set; } =
			new(WorkspaceFileMoveOutcome.Moved);

		public WorkspaceFileMoveResult MoveDirectoryResult { get; set; } =
			new(WorkspaceFileMoveOutcome.Moved);

		public Task<WorkspaceFileMoveResult>? PendingMove { get; set; }

		public Task<WorkspaceFileMoveResult>? PendingMoveDirectory { get; set; }

		/// <summary>
		/// When set, invoked with the cancellation token of a directory move so a test can observe the
		/// token the store linked to its lifetime token.
		/// </summary>
		public Action<CancellationToken>? MoveDirectoryCancellationObserver { get; set; }

		public Task<WorkspaceFileDeleteResult>? PendingDirectoryDelete { get; set; }

		/// <summary>
		/// When set, the matching file-system operation throws instead of returning a result, modeling a
		/// local file system whose platform call fails.
		/// </summary>
		public bool ThrowOnMove { get; set; }

		public bool ThrowOnMoveDirectory { get; set; }

		public bool ThrowOnDelete { get; set; }

		public bool ThrowOnDirectoryDelete { get; set; }

		/// <summary>
		/// Signals that a file move started; waiters name the one-based ordinal.
		/// </summary>
		public TestSignal Moves { get; } = new();

		/// <summary>
		/// Gets the expected source stamp forwarded by the most recent file move.
		/// </summary>
		public FileStamp? LastMoveExpectedStamp { get; private set; }

		/// <summary>
		/// Gets the destination path forwarded by the most recent file move.
		/// </summary>
		public string? LastMoveDestination { get; private set; }

		/// <summary>
		/// Signals that a directory move started; waiters name the one-based ordinal.
		/// </summary>
		public TestSignal MoveDirectories { get; } = new();

		/// <summary>
		/// Signals that a directory delete started; waiters name the one-based ordinal.
		/// </summary>
		public TestSignal DirectoryDeletes { get; } = new();

		public int MoveDirectoryCount { get; private set; }

		public string? LastMoveDirectorySource { get; private set; }

		public string? LastMoveDirectoryDestination { get; private set; }

		public WorkspaceFileDeleteOutcome DeleteOutcome { get; set; } =
			WorkspaceFileDeleteOutcome.Deleted;

		public WorkspaceFileDeleteOutcome DirectoryDeleteOutcome { get; set; } =
			WorkspaceFileDeleteOutcome.Deleted;

		public string? LastDirectoryDeletePath { get; private set; }

		public override Task<WorkspaceFileMoveResult> MoveAsync(
			string sourcePath,
			string destinationPath,
			FileStamp expectedSourceStamp,
			CancellationToken cancellationToken)
		{
			Moves.Advance();
			LastMoveExpectedStamp = expectedSourceStamp;
			LastMoveDestination = destinationPath;
			cancellationToken.ThrowIfCancellationRequested();

			if (ThrowOnMove)
				throw new IOException("The move failed.");

			return PendingMove?.WaitAsync(cancellationToken) ?? Task.FromResult(MoveResult);
		}

		public override Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
			string sourcePath,
			string destinationPath,
			CancellationToken cancellationToken)
		{
			MoveDirectoryCount++;
			LastMoveDirectorySource = sourcePath;
			LastMoveDirectoryDestination = destinationPath;
			MoveDirectories.Advance();
			cancellationToken.ThrowIfCancellationRequested();

			if (ThrowOnMoveDirectory)
				throw new IOException("The directory move failed.");

			MoveDirectoryCancellationObserver?.Invoke(cancellationToken);

			return PendingMoveDirectory?.WaitAsync(cancellationToken) ?? Task.FromResult(MoveDirectoryResult);
		}

		public override Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			Deletes.Advance();
			LastDeleteExpectedStamp = expectedStamp;
			cancellationToken.ThrowIfCancellationRequested();

			if (ThrowOnDelete)
				throw new IOException("The delete failed.");

			return PendingDelete?.WaitAsync(cancellationToken)
				?? Task.FromResult(new WorkspaceFileDeleteResult(DeleteOutcome));
		}

		public override Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
			string path,
			CancellationToken cancellationToken)
		{
			LastDirectoryDeletePath = path;
			DirectoryDeletes.Advance();
			cancellationToken.ThrowIfCancellationRequested();

			if (ThrowOnDirectoryDelete)
				throw new IOException("The directory delete failed.");

			return PendingDirectoryDelete?.WaitAsync(cancellationToken)
				?? Task.FromResult(new WorkspaceFileDeleteResult(DirectoryDeleteOutcome));
		}

		public WorkspaceFileReadResult CreateReadResult()
		{
			return CreateReadResult(new FileStamp(true, Content.Length, DateTime.UnixEpoch, "hash"));
		}
	}
}
