using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Provides one change of a document that has an attached view.</summary>
public sealed class AttachedDocumentChangedEventArgs : EventArgs
{
	/// <param name="kind">The kind of change.</param>
	/// <param name="snapshot">The snapshot of the document after the change.</param>
	/// <param name="oldDocumentId">The id the document had before a rename, or <see langword="null"/> for any other change.</param>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="oldDocumentId"/> is <see langword="null"/> for a rename, or set for another change.
	/// </exception>
	public AttachedDocumentChangedEventArgs(AttachedDocumentChangeKind kind, WorkspaceDocumentSnapshot snapshot, string? oldDocumentId)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		if (kind == AttachedDocumentChangeKind.Renamed != (oldDocumentId is not null))
			throw new ArgumentException("A rename names the old document id, and no other change does.", nameof(oldDocumentId));

		Kind = kind;
		Snapshot = snapshot;
		OldDocumentId = oldDocumentId;
	}

	public AttachedDocumentChangeKind Kind { get; }

	/// <summary>The snapshot of the document after the change; a close carries the last snapshot the coordinator saw.</summary>
	public WorkspaceDocumentSnapshot Snapshot { get; }

	/// <summary>The id the document had before a rename, or <see langword="null"/> for any other change.</summary>
	public string? OldDocumentId { get; }
}
