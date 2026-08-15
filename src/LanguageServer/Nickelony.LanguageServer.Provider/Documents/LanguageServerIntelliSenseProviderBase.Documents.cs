using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	// The number of extra attempts an open makes while a startup or restart-replay flow settles.
	private const int OpenDeferralRetryLimit = 2;

	// The transport generation whose first no-synchronization-mode change skip was logged at warning level; later
	// skips on the same generation drop to debug so a per-keystroke warning is not emitted.
	private long _changeSkipWarningTransportGeneration = long.MinValue;

	/// <summary>
	/// Ensures the transport outside the document's scheduler slot and then opens the document.
	/// </summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The initial document content.</param>
	/// <remarks>
	/// <para>
	/// The transport must be ensured before a slot runs: a restart replay queues per-document scheduler
	/// operations, and queueing them from inside a slot is rejected by the scheduler and can deadlock the slot's
	/// own chain while it waits on the startup lock. A slot therefore never starts the transport itself; it only
	/// observes <see cref="IsTransportReady"/> after this flow returns.
	/// </para>
	/// <para>
	/// Unlike an update, an open cannot be re-established by a later operation that supplies content: the open
	/// reference the host acquired must not be dropped. When the open's slot ran while a startup or a restart
	/// replay owned the tracked records, this flow retries the open while a new startup settles, up to
	/// <see cref="OpenDeferralRetryLimit"/> retries; a transport that keeps failing leaves the open unapplied
	/// like every other failed-startup operation.
	/// </para>
	/// </remarks>
	private async Task OpenDocumentAsync(string filePath, string content)
	{
		await TryEnsureStartedForOperationAsync(CancellationToken.None).ConfigureAwait(false);

		DocumentSynchronizationResult result = await SynchronizeDocumentAsync(filePath, content, DocumentSynchronizationMode.Open, requestReference: null, CancellationToken.None).ConfigureAwait(false);

		for (int attempt = 0; attempt < OpenDeferralRetryLimit && result.SkippedWhileUnavailable; attempt++)
		{
			if (!await TryEnsureStartedForOperationAsync(CancellationToken.None).ConfigureAwait(false))
				break;

			result = await SynchronizeDocumentAsync(filePath, content, DocumentSynchronizationMode.Open, requestReference: null, CancellationToken.None).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Ensures the transport outside the document's scheduler slot and then synchronizes the latest update.
	/// </summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The latest content of the document.</param>
	/// <remarks>See <see cref="OpenDocumentAsync(string, string)"/> for why the transport is ensured outside the slot.</remarks>
	private async Task UpdateDocumentAsync(string filePath, string content)
	{
		await TryEnsureStartedForOperationAsync(CancellationToken.None).ConfigureAwait(false);

		await SynchronizeLatestDocumentAsync(filePath, content).ConfigureAwait(false);
	}

	/// <summary>
	/// Coalesces a document update into the latest-update slot and synchronizes it when the slot runs.
	/// </summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The latest content of the document.</param>
	/// <returns><see langword="true"/> when the synchronization succeeded or was superseded; otherwise, <see langword="false"/>.</returns>
	private async Task<bool> SynchronizeLatestDocumentAsync(string filePath, string content)
	{
		if (_isDisposed)
			return false;

		if (_client is null)
		{
			ReportMissingClientFailure();
			return false;
		}

		try
		{
			// The latest-update node runs the synchronization as the scheduled operation itself. Queueing an inner
			// per-document operation from inside the delegate would deadlock the document's chain, so the in-slot
			// synchronization must not enqueue again.
			await _documentScheduler.EnqueueLatestUpdateAsync(filePath,
				token => SynchronizeDocumentInSlotAsync(filePath, content,
					DocumentSynchronizationMode.Update,
					requestReference: null,
					token)).ConfigureAwait(false);

			return true;
		}
		catch (OperationCanceledException) when (_isDisposed)
		{
			// Disposal-driven cancellation surfaces as the documented false result.
			return false;
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// The tracked synchronization was already invalidated by the send-site catch inside the operation;
			// invalidating again here would run after the chain slot released and could mark the document closed
			// after a queued operation already reopened it. A disposal race needs no unavailable transition, and
			// TrySetState ignores one anyway.
			if (!_isDisposed)
				MarkStartupTransportUnavailable();

			return false;
		}
	}

	private async Task<DocumentSynchronizationResult> SynchronizeDocumentAsync(string filePath, string content, DocumentSynchronizationMode mode,
		DocumentRequestReference? requestReference, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_isDisposed)
			return new(false, null, NotificationDelivered: false);

		if (_client is null)
		{
			ReportMissingClientFailure();
			return new(false, null, NotificationDelivered: false);
		}

		try
		{
			return await _documentScheduler.EnqueuePerDocumentAsync(
				filePath,
				token => SynchronizeDocumentInSlotAsync(filePath, content, mode, requestReference, token),
				cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_isDisposed)
		{
			// Disposal-driven cancellation surfaces as the documented false result.
			return new(false, null, NotificationDelivered: false);
		}
		catch (OperationCanceledException)
		{
			// An unowned cancellation (neither the caller token nor disposal) is classified like the dispatcher
			// classifies it: the documented false result instead of a leaked cancellation.
			return new(false, null, NotificationDelivered: false);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// Already invalidated by the send-site catch inside the per-document scheduler slot; see
			// SynchronizeLatestDocumentAsync. A disposal race needs no unavailable transition.
			if (!_isDisposed)
				MarkStartupTransportUnavailable();

			return new(false, null, NotificationDelivered: false);
		}
	}

	/// <summary>
	/// Synchronizes one document while its scheduler slot is already held and refreshes the language-specific state
	/// when the mode requests it.
	/// </summary>
	/// <remarks>
	/// The slot must not start or restart the transport; the entry points ensure it before the slot runs (see
	/// <see cref="OpenDocumentAsync(string, string)"/>).
	/// </remarks>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The content to synchronize.</param>
	/// <param name="mode">The reference acquisition and refresh behavior of this synchronization.</param>
	/// <param name="requestReference">The reference that receives the request reference acquired for this synchronization, or <see langword="null"/> when the synchronization acquires no request reference.</param>
	/// <param name="cancellationToken">A token that can cancel the synchronization.</param>
	/// <returns>The synchronization result: the store's success flag, the tracked document when one remains, and whether a notification was delivered to the server.</returns>
	private async Task<DocumentSynchronizationResult> SynchronizeDocumentInSlotAsync(string filePath, string content,
		DocumentSynchronizationMode mode, DocumentRequestReference? requestReference, CancellationToken cancellationToken)
	{
		DocumentSynchronizationResult result;

		try
		{
			result = await SynchronizeDocumentCoreAsync(
				filePath, content, mode, requestReference, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			// An open reference cancels a close that was deferred while a temporary request reference kept the
			// document tracked: the host holds the document open again, so the deferred close must not run later.
			// This runs even when the synchronization throws, because the host acquired the open reference by
			// requesting the open regardless of whether the synchronization succeeded.
			if (mode.AcquiresOpenReference)
				_pendingDocumentCloses.TryRemove(filePath, out _);
		}

		// A change the negotiated synchronization mode could not express does not advance the server's copy, so a
		// refresh computed from it would describe stale content (stale semantic tokens, for example).
		if (mode.RefreshDocument && result.Succeeded && result.NotificationDelivered && result.Document is { } synchronizedDocument)
		{
			await InvokeContainedHookAsync(() => OnDocumentSynchronizedAsync(synchronizedDocument, cancellationToken), "post-synchronization").ConfigureAwait(false);
			TriggerSemanticTokenRefresh(synchronizedDocument);
			TriggerDiagnosticsPull(synchronizedDocument);
		}

		return result;
	}

	/// <summary>
	/// Marks one document's tracked state as no longer mirrored to the server.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="MarkStartupTransportUnavailable"/> this only touches tracked state, so callers can run it
	/// while the per-document scheduler slot is still held; that keeps the invalidation ordered before any
	/// operation queued behind a failed send, which then observes the invalidated state and reopens the
	/// document instead of trusting a server copy that never received the change.
	/// </remarks>
	/// <param name="filePath">The document whose tracked server synchronization should be invalidated.</param>
	private void InvalidateTrackedServerSynchronization(string filePath)
	{
		InvokeContainedHook(() => InvalidateTrackedDocumentSynchronization(filePath), "tracked-document invalidation");
		InvokeContainedHook(() => OnTrackedDocumentInvalidated(filePath), "tracked-document invalidation");
		CancelSemanticTokenRequest(filePath);
		CancelDiagnosticRequest(filePath);

		// Deliberately no CancelQueuedDocumentUpdate here: this runs while the update that just failed still
		// holds the document's scheduler slot, and any still-pending update is the newest one for the path.
		// It waits for the failed update to complete, so it observes the invalidated state and reopens the
		// document; canceling it would drop that recovery.
	}

	private async Task<bool> MoveDocumentAsync(string oldFilePath, string newFilePath, string content, CancellationToken cancellationToken)
	{
		if (_isDisposed || _client is null)
			return false;

		try
		{
			DocumentRenameRequest? request = await _documentScheduler.EnqueueExclusivePerDocumentAsync(
				oldFilePath,
				newFilePath,
				token => MoveDocumentCoreAsync(oldFilePath, newFilePath, content, token),
				cancellationToken).ConfigureAwait(false);

			if (request is not { } renameRequest)
				return false;

			string filePath = renameRequest.RenamedDocument.FilePath;
			RaiseDiagnosticsUpdated(filePath, InvokeContainedHook(() => GetTrackedDiagnostics(filePath), [], "tracked diagnostics"));
			RaiseSemanticTokensUpdated(filePath, InvokeContainedHook(() => GetTrackedSemanticTokens(filePath), [], "tracked semantic tokens"));
			InvokeContainedHook(() => OnDocumentMoved(filePath), "document move");

			// A path-only rename re-keyed the cached semantic tokens and diagnostics to the new path, so the state just
			// announced is already current and no server round-trip is needed. A rename that changed the content (or
			// opened a previously untracked document) behaves like an ordinary open: refresh the semantic tokens and
			// pull diagnostics for the reopened document, after the re-keyed or cleared state has been announced.
			if (ContentChangedByRename(renameRequest))
			{
				TriggerSemanticTokenRefresh(renameRequest.RenamedDocument);
				TriggerDiagnosticsPull(renameRequest.RenamedDocument);
			}

			return true;
		}
		// Rename runs with no caller cancellation token of its own: an OperationCanceledException can only be
		// disposal-driven and stays with the observed background task (the observer treats it as expected teardown).
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// Already invalidated by the send-site catch inside the exclusive move slot; see
			// SynchronizeLatestDocumentAsync. A disposal race needs no unavailable transition.
			if (!_isDisposed)
				MarkStartupTransportUnavailable();

			return false;
		}
	}

	/// <summary>
	/// Synchronizes one document and classifies the final synchronization result for the caller.
	/// </summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The content to synchronize.</param>
	/// <param name="mode">The reference acquisition and refresh behavior of this synchronization.</param>
	/// <param name="requestReference">The reference that receives the request reference acquired for this synchronization, or <see langword="null"/> when the synchronization acquires no request reference.</param>
	/// <param name="cancellationToken">A token that can cancel the synchronization.</param>
	/// <returns>The synchronization result: the store's success flag, the tracked document when one remains, and whether a notification was delivered to the server.</returns>
	/// <remarks>
	/// <para>
	/// When the transport is not ready, the content is committed locally only while the tracked records are not
	/// owned by a restart replay, so a local commit cannot reopen a record that the guarded replay then skips.
	/// </para>
	/// <para>
	/// A fault raised by the tracked-document store's required transforms propagates unchanged. The framework's
	/// hook-containment policy covers its informational language hooks only: the store's transforms produce the
	/// record this synchronization mirrors, so a contained fault could not produce a truthful result and would
	/// also swallow the store's deliberate contract exceptions (an already-bound request reference, for example).
	/// The identity-bound request reference is still released on that path because the caller releases it by
	/// identity once it is bound.
	/// </para>
	/// </remarks>
	private async Task<DocumentSynchronizationResult> SynchronizeDocumentCoreAsync(
		string filePath,
		string content,
		DocumentSynchronizationMode mode,
		DocumentRequestReference? requestReference,
		CancellationToken cancellationToken)
	{
		// The path is already normalized (the provider normalizes it as it enters the request pipeline), so the
		// store's normalized-key fast path is used to avoid a redundant Path.GetFullPath and a throwaway snapshot.
		bool shouldTrackLocallyWhileUnavailable = mode.AcquiresOpenReference || _documents.IsTrackedNormalized(filePath);
		bool includeChangeRange = _client?.TextDocumentSyncKind == TextDocumentSyncKind.Incremental;

		// A slot never starts or restarts the transport: the entry points ensure it before the slot runs (see
		// EnsureTransportStartedAsync and OpenDocumentAsync), and the slot only observes the readiness snapshot.
		if (!IsTransportReady)
		{
			// A restart attempt or a pending replay owns the tracked records, and a local content commit can
			// reopen a record that the guarded replay then skips, leaving a record that claims a server-open
			// document that was never sent. Skip the commit while that flow is in progress, and report the skip
			// so an entry point that owes the host a guaranteed effect can retry once the flow settles.
			bool committedLocally = shouldTrackLocallyWhileUnavailable
				&& !_isDisposed
				&& State != LanguageServerProviderState.Starting
				&& _pendingReopenDocuments is null;

			if (committedLocally)
				_documents.SynchronizeNormalized(filePath, content, mode.References & DocumentReferenceAcquisition.Open,
					includeChangeRange: includeChangeRange, requestReference: null);

			return new(false, null, NotificationDelivered: false, SkippedWhileUnavailable: !committedLocally);
		}

		// The store binds the reference while it holds its store lock, so the request pipeline releases it on every
		// exit path, including a failed send or a store hook that throws after the acquisition.
		DocumentSynchronizationRequest? request = _documents.SynchronizeNormalized(filePath, content, mode.References,
			includeChangeRange: includeChangeRange, requestReference: requestReference);

		// A null request means the store already mirrors the document: nothing was sent and no refresh runs.
		if (request is not { } pendingRequest)
			return new(true, null, NotificationDelivered: false);

		bool notificationDelivered;

		try
		{
			notificationDelivered = await SendDocumentSynchronizationNotificationAsync(pendingRequest, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// A transport boundary or a disposal race invalidates the tracked server copy before the failure
			// propagates, so the next synchronization reopens the document instead of trusting an unsent change.
			if (!_isDisposed)
				InvalidateTrackedServerSynchronization(filePath);

			throw;
		}
		catch (OperationCanceledException)
		{
			// The content and version were committed before the send, so a cancellation that aborts the send (a
			// superseding open/close cancels a running update, or the caller cancels a request) can leave the tracked
			// record claiming a server state that was never delivered. Invalidate so the next synchronization reopens
			// the document instead of computing incremental ranges against unsent content.
			if (!_isDisposed)
				InvalidateTrackedServerSynchronization(filePath);

			throw;
		}

		return new(true, pendingRequest.Document, notificationDelivered);
	}

	/// <summary>
	/// Rekeys the tracked record and moves its server copy: closes the previous identity and opens the moved
	/// document when a server session is running. The caller must hold the exclusive move slot.
	/// </summary>
	/// <param name="oldFilePath">The normalized path of the tracked document.</param>
	/// <param name="newFilePath">The normalized replacement path.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the sends.</param>
	/// <returns>The move request when the document was rekeyed; otherwise, <see langword="null"/>.</returns>
	private async Task<DocumentRenameRequest?> MoveDocumentCoreAsync(
		string oldFilePath,
		string newFilePath,
		string content,
		CancellationToken cancellationToken)
	{
		if (_client is null)
			return null;

		DocumentRenameResult result = _documents.Rename(oldFilePath, newFilePath, content);

		if (result.Request is not { } renameRequest)
			return null;

		// The rekey vacated the old identity; cancel an update queued behind this exclusive move slot for it so
		// the update cannot recreate a tracked record under the old path. Destination updates are deliberately
		// left queued: they were requested after the move call and carry newer content for the new identity.
		CancelQueuedDocumentUpdate(oldFilePath);

		InvokeContainedHook(() => OnTrackedDocumentInvalidated(oldFilePath), "tracked-document invalidation");
		InvokeContainedHook(() => OnTrackedDocumentInvalidated(newFilePath), "tracked-document invalidation");

		// The rekey vacated the old identity and the destination identity may already have had a refresh in
		// flight; cancel both so neither can re-store tokens or raise an update for a path this move changed.
		CancelSemanticTokenRequest(oldFilePath);
		CancelSemanticTokenRequest(newFilePath);
		CancelDiagnosticRequest(oldFilePath);
		CancelDiagnosticRequest(newFilePath);

		// A close deferred while a request reference kept the old path tracked follows the rekeyed document, so the
		// eventual request release still completes it.
		if (_pendingDocumentCloses.TryRemove(oldFilePath, out _))
			_pendingDocumentCloses[newFilePath] = 0;

		if (!renameRequest.ReopenServerDocument)
			return renameRequest;

		// The startup-succeeded flag is not consulted here either (see CloseDocumentAsync): during a restart replay
		// the server is already running, and a document that the replay reopened carries a live server copy that
		// the rename must move.
		if (!_client.IsReady)
		{
			InvokeContainedHook(() => InvalidateTrackedDocumentSynchronization(newFilePath), "tracked-document invalidation");
			return renameRequest;
		}

		try
		{
			if (renameRequest.PreviousDocument is not null)
			{
				await SendDocumentSynchronizationNotificationAsync(
					new DocumentSynchronizationRequest(DocumentSynchronizationKind.Close, renameRequest.PreviousDocument),
					cancellationToken).ConfigureAwait(false);
			}

			await SendDocumentSynchronizationNotificationAsync(
				new DocumentSynchronizationRequest(DocumentSynchronizationKind.Open, renameRequest.RenamedDocument),
				cancellationToken).ConfigureAwait(false);

			// The reopened document runs the same post-synchronization hook an ordinary open runs, so a provider
			// that overrides it observes the move as a synchronization rather than only through the move hook.
			await InvokeContainedHookAsync(() => OnDocumentSynchronizedAsync(renameRequest.RenamedDocument, cancellationToken), "post-synchronization").ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// A transport boundary or a disposal race invalidates the moved document's server copy before the failure
			// propagates, so the next synchronization opens it again under the new path.
			if (!_isDisposed)
				InvalidateTrackedServerSynchronization(newFilePath);

			throw;
		}

		return renameRequest;
	}

	/// <summary>
	/// Determines whether a rename changed the document's content, so the reopened document needs a fresh
	/// semantic-token refresh and diagnostics pull.
	/// </summary>
	/// <param name="renameRequest">The rename request to classify.</param>
	/// <returns>
	/// <see langword="true"/> when the content changed or no previous document existed (an untracked document was
	/// opened); <see langword="false"/> for a path-only rename whose cached payloads were re-keyed unchanged.
	/// </returns>
	private static bool ContentChangedByRename(DocumentRenameRequest renameRequest)
		=> renameRequest.PreviousDocument is not { } previousDocument
			|| !string.Equals(previousDocument.Content, renameRequest.RenamedDocument.Content, StringComparison.Ordinal);

	/// <summary>
	/// Reopens the tracked documents captured before a restart, each through its own per-document scheduler slot so
	/// a concurrent close or retracking cannot race the reopen.
	/// </summary>
	/// <param name="documents">The snapshots captured by the tracked-document store before the restart.</param>
	/// <param name="cancellationToken">A token that can cancel the replay.</param>
	/// <returns>
	/// <see langword="null"/> when every document was processed; otherwise, the documents that still need a reopen,
	/// starting with the one whose reopen failed, so a later start can resume the replay.
	/// </returns>
	private async Task<IReadOnlyList<DocumentSnapshot>?> ReopenTrackedDocumentsAsync(IReadOnlyList<DocumentSnapshot> documents, CancellationToken cancellationToken)
	{
		for (int i = 0; i < documents.Count; i++)
		{
			DocumentSnapshot document = documents[i];

			try
			{
				await _documentScheduler.EnqueuePerDocumentAsync(
					document.FilePath,
					async token =>
					{
						// The guarded reopen only mirrors documents that are still tracked with an open reference,
						// so a document that was closed or retracked while the restart was in flight is skipped
						// instead of leaving a phantom server-open record behind.
						DocumentSynchronizationRequest? request = _documents.TryReopenTrackedDocument(document.FilePath);

						if (request is not { } pendingRequest)
							return false;

						try
						{
							await SendDocumentSynchronizationNotificationAsync(pendingRequest, token).ConfigureAwait(false);
							await InvokeContainedHookAsync(() => OnDocumentSynchronizedAsync(pendingRequest.Document, token), "post-synchronization").ConfigureAwait(false);
							TriggerSemanticTokenRefresh(pendingRequest.Document);
							TriggerDiagnosticsPull(pendingRequest.Document);
						}
						catch (Exception replayException) when (replayException is IOException or ObjectDisposedException or OperationCanceledException)
						{
							// The replay failed mid-flight: invalidate the tracked server copy so the document is
							// reopened on the next synchronization instead of being treated as synchronized.
							if (!_isDisposed)
								InvalidateTrackedServerSynchronization(document.FilePath);

							throw;
						}

						return true;
					},
					cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is IOException or ObjectDisposedException)
			{
				return [.. documents.Skip(i)];
			}
		}

		return null;
	}

	/// <summary>
	/// Sends the open, change, or close notification for one synchronization request; a change for a session
	/// without a negotiated synchronization mode is skipped with a warning.
	/// </summary>
	/// <param name="request">The synchronization request to deliver.</param>
	/// <param name="cancellationToken">A token that can cancel the send.</param>
	/// <returns>
	/// <see langword="true"/> when a notification was delivered; otherwise, <see langword="false"/> (no client is
	/// configured, or a change cannot be expressed by the negotiated synchronization mode).
	/// </returns>
	/// <remarks>
	/// A session that negotiated <see cref="TextDocumentSyncKind.None"/> still receives the open and the close:
	/// the server holds the document so requests that do not depend on incremental synchronization can be served
	/// against it, and only the change is skipped because it cannot be expressed without a synchronization mode.
	/// </remarks>
	private async Task<bool> SendDocumentSynchronizationNotificationAsync(DocumentSynchronizationRequest request, CancellationToken cancellationToken)
	{
		if (_client is null)
			return false;

		if (request.Kind == DocumentSynchronizationKind.Close)
		{
			await _client.SendNotificationAsync(LspMethodNames.DidClose,
				new DidCloseTextDocumentParams(new TextDocumentIdentifier(request.Document.Uri)),
				cancellationToken).ConfigureAwait(false);

			return true;
		}

		if (request.Kind == DocumentSynchronizationKind.Open)
		{
			await _client.SendNotificationAsync(LspMethodNames.DidOpen,
				new DidOpenTextDocumentParams(
					new DidOpenTextDocumentPayload(
						request.Document.Uri,
						LanguageId,
						request.Document.Version,
						request.Document.Content)),
				cancellationToken).ConfigureAwait(false);

			return true;
		}

		if (request.Kind != DocumentSynchronizationKind.Change)
			return false;

		// A session without a negotiated synchronization mode cannot express the change and may also report
		// None after its active transport was detached. Skip the change with a warning instead of failing the
		// operation: the tracked content stays committed, nothing is sent while the mode is unavailable, and
		// the next session re-establishes the full content through the restart replay.
		if (_client.TextDocumentSyncKind == TextDocumentSyncKind.None)
		{
			long transportGeneration = _client.TransportGeneration;

			// Log the first skip on a transport generation at warning level and later skips at debug, so a session
			// that negotiated no synchronization mode does not emit one warning per keystroke.
			if (Interlocked.Exchange(ref _changeSkipWarningTransportGeneration, transportGeneration) != transportGeneration)
			{
				_logger.LogWarning("{DisplayName} skipped a document change for '{FilePath}' because the language server session negotiated no document synchronization mode.",
					ProviderDisplayName, request.Document.FilePath);
			}
			else
			{
				_logger.LogDebug("{DisplayName} skipped a document change for '{FilePath}' because the language server session negotiated no document synchronization mode.",
					ProviderDisplayName, request.Document.FilePath);
			}

			return false;
		}

		TextDocumentContentChangePayload contentChange = _client.TextDocumentSyncKind switch
		{
			TextDocumentSyncKind.Incremental when request.ChangeRange is { } changeRange => new(
				changeRange.Text,
				new ProtocolRangePayload(
					new ProtocolPosition(changeRange.StartLine, changeRange.StartCharacter),
					new ProtocolPosition(changeRange.EndLine, changeRange.EndCharacter))),

			// Full synchronization, and incremental requests without a computed range, send the whole content.
			_ => new(request.Document.Content)
		};

		await _client.SendNotificationAsync(LspMethodNames.DidChange,
			new DidChangeTextDocumentParams(
				new VersionedTextDocumentIdentifier(request.Document.Uri, request.Document.Version),
				[contentChange]),
			cancellationToken).ConfigureAwait(false);

		return true;
	}

	/// <summary>
	/// Closes one document through its per-document scheduler slot: releases the last open reference, defers the
	/// close while a request reference is still active, and forwards <c>textDocument/didClose</c> when a server
	/// session is running.
	/// </summary>
	/// <param name="filePath">The normalized document path to close.</param>
	/// <param name="cancellationToken">A token that can cancel the close operation.</param>
	private async Task CloseDocumentAsync(string filePath, CancellationToken cancellationToken)
	{
		if (_client is null)
			return;

		try
		{
			await _documentScheduler.EnqueuePerDocumentAsync(
				filePath,
				async token =>
				{
					DocumentCloseOutcome closeResult = _documents.TryClose(filePath, out DocumentSnapshot? document);

					if (closeResult == DocumentCloseOutcome.BusyWithRequests)
					{
						// The close released the last open reference, but a temporary request reference still keeps the
						// document tracked. Remember the intent so the release path completes the close instead of leaving
						// the server copy open indefinitely.
						_pendingDocumentCloses[filePath] = 0;
						return false;
					}

					_pendingDocumentCloses.TryRemove(filePath, out _);

					if (closeResult != DocumentCloseOutcome.Closed)
						return false;

					// The document just dropped its last open reference; clear language-specific in-flight
					// work for it now that no consumer path will display the result.
					InvokeContainedHook(() => OnTrackedDocumentInvalidated(filePath), "tracked-document invalidation");

					// Forward the close only while a server session is running (see CloseDocument): starting the server
					// just to send didClose would be wasteful and can race with disposal.
					if (document is null || !_client.IsReady)
						return false;

					return await SendDocumentSynchronizationNotificationAsync(
						new DocumentSynchronizationRequest(DocumentSynchronizationKind.Close, document),
						token).ConfigureAwait(false);
				},
				cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _isDisposed)
		{
			_logger.LogDebug("{DisplayName} best-effort document close for '{FilePath}' was canceled because the request token fired or the provider was disposed.",
				ProviderDisplayName, filePath);
		}
		catch (Exception exception)
		{
			LogBestEffortFailure("document close", filePath, exception);
		}
	}

	private void HandleDiagnosticsPublished(object? sender, DiagnosticsPublishedEventArgs eventArgs)
	{
		if (_isDisposed)
			return;

		PublishDiagnosticsParams parameters = eventArgs.Parameters;

		if (!LanguageServerPaths.TryGetLocalPath(parameters.Uri, out string filePath))
		{
			_logger.LogDebug("{DisplayName} diagnostics for URI '{Uri}' could not be matched to a local file path.",
				ProviderDisplayName, parameters.Uri);
			return;
		}

		DocumentSnapshot? document = _documents.GetDocumentSnapshot(filePath);

		// Diagnostics for documents that are not tracked locally are ignored: without a tracked snapshot, there is no
		// synchronized content to map them to document ranges, and reading the file from disk on the LSP read loop just to
		// discard the result is wasteful.
		if (document is null)
			return;

		IReadOnlyList<TextDiagnostic>? diagnostics = InvokeContainedHook<IReadOnlyList<TextDiagnostic>?>(
			() => HandleDiagnosticsPayload(filePath, parameters, document),
			fallbackValue: null,
			"diagnostics payload handling");

		if (_isDisposed || diagnostics is null)
			return;

		RaiseDiagnosticsUpdated(filePath, diagnostics);
	}

	private void CancelQueuedDocumentUpdate(string filePath)
		=> _documentScheduler.CancelQueuedUpdate(filePath);

	private void CancelAllQueuedDocumentUpdates()
		=> _documentScheduler.CancelAllQueuedUpdates();

	/// <summary>
	/// Describes the result of one document synchronization.
	/// </summary>
	/// <param name="Succeeded">Whether the synchronization completed successfully.</param>
	/// <param name="Document">The synchronized document snapshot, when one remains tracked.</param>
	/// <param name="NotificationDelivered">Whether an <c>Open</c> or <c>Change</c> notification was sent to the server.</param>
	/// <param name="SkippedWhileUnavailable">
	/// Whether the not-ready path declined to apply the operation at all: nothing was committed locally and nothing
	/// was sent, so an entry point that owes the host a guaranteed effect can retry it once the startup flow settles.
	/// </param>
	private readonly record struct DocumentSynchronizationResult(bool Succeeded, DocumentSnapshot? Document, bool NotificationDelivered, bool SkippedWhileUnavailable = false);

	/// <summary>
	/// Describes how one document synchronization acquires references and whether it refreshes language-specific
	/// state afterwards.
	/// </summary>
	/// <param name="References">The references the synchronization acquires for the document.</param>
	/// <param name="RefreshDocument">Whether the language-specific state should be refreshed after a successful synchronization.</param>
	private readonly record struct DocumentSynchronizationMode(
		DocumentReferenceAcquisition References,
		bool RefreshDocument)
	{
		/// <summary>
		/// Gets a value indicating whether the synchronization acquires an open-document reference.
		/// </summary>
		internal bool AcquiresOpenReference => (References & DocumentReferenceAcquisition.Open) != 0;

		/// <summary>An explicit document open: acquires an open reference and refreshes language-specific state.</summary>
		internal static DocumentSynchronizationMode Open { get; } = new(DocumentReferenceAcquisition.Open, RefreshDocument: true);

		/// <summary>A coalesced content update: keeps the existing references and refreshes language-specific state.</summary>
		internal static DocumentSynchronizationMode Update { get; } = new(DocumentReferenceAcquisition.None, RefreshDocument: true);

		/// <summary>A request-driven synchronization: acquires only a temporary request reference.</summary>
		internal static DocumentSynchronizationMode Request { get; } = new(DocumentReferenceAcquisition.Request, RefreshDocument: false);
	}
}
