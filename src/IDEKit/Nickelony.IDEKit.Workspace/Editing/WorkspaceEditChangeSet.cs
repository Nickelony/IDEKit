namespace Nickelony.IDEKit.Workspace.Editing;

/// <summary>
/// Represents a non-atomic before-and-after change set from a workspace-edit application.
/// </summary>
/// <remarks>
/// The change set captures confirmed changes after the fact; it does not provide rollback or
/// all-target atomicity. Only the confirmed changes are listed, and persisting or rolling back the
/// recorded changes is the host's responsibility.
/// </remarks>
public sealed class WorkspaceEditChangeSet
{
	/// <summary>
	/// Creates a change set from the confirmed before-and-after document changes.
	/// </summary>
	/// <param name="documentChanges">The confirmed before-and-after document changes to capture.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentChanges"/> is <see langword="null"/>.</exception>
	public WorkspaceEditChangeSet(IReadOnlyList<WorkspaceDocumentChange> documentChanges)
	{
		ArgumentNullException.ThrowIfNull(documentChanges);
		DocumentChanges = Array.AsReadOnly([.. documentChanges]);
	}

	/// <summary>
	/// Gets the per-document changes captured in the change set.
	/// </summary>
	/// <remarks>
	/// The caller-provided collection is copied into owned read-only storage so later caller
	/// mutations cannot leak into the change set.
	/// </remarks>
	public IReadOnlyList<WorkspaceDocumentChange> DocumentChanges { get; }

	/// <summary>
	/// Gets a value indicating whether the change set contains any changes.
	/// </summary>
	public bool HasChanges => DocumentChanges.Count > 0;
}
