using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Replacement;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Applies a language-server <see cref="WorkspaceEdit"/> through a view coordinator, so every attached view follows the change.</summary>
/// <remarks>
/// <para>
/// An apply opens the documents the store does not track, has the views of each document publish their pending edits, and then plans the edit
/// against the published text. The plan is all or nothing: when it does not succeed, no document changes.
/// </para>
/// <para>
/// The replacements run one document at a time and are not atomic: a confirmed change
/// stays applied when a later document is not replaced, and <see cref="WorkspaceEditApplyResult.Replacement"/> reports each document.
/// Each replacement goes through <see cref="IWorkspaceViewCoordinator.ReplaceAsync"/>, so a document whose view holds pending
/// edits when its replacement runs is not replaced, with <see cref="WorkspaceViewOperationFailureCodes.ViewHoldsPendingEdits"/>, because the edit
/// would overwrite the typed text. <see cref="WorkspaceEditApplyResult.ViewSynchronization"/> names the views that blocked a replacement
/// or did not follow one.
/// </para>
/// </remarks>
public sealed class WorkspaceEditApplier
{
	private readonly IWorkspaceViewCoordinator _coordinator;
	private readonly WorkspaceDocumentOpenOptions? _openOptions;
	private readonly DocumentReplacementApplierOptions _replacementOptions;

	/// <param name="coordinator">The coordinator whose store holds the documents and whose views follow the replacements.</param>
	/// <param name="options">The open and replacement choices; the default plans only the documents the store tracks.</param>
	/// <exception cref="ArgumentNullException"><paramref name="coordinator"/> is <see langword="null"/>.</exception>
	public WorkspaceEditApplier(IWorkspaceViewCoordinator coordinator, WorkspaceEditApplierOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(coordinator);

		options ??= WorkspaceEditApplierOptions.Default;

		_coordinator = coordinator;
		_openOptions = options.OpenOptions;
		_replacementOptions = options.Replacement;
	}

	/// <summary>Applies the edit when every document it names can be changed as asked.</summary>
	/// <param name="edit">The edit a language-server provider returned.</param>
	/// <param name="cancellationToken">A token that can cancel the apply.</param>
	/// <returns>The plan, and the replacement result when the plan succeeded.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="edit"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator was stopped or disposed before the plan, so no document changed.</exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> was canceled before the plan, so no document changed.
	/// </exception>
	/// <remarks>
	/// From the first replacement on, a cancellation or a stop of the coordinator ends the apply with a canceled or partial
	/// <see cref="WorkspaceEditApplyResult.Replacement"/> instead of throwing, so the host learns which documents changed. A document the apply
	/// opened stays open when the plan fails.
	/// </remarks>
	public Task<WorkspaceEditApplyResult> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(edit);
		return ApplyCoreAsync(edit, cancellationToken);
	}

	private async Task<WorkspaceEditApplyResult> ApplyCoreAsync(WorkspaceEdit edit, CancellationToken cancellationToken)
	{
		IWorkspaceDocumentReader documents = _coordinator.Documents;
		StringComparer comparer = documents.Options.PathComparison.Comparer;
		List<string> documentIds = [.. edit.DocumentEdits.Select(documentEdit => documentEdit.DocumentId).Distinct(comparer)];
		Dictionary<string, WorkspaceEditPlanIssue> issues = new(comparer);
		Dictionary<string, long> publishedVersions = new(comparer);

		// Every untracked document opens before the first publication, so the publications and the plan follow each other closely.
		if (_openOptions is { } openOptions)
		{
			foreach (string documentId in documentIds)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (documents.TryGetSnapshot(documentId, out _))
					continue;

				WorkspaceViewCoordinatorOpenResult open = await _coordinator
					.OpenAsync(documentId, openOptions, cancellationToken)
					.ConfigureAwait(false);

				if (open.Outcome is not (WorkspaceViewCoordinatorOpenOutcome.Opened or WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen))
					issues[documentId] = new WorkspaceEditPlanIssue(documentId, WorkspaceEditPlanIssueKind.DocumentNotTracked, open);
			}
		}

		foreach (string documentId in documentIds)
		{
			if (issues.ContainsKey(documentId))
				continue;

			cancellationToken.ThrowIfCancellationRequested();

			WorkspaceViewCoordinatorPublicationResult published = await _coordinator
				.PublishPendingEditsAsync(documentId, cancellationToken)
				.ConfigureAwait(false);

			// The version the publication left is the one the plan must read, so a later publication is a plan issue, not text the edit addresses.
			if (published.ViewSynchronization.Outcome != WorkspaceDocumentViewSynchronizationOutcome.Synchronized)
				issues[documentId] = new WorkspaceEditPlanIssue(documentId, WorkspaceEditPlanIssueKind.PendingEditsNotPublished);
			else if (published.Snapshot is { } snapshot)
				publishedVersions[documentId] = snapshot.Version;
		}

		cancellationToken.ThrowIfCancellationRequested();

		WorkspaceEditPlan plan = WorkspaceEditPlanner.Plan(edit, documents, preparationIssues: issues, publishedVersions);

		Dictionary<string, WorkspaceDocumentViewSynchronizationResult> viewSynchronization = new(comparer);

		if (!plan.Succeeded)
			return new WorkspaceEditApplyResult(plan, Replacement: null, viewSynchronization);

		// A replacement that does not reach the store cancels this source, so the replacements end as a cancellation does.
		using var replacements = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		var applier = new DocumentReplacementApplier(
			(request, token) => ReplaceAsync(request, replacements, viewSynchronization, cancellationToken: token),
			documents.Options,
			_replacementOptions
		);

		DocumentReplacementResult replacement = await applier.ApplyAsync(plan.Targets, replacements.Token).ConfigureAwait(false);

		return new WorkspaceEditApplyResult(plan, replacement, viewSynchronization);
	}

	// A replacement the store rejects, such as a stale one after the user typed meanwhile, is a target that was not applied; it is not retried,
	// because the edit addressed the text the plan read.
	private async Task<DocumentReplacementAttemptResult> ReplaceAsync(
		WorkspaceDocumentReplaceRequest request,
		CancellationTokenSource replacements,
		Dictionary<string, WorkspaceDocumentViewSynchronizationResult> viewSynchronization,
		CancellationToken cancellationToken
	)
	{
		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced;

		try
		{
			replaced = await _coordinator.ReplaceAsync(request, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			// The coordinator returns a result once the store changed the document, so a throw means the store was not reached: a stopped
			// coordinator, a disposed store or a dispatcher that rejected the action. The replacements end as a cancellation does, with the fault.
			await replacements.CancelAsync().ConfigureAwait(false);
			throw new OperationCanceledException("The replacement did not reach the store.", exception, replacements.Token);
		}

		viewSynchronization[request.Identity.DocumentId] = replaced.ViewSynchronization;

		if (replaced.StoreResult is { } storeResult)
			return DocumentReplacementAttemptResult.Reached(storeResult);

		string viewIds = string.Join(", ", replaced.ViewSynchronization.Issues.Select(issue => issue.ViewId));

		return DocumentReplacementAttemptResult.Refused(
			new WorkspaceOperationFailure(
				WorkspaceViewOperationFailureCodes.ViewHoldsPendingEdits,
				$"The views {viewIds} of '{request.Identity.DocumentId}' hold edits the store does not have, so the language-server edit was not applied over them."
			)
		);
	}
}
