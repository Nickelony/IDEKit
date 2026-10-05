namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Describes how attached views synchronized around a coordinator operation.</summary>
public enum WorkspaceDocumentViewSynchronizationOutcome
{
	/// <summary>No attached view needed synchronization, or every view operation succeeded.</summary>
	Synchronized,

	/// <summary>One or more attached views prevented the operation before the document store was reached.</summary>
	Blocked,

	/// <summary>The document operation completed, but one or more attached views remained unsynchronized afterward.</summary>
	Unsynchronized,
}

/// <summary>
/// Identifies a view that blocked a coordinator operation or remained unsynchronized after it, with the failure the view reported
/// when its synchronization failed.
/// </summary>
/// <param name="ViewId">
/// The stable identifier of the view, or the marker <c>(unidentified view ...)</c> when a registered view cannot report its id. The marker carries
/// a per-instance discriminator so two such views stay distinct in a normalized issue list.
/// </param>
/// <param name="Failure">Explains the failed synchronization, when the view reported one.</param>
/// <remarks>
/// <see cref="Failure"/> is populated when one was reported: a refresh, acknowledgment, identity update, delete-lease, close, or state-probe failure
/// returned or thrown by the view, including a synchronization failure recorded earlier. It is <see langword="null"/> when the view blocked
/// the operation through its state - pending edits or a conflict - which the host can inspect on the view itself. It is also <see langword="null"/>
/// when no detail exists, such as a failed member call that returned a failure outcome without one.
/// </remarks>
public sealed record WorkspaceDocumentViewSynchronizationIssue(string ViewId, WorkspaceOperationFailure? Failure = null)
{
	/// <summary>
	/// The stable identifier of the view, or the marker for a registered view that cannot report its id. The value is never <see langword="null"/>.
	/// </summary>
	public string ViewId { get; init; } = ViewId ?? throw new ArgumentNullException(nameof(ViewId));
}

/// <summary>Contains the view-synchronization outcome of a coordinator operation.</summary>
/// <remarks>
/// <para>
/// A coordinator operation reports the document-authority outcome and the view-synchronization outcome separately: the store result describes
/// what happened to the document, while this value describes whether attached views blocked the operation or stayed unsynchronized after it.
/// </para>
/// <para>
/// <see cref="Issues"/> is de-duplicated by view id with an ordinal comparison and sorted ordinally,
/// so the reported order does not depend on registration or enumeration order. When the same view is reported more than once, the entry that carries
/// a <see cref="WorkspaceDocumentViewSynchronizationIssue.Failure"/> is kept. Only the factories create a value, so every instance honors that.
/// </para>
/// </remarks>
public sealed record WorkspaceDocumentViewSynchronizationResult
{
	private WorkspaceDocumentViewSynchronizationResult(
		WorkspaceDocumentViewSynchronizationOutcome outcome,
		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues
	)
	{
		Outcome = outcome;
		Issues = issues;
	}

	/// <summary>The synchronization outcome.</summary>
	public WorkspaceDocumentViewSynchronizationOutcome Outcome { get; }

	/// <summary>
	/// The views that blocked the operation or remained unsynchronized;
	/// empty when the outcome is <see cref="WorkspaceDocumentViewSynchronizationOutcome.Synchronized"/>. The value is never <see langword="null"/>.
	/// </summary>
	public IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> Issues { get; }

	/// <summary>A synchronization outcome in which every attached view synchronized successfully, including when no view was attached.</summary>
	public static WorkspaceDocumentViewSynchronizationResult Synchronized { get; } =
		new(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, []);

	/// <summary>Creates the outcome of an operation that attached views blocked before the document store was reached.</summary>
	/// <param name="issues">The views that blocked the operation; a view listed more than once keeps the entry that carries a failure.</param>
	/// <returns>The blocked outcome with the normalized issues.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="issues"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentViewSynchronizationResult Blocked(IEnumerable<WorkspaceDocumentViewSynchronizationIssue> issues)
	{
		ArgumentNullException.ThrowIfNull(issues);
		return new(WorkspaceDocumentViewSynchronizationOutcome.Blocked, Normalize(issues));
	}

	/// <summary>Creates the outcome of a completed operation after which attached views remained unsynchronized.</summary>
	/// <param name="issues">The views that remained unsynchronized; a view listed more than once keeps the entry that carries a failure.</param>
	/// <returns>The unsynchronized outcome with the normalized issues.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="issues"/> is <see langword="null"/>.</exception>
	public static WorkspaceDocumentViewSynchronizationResult Unsynchronized(IEnumerable<WorkspaceDocumentViewSynchronizationIssue> issues)
	{
		ArgumentNullException.ThrowIfNull(issues);
		return new(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, Normalize(issues));
	}

	private static WorkspaceDocumentViewSynchronizationIssue[] Normalize(IEnumerable<WorkspaceDocumentViewSynchronizationIssue> issues)
	{
		return
		[
			.. issues
				.GroupBy(issue => issue.ViewId, StringComparer.Ordinal)
				.OrderBy(group => group.Key, StringComparer.Ordinal)
				.Select(group => group.FirstOrDefault(issue => issue.Failure is not null) ?? group.First()),
		];
	}
}
