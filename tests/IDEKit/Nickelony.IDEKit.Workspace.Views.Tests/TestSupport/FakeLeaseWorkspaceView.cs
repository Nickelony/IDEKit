namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>A <see cref="FakeWorkspaceView"/> that also supports the delete lease, with a configurable result for each lease call.</summary>
internal sealed class FakeLeaseWorkspaceView(string viewId = "view") : FakeWorkspaceView(viewId), IWorkspaceDocumentDeleteLeaseView
{
	public WorkspaceDocumentViewDeleteLeaseResult EnterLeaseResult { get; set; } = new(WorkspaceDocumentViewDeleteLeaseOutcome.Entered);

	public WorkspaceDocumentViewDeleteLeaseResult ReleaseLeaseResult { get; set; } = new(WorkspaceDocumentViewDeleteLeaseOutcome.Released);

	public bool ThrowOnEnterLease { get; set; }

	public bool ThrowOnReleaseLease { get; set; }

	public int EnterLeaseCount { get; private set; }

	public int ReleaseLeaseCount { get; private set; }

	public WorkspaceDocumentViewDeleteLeaseResult EnterDeleteLease()
	{
		EnterLeaseCount++;
		return ThrowOnEnterLease ? throw new InvalidOperationException("The lease entry failed.") : EnterLeaseResult;
	}

	public WorkspaceDocumentViewDeleteLeaseResult ReleaseDeleteLease()
	{
		ReleaseLeaseCount++;
		return ThrowOnReleaseLease ? throw new InvalidOperationException("The lease release failed.") : ReleaseLeaseResult;
	}
}
