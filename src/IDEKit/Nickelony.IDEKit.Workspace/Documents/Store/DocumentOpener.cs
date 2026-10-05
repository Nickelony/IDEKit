using System.Diagnostics;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Loads a file as a tracked document, joining an open of the same path that is already in flight.</summary>
/// <remarks>
/// An open holds a reservation for its path from the first decision to the tracked document, so a concurrent open of the path
/// waits for the owner's result instead of loading the file twice. The load is an admitted operation that holds no disk gate, so a disposal requested
/// from inside its file-system read throws instead of waiting for the read.
/// </remarks>
internal sealed class DocumentOpener
{
	private readonly object _syncRoot;
	private readonly WorkspaceDocumentTracker _tracker;
	private readonly OperationGate _operationGate;
	private readonly IWorkspaceFileSystem _fileSystem;
	private readonly StoreLifetime _lifetime;
	private readonly WorkspaceDocumentStoreTestHooks _testHooks;

	/// <param name="syncRoot">The store's lock, which orders the tracked documents.</param>
	/// <param name="tracker">The tracker of the documents and the reservations.</param>
	/// <param name="operationGate">The gate that admits the load and reports whether the store began stopping.</param>
	/// <param name="fileSystem">The file system the load reads from.</param>
	/// <param name="lifetime">The store lifetime, which a load observes besides the caller's token.</param>
	/// <param name="testHooks">The hooks a test uses to hold an open at its interleaving points.</param>
	internal DocumentOpener(
		object syncRoot,
		WorkspaceDocumentTracker tracker,
		OperationGate operationGate,
		IWorkspaceFileSystem fileSystem,
		StoreLifetime lifetime,
		WorkspaceDocumentStoreTestHooks testHooks
	)
	{
		_syncRoot = syncRoot;
		_tracker = tracker;
		_operationGate = operationGate;
		_fileSystem = fileSystem;
		_lifetime = lifetime;
		_testHooks = testHooks;
	}

