using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Replacement;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Holds the open and replacement choices of a <see cref="WorkspaceEditApplier"/>.</summary>
public sealed record WorkspaceEditApplierOptions
{
	private readonly DocumentReplacementApplierOptions _replacement = DocumentReplacementApplierOptions.Default;

	/// <summary>The options that plan only the tracked documents and stop at the first document that is not replaced.</summary>
	public static WorkspaceEditApplierOptions Default { get; } = new();

	/// <summary>
	/// The options that open a document the store does not track before the plan; <see langword="null"/> (the default) reports the document
	/// as <see cref="WorkspaceEditPlanIssueKind.DocumentNotTracked"/>.
	/// </summary>
	/// <remarks>
	/// With <see cref="WorkspaceDocumentOpenOptions.CreateIfMissing"/> set, an edit that names a missing file opens it as a new empty document;
	/// a failed open is a <see cref="WorkspaceEditPlanIssueKind.DocumentNotTracked"/> issue that carries the open result.
	/// </remarks>
	public WorkspaceDocumentOpenOptions? OpenOptions { get; init; }

	/// <summary>The choices of the replacement, such as whether it continues past a document that is not replaced.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public DocumentReplacementApplierOptions Replacement
	{
		get => _replacement;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_replacement = value;
		}
	}
}
