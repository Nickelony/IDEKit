namespace Nickelony.LanguageServer.Provider;

/// <summary>Reopens the documents that a restart captured, each through its own per-document scheduler slot.</summary>
/// <remarks>
/// A document that was closed or retracked while the restart was in flight is skipped, so a replay never leaves a phantom
/// server-open record behind. A reopen that fails invalidates the tracked server copy, and the unfinished documents are handed back so a later
/// start resumes.
/// </remarks>
internal sealed class DocumentRestartReplayRunner : IDocumentRestartReplay
{
	private readonly ProviderContext _context;
	private readonly DocumentTracker _documents;
	private readonly DocumentOperationScheduler _scheduler;
	private readonly DocumentNotificationSender _notificationSender;
	private readonly DocumentSignals _signals;

	/// <param name="context">The client, the tracked documents and the disposal every provider collaborator shares.</param>
	/// <param name="scheduler">The scheduler that serializes the operations of one document.</param>
	/// <param name="notificationSender">The sender of the open notification.</param>
	/// <param name="signals">The reactions of the features that consume what happened to a document.</param>
	internal DocumentRestartReplayRunner(
		ProviderContext context,
		DocumentOperationScheduler scheduler,
		DocumentNotificationSender notificationSender,
		DocumentSignals signals
	)
	{
		_context = context;
		_documents = context.Documents;
		_scheduler = scheduler;
		_notificationSender = notificationSender;
		_signals = signals;
	}

	/// <summary>
	/// Reopens the tracked documents captured before a restart, each through its own per-document scheduler slot so a concurrent close or retracking
	/// cannot race the reopen.
	/// </summary>
	/// <param name="documents">The snapshots captured by the document tracker before the restart.</param>
	/// <param name="cancellationToken">A token that can cancel the replay.</param>
	/// <returns>
	/// <see langword="null"/> when every document was processed; otherwise, the documents that still need a reopen,
	/// starting with the one whose reopen failed, so a later start can resume the replay.
	/// </returns>
	public async Task<IReadOnlyList<DocumentSnapshot>?> ReopenTrackedDocumentsAsync(
		IReadOnlyList<DocumentSnapshot> documents,
		CancellationToken cancellationToken
	)
	{
		for (int i = 0; i < documents.Count; i++)
		{
			DocumentSnapshot document = documents[i];

			try
			{
				await _scheduler
					.EnqueuePerDocumentAsync(
						document.FilePath,

						async token =>
						{
							// The guarded reopen only mirrors documents that are still tracked with an open lease,
							// so a document that was closed or retracked while the restart was in flight is skipped
							// instead of leaving a phantom server-open record behind.
							DocumentSynchronizationDirective? request = _documents.ReopenAfterRestart(document.FilePath);

							if (request is not { } pendingRequest)
								return false;

							try
							{
								await _notificationSender.SendAsync(pendingRequest, token).ConfigureAwait(false);
								_signals.Synchronized(pendingRequest.Document);
							}
							catch (Exception replayException)
								when (replayException is IOException or ObjectDisposedException or OperationCanceledException)
							{
								// The replay failed mid-flight: invalidate the tracked server copy so the document is
								// reopened on the next synchronization instead of being treated as synchronized.
								if (!_context.Disposal.IsDisposed)
									TrackedSynchronization.Invalidate(_documents, _signals, document.FilePath);

								throw;
							}

							return true;
						},

						cancellationToken
					)
					.ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is IOException or ObjectDisposedException)
			{
				return [.. documents.Skip(i)];
			}
		}

		return null;
	}
}