	/// <summary>Opens a file as a tracked document, or reports the document that is already open.</summary>
	/// <param name="filePath">The path of the file, in the caller's spelling.</param>
	/// <param name="options">The format a new or encodable file is read with.</param>
	/// <param name="cancellationToken">A token that can cancel the open.</param>
	/// <returns>The outcome and the snapshot of the opened document.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping before the open started.</exception>
	internal async Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken
	)
	{
		if (filePath is null || string.IsNullOrWhiteSpace(filePath))
			return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.InvalidPath, null);

		if (!LocalPath.TryNormalize(filePath, out string documentId))
			return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.InvalidPath, null);

		// An iteration after the first has already claimed or joined a reservation, so a call that
		// observes disposal then is in flight: it reports Canceled like every other operation that
		// disposal interrupts. The first pass has started nothing yet and keeps the
		// ObjectDisposedException contract the entry check enforces.
		bool isInFlight = false;

		while (true)
		{
			OpenLoopDirective directive;

			lock (_syncRoot)
				directive = DecideOpenStep(documentId, isInFlight);

			if (directive.Result is not null)
				return directive.Result;

			if (directive.PendingWait is { } pendingWait)
			{
				if (!await TryWaitForResolutionAsync(pendingWait, cancellationToken).ConfigureAwait(false))
					return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null);

				isInFlight = true;
				continue;
			}

			// Every path that reaches this point holds a reservation: either one that was already
			// registered for the path or the one created above for this call.
			OpenReservation reservation = directive.Reservation ?? throw new UnreachableException("An open reservation was expected for this path.");

			if (!directive.IsLoader)
			{
				// Another open owns the load for this path: wait for its result and re-evaluate the path in
				// this loop. The wait is iterative rather than recursive, so many competing callers cannot
				// build a call stack; a loader that failed or was canceled makes this call a fresh attempt.
				WorkspaceDocumentOpenResult ownerResult;

				try
				{
					ownerResult = await reservation.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null);
				}

				if (ownerResult.Outcome == WorkspaceDocumentOpenOutcome.Opened)
				{
					_testHooks.OpenWaiterBeforeRecheck?.Invoke();

					// The owner's snapshot is stale by now, so the document is re-checked under the state
					// lock: the caller gets AlreadyOpen only while the same instance is still tracked with
					// no delete in flight; otherwise, the loop re-evaluates the path like any waiter.
					lock (_syncRoot)
					{
						if (_tracker.TryGetDocument(documentId, out LogicalDocument? document) && !_tracker.IsDeleteActive(document))
							return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.AlreadyOpen, CreateSnapshot(document));
					}
				}

				isInFlight = true;
				continue;
			}

			OperationLease operation = directive.Operation ?? throw new UnreachableException("A loading open must be admitted as an operation.");

			try
			{
				return await LoadReservedDocumentAsync(documentId, filePath, options, reservation, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				_operationGate.Complete(operation);
			}
		}
	}

	// Waits for a pending store operation - a delete, a destination reservation, a directory
	// operation, or an earlier open of the same path - to resolve. Returns false when the caller's
	// token canceled the wait; the caller reports Canceled without inspecting the other outcome.
	private static async Task<bool> TryWaitForResolutionAsync(Task pendingOperation, CancellationToken cancellationToken)
	{
		try
		{
			await pendingOperation.WaitAsync(cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	// Requires the store lock. Decides what a pass of the open loop does for a path: return a completed
	// result, wait for a pending operation, or hold the reservation that makes this call the loader or
	// joins it to the owner of an existing load. The pass flags disposal once it is in flight, because
	// a later pass has already claimed or joined a reservation.
	private OpenLoopDirective DecideOpenStep(string documentId, bool isInFlight)
	{
		if (isInFlight && _operationGate.IsStopping)
			return new OpenLoopDirective { Result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null) };

		_operationGate.ThrowIfStopping();

		if (_tracker.TryGetDocument(documentId, out LogicalDocument? document))
		{
			// A delete holds the document gate, so operations that need that gate report
			// OperationInProgress while it is in flight. Reporting the document as already open would
			// hand the caller an instance that the in-flight delete is about to remove, so wait for the
			// delete to resolve and re-evaluate the path instead.
			if (_tracker.TryGetDeleteReservation(document, out DeleteReservation? deletion))
				return new OpenLoopDirective { PendingWait = deletion.Completion.Task };

			return new OpenLoopDirective
			{
				Result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.AlreadyOpen, CreateSnapshot(document)),
			};
		}

		// The path is reserved as the destination of an in-flight move or save-as: wait for that
		// operation to resolve and re-evaluate the path.
		if (_tracker.TryGetDestinationReservation(documentId, out DestinationReservation? destinationReservation))
			return new OpenLoopDirective { PendingWait = destinationReservation.Completion.Task };

		// Another open owns the load for this path: wait for its result instead of loading
		// the same file twice.
		if (_tracker.TryGetOpenReservation(documentId, out OpenReservation? existingReservation))
			return new OpenLoopDirective { Reservation = existingReservation };

		// The path is inside a directory whose rename or recursive delete is in flight: wait for it
		// to resolve, then re-evaluate. A load that resumes reads the post-operation state - a vacated
		// source path after a rename is created as a new empty document when the file is gone.
		if (_tracker.FindDirectoryOperationUnder(documentId) is { } directoryOperation)
			return new OpenLoopDirective { PendingWait = directoryOperation.Completion.Task };

		OpenReservation reservation = _tracker.ReserveOpen(documentId);

		// The load is an admitted operation that holds no disk gate, so a disposal requested from inside its
		// file-system read throws instead of waiting for the read.
		OperationLease operation = _operationGate.Admit([]);

		return new OpenLoopDirective
		{
			Reservation = reservation,
			Operation = operation,
			IsLoader = true,
		};
	}

	// The directive a pass of the open loop reaches under the store lock: a completed result to return, a
	// pending signal to wait on, and the reservation this call holds, whether it owns the load, and the
	// operation that admits the load.
	private readonly struct OpenLoopDirective
	{
		internal WorkspaceDocumentOpenResult? Result { get; init; }
		internal Task? PendingWait { get; init; }
		internal OpenReservation? Reservation { get; init; }
		internal OperationLease? Operation { get; init; }
		internal bool IsLoader { get; init; }
	}

	private async Task<WorkspaceDocumentOpenResult> LoadReservedDocumentAsync(
		string documentId,
		string filePath,
		WorkspaceDocumentOpenOptions options,
		OpenReservation reservation,
		CancellationToken cancellationToken
	)
	{
		WorkspaceDocumentOpenResult result;

		using (CancellationTokenSource linkedCancellation = _lifetime.CreateLinkedCancellation(cancellationToken))
		{
			try
			{
				linkedCancellation.Token.ThrowIfCancellationRequested();

				WorkspaceFileReadResult file = await _fileSystem.ReadAsync(documentId, linkedCancellation.Token).ConfigureAwait(false);

				linkedCancellation.Token.ThrowIfCancellationRequested();

				if (file.IsDirectory)
				{
					// A directory is not a document: tracking it would produce a document whose writes and
					// deletes can never succeed. The path is rejected with a dedicated outcome instead of
					// being tracked as a new empty document for a missing file.
					result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.IsDirectory, null);
					CompleteOpenReservation(documentId, reservation, result);
					return result;
				}

				if (!file.OnDiskStamp.Exists && !options.CreateIfMissing)
				{
					// The caller requires an existing file; opening the path as new content would
					// silently turn a missing file into a tracked document.
					result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.NotFound, null);
					CompleteOpenReservation(documentId, reservation, result);
					return result;
				}

				string content = file.ResolveContent(options.NewFileFormat, options.NoBomEncodings, out TextFileFormat fileFormat);

				lock (_syncRoot)
				{
					if (_operationGate.IsStopping)
					{
						result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null);
					}
					else
					{
						LogicalDocument document = new(
							syncRoot: _syncRoot,
							documentKey: new WorkspaceDocumentKey(Guid.NewGuid()),
							documentId,
							displayPath: filePath,
							content,
							fileFormat,
							onDiskStamp: file.OnDiskStamp,
							noBomEncodings: options.NoBomEncodings
						);

						// The snapshot is created before the document becomes tracked: an exception while
						// snapshotting must not leave a tracked document behind a reported load failure.
						WorkspaceDocumentSnapshot snapshot = CreateSnapshot(document);

						_tracker.AddDocument(documentId, document);
						result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Opened, snapshot);
					}

					_tracker.CompleteOpenReservation(documentId, reservation, result);
				}
			}
			catch (OperationCanceledException)
			{
				result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null);
				CompleteOpenReservation(documentId, reservation, result);
			}
			catch (Exception exception)
			{
				// The failure code follows the classification the reload reads use, so a permission or
				// encoding problem reads the same from an open and from a reload.
				result = new WorkspaceDocumentOpenResult(
					WorkspaceDocumentOpenOutcome.LoadFailed,
					null,

					new WorkspaceOperationFailure(
						WorkspaceOperationFailureCodes.ClassifyReadFailure(exception, WorkspaceOperationFailureCodes.LoadFailed),
						exception.Message,
						exception
					)
				);

				CompleteOpenReservation(documentId, reservation, result);
			}
		}

		return result;
	}

	private void CompleteOpenReservation(string documentId, OpenReservation reservation, WorkspaceDocumentOpenResult result)
	{
		lock (_syncRoot)
			_tracker.CompleteOpenReservation(documentId, reservation, result);
	}
}
