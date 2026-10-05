using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>
/// Verifies the construction leases and convenience members of the view-synchronization result records that the coordinator composes for its callers.
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentViewSynchronizationTests
{
	[TestMethod]
	public void Issue_NullViewId_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceDocumentViewSynchronizationIssue(null!));

	[TestMethod]
	public void Factories_NullIssues_ThrowArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceDocumentViewSynchronizationResult.Blocked(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceDocumentViewSynchronizationResult.Unsynchronized(null!));
	}

	[TestMethod]
	public void Factories_NormalizeTheIssues_ByViewIdKeepingTheEntryWithAFailure()
	{
		var failure = new WorkspaceOperationFailure("code", "message");

		var result = WorkspaceDocumentViewSynchronizationResult.Unsynchronized([
			new WorkspaceDocumentViewSynchronizationIssue("b"),
			new WorkspaceDocumentViewSynchronizationIssue("a"),
			new WorkspaceDocumentViewSynchronizationIssue("b", failure),
		]);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.Outcome);
		Assert.AreSequenceEqual(["a", "b"], result.Issues.Select(issue => issue.ViewId));
		Assert.AreSame(failure, result.Issues[1].Failure);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, WorkspaceDocumentViewSynchronizationResult.Blocked([]).Outcome);
	}

	[TestMethod]
	public void Synchronized_HasNoIssues()
	{
		WorkspaceDocumentViewSynchronizationResult result = WorkspaceDocumentViewSynchronizationResult.Synchronized;

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.Outcome);
		Assert.IsEmpty(result.Issues);
	}
}
