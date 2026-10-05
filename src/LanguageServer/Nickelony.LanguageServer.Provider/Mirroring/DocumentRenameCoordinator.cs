namespace Nickelony.LanguageServer.Provider;

/// <summary>Renames the tracked record of a document to a new path and its server copy with it, through the exclusive slot of both paths.</summary>
/// <remarks>
/// A rename cancels the update queued for the vacated path, announces both identities as invalidated, and renames a close deferred by a request lease
/// with the document. A server copy that was open is closed under the old identity and opened under the new one when a server session is running.
/// </remarks>
internal sealed class DocumentRenameCoordinator
{
	private readonly ProviderContext _context;
	private readonly ILanguageServerClient? _client;
	private readonly DocumentTracker _documents;
	private readonly DocumentOperationScheduler _scheduler;
	private readonly PendingCloseTracker _pendingCloses;
	private readonly ProviderLifecycleCoordinator _lifecycle;
	private readonly DocumentNotificationSender _notificationSender;
	private readonly DocumentSignals _signals;

	/// <param name="context">The client, the tracked documents and the disposal every provider collaborator shares.</param>
	/// <param name="scheduler">The scheduler that serializes the operations of one document.</param>
	/// <param name="pendingCloses">The tracker of the closes deferred while a request lease kept their document tracked.</param>
	/// <param name="lifecycle">The coordinator that classifies a failed send as an unavailable connection.</param>
	/// <param name="notificationSender">The sender of the open and close notifications.</param>
	/// <param name="signals">The reactions of the features that consume what happened to a document.</param>
	internal DocumentRenameCoordinator(
		ProviderContext context,
		DocumentOperationScheduler scheduler,
		PendingCloseTracker pendingCloses,
		ProviderLifecycleCoordinator lifecycle,
		DocumentNotificationSender notificationSender,
		DocumentSignals signals
	)
	{
		_context = context;
		_client = context.Client;
		_documents = context.Documents;
		_scheduler = scheduler;
		_pendingCloses = pendingCloses;
		_lifecycle = lifecycle;
		_notificationSender = notificationSender;
		_signals = signals;
	}

