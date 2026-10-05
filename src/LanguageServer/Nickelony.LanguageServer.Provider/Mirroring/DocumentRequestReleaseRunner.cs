namespace Nickelony.LanguageServer.Provider;

/// <summary>Releases the temporary lease of a document request and carries out what the release makes due.</summary>
/// <remarks>
/// <para>
/// The release comes first: it makes the record the lease held eligible for the close it deferred and for the idle trim.
/// The deferred close of the released path and the choice of the evicted records run in that path's scheduler slot, so no synchronization
/// of the path interleaves. The server copies of the evicted records close after the slot, each in its own slot, because a queued operation
/// cannot enqueue another, and a close that follows a moved record runs last, through the record's current path.
/// </para>
/// <para>
/// The release runs even when the caller cancels, because a skipped release would pin a request-only record indefinitely.
/// </para>
/// </remarks>
internal sealed class DocumentRequestReleaseRunner
{
	// Upper bound for following a renamed record while completing its deferred close: each hop completes the close
	// through the record's current path, and a rename beyond this bound leaves the close pending for a later release.
	private const int PendingCloseRenameFollowLimit = 4;

	private readonly ProviderContext _context;
	private readonly DocumentTracker _documents;
	private readonly DocumentOperationScheduler _scheduler;
	private readonly PendingCloseTracker _pendingCloses;
	private readonly DocumentCloser _closer;

	/// <param name="context">The tracked documents, the options and the logger the release runs against.</param>
	/// <param name="scheduler">The scheduler that serializes the operations of one document.</param>
	/// <param name="pendingCloses">The tracker of the closes deferred while a request lease kept their document tracked.</param>
	/// <param name="closer">The closer that completes a deferred close and closes the server copy of an evicted record.</param>
	internal DocumentRequestReleaseRunner(
		ProviderContext context,
		DocumentOperationScheduler scheduler,
		PendingCloseTracker pendingCloses,
		DocumentCloser closer
	)
	{
		_context = context;
		_documents = context.Documents;
		_scheduler = scheduler;
		_pendingCloses = pendingCloses;
		_closer = closer;
	}

	/// <summary>
	/// Releases one request lease inside the record's per-document scheduler slot, completes a close that was deferred while the lease was active,
	/// and closes the server copies of the idle records the release evicts.
	/// </summary>
	/// <param name="requestLease">The lease bound to the tracked record.</param>
	/// <param name="cancellationToken">A token that can cancel the request; the release still runs when it is canceled.</param>
	internal async Task ReleaseAsync(DocumentRequestLease requestLease, CancellationToken cancellationToken)
	{
		// The tracker release runs unconditionally: it only mutates tracked state, and every close-send path it can
		// reach gates the send on the client itself. A null client must therefore not skip the release and pin a
		// request-only record indefinitely.
		if (requestLease.CurrentFilePath is not { } releasePath)
			return;

		try
		{
			IReadOnlyList<DocumentSnapshot> documentsToClose = await _scheduler
				.EnqueuePerDocumentAsync(
					releasePath,

					async token =>
					{
						// Caller cancellation must not skip request-lease cleanup, otherwise a canceled
						// language-feature request can leave a request-only tracked document pinned indefinitely.
						_documents.ReleaseRequestByLease(requestLease);

						// A rename that landed before this release ran carried any deferred close to the record's current
						// path; completing it needs that path's own scheduler slot, so the follow-up loop handles it.
						if (
							requestLease.CurrentFilePath is { } currentPath
							&& _context.Options.PathComparison.Comparer.Equals(currentPath, releasePath)
						)
						{
							await _closer.CompletePendingDocumentCloseAsync(releasePath).ConfigureAwait(false);
						}

						return _documents.TrimIdleDocuments(_context.Options.MaxTrackedIdleDocuments);
					},

					CancellationToken.None
				)
				.ConfigureAwait(false);

			for (int i = 0; i < documentsToClose.Count; i++)
				await _closer.CloseTrimmedDocumentAsync(documentsToClose[i]).ConfigureAwait(false);

			await FollowRenamedDocumentCloseAsync(requestLease, releasePath).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _context.Disposal.IsDisposed) { }
		catch (Exception exception)
		{
			_context.LogBestEffortFailure("request-document release", releasePath, exception);
		}
	}

	/// <summary>
	/// Follows a record whose path is renamed while its request lease was being released and completes its deferred close through the record's
	/// current path.
	/// </summary>
	/// <param name="requestLease">The released lease bound to the record.</param>
	/// <param name="releasePath">The document path whose scheduler slot the release already went through.</param>
	/// <remarks>
	/// The follow is bounded by <see cref="PendingCloseRenameFollowLimit"/> hops, so a record that is renamed more often than that during one release
	/// leaves its close pending. The pending marker is retained, so a later request release for the path completes the deferred close;
	/// an open or an idle trim for the path clears the marker instead.
	/// </remarks>
	private async Task FollowRenamedDocumentCloseAsync(DocumentRequestLease requestLease, string releasePath)
	{
		for (int i = 0; i < PendingCloseRenameFollowLimit; i++)
		{
			if (
				requestLease.CurrentFilePath is not { } currentPath
				|| _context.Options.PathComparison.Comparer.Equals(currentPath, releasePath)
				|| !_pendingCloses.IsPending(currentPath)
			)
			{
				return;
			}

			string closePath = currentPath;

			await _scheduler
				.EnqueuePerDocumentAsync(
					closePath,

					async _ =>
					{
						await _closer.CompletePendingDocumentCloseAsync(closePath).ConfigureAwait(false);
						return true;
					},

					CancellationToken.None
				)
				.ConfigureAwait(false);

			releasePath = closePath;
		}
	}
}
