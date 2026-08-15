using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Describes how attached views synchronized around a manager operation.
/// </summary>
public enum WorkspaceDocumentViewSynchronizationOutcome
{
	/// <summary>No attached view needed synchronization, or every view operation succeeded.</summary>
	Synchronized,

	/// <summary>One or more attached views prevented the operation before the document store was reached.</summary>
	Blocked,

	/// <summary>The document operation completed, but one or more attached views remained unsynchronized afterward.</summary>
	Unsynchronized
}

/// <summary>
/// Identifies a view that blocked a manager operation or remained unsynchronized after it, with the
/// failure the view reported when its synchronization failed.
/// </summary>
/// <remarks>
/// <see cref="Failure"/> is populated when one was reported: a refresh, acknowledgment, identity
/// update, delete-guard, or state-probe failure returned or thrown by the view, including a previously
/// recorded synchronization failure. It is <see langword="null"/> when the view blocked the operation
/// through its state - pending edits or a conflict - which the host can inspect on the view itself. It
/// is also <see langword="null"/> when a failed member call returned a failure outcome without detail.
/// </remarks>
/// <param name="ViewId">
/// The stable identifier of the view, or the marker <c>(unidentified view ...)</c> when a registered
/// view cannot report its id. The marker carries a per-instance discriminator so two such views stay
/// distinct in a normalized issue list.
/// </param>
/// <param name="Failure">Explains the failed synchronization, when the view reported one.</param>
public sealed record WorkspaceDocumentViewSynchronizationIssue(
	string ViewId,
	WorkspaceOperationFailure? Failure = null)
{
	/// <summary>
	/// Gets the stable identifier of the view, or the marker for a registered view that cannot report
	/// its id. The value is never <see langword="null"/>.
	/// </summary>
	public string ViewId { get; init; } = ViewId ?? throw new ArgumentNullException(nameof(ViewId));
}

/// <summary>
/// Contains the view-synchronization outcome of a manager operation.
/// </summary>
/// <remarks>
/// <para>
/// A manager operation reports the document-authority outcome and the view-synchronization outcome
/// separately: the store result describes what happened to the document, while this value describes
/// whether attached views blocked the operation or stayed unsynchronized after it.
/// </para>
/// <para>
/// <see cref="Issues"/> is de-duplicated by view id with an ordinal comparison and sorted ordinally,
/// so the reported order does not depend on registration or enumeration order. When the same view is
/// reported more than once, the entry that carries a
/// <see cref="WorkspaceDocumentViewSynchronizationIssue.Failure"/> is kept.
/// </para>
/// </remarks>
/// <param name="Outcome">The synchronization outcome.</param>
/// <param name="Issues">
/// The views that blocked the operation or remained unsynchronized; empty when the outcome is
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Synchronized"/>.
/// </param>
public sealed record WorkspaceDocumentViewSynchronizationResult(
	WorkspaceDocumentViewSynchronizationOutcome Outcome,
	IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> Issues)
{
	/// <summary>
	/// Gets the views that blocked the operation or remained unsynchronized; empty when the outcome is
	/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Synchronized"/>. The value is never
	/// <see langword="null"/>.
	/// </summary>
	public IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> Issues { get; init; } =
		Issues ?? throw new ArgumentNullException(nameof(Issues));

	/// <summary>
	/// Gets a synchronization outcome in which every attached view synchronized successfully, including
	/// when no view was attached.
	/// </summary>
	public static WorkspaceDocumentViewSynchronizationResult Synchronized { get; } =
		new(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, []);
}