	/// <summary>Renames a tracked document to a new path, mirrors the rename to the server, and announces it to the features.</summary>
	/// <param name="oldFilePath">The normalized path of the tracked document.</param>
	/// <param name="newFilePath">The normalized replacement path.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the rename.</param>
	/// <param name="newDocumentId">
	/// The id the host renamed the document to, which the renamed record takes,
	/// or <see langword="null"/> when it equals <paramref name="newFilePath"/>.
	/// </param>
	/// <returns><see langword="true"/> when the document's path is renamed; otherwise, <see langword="false"/>.</returns>
	internal async Task<bool> RenameAsync(
		string oldFilePath,
		string newFilePath,
		string content,
		CancellationToken cancellationToken,
		string? newDocumentId = null
	)
	{
		if (_context.Disposal.IsDisposed || _client is null)
			return false;

		try
		{
			DocumentRenameDirective? request = await _scheduler
				.EnqueueExclusivePerDocumentAsync(
					firstFilePath: oldFilePath,
					secondFilePath: newFilePath,
					operation: token => RenameCoreAsync(oldFilePath, newFilePath, content, newDocumentId, cancellationToken: token),
					cancellationToken
				)
				.ConfigureAwait(false);

			if (request is not { } renameRequest)
				return false;

			string filePath = renameRequest.RenamedDocument.FilePath;
			_signals.Renamed(filePath);

			// A path-only rename carried the cached semantic tokens and diagnostics to the new path, so the announced
			// state is already current and no server round-trip is needed. A rename that changed the content of a
			// server-open document behaves like an ordinary open: refresh the semantic tokens and pull diagnostics for
			// the renamed document, after the renamed or cleared state has been announced. A record the server did not
			// hold open sent no open, so a request for its new path would ask about a document the server lacks.
			if (ContentChangedByRename(renameRequest))
				_signals.Synchronized(renameRequest.RenamedDocument);

			return true;
		}
		// A rename runs with no caller cancellation token of its own: an OperationCanceledException can only be
		// disposal-driven and stays with the observed background task (the observer treats it as expected teardown).
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// Already invalidated by the send-site catch inside the exclusive rename slot. A disposal race needs no
			// unavailable transition.
			if (!_context.Disposal.IsDisposed)
				_lifecycle.MarkConnectionUnavailable();

			return false;
		}
	}

	/// <summary>
	/// Renames the tracked record and its server copy: closes the previous identity and opens the renamed document when a server session is running.
	/// </summary>
	/// <param name="oldFilePath">The normalized path of the tracked document.</param>
	/// <param name="newFilePath">The normalized replacement path.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="newDocumentId">
	/// The id the host renamed the document to, or <see langword="null"/> when it equals <paramref name="newFilePath"/>.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the sends.</param>
	/// <returns>The rename request when the document's path is renamed; otherwise, <see langword="null"/>.</returns>
	/// <remarks>
	/// The caller must hold the exclusive rename slot.
	/// </remarks>
	private async Task<DocumentRenameDirective?> RenameCoreAsync(
		string oldFilePath,
		string newFilePath,
		string content,
		string? newDocumentId,
		CancellationToken cancellationToken
	)
	{
		if (_client is null)
			return null;

		DocumentRenameResult result = _documents.Rename(oldFilePath, newFilePath, content, newDocumentId);

		if (result.Directive is not { } renameRequest)
			return null;

		// The rename vacated the old identity; cancel an update queued behind this exclusive rename slot for it so
		// the update cannot recreate a tracked record under the old path. Destination updates are deliberately
		// left queued: they were requested after the rename call and carry newer content for the new identity.
		_scheduler.CancelLatestUpdate(oldFilePath);

		// The rename vacated the old identity and the destination identity may already have had a refresh in
		// flight; cancel each so neither can re-store tokens or raise an update for a path this rename changed.
		_signals.Invalidated(oldFilePath);
		_signals.Invalidated(newFilePath);

		// A close deferred while a request lease kept the old path tracked follows the renamed document, so the
		// eventual request release still completes it.
		_pendingCloses.Rename(oldFilePath, newFilePath);

		if (!renameRequest.ReopenServerDocument)
			return renameRequest;

		// The startup-succeeded flag is not consulted here either (see CloseDocument): during a restart replay
		// the server is already running, and a document that the replay reopened carries a live server copy that
		// the rename must follow.
		if (!_client.IsReady)
		{
			_documents.InvalidateServerSynchronization(newFilePath);
			return renameRequest;
		}

		try
		{
			if (renameRequest.PreviousDocument is not null)
			{
				await _notificationSender
					.SendAsync(
						new DocumentSynchronizationDirective(DocumentSynchronizationKind.Close, renameRequest.PreviousDocument),
						cancellationToken
					)
					.ConfigureAwait(false);
			}

			await _notificationSender
				.SendAsync(new DocumentSynchronizationDirective(DocumentSynchronizationKind.Open, renameRequest.RenamedDocument), cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// A connection boundary or a disposal race invalidates the renamed document's server copy before the failure
			// propagates, so the next synchronization opens it again under the new path.
			if (!_context.Disposal.IsDisposed)
				TrackedSynchronization.Invalidate(_documents, _signals, newFilePath);

			throw;
		}

		return renameRequest;
	}

	/// <summary>
	/// Determines whether a rename changed the content of a document the server held open, so the reopened document needs a fresh semantic-token
	/// refresh and diagnostics pull.
	/// </summary>
	/// <param name="renameRequest">The rename request to classify.</param>
	/// <returns>
	/// <see langword="true"/> when the server held the document open and the rename changed its content; otherwise, <see langword="false"/>.
	/// A path-only rename carries its cached payloads along unchanged. A record the server did not hold open sends no open,
	/// so it has nothing to refresh until a later synchronization opens it.
	/// </returns>
	private static bool ContentChangedByRename(DocumentRenameDirective renameRequest)
	{
		return renameRequest.PreviousDocument is { } previousDocument
			&& !string.Equals(previousDocument.Content, renameRequest.RenamedDocument.Content, StringComparison.Ordinal);
	}
}
