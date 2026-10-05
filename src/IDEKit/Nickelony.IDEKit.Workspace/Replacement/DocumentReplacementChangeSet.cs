namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>Represents a non-atomic before-and-after change set from a workspace-edit application.</summary>
/// <remarks>
/// The change set captures confirmed changes after the fact and provides no rollback or all-target atomicity; persisting or rolling back the recorded
/// changes is the host's responsibility.
/// </remarks>
public sealed class DocumentReplacementChangeSet
{
	/// <exception cref="ArgumentNullException"><paramref name="documentChanges"/> is <see langword="null"/>.</exception>
	public DocumentReplacementChangeSet(IReadOnlyList<DocumentReplacementChange> documentChanges)
	{
		ArgumentNullException.ThrowIfNull(documentChanges);
		DocumentChanges = Array.AsReadOnly([.. documentChanges]);
	}

	/// <summary>The per-document changes captured in the change set.</summary>
	/// <remarks>
	/// The caller-provided collection is copied into owned read-only storage, so later caller mutations cannot leak into the change set.
	/// </remarks>
	public IReadOnlyList<DocumentReplacementChange> DocumentChanges { get; }

	/// <summary>Whether the change set contains any changes.</summary>
	public bool HasChanges => DocumentChanges.Count > 0;
}
