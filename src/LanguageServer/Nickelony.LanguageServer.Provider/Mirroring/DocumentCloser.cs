namespace Nickelony.LanguageServer.Provider;

/// <summary>Closes the server copy of a document once no lease keeps it open, deferring the close while a request lease keeps it tracked.</summary>
/// <remarks>
/// <para>
/// Every close runs in the document's scheduler slot, so it never overlaps a synchronization of the same document. A close sends
/// <c>textDocument/didClose</c> only while the client is ready and never starts the connection.
/// </para>
/// <para>
/// A close that releases the last open lease while a request lease remains is recorded in the <see cref="PendingCloseTracker"/>.
/// <see cref="DocumentRequestReleaseRunner"/> completes it when the request lease is released, and its completion claims the record:
/// it closes when no request lease remains, and otherwise marks the close pending again, so the deferred close completes once.
/// </para>
/// </remarks>
internal sealed class DocumentCloser
{
	private readonly ProviderContext _context;
	private readonly ILanguageServerClient? _client;
	private readonly DocumentTracker _documents;
	private readonly DocumentOperationScheduler _scheduler;
	private readonly PendingCloseTracker _pendingCloses;
	private readonly DocumentNotificationSender _notificationSender;
	private readonly DocumentSignals _signals;

	/// <param name="context">The client, the tracked documents, the options and the logger the closes run against.</param>
	/// <param name="scheduler">The scheduler that serializes the operations of one document.</param>
	/// <param name="pendingCloses">The tracker of the closes deferred while a request lease kept their document tracked.</param>
	/// <param name="notificationSender">The sender of the <c>textDocument/didClose</c> notifications.</param>
	/// <param name="signals">The reactions of the features that consume what happened to a document.</param>
	internal DocumentCloser(
		ProviderContext context,
		DocumentOperationScheduler scheduler,
		PendingCloseTracker pendingCloses,
		DocumentNotificationSender notificationSender,
		DocumentSignals signals
	)
	{
		_context = context;
		_client = context.Client;
		_documents = context.Documents;
		_scheduler = scheduler;
		_pendingCloses = pendingCloses;
		_notificationSender = notificationSender;
		_signals = signals;
	}

	/// <summary>
	/// Closes one document through its per-document scheduler slot: releases the last open lease, defers the close while a request lease
	/// is still active, and forwards <c>textDocument/didClose</c> when a server session is running.
	/// </summary>
	/// <param name="filePath">The normalized document path to close.</param>
	/// <param name="cancellationToken">A token that can cancel the close operation.</param>
	internal async Task CloseDocumentAsync(string filePath, CancellationToken cancellationToken)
	{
		if (_client is null)
			return;

		try
		{
			await _scheduler
				.EnqueuePerDocumentAsync(
					filePath,

					async token =>
					{
						DocumentCloseResult closeResult = _documents.Close(filePath);

						if (closeResult.Outcome == DocumentCloseOutcome.BusyWithRequests)
						{
							// The close released the last open lease, but a temporary request lease still keeps the
							// document tracked. Remember the intent so the release path completes the close instead of leaving
							// the server copy open indefinitely.
							_pendingCloses.MarkPending(filePath);
							return false;
						}

						_pendingCloses.Cancel(filePath);

						if (closeResult.Outcome != DocumentCloseOutcome.Closed)
							return false;

						// The document dropped its last open lease; clear language-specific in-flight
						// work for it, because no consumer path will display the result.
						_signals.Invalidated(filePath);

						// Forward the close only while a server session is running (see CloseDocument): starting the server
						// only to send didClose would be wasteful and can race with disposal.
						if (closeResult.ClosingDocument is not { } document || !_client.IsReady)
							return false;

						return await _notificationSender
							.SendAsync(new DocumentSynchronizationDirective(DocumentSynchronizationKind.Close, document), token)
							.ConfigureAwait(false);
					},

					cancellationToken
				)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _context.Disposal.IsDisposed)
		{
			_context.Logger.LogDebug(
				"{DisplayName} best-effort document close for '{FilePath}' was canceled because the caller canceled it or the provider was disposed.",
				_context.ProviderDisplayName,
				filePath
			);
		}
		catch (Exception exception)
		{
			_context.LogBestEffortFailure("document close", filePath, exception);
		}
	}

	/// <summary>Closes the server copy of one document evicted by idle-document trimming.</summary>
	/// <param name="document">The snapshot evicted by the document tracker.</param>
	/// <remarks>
	/// The close runs through the evicted document's own scheduler slot and re-checks the tracked state first, so a record that was recreated
	/// after the trim keeps its server copy open.
	/// </remarks>
	internal async Task CloseTrimmedDocumentAsync(DocumentSnapshot document)
	{
		if (_client is null || _context.Disposal.IsDisposed)
			return;

		try
		{
			await _scheduler
				.EnqueuePerDocumentAsync(
					document.FilePath,

					async token =>
					{
						// A concurrent open or update recreated the record after the trim; its own lifecycle owns the
						// server copy now, so an unguarded close here would close a document the client considers
						// open again.
						if (_documents.IsTracked(document.FilePath))
							return false;

						_pendingCloses.Cancel(document.FilePath);
						_signals.Invalidated(document.FilePath);

						if (!_client.IsReady)
							return false;

						return await _notificationSender
							.SendAsync(new DocumentSynchronizationDirective(DocumentSynchronizationKind.Close, document), CancellationToken.None)
							.ConfigureAwait(false);
					},

					CancellationToken.None
				)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (_context.Disposal.IsDisposed) { }
		catch (Exception exception)
		{
			_context.LogBestEffortFailure("trimmed-document close", document.FilePath, exception);
		}
	}

	/// <summary>Completes a close that was deferred while a temporary request lease kept the document tracked.</summary>
	/// <param name="filePath">The normalized document path whose deferred close should be completed.</param>
	/// <remarks>
	/// The caller must hold the document's scheduler slot.
	/// </remarks>
	internal async Task CompletePendingDocumentCloseAsync(string filePath)
	{
		if (!_pendingCloses.TryClaim(filePath))
			return;

		DocumentCloseResult closeResult = _documents.Close(filePath);

		if (closeResult.Outcome == DocumentCloseOutcome.BusyWithRequests)
		{
			// Another request lease is still active; keep the close pending for its release.
			_pendingCloses.MarkPending(filePath);
			return;
		}

		if (closeResult.Outcome != DocumentCloseOutcome.Closed || closeResult.ClosingDocument is not { } document)
			return;

		_signals.Invalidated(filePath);

		// The startup-succeeded flag is not consulted here either (see CloseDocument): a
		// deferred close must still run while a restart replay owns the server session.
		if (_client is null || !_client.IsReady)
			return;

		try
		{
			await _notificationSender
				.SendAsync(new DocumentSynchronizationDirective(DocumentSynchronizationKind.Close, document), CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			_context.LogBestEffortFailure("deferred document close", filePath, exception);
		}
	}
}
