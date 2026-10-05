using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Serializes the file operations on a workspace's documents.</summary>
/// <remarks>
/// <para>
/// Document ids are normalized full paths, and identity comparison uses the <see cref="WorkspaceOptions.PathComparison"/> of the options passed
/// to the constructor.
/// </para>
/// <para>
/// Public operations are safe to call concurrently: document mutations and snapshot creation are serialized under the store state lock,
/// and disk operations use per-document disk gates. A reload of a dirty document is registered as an active operation like
/// the others, so disposal waits for it as described on <see cref="DisposeAsync"/>.
/// </para>
/// </remarks>
public sealed class WorkspaceDocumentStore : IWorkspaceDocumentStore
{
	private readonly OperationGate _operationGate;
	private readonly DocumentOpener _opener;
	private readonly DocumentPersister _persister;
	private readonly DocumentRelocator _relocator;
	private readonly DirectoryOperationRunner _directories;
	private readonly LogicalContentReplacer _replacer;
	private readonly DocumentSnapshotReader _reader;
	private readonly StoreTeardown _teardown;

	/// <param name="fileSystem">The file-system implementation used for reads, writes, moves, and deletes.</param>
	/// <param name="options">
	/// The options shared with the file system and the reload coordinator. The default follows the operating system -
	/// case-insensitive on Windows and macOS, ordinal on other platforms. The same value must reach every component of the layer when the file
	/// system's semantics differ from the operating system, for example a case-sensitive volume on macOS; consumers that track the same identities
	/// read <see cref="Options"/>.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
	public WorkspaceDocumentStore(IWorkspaceFileSystem fileSystem, WorkspaceOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);

		object stateLock = new();
		Options = options ?? WorkspaceOptions.Default;
		TestHooks = new WorkspaceDocumentStoreTestHooks(stateLock);

		StoreLifetime lifetime = new();
		StampedFileWorker io = new(fileSystem, lifetime);
		WorkspaceDocumentTracker tracker = new(stateLock, Options);
		StopGate<OperationLease> stopGate = new(stateLock);

		_operationGate = new OperationGate(syncRoot: stateLock, tracker, stopGate, owner: this);
		AdmittedOperationRunner runner = new(_operationGate, lifetime);

		_opener = new DocumentOpener(syncRoot: stateLock, tracker, operationGate: _operationGate, fileSystem, lifetime, TestHooks);
		_persister = new DocumentPersister(syncRoot: stateLock, operationGate: _operationGate, runner, fileSystem, io, lifetime);
		_relocator = new DocumentRelocator(syncRoot: stateLock, tracker, operationGate: _operationGate, runner, fileSystem, io);

		_directories = new DirectoryOperationRunner(
			syncRoot: stateLock,
			tracker,
			operationGate: _operationGate,
			TestHooks,
			Options,
			fileSystem,
			io,
			lifetime
		);

		_replacer = new LogicalContentReplacer(stateLock, tracker, _operationGate);
		_reader = new DocumentSnapshotReader(syncRoot: stateLock, tracker, operationGate: _operationGate, Options);
		_teardown = new StoreTeardown(syncRoot: stateLock, tracker, stopGate, lifetime);
	}

	/// <inheritdoc/>
	public WorkspaceOptions Options { get; }

	// Test accessor: groups the store's test-only seams. The type is internal and reachable only
	// through InternalsVisibleTo; production callers leave every hook null and skip its branches.
	internal WorkspaceDocumentStoreTestHooks TestHooks { get; }

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(options);
		_operationGate.EnsureNotStopping();

		return await _opener.OpenAsync(filePath, options, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();

		return await _persister.CommitAsync(request, forceWrite: false, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();

		return await _persister.ReloadAsync(request, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();

		return await _persister.ResolveExternalConflictAsync(request, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();

		return await _relocator.RenameAsync(request, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();

		return await _relocator.SaveAsAsync(request, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();

		return await _relocator.DeleteAsync(request, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();

		return await _directories.RenameAsync(request, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();

		return await _directories.DeleteAsync(request, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public WorkspaceDocumentMutationResult Replace(WorkspaceDocumentReplaceRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);
		ArgumentNullException.ThrowIfNull(request.Content);

		return _replacer.Replace(request);
	}

	/// <inheritdoc/>
	public WorkspaceDocumentMutationResult Discard(WorkspaceDocumentDiscardRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		_operationGate.EnsureNotStopping();
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);

		return _replacer.Discard(request);
	}

	/// <inheritdoc/>
	public bool TryGetSnapshot(string? filePath, [NotNullWhen(true)] out WorkspaceDocumentSnapshot? snapshot) =>
		_reader.TryGetSnapshot(filePath, out snapshot);

	/// <inheritdoc/>
	public IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string? directoryPath) =>
		_reader.GetSnapshotsUnderDirectory(directoryPath);

	/// <summary>Releases all document resources after tracked open reservations and registered disk operations complete.</summary>
	/// <exception cref="InvalidOperationException">The call runs inside an operation, such as a file-system callback of one.</exception>
	/// <remarks>
	/// Disposal is idempotent: the first call marks the store disposed, cancels the lifetime token that in-flight operations observe,
	/// waits for open reservations and registered operations, and disposes the per-document disk gates; later calls observe the same disposal task.
	/// An operation in flight reports its canceled outcome, and only a member invoked after disposal throws <see cref="ObjectDisposedException"/>.
	/// A call from inside an operation, an open's file-system read included, throws, because disposal waits for that operation, which cannot finish
	/// while it waits for the disposal.
	/// </remarks>
	public ValueTask DisposeAsync() => _teardown.StopAsync();
}
