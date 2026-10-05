using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Mirrors the host's documents to the language server through one scheduler slot per document.</summary>
/// <remarks>
/// <para>
/// Every operation on one document runs in that document's scheduler slot, so the tracked record, the server copy and the temporary request leases
/// never see two operations at once. A slot never starts the connection: each entry point ensures it before the slot runs and the slot only observes
/// the readiness snapshot, because a restart replays the tracked documents through the same scheduler.
/// </para>
/// <para>
/// Each entry point reserves its place among the calls on the document when the host makes the call, and only then waits
/// for the connection, so the calls reach the document's slot in the order the host made them, whichever order their waits end in. A call completes
/// its reservation on every exit path: an update when it has joined the scheduler, so a later update can still supersede it, and the other calls
/// when they have finished.
/// </para>
/// <para>
/// The synchronizer announces what happened to a document through <see cref="DocumentSignals"/> and knows nothing of the features
/// that consume the signals: the order in which they react belongs to the provider that composes them.
/// </para>
/// </remarks>
internal sealed class DocumentSynchronizer
{
	// The number of extra attempts an open makes while a startup or restart-replay flow settles.
	private const int OpenDeferralRetryLimit = 2;

	private readonly ProviderContext _context;
	private readonly ILanguageServerClient? _client;
	private readonly DocumentTracker _documents;
	private readonly DocumentOperationScheduler _scheduler;
	private readonly PendingCloseTracker _pendingCloses;
	private readonly ProviderLifecycleCoordinator _lifecycle;
	private readonly DocumentNotificationSender _notificationSender;
	private readonly DocumentCloser _closer;
	private readonly DocumentRenameCoordinator _renamer;
	private readonly DocumentSignals _signals;

	/// <param name="context">The collaborators the synchronizer runs against.</param>
	internal DocumentSynchronizer(DocumentSynchronizerContext context)
	{
		_context = context.Provider;
		_client = _context.Client;
		_documents = _context.Documents;
		_scheduler = context.Scheduler;
		_pendingCloses = context.PendingCloses;
		_lifecycle = context.Lifecycle;
		_notificationSender = context.NotificationSender;
		_closer = context.Closer;
		_renamer = context.Renamer;
		_signals = context.Signals;
	}

	/// <summary>Opens a document: cancels its pending update, then ensures the connection and synchronizes the open in the background.</summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The initial document content.</param>
	/// <param name="documentId">
	/// The id the host opened the document with, which the tracked record takes,
	/// or <see langword="null"/> when it equals <paramref name="filePath"/>.
	/// </param>
	internal void Open(string filePath, string content, string? documentId = null)
	{
		_scheduler.CancelLatestUpdate(filePath);

		CallOrderTracker.CallReservation reservation = _scheduler.ReserveCall(filePath);
		_context.ObserveBackgroundTask(OpenDocumentAsync(reservation, documentId, filePath, content), $"Document open '{filePath}'");
	}

	/// <summary>Synchronizes the latest content of a document in the background, coalescing it with the updates still pending for the path.</summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The latest content of the document.</param>
	/// <param name="documentId">
	/// The id the host supplied, which a record this update creates takes, or <see langword="null"/> when it equals <paramref name="filePath"/>.
	/// </param>
	internal void Update(string filePath, string content, string? documentId = null)
	{
		CallOrderTracker.CallReservation reservation = _scheduler.ReserveCall(filePath);
		_context.ObserveBackgroundTask(UpdateDocumentAsync(reservation, documentId, filePath, content), $"Document change '{filePath}'");
	}

	/// <summary>Closes a document: cancels its pending update, then releases one open lease in the background.</summary>
	/// <param name="filePath">The normalized document path.</param>
	internal void Close(string filePath)
	{
		_scheduler.CancelLatestUpdate(filePath);

		CallOrderTracker.CallReservation reservation = _scheduler.ReserveCall(filePath);
		_context.ObserveBackgroundTask(CloseDocumentAsync(reservation, filePath), $"Document close '{filePath}'");
	}

