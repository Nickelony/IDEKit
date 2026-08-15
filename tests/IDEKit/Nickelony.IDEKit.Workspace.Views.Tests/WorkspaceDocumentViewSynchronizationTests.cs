namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>
/// Verifies the construction guards and convenience members of the view-synchronization result
/// records that the manager composes for its callers.
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentViewSynchronizationTests
{
	[TestMethod]
	public void Issue_NullViewId_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(
			() => new WorkspaceDocumentViewSynchronizationIssue(null!));
	}

	[TestMethod]
	public void Result_NullIssues_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(
			() => new WorkspaceDocumentViewSynchronizationResult(
				WorkspaceDocumentViewSynchronizationOutcome.Synchronized,
				null!));
	}

	[TestMethod]
	public void Synchronized_HasNoIssues()
	{
		WorkspaceDocumentViewSynchronizationResult result = WorkspaceDocumentViewSynchronizationResult.Synchronized;

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.Outcome);
		Assert.AreEqual(0, result.Issues.Count);
	}
}
