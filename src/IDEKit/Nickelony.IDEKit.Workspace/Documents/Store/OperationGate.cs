using Nickelony.IDEKit.Core.Threading;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Admits operations on tracked documents under the store's lock, holding each one until it completes.</summary>
/// <remarks>
/// <para>
/// The store passes the lock that orders its other state. <see cref="Begin"/> takes that lock and keeps it from the stop check to the registration,
/// so an operation is admitted only while the store is not stopping, and a stop that begins afterwards waits for every registered operation.
/// A request that fails validation holds nothing: the document's disk gate is taken, and the operation registered, only after every check passed.
/// Every admitted operation holds the gate, because a file operation on the document, a stamp capture included, must not interleave with another.
/// </para>
/// <para>
/// <see cref="Admit"/> requires the caller to hold the lock, and the stop gate throws when it does not. The completion members
/// take the lock themselves, release what the operation held and complete the waiter task last, so a waiter that resumes finds the state
/// after the operation.
/// </para>
/// </remarks>
internal sealed class OperationGate
{
	private readonly object _syncRoot;
	private readonly WorkspaceDocumentTracker _tracker;
	private readonly StopGate<OperationLease> _stopGate;
	private readonly object _owner;

	/// <param name="syncRoot">The store's lock, which also orders the stop gate and the tracker.</param>
	/// <param name="tracker">The tracker of the documents and the reservations.</param>
	/// <param name="stopGate">The store's stop gate, which tracks the admitted operations.</param>
	/// <param name="owner">The store, named by the exception that rejects an operation after a stop began.</param>
	internal OperationGate(object syncRoot, WorkspaceDocumentTracker tracker, StopGate<OperationLease> stopGate, object owner)
	{
		_syncRoot = syncRoot;
		_tracker = tracker;
		_stopGate = stopGate;
		_owner = owner;
	}

	/// <summary>Validates a request against the tracked document, then takes the document's disk gate and registers the operation.</summary>
	/// <param name="identity">The document instance and version the request was made against.</param>
	/// <param name="captureSnapshot">Whether to capture the document's snapshot under the lock for the caller.</param>
	/// <param name="preGateCheck">A check that stops the operation before the gate is taken, such as a rename to the document's own path.</param>
	/// <param name="postGateSetup">Runs under the lock once the gate is held, to publish the operation's reservation.</param>
	/// <returns>The begun operation, or the outcome and the snapshot of the state that failed validation.</returns>
	/// <exception cref="ArgumentException">The identity names no document id.</exception>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	/// <remarks>
	/// A setup that throws releases the gate and completes the document's delete reservation before the exception propagates.
	/// </remarks>
	internal OperationBeginResult Begin(
		WorkspaceDocumentRequestIdentity identity,
		bool captureSnapshot,
		Func<LogicalDocument, OperationPreGateOutcome>? preGateCheck = null,
		Action<LogicalDocument>? postGateSetup = null
	)
	{
		// A null or blank id must not reach the dictionary lookup, which would throw with the
		// misleading parameter name "key"; request ids are the normalized id from a snapshot.
		ArgumentException.ThrowIfNullOrWhiteSpace(identity.DocumentId);

		lock (_syncRoot)
		{
			_stopGate.ThrowIfStopping(_owner);

			if (!_tracker.TryGetDocument(identity.DocumentId, out LogicalDocument? document))
				return OperationBeginResult.NotFound();

			if (document.DocumentKey != identity.DocumentKey)
				return OperationBeginResult.Rejected(OperationBeginOutcome.StaleDocumentInstance, document, CreateSnapshot(document));

			if (document.Version != identity.Version)
				return OperationBeginResult.Rejected(OperationBeginOutcome.StaleDocument, document, CreateSnapshot(document));

			if (preGateCheck is not null)
			{
				OperationPreGateOutcome preGateOutcome = preGateCheck(document);

				if (preGateOutcome != OperationPreGateOutcome.Proceed)
					return OperationBeginResult.StoppedBeforeGate(preGateOutcome, document, CreateSnapshot(document));
			}

			WorkspaceDocumentSnapshot? capturedSnapshot = captureSnapshot ? CreateSnapshot(document) : null;

			// The gate admits one operation per document. A delete in flight holds it from its reservation to its completion, so the gate alone
			// refuses every operation that arrives meanwhile.
			if (!document.DiskOperationGate.Wait(0, CancellationToken.None))
				return OperationBeginResult.Rejected(OperationBeginOutcome.OperationInProgress, document, CreateSnapshot(document));

			try
			{
				postGateSetup?.Invoke(document);
			}
			catch
			{
				// A throwing setup must not leave the acquired gate held, and the delete setup must not
				// leave its reservation behind; the original failure still propagates.
				_tracker.CompleteDelete(document);
				document.DiskOperationGate.Release();
				throw;
			}

			OperationLease operation = new(document.DiskOperationGate);
			_stopGate.Admit(operation, _owner);

			return OperationBeginResult.Gated(document, capturedSnapshot, operation);
		}
	}

	/// <summary>Reports whether the store began stopping; the caller holds the store's lock.</summary>
	internal bool IsStopping => _stopGate.IsStopping;

	/// <summary>Throws when the store began stopping; the caller holds the store's lock.</summary>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal void ThrowIfStopping() => _stopGate.ThrowIfStopping(_owner);

	/// <summary>Throws when the store began stopping; takes the store's lock for the check.</summary>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal void EnsureNotStopping()
	{
		lock (_syncRoot)
			ThrowIfStopping();
	}

	/// <summary>Registers an operation that holds the given disk gates, which its completion releases.</summary>
	/// <param name="gates">The gates the caller acquired for the operation; empty for an operation that holds none.</param>
	/// <returns>The registration the operation completes.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	/// <remarks>
	/// The caller holds the store's lock. A caller that already acquired a resource for the operation checks for a stop earlier
	/// in the same critical section, because a throw here would leave the resource held.
	/// </remarks>
	internal OperationLease Admit(IReadOnlyList<SemaphoreSlim> gates)
	{
		OperationLease operation = new(gates);

		_stopGate.Admit(operation, _owner);

		return operation;
	}

	/// <summary>Releases an admitted operation: its disk gates, its place in the stop gate and its waiter task.</summary>
	/// <param name="operation">The registration the operation received.</param>
	internal void Complete(OperationLease operation)
	{
		lock (_syncRoot)
		{
			_stopGate.Complete(operation);
			operation.Complete();
		}
	}

	/// <summary>Releases a destination reservation and the operations that wait on it.</summary>
	/// <param name="reservation">The reservation the move or save-as held.</param>
	internal void CompleteDestinationReservation(DestinationReservation reservation)
	{
		lock (_syncRoot)
			_tracker.CompleteDestinationReservation(reservation);
	}

	/// <summary>Releases a directory operation's reservation and the opens that wait on it.</summary>
	/// <param name="reservation">The reservation the directory operation held.</param>
	internal void CompleteDirectoryOperation(DirectoryOperationReservation reservation)
	{
		lock (_syncRoot)
			_tracker.CompleteDirectoryOperation(reservation);
	}

	/// <summary>Releases the delete reservation of a document and the opens that wait on it.</summary>
	/// <param name="document">The document the delete reserved.</param>
	internal void CompleteDelete(LogicalDocument document)
	{
		lock (_syncRoot)
			_tracker.CompleteDelete(document);
	}
}