	/// <summary>Renames a tracked document to a new path in the background.</summary>
	/// <param name="oldFilePath">The normalized path of the tracked document.</param>
	/// <param name="newFilePath">The normalized replacement path.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="newDocumentId">
	/// The id the host renamed the document to, which the renamed record takes,
	/// or <see langword="null"/> when it equals <paramref name="newFilePath"/>.
	/// </param>
	internal void Rename(string oldFilePath, string newFilePath, string content, string? newDocumentId = null)
	{
		CallOrderTracker.CallReservation reservation = _scheduler.ReserveCall(oldFilePath, newFilePath);

		_context.ObserveBackgroundTask(
			RenameDocumentAsync(reservation, oldFilePath, newFilePath, content, newDocumentId),
			$"Document rename '{oldFilePath}' to '{newFilePath}'"
		);
	}

	/// <summary>Synchronizes the document a request is about to ask the server for, acquiring the request's temporary lease.</summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The document content the request carries.</param>
	/// <param name="requestLease">The lease that receives the request lease acquired for this synchronization.</param>
	/// <param name="cancellationToken">A token that can cancel the synchronization.</param>
	/// <param name="documentId">
	/// The id the request carries, which a record this synchronization creates takes,
	/// or <see langword="null"/> when it equals <paramref name="filePath"/>.
	/// </param>
	/// <returns><see langword="true"/> when the tracked state is up to date for the request; otherwise, <see langword="false"/>.</returns>
	internal async Task<bool> SynchronizeForRequestAsync(
		string filePath,
		string content,
		DocumentRequestLease requestLease,
		CancellationToken cancellationToken,
		string? documentId = null
	)
	{
		DocumentSynchronizationResult result = await SynchronizeDocumentAsync(
				documentId,
				filePath,
				content,
				mode: DocumentSynchronizationMode.Request,
				requestLease,
				cancellationToken
			)
			.ConfigureAwait(false);

		return result.Succeeded;
	}

	/// <summary>Ensures the connection outside the document's scheduler slot and then opens the document.</summary>
	/// <param name="reservation">The place of the call among the calls on the document, which this method completes on every exit path.</param>
	/// <param name="documentId">The id the host supplied, or <see langword="null"/> when it equals <paramref name="filePath"/>.</param>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The initial document content.</param>
	/// <remarks>
	/// <para>
	/// The connection must be ensured before a slot runs: a restart replay queues per-document scheduler operations,
	/// and queueing them from inside a slot is rejected by the scheduler and can deadlock the slot's own lane while it waits on the startup lock.
	/// A slot therefore never starts the connection itself; it only observes <see cref="ProviderLifecycleCoordinator.IsConnectionReady"/>
	/// after this flow returns.
	/// </para>
	/// <para>
	/// Unlike an update, an open cannot be re-established by a later operation that supplies content:
	/// the open lease the host acquired must not be dropped. When the open's slot ran while a startup or a restart replay owned the tracked records,
	/// or while the provider had cleared its startup flag but the client was still ready, this flow retries the open while a new startup settles,
	/// up to <see cref="OpenDeferralRetryLimit"/> retries. A retry whose start fails records the open locally for the next replay unless the client
	/// is ready or a replay is pending, and a connection that keeps failing leaves the open unapplied like every other failed-startup operation.
	/// </para>
	/// </remarks>
	private async Task OpenDocumentAsync(CallOrderTracker.CallReservation reservation, string? documentId, string filePath, string content)
	{
		try
		{
			await _lifecycle.TryEnsureStartedForOperationAsync(CancellationToken.None).ConfigureAwait(false);

			if (!await WaitForTurnAsync(reservation).ConfigureAwait(false))
				return;

			DocumentSynchronizationResult result = await SynchronizeDocumentAsync(
					documentId,
					filePath,
					content,
					mode: DocumentSynchronizationMode.Open,
					requestLease: null,
					cancellationToken: CancellationToken.None
				)
				.ConfigureAwait(false);

			// The reservation stays until the last attempt, so a call the host made after this one cannot join the lane between two attempts.
			for (int attempt = 0; attempt < OpenDeferralRetryLimit && result.Outcome == DocumentMirrorOutcome.SkippedWhileUnavailable; attempt++)
			{
				bool started = await _lifecycle.TryEnsureStartedForOperationAsync(CancellationToken.None).ConfigureAwait(false);

				// A failed start still lets the open record itself locally unless the client is ready or a replay is pending: the
				// start that made the previous attempt skip has settled, and the next replay carries the record.
				result = await SynchronizeDocumentAsync(
						documentId,
						filePath,
						content,
						mode: DocumentSynchronizationMode.Open,
						requestLease: null,
						cancellationToken: CancellationToken.None
					)
					.ConfigureAwait(false);

				if (!started)
					break;
			}
		}
		finally
		{
			reservation.Complete();
		}
	}

