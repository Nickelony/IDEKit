using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>A configurable view double for the tests of the coordinator's collaborators.</summary>
/// <remarks>
/// It exposes its subscriber count, its call counts and the arguments of its last calls. A result property scripts what each member returns,
/// a flag makes a member throw, and <see cref="OnMember"/> reports every property read and method call, so a test exercises the isolation
/// of a throwing view and checks where the coordinator touches it. The event accessors are not reported.
/// </remarks>
internal class FakeWorkspaceView(string viewId = "view") : IWorkspaceDocumentView
{
	private EventHandler<ApplyRequestedEventArgs>? _applyRequested;
	private string _viewId = viewId;
	private WorkspaceDocumentKey? _documentKey;

	private bool _hasPendingEdits;
	private bool _hasConflict;

	public event EventHandler<ApplyRequestedEventArgs>? ApplyRequested
	{
		add
		{
			OnAddHandler?.Invoke();

			if (ThrowOnAddHandler)
				throw new InvalidOperationException("The event accessor failed.");

			_applyRequested += value;
		}
		remove
		{
			if (ThrowOnRemoveHandler)
				throw new InvalidOperationException("The event accessor failed.");

			_applyRequested -= value;
		}
	}

	/// <summary>A callback that receives the name of each member the view serves, before the member runs.</summary>
	public Action<string>? OnMember { get; set; }

	/// <summary>An action that runs inside the event subscription, before the handler is added, so a test can interleave a detach.</summary>
	public Action? OnAddHandler { get; set; }

	public string ViewId
	{
		get
		{
			Visit(nameof(ViewId));
			return ThrowOnViewId ? throw new InvalidOperationException("The view id failed.") : _viewId;
		}
		set => _viewId = value;
	}

	public WorkspaceDocumentKey? DocumentKey
	{
		get
		{
			Visit(nameof(DocumentKey));
			return ThrowOnDocumentKey ? throw new InvalidOperationException("The document key failed.") : _documentKey;
		}
		set => _documentKey = value;
	}

	public bool HasPendingEdits
	{
		get
		{
			Visit(nameof(HasPendingEdits));
			return ThrowOnStateProbe ? throw new InvalidOperationException("The state probe failed.") : _hasPendingEdits;
		}
		set => _hasPendingEdits = value;
	}

	public bool HasConflict
	{
		get
		{
			Visit(nameof(HasConflict));
			return ThrowOnStateProbe ? throw new InvalidOperationException("The state probe failed.") : _hasConflict;
		}
		set => _hasConflict = value;
	}

	public bool ThrowOnViewId { get; set; }

	public bool ThrowOnDocumentKey { get; set; }

	public bool ThrowOnStateProbe { get; set; }

	public bool ThrowOnOpen { get; set; }

	public bool ThrowOnRefresh { get; set; }

	public bool ThrowOnAcknowledgeIdentity { get; set; }

	public bool ThrowOnAcknowledgeApply { get; set; }

	public bool ThrowOnClose { get; set; }

	public bool ThrowOnAddHandler { get; set; }

	public bool ThrowOnRemoveHandler { get; set; }

	public WorkspaceDocumentViewOpenResult OpenResult { get; set; } = new(WorkspaceDocumentViewOpenOutcome.Opened);

	public WorkspaceDocumentViewRefreshResult RefreshResult { get; set; } = new(WorkspaceDocumentViewRefreshOutcome.Refreshed);

	public WorkspaceDocumentViewIdentityResult IdentityResult { get; set; } = new(WorkspaceDocumentViewIdentityOutcome.Updated);

	public WorkspaceDocumentViewApplyResult ApplyResult { get; set; } = new(WorkspaceDocumentViewApplyOutcome.Applied);

	public int OpenCount { get; private set; }

	public int RefreshCount { get; private set; }

	public int CloseCount { get; private set; }

	public WorkspaceDocumentSnapshot? LastRefreshSnapshot { get; private set; }

	public WorkspaceDocumentViewIdentityChange? LastIdentityChange { get; private set; }

	public WorkspaceDocumentMutationResult? LastApplyResult { get; private set; }

	public int SubscriberCount => _applyRequested?.GetInvocationList().Length ?? 0;

	public WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot)
	{
		Visit(nameof(Open));
		OpenCount++;

		if (ThrowOnOpen)
			throw new InvalidOperationException("The open failed.");

		_documentKey = snapshot.DocumentKey;
		return OpenResult;
	}

	public WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot)
	{
		Visit(nameof(Refresh));
		RefreshCount++;
		LastRefreshSnapshot = snapshot;

		return ThrowOnRefresh ? throw new InvalidOperationException("The refresh failed.") : RefreshResult;
	}

	public WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentViewIdentityChange change)
	{
		Visit(nameof(AcknowledgeIdentity));
		LastIdentityChange = change;

		return ThrowOnAcknowledgeIdentity ? throw new InvalidOperationException("The identity acknowledgment failed.") : IdentityResult;
	}

	public WorkspaceDocumentViewApplyResult AcknowledgeApply(WorkspaceDocumentMutationResult result)
	{
		Visit(nameof(AcknowledgeApply));
		LastApplyResult = result;

		return ThrowOnAcknowledgeApply ? throw new InvalidOperationException("The apply acknowledgment failed.") : ApplyResult;
	}

	public void Close()
	{
		Visit(nameof(Close));
		CloseCount++;

		if (ThrowOnClose)
			throw new InvalidOperationException("The close failed.");
	}

	private void Visit(string member) => OnMember?.Invoke(member);
}
