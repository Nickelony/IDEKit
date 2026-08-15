using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerMutationResult> DiscardAsync(WorkspaceDocumentDiscardRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		// Discard deliberately overrides attached view state: the caller already decided that the
		// logical changes are abandoned, so attached view state does not block it. Every attached view
		// is asked to acknowledge the restored snapshot, which clears its pending state; an
		// acknowledgment failure is retained as unsynchronized view state and reported on the result.
		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentMutationResult result = _store.Discard(request);
			if (result.Snapshot is null
				|| result.Outcome is not (WorkspaceDocumentMutationOutcome.Changed or WorkspaceDocumentMutationOutcome.NoChange))
				return WorkspaceDocumentManagerMutationResult.FromStore(result, WorkspaceDocumentViewSynchronizationResult.Synchronized);

			List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = await AcknowledgeDocumentViewsAsync(result).ConfigureAwait(false);
			return WorkspaceDocumentManagerMutationResult.FromStore(result, ComposeSynchronization(failedViewIssues));
		});
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerMutationResult> ReplaceAsync(WorkspaceDocumentReplaceRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return ReplaceCoreAsync(request, sourceView: null);
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// Attached view state does not block a rename: the move cannot lose view edits, and the
		// identity change is acknowledged afterwards. Only an acknowledgment failure is reported.
		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentRenameResult result = await _store
				.RenameAsync(request, cancellationToken)
				.ConfigureAwait(false);
			if (result.Outcome != WorkspaceDocumentRenameOutcome.Renamed || result.Snapshot is null)
				return WorkspaceDocumentManagerRenameResult.FromStore(result, WorkspaceDocumentViewSynchronizationResult.Synchronized);

			return WorkspaceDocumentManagerRenameResult.FromStore(
				result,
				await SynchronizeViewIdentitiesAsync(request.Identity.DocumentId, request.Identity.DocumentKey, result.Snapshot).ConfigureAwait(false));
		});
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// Attached view state does not block a save-as: the write cannot lose view edits, and the
		// identity change is acknowledged afterwards. Only an acknowledgment failure is reported.
		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentSaveAsResult result = await _store
				.SaveAsAsync(request, cancellationToken)
				.ConfigureAwait(false);
			if (result.Outcome != WorkspaceDocumentSaveAsOutcome.SavedAs || result.Snapshot is null)
				return WorkspaceDocumentManagerSaveAsResult.FromStore(result, WorkspaceDocumentViewSynchronizationResult.Synchronized);

			return WorkspaceDocumentManagerSaveAsResult.FromStore(
				result,
				await SynchronizeViewIdentitiesAsync(request.Identity.DocumentId, request.Identity.DocumentKey, result.Snapshot).ConfigureAwait(false));
		});
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			// A view with pending edits or a conflict blocks the delete: the delete destroys the
			// document whose unsaved state the view still holds.
			IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? blockingIssues = await GetBlockingIssuesAsync(request.Identity.DocumentId).ConfigureAwait(false);
			if (blockingIssues is not null)
				return WorkspaceDocumentManagerDeleteResult.Blocked(
					GetCurrentSnapshot(request.Identity.DocumentId),
					Blocked(blockingIssues));

			IWorkspaceDocumentView[] views = GetPeers(request.Identity.DocumentId, sourceView: null);
			DeleteGuardScope guardScope = new(this);

			// Entered guards are latched on live views, so the scope releases whatever is still held when
			// an earlier exit skipped the explicit release.
			try
			{
				List<WorkspaceDocumentViewSynchronizationIssue> failedGuardIssues = await EnterDeleteGuardsViaDispatchAsync(views, guardScope.EnteredGuards).ConfigureAwait(false);
				if (failedGuardIssues.Count > 0)
					return WorkspaceDocumentManagerDeleteResult.Blocked(
						GetCurrentSnapshot(request.Identity.DocumentId),
						Blocked(failedGuardIssues));

				WorkspaceDocumentDeleteResult result = await _store
					.DeleteAsync(request, cancellationToken)
					.ConfigureAwait(false);
				// A document the store no longer tracks is gone either way, so it closes its views like a
				// completed delete: leaving them registered would report them synchronized for a document
				// that no longer exists.
				if (result.Outcome is not (WorkspaceDocumentDeleteOutcome.Deleted or WorkspaceDocumentDeleteOutcome.DocumentNotFound))
				{
					// The store failed, so the guards are released here; a release failure leaves the view
					// unsynchronized and is reported on the result.
					List<WorkspaceDocumentViewSynchronizationIssue> releaseIssues = [];
					await ReleaseDeleteGuardsViaDispatchAsync(guardScope.EnteredGuards, releaseIssues).ConfigureAwait(false);
					guardScope.MarkReleased();
					return WorkspaceDocumentManagerDeleteResult.FromStore(result, ComposeSynchronization(releaseIssues));
				}

				List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];
				await _dispatchViewAction(() =>
				{
					// Guards are released before the views close; the release failure is reported through the
					// result rather than recorded on a view that is about to be unregistered.
					ReleaseDeleteGuards(guardScope.EnteredGuards, failedViewIssues, recordState: false);

					foreach (IWorkspaceDocumentView view in views)
						CloseDeletedView(view, failedViewIssues);

					CloseLateAttachedViews(request.Identity.DocumentId, request.Identity.DocumentKey, failedViewIssues);
				}).ConfigureAwait(false);
				guardScope.MarkReleased();

				return WorkspaceDocumentManagerDeleteResult.FromStore(
					result,
					ComposeSynchronization(failedViewIssues));
			}
			finally
			{
				await guardScope.ReleaseOnExitAsync().ConfigureAwait(false);
			}
		});
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// Attached view state does not block a directory rename for the same reason it does not block
		// a file rename: the move cannot lose view edits, and the identity changes are acknowledged
		// afterwards.
		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentSnapshot[] sourceSnapshots = _store
				.GetSnapshotsUnderDirectory(request.SourceDirectoryPath)
				.ToArray();
			List<ViewBinding> bindings = GetViewBindings(sourceSnapshots);
			DeleteGuardScope guardScope = new(this);

			// Entered guards are latched on live views, so the scope releases whatever is still held when
			// an earlier exit skipped the explicit release.
			try
			{
				List<WorkspaceDocumentViewSynchronizationIssue> failedGuardIssues = await EnterDeleteGuardsViaDispatchAsync(
					bindings.Select(binding => binding.View),
					guardScope.EnteredGuards)
					.ConfigureAwait(false);
				if (failedGuardIssues.Count > 0)
					return WorkspaceDocumentManagerDirectoryRenameResult.Blocked(sourceSnapshots, Blocked(failedGuardIssues));

				WorkspaceDocumentDirectoryRenameResult result = await _store
					.RenameDirectoryAsync(request, cancellationToken)
					.ConfigureAwait(false);
				if (result.Outcome != WorkspaceDocumentDirectoryRenameOutcome.Renamed)
				{
					// The store failed, so the guards are released here; a release failure leaves the view
					// unsynchronized and is reported on the result.
					List<WorkspaceDocumentViewSynchronizationIssue> releaseIssues = [];
					await ReleaseDeleteGuardsViaDispatchAsync(guardScope.EnteredGuards, releaseIssues).ConfigureAwait(false);
					guardScope.MarkReleased();
					return WorkspaceDocumentManagerDirectoryRenameResult.FromStore(result, ComposeSynchronization(releaseIssues));
				}

				List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];
				await _dispatchViewAction(() =>
				{
					// The result snapshots are indexed once; a per-binding linear search would make the
					// tail quadratic in the number of moved documents.
					Dictionary<WorkspaceDocumentKey, WorkspaceDocumentSnapshot> snapshotsByKey = [];
					foreach (WorkspaceDocumentSnapshot candidate in result.Snapshots)
						snapshotsByKey[candidate.DocumentKey] = candidate;

					HashSet<IWorkspaceDocumentView> capturedViews = new(ReferenceEqualityComparer.Instance);
					foreach (ViewBinding binding in bindings)
					{
						capturedViews.Add(binding.View);
						if (!snapshotsByKey.TryGetValue(binding.Snapshot.DocumentKey, out WorkspaceDocumentSnapshot? snapshot))
						{
							// The result did not carry the view's snapshot, so its binding is stale: record it
							// as unsynchronized in addition to reporting it, matching the acknowledgment-failure
							// branch inside AcknowledgeViewIdentity.
							RecordViewFailure(binding.View);
							failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(binding.View)));
							continue;
						}

						AcknowledgeViewIdentity(
							binding.View,
							new WorkspaceDocumentViewIdentityChange(
								binding.Snapshot.DocumentKey,
								binding.Snapshot.DocumentId,
								snapshot),
							failedViewIssues);
					}

					// A view that attached while the store call was in flight is not in the captured
					// binding list. It is matched to its moved document by the document key it reports and
					// acknowledged the same way, so it does not stay bound to the vacated id.
					foreach (IWorkspaceDocumentView view in GetRegisteredViews())
					{
						if (capturedViews.Contains(view)
							|| !TryGetViewDocumentKey(view, out WorkspaceDocumentKey? viewDocumentKey)
							|| !snapshotsByKey.TryGetValue(viewDocumentKey.Value, out WorkspaceDocumentSnapshot? lateSnapshot))
						{
							continue;
						}

						string? lateDocumentId = GetRegisteredDocumentId(view);
						if (lateDocumentId is null)
							continue;

						AcknowledgeViewIdentity(
							view,
							new WorkspaceDocumentViewIdentityChange(viewDocumentKey.Value, lateDocumentId, lateSnapshot),
							failedViewIssues);
					}

					ReleaseDeleteGuards(guardScope.EnteredGuards, failedViewIssues);
				}).ConfigureAwait(false);
				guardScope.MarkReleased();

				return WorkspaceDocumentManagerDirectoryRenameResult.FromStore(
					result,
					ComposeSynchronization(failedViewIssues));
			}
			finally
			{
				await guardScope.ReleaseOnExitAsync().ConfigureAwait(false);
			}
		});
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			WorkspaceDocumentSnapshot[] sourceSnapshots = _store
				.GetSnapshotsUnderDirectory(request.DirectoryPath)
				.ToArray();
			List<ViewBinding> bindings = GetViewBindings(sourceSnapshots);

			// A view with pending edits or a conflict blocks the delete: the delete destroys the
			// documents whose unsaved state the view still holds.
			IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? blockingIssues = await GetBlockingIssuesAsync(bindings.Select(binding => binding.View)).ConfigureAwait(false);
			if (blockingIssues is not null)
				return WorkspaceDocumentManagerDirectoryDeleteResult.Blocked(sourceSnapshots, Blocked(blockingIssues));

			DeleteGuardScope guardScope = new(this);

			// Entered guards are latched on live views, so the scope releases whatever is still held when
			// an earlier exit skipped the explicit release.
			try
			{
				List<WorkspaceDocumentViewSynchronizationIssue> failedGuardIssues = await EnterDeleteGuardsViaDispatchAsync(
					bindings.Select(binding => binding.View),
					guardScope.EnteredGuards)
					.ConfigureAwait(false);
				if (failedGuardIssues.Count > 0)
					return WorkspaceDocumentManagerDirectoryDeleteResult.Blocked(sourceSnapshots, Blocked(failedGuardIssues));

				WorkspaceDocumentDirectoryDeleteResult result = await _store
					.DeleteDirectoryAsync(request, cancellationToken)
					.ConfigureAwait(false);
				if (result.Outcome != WorkspaceDocumentDirectoryDeleteOutcome.Deleted)
				{
					// The store failed, so the guards are released here; a release failure leaves the view
					// unsynchronized and is reported on the result.
					List<WorkspaceDocumentViewSynchronizationIssue> releaseIssues = [];
					await ReleaseDeleteGuardsViaDispatchAsync(guardScope.EnteredGuards, releaseIssues).ConfigureAwait(false);
					guardScope.MarkReleased();
					return WorkspaceDocumentManagerDirectoryDeleteResult.FromStore(result, ComposeSynchronization(releaseIssues));
				}

				List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];
				await _dispatchViewAction(() =>
				{
					// Guards are released before the views close; a release failure is reported through the
					// result rather than recorded on a view that is about to be unregistered.
					ReleaseDeleteGuards(guardScope.EnteredGuards, failedViewIssues, recordState: false);

					foreach (ViewBinding binding in bindings)
						CloseDeletedView(binding.View, failedViewIssues);

					// The result snapshots cover every removed descendant, including descendants that
					// attached after this operation enumerated them; views reopened at the same paths
					// carry new keys and are left alone.
					foreach (WorkspaceDocumentSnapshot removedSnapshot in result.Snapshots)
						CloseLateAttachedViews(removedSnapshot.DocumentId, removedSnapshot.DocumentKey, failedViewIssues);
				}).ConfigureAwait(false);
				guardScope.MarkReleased();

				return WorkspaceDocumentManagerDirectoryDeleteResult.FromStore(
					result,
					ComposeSynchronization(failedViewIssues));
			}
			finally
			{
				await guardScope.ReleaseOnExitAsync().ConfigureAwait(false);
			}
		});
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			// A view whose only outstanding state is a prior synchronization failure does not block the
			// commit: the commit is retried so the write can proceed (a clean document makes it a no-op)
			// and the post-commit refresh retries the failed synchronization. Pending edits, conflicts,
			// and a state probe that throws still block the commit.
			IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? blockingIssues = await GetBlockingIssuesAsync(request.Identity.DocumentId, includeUnsynchronizedState: false).ConfigureAwait(false);
			if (blockingIssues is not null)
				return WorkspaceDocumentManagerCommitResult.Blocked(
					GetCurrentSnapshot(request.Identity.DocumentId),
					Blocked(blockingIssues));

			WorkspaceDocumentCommitResult result = await _store
				.CommitAsync(request, cancellationToken)
				.ConfigureAwait(false);
			if (result.Outcome != WorkspaceDocumentCommitOutcome.Committed)
			{
				// The operation did not reach its post-commit refresh, so the report is composed from the
				// current view state: a view that still has pending edits or a conflict, or one whose earlier
				// synchronization failure was not retried, is reported as unsynchronized instead of a clean
				// result.
				return WorkspaceDocumentManagerCommitResult.FromStore(
					result,
					ComposeSynchronization(await CollectUnsynchronizedIssuesAsync(request.Identity.DocumentId).ConfigureAwait(false)));
			}

			IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> unsynchronizedIssues = result.Snapshot is null
				? []
				: await RefreshViewsAndCollectUnsynchronizedAsync(request.Identity.DocumentId, result.Snapshot).ConfigureAwait(false);

			return WorkspaceDocumentManagerCommitResult.FromStore(
				result,
				ComposeSynchronization(unsynchronizedIssues));
		});
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			// A view with pending edits or a conflict would lose that state when the document content is
			// replaced from disk, so it blocks the reload instead of being refreshed over. A view whose
			// only outstanding state is a prior synchronization failure does not block: the reload
			// retries the failed synchronization.
			IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? blockingIssues = await GetBlockingIssuesAsync(request.Identity.DocumentId, includeUnsynchronizedState: false).ConfigureAwait(false);
			if (blockingIssues is not null)
				return WorkspaceDocumentManagerReloadResult.Blocked(
					GetCurrentSnapshot(request.Identity.DocumentId),
					Blocked(blockingIssues));

			WorkspaceDocumentReloadResult result = await _store
				.ReloadAsync(request, cancellationToken)
				.ConfigureAwait(false);

			if (result.Outcome == WorkspaceDocumentReloadOutcome.Reloaded && result.Snapshot is not null)
			{
				return WorkspaceDocumentManagerReloadResult.FromStore(
					result,
					ComposeSynchronization(await RefreshViewsAndCollectUnsynchronizedAsync(request.Identity.DocumentId, result.Snapshot).ConfigureAwait(false)));
			}

			// The operation did not reach its post-reload refresh, so the report is composed from the current
			// view state instead of claiming a clean result; see CommitAsync.
			return WorkspaceDocumentManagerReloadResult.FromStore(
				result,
				ComposeSynchronization(await CollectUnsynchronizedIssuesAsync(request.Identity.DocumentId).ConfigureAwait(false)));
		});
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentManagerConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return RunOperationAsync(async () =>
		{
			// Pending edits or a conflict block the resolution: the view state would be replaced by the
			// resolution. A prior synchronization failure does not block; the post-resolution refresh
			// retries it.
			IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? blockingIssues = await GetBlockingIssuesAsync(request.Identity.DocumentId, includeUnsynchronizedState: false).ConfigureAwait(false);
			if (blockingIssues is not null)
				return WorkspaceDocumentManagerConflictResolutionResult.Blocked(
					GetCurrentSnapshot(request.Identity.DocumentId),
					Blocked(blockingIssues));

			WorkspaceDocumentConflictResolutionResult result = await _store
				.ResolveExternalConflictAsync(request, cancellationToken)
				.ConfigureAwait(false);

			if ((result.Outcome == WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk
				|| result.Outcome == WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical)
				&& result.Snapshot is not null)
			{
				return WorkspaceDocumentManagerConflictResolutionResult.FromStore(
					result,
					ComposeSynchronization(await RefreshViewsAndCollectUnsynchronizedAsync(request.Identity.DocumentId, result.Snapshot).ConfigureAwait(false)));
			}

			// The operation did not reach its post-resolution refresh, so the report is composed from the
			// current view state instead of claiming a clean result; see CommitAsync.
			return WorkspaceDocumentManagerConflictResolutionResult.FromStore(
				result,
				ComposeSynchronization(await CollectUnsynchronizedIssuesAsync(request.Identity.DocumentId).ConfigureAwait(false)));
		});
	}

	private Task<WorkspaceDocumentManagerMutationResult> ReplaceCoreAsync(
		WorkspaceDocumentReplaceRequest request,
		IWorkspaceDocumentView? sourceView)
		=> RunOperationAsync(async () =>
		{
			if (sourceView is not null && !IsRegistered(sourceView))
				throw new InvalidOperationException("An unregistered view published a document replacement.");

			// The store mutation runs on the caller's context, matching the other manager store
			// calls; the single dispatch action below carries the view acknowledgment and peer refreshes
			// so no view access runs outside the dispatch delegate.
			WorkspaceDocumentMutationResult result = _store.Replace(request);

			List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];
			await _dispatchViewAction(() =>
			{
				if (sourceView is not null)
				{
					WorkspaceDocumentViewApplyResult acknowledgment = TryAcknowledgeApply(sourceView, result);
					bool applied = acknowledgment.Outcome == WorkspaceDocumentViewApplyOutcome.Applied;
					RecordViewResult(sourceView, applied, acknowledgment.Failure, result.Snapshot);
					if (!applied)
						failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(sourceView), acknowledgment.Failure));
				}

				if (result.Snapshot is null
					|| result.Outcome is not (WorkspaceDocumentMutationOutcome.Changed or WorkspaceDocumentMutationOutcome.NoChange))
				{
					return;
				}

				// The publishing view was acknowledged above; peers that fell behind the replacement or whose
				// synchronization failed earlier are refreshed. A no-change replacement still carries the
				// authoritative snapshot, so peers are reconciled against it as well.
				RefreshPeerViews(GetPeers(result.Snapshot.DocumentId, sourceView), result.Snapshot, failedViewIssues);
			}).ConfigureAwait(false);

			return WorkspaceDocumentManagerMutationResult.FromStore(
				result,
				ComposeSynchronization(failedViewIssues));
		});

	private WorkspaceDocumentSnapshot? GetCurrentSnapshot(string documentId)
		=> _store.TryGetSnapshot(documentId, out WorkspaceDocumentSnapshot? snapshot) ? snapshot : null;
}
