using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Paths;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Answers the read-only questions about the tracked documents, each as a snapshot taken under the store lock.</summary>
internal sealed class DocumentSnapshotReader
{
	private readonly object _syncRoot;
	private readonly WorkspaceDocumentTracker _tracker;
	private readonly OperationGate _operationGate;
	private readonly LocalPathComparisonPolicy _pathComparison;

	/// <param name="syncRoot">The store's lock, which orders the tracked documents.</param>
	/// <param name="tracker">The tracker of the documents.</param>
	/// <param name="operationGate">The gate that reports whether the store began stopping.</param>
	/// <param name="options">The workspace options whose path comparison decides which documents lie under a directory.</param>
	internal DocumentSnapshotReader(object syncRoot, WorkspaceDocumentTracker tracker, OperationGate operationGate, WorkspaceOptions options)
	{
		_syncRoot = syncRoot;
		_tracker = tracker;
		_operationGate = operationGate;
		_pathComparison = options.PathComparison;
	}

	/// <summary>Looks up the snapshot of the document tracked for a path.</summary>
	/// <param name="filePath">The path, in the caller's spelling.</param>
	/// <param name="snapshot">Receives the snapshot of the tracked document.</param>
	/// <returns><see langword="true"/> when a document is tracked for the path; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal bool TryGetSnapshot(string? filePath, [NotNullWhen(true)] out WorkspaceDocumentSnapshot? snapshot)
	{
		// Disposal is checked before path validity so every member follows the documented
		// ObjectDisposedException contract for a disposed store, regardless of the supplied path.
		_operationGate.EnsureNotStopping();

		if (filePath is null || string.IsNullOrWhiteSpace(filePath))
		{
			snapshot = null;
			return false;
		}

		if (!LocalPath.TryNormalize(filePath, out string documentId))
		{
			snapshot = null;
			return false;
		}

		lock (_syncRoot)
		{
			_operationGate.ThrowIfStopping();

			if (_tracker.TryGetDocument(documentId, out LogicalDocument? document))
			{
				snapshot = CreateSnapshot(document);
				return true;
			}
		}

		snapshot = null;
		return false;
	}

	/// <summary>Collects the snapshots of the documents at a directory path and below it.</summary>
	/// <param name="directoryPath">The directory path, in the caller's spelling.</param>
	/// <returns>The snapshots sorted by document id under the path comparison; empty for a blank or unnormalizable path.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string? directoryPath)
	{
		_operationGate.EnsureNotStopping();

		// Null and blank directory paths follow the same convention as the other path-accepting
		// members: they are treated as invalid input and return no snapshots instead of throwing.
		if (string.IsNullOrWhiteSpace(directoryPath))
			return [];

		if (!LocalPath.TryNormalize(directoryPath, out string normalizedDirectoryPath))
			return [];

		StringComparer comparer = _pathComparison.Comparer;

		// One pass under the lock avoids the LINQ buffers of a filter-order-project chain; the sort
		// runs after the lock is released because it reads no store state. A document at the exact
		// directory id is included - a subtree rename or delete reaches it - matching
		// WorkspaceDocumentTracker.CollectDescendants and the directory reservation.
		List<WorkspaceDocumentSnapshot> snapshots = [];

		lock (_syncRoot)
		{
			_operationGate.ThrowIfStopping();

			foreach (LogicalDocument document in _tracker.Documents)
			{
				if (LocalPath.IsDirectoryOrDescendant(document.DocumentId, normalizedDirectoryPath, _pathComparison))
					snapshots.Add(CreateSnapshot(document));
			}
		}

		snapshots.Sort((left, right) => comparer.Compare(left.DocumentId, right.DocumentId));
		return snapshots;
	}
}
