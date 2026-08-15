using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	private static void ReleaseGates(IEnumerable<SemaphoreSlim> gates)
	{
		foreach (SemaphoreSlim gate in gates.Reverse())
			gate.Release();
	}

	private static WorkspaceDocumentRenameOutcome MapRenamePreGateOutcome(OperationPreGateOutcome outcome)
		=> outcome switch
		{
			OperationPreGateOutcome.NoChange => WorkspaceDocumentRenameOutcome.NoChange,
			OperationPreGateOutcome.DestinationInUse => WorkspaceDocumentRenameOutcome.DestinationInUse,
			_ => WorkspaceDocumentRenameOutcome.DestinationBusy,
		};

	private static WorkspaceDocumentSaveAsOutcome MapSaveAsPreGateOutcome(OperationPreGateOutcome outcome)
		=> outcome switch
		{
			OperationPreGateOutcome.DestinationInUse => WorkspaceDocumentSaveAsOutcome.DestinationInUse,
			_ => WorkspaceDocumentSaveAsOutcome.DestinationBusy,
		};

	// Maps a failed OperationBegin to the caller's result type. The four values are the operation's
	// failure vocabulary; the operation-in-progress value is the fallback for any outcome the caller
	// did not name (currently only OperationInProgress). The factory receives the mapped outcome and
	// the failure snapshot (null for a missing document).
	private static TResult MapOperationBeginFailure<TOutcome, TResult>(
		OperationBegin begin,
		TOutcome documentNotFound,
		TOutcome staleDocumentInstance,
		TOutcome staleDocument,
		TOutcome operationInProgress,
		Func<TOutcome, WorkspaceDocumentSnapshot?, TResult> factory)
		=> begin.Outcome switch
		{
			OperationBeginOutcome.DocumentNotFound => factory(documentNotFound, null),
			OperationBeginOutcome.StaleDocumentInstance => factory(staleDocumentInstance, begin.FailureSnapshot),
			OperationBeginOutcome.StaleDocument => factory(staleDocument, begin.FailureSnapshot),
			_ => factory(operationInProgress, begin.FailureSnapshot),
		};

	/// <summary>
	/// The validation outcome of a <c>TryBeginOperation</c> attempt.
	/// </summary>
	private enum OperationBeginOutcome
	{
		DocumentNotFound,
		StaleDocumentInstance,
		StaleDocument,
		OperationInProgress,
	}

	/// <summary>
	/// The outcome of an operation's pre-gate check. A non-<see cref="Proceed"/> value stops the
	/// operation before the disk gate is taken and is carried on
	/// <see cref="OperationBegin.PreGateOutcome"/> so the caller can map it to its own outcome
	/// without out-of-band state.
	/// </summary>
	private enum OperationPreGateOutcome
	{
		Proceed,
		NoChange,
		DestinationInUse,
		DestinationBusy,
	}

	// Outcome of a TryBeginOperation attempt. On success the document and, when requested, the
	// captured snapshot are populated; a gated operation also has an operation registration. On
	// failure the outcome and, when available, a snapshot of the state that failed validation
	// are populated; the failure snapshot is captured under _stateLock so it matches the state that
	// was validated.
	private readonly struct OperationBegin
	{
		public LogicalDocument? Document { get; init; }
		public WorkspaceDocumentSnapshot? CapturedSnapshot { get; init; }
		public WorkspaceDocumentSnapshot? FailureSnapshot { get; init; }
		public OperationRegistration? Operation { get; init; }
		public OperationBeginOutcome? Outcome { get; init; }
		public OperationPreGateOutcome? PreGateOutcome { get; init; }

		public bool Succeeded => Outcome is null && PreGateOutcome is null;
	}

	// Performs the shared operation preamble under _stateLock: validates the
	// disposed state and the request's document presence, key, and version
	// expectations; runs the optional pre-gate check; rejects a document whose
	// delete is in flight; captures a snapshot; acquires the document's disk gate;
	// runs the optional post-gate setup; and registers the operation. When
	// skipGateWhen returns true (used by reload for dirty documents) the operation
	// proceeds without holding the gate or registering an operation. Returns an
	// OperationBegin describing either the begun operation or the failure.
	private OperationBegin TryBeginOperation(
		WorkspaceDocumentRequestIdentity identity,
		bool captureSnapshot,
		Func<LogicalDocument, OperationPreGateOutcome>? preGateCheck = null,
		Func<LogicalDocument, WorkspaceDocumentSnapshot?, bool>? skipGateWhen = null,
		Action<LogicalDocument>? postGateSetup = null)
	{
		// A null or blank id must not reach the dictionary lookup, which would throw with the
		// misleading parameter name "key"; request ids are the normalized id from a snapshot.
		ArgumentException.ThrowIfNullOrWhiteSpace(identity.DocumentId);

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (!_documents.TryGetValue(identity.DocumentId, out LogicalDocument? document))
				return new OperationBegin { Outcome = OperationBeginOutcome.DocumentNotFound };

			if (document.DocumentKey != identity.DocumentKey)
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Outcome = OperationBeginOutcome.StaleDocumentInstance
				};

			if (document.Version != identity.Version)
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Outcome = OperationBeginOutcome.StaleDocument
				};

			if (preGateCheck is not null)
			{
				OperationPreGateOutcome preGateOutcome = preGateCheck(document);
				if (preGateOutcome != OperationPreGateOutcome.Proceed)
				{
					return new OperationBegin
					{
						Document = document,
						FailureSnapshot = CreateSnapshot(document),
						PreGateOutcome = preGateOutcome,
					};
				}
			}

			// A delete in flight marks the document as rejecting replacements and holds its disk gate, so
			// every gated operation would fail on that gate. The dirty reload skips the gate, and the
			// delete state is checked here as well so that path cannot report an outcome that describes a
			// document the delete is about to remove.
			if (document.DeleteOperationActive)
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Outcome = OperationBeginOutcome.OperationInProgress
				};

			WorkspaceDocumentSnapshot? capturedSnapshot = captureSnapshot ? CreateSnapshot(document) : null;
			if (skipGateWhen is not null && skipGateWhen(document, capturedSnapshot))
				return new OperationBegin
				{
					Document = document,
					CapturedSnapshot = capturedSnapshot
				};

			if (!document.DiskOperationGate.Wait(0, CancellationToken.None))
				return new OperationBegin
				{
					Document = document,
					FailureSnapshot = CreateSnapshot(document),
					Outcome = OperationBeginOutcome.OperationInProgress
				};

			try
			{
				postGateSetup?.Invoke(document);
			}
			catch
			{
				// A throwing setup must not leave the acquired gate held, and the delete setup must not
				// leave the document rejecting replacements; the original failure still propagates.
				if (document.DeleteOperationActive)
				{
					document.DeleteOperationActive = false;
					document.DeleteCompletion?.TrySetResult();
					document.DeleteCompletion = null;
				}

				document.DiskOperationGate.Release();
				throw;
			}

			OperationRegistration operation = new(document.DiskOperationGate);
			_activeOperations.Add(operation);

			return new OperationBegin
			{
				Document = document,
				CapturedSnapshot = capturedSnapshot,
				Operation = operation
			};
		}
	}

	private void CompleteOperation(OperationRegistration operation)
	{
		lock (_stateLock)
		{
			_activeOperations.Remove(operation);
			operation.Complete();
		}
	}
}