	/// <summary>Ensures the connection outside the document's scheduler slot and then synchronizes the latest update.</summary>
	/// <param name="reservation">The place of the call among the calls on the document, which this method completes on every exit path.</param>
	/// <param name="documentId">The id the host supplied, or <see langword="null"/> when it equals <paramref name="filePath"/>.</param>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The latest content of the document.</param>
	/// <remarks>
	/// See <see cref="OpenDocumentAsync(CallOrderTracker.CallReservation, string?, string, string)"/> for why the connection is ensured
	/// outside the slot.
	/// </remarks>
	private async Task UpdateDocumentAsync(CallOrderTracker.CallReservation reservation, string? documentId, string filePath, string content)
	{
		try
		{
			await _lifecycle.TryEnsureStartedForOperationAsync(CancellationToken.None).ConfigureAwait(false);

			if (!await WaitForTurnAsync(reservation).ConfigureAwait(false))
				return;

			await SynchronizeLatestDocumentAsync(reservation, documentId, filePath, content).ConfigureAwait(false);
		}
		finally
		{
			reservation.Complete();
		}
	}

	/// <summary>Closes the document once the calls the host made before the close have joined the scheduler.</summary>
	/// <param name="reservation">The place of the call among the calls on the document, which this method completes on every exit path.</param>
	/// <param name="filePath">The normalized document path.</param>
	private async Task CloseDocumentAsync(CallOrderTracker.CallReservation reservation, string filePath)
	{
		try
		{
			if (!await WaitForTurnAsync(reservation).ConfigureAwait(false))
				return;

			await _closer.CloseDocumentAsync(filePath, CancellationToken.None).ConfigureAwait(false);
		}
		finally
		{
			reservation.Complete();
		}
	}

	/// <summary>Renames the document once the calls the host made before the rename have joined the scheduler.</summary>
	/// <param name="reservation">The place of the call among the calls on both paths, which this method completes on every exit path.</param>
	/// <param name="oldFilePath">The normalized path of the tracked document.</param>
	/// <param name="newFilePath">The normalized replacement path.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="newDocumentId">
	/// The id the host renamed the document to, or <see langword="null"/> when it equals <paramref name="newFilePath"/>.
	/// </param>
	private async Task RenameDocumentAsync(
		CallOrderTracker.CallReservation reservation,
		string oldFilePath,
		string newFilePath,
		string content,
		string? newDocumentId
	)
	{
		try
		{
			if (!await WaitForTurnAsync(reservation).ConfigureAwait(false))
				return;

			await _renamer
				.RenameAsync(oldFilePath, newFilePath, content, cancellationToken: CancellationToken.None, newDocumentId)
				.ConfigureAwait(false);
		}
		finally
		{
			reservation.Complete();
		}
	}

