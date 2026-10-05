using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Replaces the logical content of a tracked document, or discards it in favor of the persisted content.</summary>
/// <remarks>
/// Both members run synchronously under the store lock and take no disk gate: a replace is refused only while a delete is in flight, and a discard
/// is refused while any file operation holds the document's gate.
/// </remarks>
internal sealed class LogicalContentReplacer
{
	private readonly object _syncRoot;
	private readonly WorkspaceDocumentTracker _tracker;
	private readonly OperationGate _operationGate;

	/// <param name="syncRoot">The store's lock, which orders the tracked documents.</param>
	/// <param name="tracker">The tracker of the documents and the delete reservations.</param>
	/// <param name="operationGate">The gate that reports whether the store began stopping.</param>
	internal LogicalContentReplacer(object syncRoot, WorkspaceDocumentTracker tracker, OperationGate operationGate)
	{
		_syncRoot = syncRoot;
		_tracker = tracker;
		_operationGate = operationGate;
	}

	/// <summary>Replaces the logical content and format of the document the request names.</summary>
	/// <param name="request">The replace request.</param>
	/// <returns>The outcome and the snapshot of the document after the replace.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal WorkspaceDocumentMutationResult Replace(WorkspaceDocumentReplaceRequest request)
	{
		// The shared codec validation rejects a byte-order mark that the encoding has none of here so an
		// unencodable format never reaches the tracked document, where every later commit would fail.
		WorkspaceTextCodec.EnsureEncodable(request.FileFormat, "request.FileFormat");

		lock (_syncRoot)
		{
			_operationGate.ThrowIfStopping();

			if (!_tracker.TryGetDocument(request.Identity.DocumentId, out LogicalDocument? document))
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.DocumentNotFound, null);

			if (document.DocumentKey != request.Identity.DocumentKey)
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.StaleDocumentInstance, CreateSnapshot(document));

			if (document.Version != request.Identity.Version)
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.StaleDocument, CreateSnapshot(document));

			if (_tracker.IsDeleteActive(document))
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.OperationInProgress, CreateSnapshot(document));

			if (string.Equals(document.Content, request.Content, StringComparison.Ordinal) && document.FileFormat == request.FileFormat)
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.NoChange, CreateSnapshot(document));

			document.ReplaceContent(request.Content, request.FileFormat);

			return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.Changed, CreateSnapshot(document));
		}
	}

	/// <summary>Restores the persisted content and format of the document the request names.</summary>
	/// <param name="request">The discard request.</param>
	/// <returns>The outcome and the snapshot of the document after the discard.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal WorkspaceDocumentMutationResult Discard(WorkspaceDocumentDiscardRequest request)
	{
		lock (_syncRoot)
		{
			_operationGate.ThrowIfStopping();

			if (!_tracker.TryGetDocument(request.Identity.DocumentId, out LogicalDocument? document))
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.DocumentNotFound, null);

			if (document.DocumentKey != request.Identity.DocumentKey)
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.StaleDocumentInstance, CreateSnapshot(document));

			if (document.Version != request.Identity.Version)
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.StaleDocument, CreateSnapshot(document));

			// Gate transitions happen under the state lock, so the gate's count is an exact probe and
			// the gate does not need to be acquired and released to observe it.
			if (document.DiskOperationGate.CurrentCount == 0)
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.OperationInProgress, CreateSnapshot(document));

			if (!document.IsDirty)
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.NoChange, CreateSnapshot(document));

			document.DiscardChanges();

			return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.Changed, CreateSnapshot(document));
		}
	}
}