	/// <summary>Waits until every call the host made before this one has joined the scheduler.</summary>
	/// <param name="reservation">The place of the call among the calls on the document.</param>
	/// <returns>
	/// <see langword="true"/> when the call may join the scheduler; otherwise, <see langword="false"/> because the provider is disposing.
	/// </returns>
	/// <remarks>
	/// The wait is already complete when no earlier call is pending, so the call joins the scheduler before the host's call returns.
	/// Disposal ends the wait, so a call behind a stalled one never outlives the provider.
	/// </remarks>
	private async Task<bool> WaitForTurnAsync(CallOrderTracker.CallReservation reservation)
	{
		try
		{
			await reservation.WaitForTurnAsync(_context.Disposal.Token).ConfigureAwait(false);
			return true;
		}
		catch (OperationCanceledException) when (_context.Disposal.IsDisposed)
		{
			return false;
		}
	}

	/// <summary>Coalesces a document update into the latest-update registration and synchronizes it when the registration runs.</summary>
	/// <param name="reservation">
	/// The place of the update among the calls on the document, which completes when the update has joined the scheduler.
	/// </param>
	/// <param name="documentId">The id the host supplied, or <see langword="null"/> when it equals <paramref name="filePath"/>.</param>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The latest content of the document.</param>
	/// <returns>
	/// <see langword="true"/> when the scheduled update finished or was superseded, even if the synchronization was skipped because the connection
	/// was not ready; otherwise, <see langword="false"/> when the provider is disposed, no client is configured, or a connection failure interrupted
	/// the update.
	/// </returns>
	private async Task<bool> SynchronizeLatestDocumentAsync(
		CallOrderTracker.CallReservation reservation,
		string? documentId,
		string filePath,
		string content
	)
	{
		if (_context.Disposal.IsDisposed)
			return false;

		if (_client is null)
		{
			_lifecycle.ReportMissingClientFailure();
			return false;
		}

		try
		{
			// The latest-update node runs the synchronization as the scheduled operation itself. Queueing an inner
			// per-document operation from inside the delegate would deadlock the document's lane, so the in-slot
			// synchronization must not enqueue again.
			Task queued = _scheduler.EnqueueLatestUpdateAsync(
				filePath,

				token =>
					SynchronizeDocumentInSlotAsync(
						documentId,
						filePath,
						content,
						mode: DocumentSynchronizationMode.Update,
						requestLease: null,
						cancellationToken: token
					),

				reservation
			);

			// The enqueue has joined the scheduler once it returns, so the update that comes next may supersede this one while it waits for its turn
			// on the lane; completing the reservation later would run every update of a burst one after the other.
			reservation.Complete();

			await queued.ConfigureAwait(false);

			return true;
		}
		catch (OperationCanceledException) when (_context.Disposal.IsDisposed)
		{
			// Disposal-driven cancellation surfaces as the documented false result.
			return false;
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// The tracked synchronization was already invalidated by the send-site catch inside the operation;
			// invalidating again here would run after the lane slot released and could mark the document closed
			// after a queued operation already reopened it. A disposal race needs no unavailable transition, and
			// TrySetState ignores one anyway.
			if (!_context.Disposal.IsDisposed)
				_lifecycle.MarkConnectionUnavailable();

			return false;
		}
	}

	private async Task<DocumentSynchronizationResult> SynchronizeDocumentAsync(
		string? documentId,
		string filePath,
		string content,
		DocumentSynchronizationMode mode,
		DocumentRequestLease? requestLease,
		CancellationToken cancellationToken
	)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (_context.Disposal.IsDisposed)
			return new(DocumentMirrorOutcome.Failed, Document: null);

		if (_client is null)
		{
			_lifecycle.ReportMissingClientFailure();
			return new(DocumentMirrorOutcome.Failed, Document: null);
		}

		try
		{
			return await _scheduler
				.EnqueuePerDocumentAsync(
					filePath,
					token => SynchronizeDocumentInSlotAsync(documentId, filePath, content, mode, requestLease, cancellationToken: token),
					cancellationToken
				)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_context.Disposal.IsDisposed)
		{
			// Disposal-driven cancellation surfaces as the documented false result.
			return new(DocumentMirrorOutcome.Failed, Document: null);
		}
		catch (OperationCanceledException)
		{
			// An unowned cancellation (neither the caller token nor disposal) is classified like the runner
			// classifies it: the documented false result instead of a leaked cancellation.
			return new(DocumentMirrorOutcome.Failed, Document: null);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// Already invalidated by the send-site catch inside the per-document scheduler slot; see
			// SynchronizeLatestDocumentAsync. A disposal race needs no unavailable transition.
			if (!_context.Disposal.IsDisposed)
				_lifecycle.MarkConnectionUnavailable();

			return new(DocumentMirrorOutcome.Failed, Document: null);
		}
	}

	/// <summary>
	/// Synchronizes one document while its scheduler slot is already held and refreshes the language-specific state after a sent notification
	/// that the mode refreshes.
	/// </summary>
	/// <param name="documentId">The id the host supplied, or <see langword="null"/> when it equals <paramref name="filePath"/>.</param>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The content to synchronize.</param>
	/// <param name="mode">The lease acquisition and refresh behavior of this synchronization.</param>
	/// <param name="requestLease">
	/// The lease that receives the request lease acquired for this synchronization, or <see langword="null"/> when the synchronization acquires
	/// no request lease.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the synchronization.</param>
	/// <returns>The synchronization result: how far the synchronization got, and the tracked document when one remains.</returns>
	/// <remarks>
	/// The slot must not start or restart the connection; the entry points ensure it before the slot runs
	/// (see <see cref="OpenDocumentAsync(CallOrderTracker.CallReservation, string?, string, string)"/>).
	/// </remarks>
	private async Task<DocumentSynchronizationResult> SynchronizeDocumentInSlotAsync(
		string? documentId,
		string filePath,
		string content,
		DocumentSynchronizationMode mode,
		DocumentRequestLease? requestLease,
		CancellationToken cancellationToken
	)
	{
		DocumentSynchronizationResult result;

		try
		{
			result = await SynchronizeDocumentCoreAsync(documentId, filePath, content, mode, requestLease, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			// An open lease cancels a close that was deferred while a temporary request lease kept the
			// document tracked: the host holds the document open again, so the deferred close must not run later.
			// This runs even when the synchronization throws, because the host acquired the open lease by
			// requesting the open regardless of whether the synchronization succeeded.
			if (mode.AcquiresOpenLease)
				_pendingCloses.Cancel(filePath);
		}

		// A change the negotiated synchronization mode could not express does not advance the server's copy, so a
		// refresh computed from it would describe stale content (stale semantic tokens, for example). A request-driven
		// change refreshes too: the host's update with the same text finds nothing to send afterwards, so it would
		// refresh nothing, and the tokens the server computed for the earlier text would stay. A request-driven open
		// is the temporary open of a document the host does not show, so it refreshes nothing.
		if (
			result.Outcome == DocumentMirrorOutcome.SucceededAndNotified
			&& result.Document is { } synchronizedDocument
			&& (mode.RefreshesAfterOpen || result.NotifiedKind == DocumentSynchronizationKind.Change)
		)
		{
			_signals.Synchronized(synchronizedDocument);
		}

		return result;
	}

	/// <summary>Synchronizes one document and classifies the final synchronization result for the caller.</summary>
	/// <param name="documentId">The id the host supplied, or <see langword="null"/> when it equals <paramref name="filePath"/>.</param>
	/// <param name="filePath">The normalized document path.</param>
	/// <param name="content">The content to synchronize.</param>
	/// <param name="mode">The lease acquisition and refresh behavior of this synchronization.</param>
	/// <param name="requestLease">
	/// The lease that receives the request lease acquired for this synchronization, or <see langword="null"/> when the synchronization acquires
	/// no request lease.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the synchronization.</param>
	/// <returns>The synchronization result: how far the synchronization got, and the tracked document when one remains.</returns>
	/// <remarks>
	/// <para>
	/// When the connection is not ready, the content is committed locally only while the client is down and no restart replay owns
	/// the tracked records, so a local commit cannot reopen a record that the guarded replay then skips, and cannot claim a copy that a live session
	/// never received.
	/// </para>
	/// <para>
	/// A fault raised by the document tracker propagates unchanged: the tracker produces the record this synchronization mirrors,
	/// so a contained fault could not produce a truthful result and would also swallow the tracker's deliberate contract exceptions
	/// (an already-bound request lease, for example). The identity-bound request lease is still released on that path because the caller releases
	/// it by identity once it is bound.
	/// </para>
	/// </remarks>
	private async Task<DocumentSynchronizationResult> SynchronizeDocumentCoreAsync(
		string? documentId,
		string filePath,
		string content,
		DocumentSynchronizationMode mode,
		DocumentRequestLease? requestLease,
		CancellationToken cancellationToken
	)
	{
		// The path is already normalized (the provider normalizes it as it enters the request pipeline), so the
		// tracker's normalized-key fast path avoids a redundant Path.GetFullPath and a throwaway snapshot.
		bool shouldTrackLocallyWhileUnavailable = mode.AcquiresOpenLease || _documents.IsTrackedNormalized(filePath);
		bool includeChangeRange = _client?.NegotiatedCapabilities.TextDocumentSyncKind == TextDocumentSyncKind.Incremental;

		// A slot never starts or restarts the connection: the entry points ensure it before the slot runs (see
		// EnsureConnectionStartedAsync and OpenDocumentAsync), and the slot only observes the readiness snapshot.
		if (!_lifecycle.IsConnectionReady)
		{
			// A restart attempt or a pending replay owns the tracked records, and a commit that lands after the replay
			// snapshot would claim a server copy that was never sent. A ready client has no replay at all, because only a
			// start that finds the client down takes the snapshot. The lifecycle decides and commits under the startup lock
			// and skips the commit in those cases; the skip is reported so an entry point that owes the host a guaranteed
			// effect can retry. A terminally failed provider is skipped too, because it never retries.
			bool committedLocally =
				shouldTrackLocallyWhileUnavailable
				&& _lifecycle.TryCommitWhileUnavailable(() =>
					_documents.SynchronizeNormalized(
						normalizedFilePath: filePath,
						content,
						leases: mode.Leases & DocumentLeaseKinds.Open,
						includeChangeRange,
						requestLease: null,
						documentId
					)
				);

			// A start that finished before the commit attempt left the connection ready, so the document is mirrored below.
			if (committedLocally || !_lifecycle.IsConnectionReady)
				return new(committedLocally ? DocumentMirrorOutcome.Failed : DocumentMirrorOutcome.SkippedWhileUnavailable, Document: null);
		}

		// The tracker binds the lease while it holds its tracker lock, so the request pipeline releases it on every
		// exit path, including a failed send.
		DocumentSynchronizationDirective? request = _documents.SynchronizeNormalized(
			normalizedFilePath: filePath,
			content,
			leases: mode.Leases,
			includeChangeRange,
			requestLease,
			documentId
		);

		// A null request means the tracker already mirrors the document: nothing was sent and no refresh runs.
		if (request is not { } pendingRequest)
			return new(DocumentMirrorOutcome.Succeeded, Document: null);

		bool notificationDelivered;

		try
		{
			notificationDelivered = await _notificationSender.SendAsync(pendingRequest, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// A connection boundary or a disposal race invalidates the tracked server copy before the failure
			// propagates, so the next synchronization reopens the document instead of trusting an unsent change.
			if (!_context.Disposal.IsDisposed)
				TrackedSynchronization.Invalidate(_documents, _signals, filePath);

			throw;
		}
		catch (OperationCanceledException)
		{
			// The content and version were committed before the send, so a cancellation that aborts the send (a
			// superseding open/close cancels a running update, or the caller cancels a request) can leave the tracked
			// record claiming a server state that was never delivered. Invalidate so the next synchronization reopens
			// the document instead of computing incremental ranges against unsent content.
			if (!_context.Disposal.IsDisposed)
				TrackedSynchronization.Invalidate(_documents, _signals, filePath);

			throw;
		}

		return notificationDelivered
			? new(DocumentMirrorOutcome.SucceededAndNotified, pendingRequest.Document, pendingRequest.Kind)
			: new(DocumentMirrorOutcome.Succeeded, pendingRequest.Document);
	}

	/// <summary>Describes the result of one document synchronization.</summary>
	/// <param name="Outcome">How far the synchronization got.</param>
	/// <param name="Document">The synchronized document snapshot, when one remains tracked.</param>
	/// <param name="NotifiedKind">The notification sent to the server, or <see langword="null"/> when none was sent.</param>
	private readonly record struct DocumentSynchronizationResult(
		DocumentMirrorOutcome Outcome,
		DocumentSnapshot? Document,
		DocumentSynchronizationKind? NotifiedKind = null
	)
	{
		/// <summary>Whether the tracked state is up to date, whether or not a notification was needed.</summary>
		internal bool Succeeded => Outcome is DocumentMirrorOutcome.Succeeded or DocumentMirrorOutcome.SucceededAndNotified;
	}

	/// <summary>Classifies how far one document synchronization got in mirroring the document to the language server.</summary>
	private enum DocumentMirrorOutcome
	{
		/// <summary>The synchronization failed or was canceled, so nothing is guaranteed about the server's copy.</summary>
		Failed,

		/// <summary>
		/// The not-ready path declined to apply the operation at all: nothing was committed locally and nothing was sent, so an entry point that owes
		/// the host a guaranteed effect can retry it once the startup flow settles.
		/// </summary>
		SkippedWhileUnavailable,

		/// <summary>
		/// The tracked state is up to date and no notification was sent, either because the server already mirrors it or because the negotiated mode
		/// could not express the change.
		/// </summary>
		Succeeded,

		/// <summary>The tracked state is up to date and an <c>Open</c> or <c>Change</c> notification was sent to the server.</summary>
		SucceededAndNotified,
	}

	/// <summary>Describes how one document synchronization acquires leases and whether an open it sends refreshes language-specific state.</summary>
	/// <param name="Leases">The leases the synchronization acquires for the document.</param>
	/// <param name="RefreshesAfterOpen">Whether a sent open refreshes language-specific state; a sent change always does.</param>
	private readonly record struct DocumentSynchronizationMode(DocumentLeaseKinds Leases, bool RefreshesAfterOpen)
	{
		internal bool AcquiresOpenLease => (Leases & DocumentLeaseKinds.Open) != 0;

		/// <summary>An explicit document open: acquires an open lease and refreshes language-specific state.</summary>
		internal static DocumentSynchronizationMode Open { get; } = new(DocumentLeaseKinds.Open, RefreshesAfterOpen: true);

		/// <summary>A coalesced content update: keeps the existing leases and refreshes language-specific state.</summary>
		internal static DocumentSynchronizationMode Update { get; } = new(DocumentLeaseKinds.None, RefreshesAfterOpen: true);

		/// <summary>A request-driven synchronization: acquires only a temporary request lease and refreshes after a change only.</summary>
		internal static DocumentSynchronizationMode Request { get; } = new(DocumentLeaseKinds.Request, RefreshesAfterOpen: false);
	}
}
