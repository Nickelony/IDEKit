using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Runs the lifecycle of a provider's language-server connection.</summary>
/// <remarks>
/// <para>
/// One startup lock serializes startup and restart, so concurrent callers share one recovery flow. The startup lock is never disposed:
/// the semaphore allocates a kernel handle only when its wait handle is requested, so skipping disposal avoids racing in-flight startup leases
/// that may still release it. The coordinator is therefore not <see cref="IDisposable"/>.
/// </para>
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1001:Types that own disposable fields should be disposable",
	Justification = "The startup lock is never disposed on purpose; see the remarks."
)]
internal sealed class ProviderLifecycleCoordinator
{
	private readonly ILanguageServerClient? _client;
	private readonly StartupState _startupState;
	private readonly ProviderLifetime _disposal;
	private readonly ProviderEvents _events;
	private readonly StartupFailureReporter _failureReporter;
	private readonly DocumentTracker _documents;
	private readonly LanguageServerFeatureProviderOptions _options;
	private readonly LanguageServerFeatureProviderTestHooks _testHooks;

	private readonly string _workspaceRootsDisplayText;

	private readonly string _providerDisplayName;
	private readonly RequestTimeoutCounter _timeouts;

	private readonly ILogger _logger;

	private readonly SemaphoreSlim _startLock = new(1, 1);

	// Snapshots whose post-restart reopen did not run to completion. Written and read under the startup lock; the
	// lock-free view in PendingReopenDocuments is for tests, so the field is volatile.
	private volatile IReadOnlyList<DocumentSnapshot>? _pendingReopenDocuments;

	// The collaborators that bring state back after a start. They depend on this coordinator, so the composition attaches them once, after it
	// built them and before any caller can start the provider.
	private IDocumentRestartReplay? _replay;
	private IWorkspaceWatchRestoration? _watchRestoration;

	/// <param name="context">The collaborators the coordinator runs against.</param>
	internal ProviderLifecycleCoordinator(ProviderLifecycleContext context)
	{
		_client = context.Provider.Client;
		_startupState = context.StartupState;
		_disposal = context.Provider.Disposal;
		_events = context.Events;
		_failureReporter = context.FailureReporter;
		_documents = context.Provider.Documents;
		_options = context.Provider.Options;
		_testHooks = context.TestHooks;
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_providerDisplayName = context.Provider.ProviderDisplayName;
		_timeouts = context.Timeouts;
		_logger = context.Provider.Logger;
	}

	/// <summary>The provider's lifecycle state.</summary>
	/// <remarks>
	/// The host's readiness decision is the provider's availability; the state serves the provider's own transitions and its tests.
	/// </remarks>
	internal LanguageServerFeatureProviderState State => _startupState.State;

	private IDocumentRestartReplay Replay => _replay ?? throw new InvalidOperationException("The restart participants are not attached.");

	private IWorkspaceWatchRestoration WatchRestoration =>
		_watchRestoration ?? throw new InvalidOperationException("The restart participants are not attached.");

	/// <summary>Attaches the collaborators that bring state back after a start succeeded.</summary>
	/// <param name="replay">Reopens the documents that a restart captured.</param>
	/// <param name="watchRestoration">Brings workspace watching back.</param>
	/// <remarks>
	/// Both collaborators call back into this coordinator, so the composition builds them after it and attaches them before the provider
	/// is handed out.
	/// </remarks>
	internal void Attach(IDocumentRestartReplay replay, IWorkspaceWatchRestoration watchRestoration)
	{
		_replay = replay;
		_watchRestoration = watchRestoration;
	}

	/// <summary>Indicates whether the connection is ready to carry a document operation, as a lock-free snapshot.</summary>
	/// <remarks>
	/// The connection is ready when a client is present and ready, its most recent startup succeeded on the current connection generation,
	/// and the provider is not disposed. This is the one readiness rule: the provider's availability adds the state check on top,
	/// and the per-document scheduler slots observe this snapshot instead of starting the connection themselves. The conjunction deliberately repeats
	/// conditions that look implied by the provider state, because <see cref="MarkConnectionUnavailable"/> invalidates the startup-succeeded flag
	/// before the state transition runs, so availability must not be reported during that transient window.
	/// </remarks>
	internal bool IsConnectionReady => !_disposal.IsDisposed && _client is not null && _client.IsReady && _startupState.StartupSucceeded;

	/// <summary>The snapshots a restart replay still owes, or <see langword="null"/> when no replay is pending.</summary>
	/// <remarks>
	/// The read takes no lock, so it is a snapshot for tests; the local-commit decision reads the field under the startup lock.
	/// </remarks>
	internal IReadOnlyList<DocumentSnapshot>? PendingReopenDocuments => _pendingReopenDocuments;

	/// <summary>
	/// Ensures the language-server connection is running and that tracked documents and workspace watching are restored after reconnects.
	/// </summary>
	/// <param name="cancellationToken">A token that can cancel the startup, unless a restart shields it.</param>
	/// <returns><see langword="true"/> when the connection is ready; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Run this outside a document scheduler slot: a restart replays tracked documents by queueing per-document scheduler operations,
	/// and queueing scheduler work from inside a slot is rejected by the scheduler and would deadlock the slot's own lane while it waits
	/// on the startup lock. Document slots observe the connection readiness snapshot instead of starting the connection themselves. A caller writes
	/// provider state only for an attempt it owns, which begins once the caller holds the startup lock and finds no completed start to join.
	/// A caller canceled earlier writes nothing, so a start that completed while it waited stays ready. A canceled owner marks the provider
	/// unavailable unless its own start had already completed.
	/// </remarks>
	internal async Task<bool> EnsureConnectionStartedAsync(CancellationToken cancellationToken)
	{
		if (_disposal.IsDisposed || _client is not { } client)
			return false;

		if (TryLatchHardFailure())
			return false;

		using var fastPathDisposeAwareCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposal.Token);

		// Fast path: once the client is healthy, keep the workspace watcher alive and avoid taking the startup lock.
		if (await TryReuseHealthyConnectionAsync(client, fastPathDisposeAwareCts.Token).ConfigureAwait(false))
			return true;

		CancellationToken startupCancellationToken = ResolveStartupCancellationToken(client, cancellationToken);

		using var disposeAwareStartupCts = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken, _disposal.Token);

		CancellationToken effectiveStartupCancellationToken = disposeAwareStartupCts.Token;

		bool startLockHeld = false;
		bool ownsAttempt = false;

		try
		{
			// Observation seam (unset in production): fires as this caller reaches the startup lock.
			_testHooks.StartupLockWaitObserver?.Invoke();

			// Serialize startup and restart work so concurrent callers share one recovery flow.
			await _startLock.WaitAsync(effectiveStartupCancellationToken).ConfigureAwait(false);
			startLockHeld = true;

			// The connection generation the reopen decision below is made on. A concurrent connection invalidation can
			// make EnsureStartedAsync replace the session; RecaptureReopenList compares this generation with the started one.
			long observedConnectionGeneration = client.ConnectionGeneration;

			// Re-check state after taking the lock so concurrent callers share the same restart/startup work.
			if (TryLatchHardFailure())
				return false;

			if (await TryJoinCompletedStartAsync(client, effectiveStartupCancellationToken).ConfigureAwait(false))
				return true;

			// The caller owns an attempt from here on, and only an owner writes provider state for it.
			ownsAttempt = true;
			SetProviderState(LanguageServerFeatureProviderState.Starting);

			IReadOnlyList<DocumentSnapshot> documentsToReopen = PrepareReopen(client, out bool isReplayListPartial);

			// Start the client, replay the documents that were tracked before the start, then settle the start.
			(bool startupSucceeded, long startedConnectionGeneration) = await EnsureClientStartedAsync(client, effectiveStartupCancellationToken)
				.ConfigureAwait(false);

			if (startupSucceeded)
			{
				documentsToReopen = RecaptureReopenList(
					documentsToReopen,
					isReplayListPartial,
					observedConnectionGeneration,
					startedConnectionGeneration
				);

				startupSucceeded = await ReplayTrackedDocumentsAsync(documentsToReopen, effectiveStartupCancellationToken).ConfigureAwait(false);
			}

			return await SettleStartAsync(startupSucceeded, startedConnectionGeneration, effectiveStartupCancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (_disposal.IsDisposed)
		{
			return false;
		}
		catch (OperationCanceledException)
		{
			// A waiter canceled at the lock, or while it joined a completed start, owns no attempt and leaves the state alone, so the
			// start it queued behind stays ready. An owner leaves the state alone as well once its own start completed.
			if (ownsAttempt && !IsStartReady(client))
				SetProviderState(LanguageServerFeatureProviderState.Unavailable, notifyCapabilitiesChanged: true);

			throw;
		}
		catch (Exception exception)
		{
			// An unexpected startup exception counts like a false start result, so repeated failures still reach
			// the hard-failure latch instead of retrying forever; caller cancellation and disposal are handled
			// above, and a fault observed while disposing is not a startup failure.
			if (!_disposal.IsDisposed)
				RegisterFailedStartupAttempt(exception);

			throw;
		}
		finally
		{
			if (startLockHeld)
				_startLock.Release();
		}
	}

	/// <summary>
	/// Ensures the connection is running before an operation enters its per-document scheduler slot, containing the startup failure modes a slot
	/// cannot recover from.
	/// </summary>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns><see langword="true"/> when the connection is ready; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="OperationCanceledException">The caller's cancellation token was canceled.</exception>
	internal async Task<bool> TryEnsureStartedForOperationAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await EnsureConnectionStartedAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_disposal.IsDisposed)
		{
			return false;
		}
		catch (OperationCanceledException)
		{
			// An unowned cancellation (neither the caller token nor disposal) is classified like the runner
			// classifies it: the documented fallback value instead of a leaked cancellation.
			return false;
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// A disposal race needs no unavailable transition, and TrySetState ignores one anyway.
			if (!_disposal.IsDisposed)
				MarkConnectionUnavailable();

			return false;
		}
	}

	/// <summary>Records a document locally while the client is down, unless a start or a pending replay owns the tracked records.</summary>
	/// <param name="commit">The local commit to run.</param>
	/// <returns>
	/// <see langword="true"/> when the commit ran; otherwise, <see langword="false"/> because a start holds the startup lock, the client is ready,
	/// the provider is starting, failed or closed, or a replay is pending.
	/// </returns>
	/// <remarks>
	/// The decision and the commit run under the startup lock, which a start holds from its replay snapshot to its completion,
	/// so a start can neither snapshot nor finish between them. The lock is only tried, because a start replays through the document
	/// slots and a slot that waited on it would deadlock its own lane. A ready client is skipped because only a start that finds the client
	/// down takes a replay snapshot, so a commit made then would never be replayed and would leave the tracker claiming a copy that the live session
	/// never received.
	/// </remarks>
	internal bool TryCommitWhileUnavailable(Action commit)
	{
		// Observation seam (unset in production): fires between the caller's readiness read and its decision under the lock.
		_testHooks.LocalCommitObserver?.Invoke();

		if (!_startLock.Wait(0))
			return false;

		try
		{
			if (
				_disposal.IsDisposed
				|| _client is not { IsReady: false }
				|| State is LanguageServerFeatureProviderState.Starting or LanguageServerFeatureProviderState.Failed
				|| _pendingReopenDocuments is not null
			)
			{
				return false;
			}

			commit();

			return true;
		}
		finally
		{
			_startLock.Release();
		}
	}

	/// <summary>
	/// Attempts a provider-state transition and raises the capabilities-changed event when the state actually changed and the transition requested
	/// a notification.
	/// </summary>
	/// <param name="state">The requested provider state.</param>
	/// <param name="notifyCapabilitiesChanged">
	/// <see langword="true"/> to request a capabilities-changed notification when the state actually changed.
	/// </param>
	internal void SetProviderState(LanguageServerFeatureProviderState state, bool notifyCapabilitiesChanged = false)
	{
		if (_startupState.TrySetState(state, notifyCapabilitiesChanged))
			_events.RaiseCapabilitiesChanged();
	}

	/// <summary>Records that the connection became unavailable and moves the provider to the unavailable state.</summary>
	/// <remarks>
	/// The hard-failure latch is not downgraded: a connection fault observed after the provider reported a persistent startup failure must not report
	/// the provider as retrying again.
	/// </remarks>
	internal void MarkConnectionUnavailable()
	{
		_startupState.InvalidateStartupSucceeded();

		if (_startupState.State == LanguageServerFeatureProviderState.Failed)
			return;

		SetProviderState(LanguageServerFeatureProviderState.Unavailable, notifyCapabilitiesChanged: true);
	}

	/// <summary>Applies the client's connection-unavailable event, which carries the generation that went unavailable.</summary>
	/// <param name="connectionGeneration">The connection generation reported as unavailable.</param>
	/// <remarks>
	/// The client clears its current capability snapshot before it raises the event. The event is matched against the generation
	/// that most recently reached ready, not the reset snapshot.
	/// </remarks>
	internal void HandleClientConnectionUnavailable(long connectionGeneration)
	{
		if (_startupState.HandleClientConnectionUnavailable(connectionGeneration))
			SetProviderState(LanguageServerFeatureProviderState.Unavailable, notifyCapabilitiesChanged: true);
	}

	/// <summary>Marks one observed connection generation unhealthy, and moves the provider to the unavailable state when the client agrees.</summary>
	/// <param name="connectionGeneration">The connection generation whose forwarding failed.</param>
	/// <remarks>
	/// A fault the client raises while it is asked to mark the generation is logged and contained, because the caller is a workspace-watcher failure
	/// path that must not fail again.
	/// </remarks>
	internal void MarkConnectionUnhealthy(long connectionGeneration)
	{
		if (_client is null)
			return;

		try
		{
			if (_client.TryMarkConnectionUnhealthy(connectionGeneration))
				MarkConnectionUnavailable();
		}
		catch (Exception exception)
		{
			_logger.LogDebug(
				exception,
				"Failed to mark the {DisplayName} language server connection unhealthy after a workspace-watcher send failure.",
				_providerDisplayName
			);
		}
	}

	/// <summary>Reports a persistent startup failure when no language-server client was configured and enters the failed state.</summary>
	/// <remarks>
	/// Idempotent: the failure is logged and raised at most once.
	/// </remarks>
	internal void ReportMissingClientFailure()
	{
		if (_disposal.IsDisposed || _client is not null)
			return;

		SetProviderState(LanguageServerFeatureProviderState.Failed, notifyCapabilitiesChanged: true);

		_failureReporter.ReportMissingClientFailure();
	}

	/// <summary>Moves the provider to the failed state when the consecutive start failures reached the hard threshold.</summary>
	/// <returns><see langword="true"/> when the latch is set, so no start may run; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The failure count is the whole latch state: a failed start attempt raises it and only a successful start resets it, so a provider
	/// that reached the threshold stays failed until it is recreated. A start reads the latch before it takes the startup lock, so a latched
	/// provider never queues, and again under the lock, because the start that a waiting caller queued behind may be the one that reached it.
	/// </remarks>
	private bool TryLatchHardFailure()
	{
		if (_startupState.ConsecutiveStartupFailures < _options.HardStartupFailureThreshold)
			return false;

		SetProviderState(LanguageServerFeatureProviderState.Failed);

		return true;
	}

	/// <summary>
	/// Indicates whether the most recent start completed on the current connection generation and the client still holds that session.
	/// </summary>
	private bool IsStartReady(ILanguageServerClient client) => _startupState.StartupSucceeded && client.IsReady;

	/// <summary>Restores workspace watching on a connection whose start already completed, and reports whether it did.</summary>
	/// <returns><see langword="true"/> when the connection was reused; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The fence is the provider's startup flag together with the client's readiness: a connection the provider
	/// invalidated is not reused although the client still reports ready, and a client that lost its session is not reused although the flag is set.
	/// Reuse takes no startup lock, changes no provider state and resets no timeout tracking, so a timeout recorded after the start still counts
	/// toward a restart.
	/// </remarks>
	private async Task<bool> TryReuseHealthyConnectionAsync(ILanguageServerClient client, CancellationToken cancellationToken)
	{
		if (!IsStartReady(client))
			return false;

		await WatchRestoration.RestoreWorkspaceWatchingAsync(cancellationToken).ConfigureAwait(false);

		return true;
	}

	/// <summary>Chooses the token that governs a start: none for a restart; otherwise, the caller's token.</summary>
	/// <remarks>
	/// A restart is a start after an earlier one completed, while the client is down. It is shared recovery work that replays the tracked documents
	/// for every caller, so one caller's cancellation does not abandon it halfway; only disposal ends it, because the start links the dispose
	/// token to the chosen one. The state is read before the startup lock is taken, so a caller that queues behind a restart is shielded as well
	/// when it found the client down.
	/// </remarks>
	private CancellationToken ResolveStartupCancellationToken(ILanguageServerClient client, CancellationToken cancellationToken) =>
		_startupState.HasCompletedStartupOnce && !client.IsReady ? CancellationToken.None : cancellationToken;

	/// <summary>Joins the start that completed while the caller waited for the startup lock, and reports whether it did.</summary>
	/// <returns><see langword="true"/> when the caller joined a completed start; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The caller chose to start before it queued, so it reads the state again under the lock. A start that completed in between is joined
	/// instead of repeated, because a repeat resets the timeout streak that requests recorded since. The join writes no provider state,
	/// because the completed start already left it ready and a caller writes the starting state only after this check.
	/// </remarks>
	private async Task<bool> TryJoinCompletedStartAsync(ILanguageServerClient client, CancellationToken cancellationToken)
	{
		if (!IsStartReady(client))
			return false;

		await WatchRestoration.RestoreWorkspaceWatchingAsync(cancellationToken).ConfigureAwait(false);

		return true;
	}

	/// <summary>Decides which tracked documents the start reopens and records them as pending, under the startup lock.</summary>
	/// <remarks>
	/// Only a start that finds the client down takes a restart snapshot here, which marks every tracked document closed locally; a start that finds
	/// the client ready reopens nothing its live session holds, until <see cref="RecaptureReopenList"/> takes a snapshot after the start replaced
	/// the session of a resumed list. The list is pending before the start runs, so a start interrupted after the client replaced the session leaves
	/// it for the next start. That start resumes it while the client is ready, as a partial list that holds only the documents the interrupted replay
	/// did not deliver. A pending list also makes <see cref="TryCommitWhileUnavailable"/> refuse a local commit.
	/// </remarks>
	private IReadOnlyList<DocumentSnapshot> PrepareReopen(ILanguageServerClient client, out bool isReplayListPartial)
	{
		IReadOnlyList<DocumentSnapshot> documentsToReopen = [];
		isReplayListPartial = false;

		if (!client.IsReady)
		{
			documentsToReopen = _documents.PrepareForRestart();

			// Track the replay so an interrupted reopen is resumed by a later start even when the client stayed
			// ready; the guarded reopen skips documents that were already reopened.
			_pendingReopenDocuments = documentsToReopen.Count > 0 ? documentsToReopen : null;

			if (_startupState.StartupSucceeded)
			{
				_logger.LogInformation(
					"{DisplayName} language server connection dropped for workspace '{Workspace}'; restarting and reopening {DocumentCount} tracked document(s).",
					_providerDisplayName,
					_workspaceRootsDisplayText,
					documentsToReopen.Count
				);
			}
		}
		else if (_pendingReopenDocuments is { Count: > 0 } pendingReopenDocuments)
		{
			// The connection stayed ready after an interrupted restart replay (for example a replacement connection
			// that is already usable); resume the reopen instead of reporting a ready provider that silently
			// skipped tracked documents.
			documentsToReopen = pendingReopenDocuments;
			isReplayListPartial = true;

			_logger.LogInformation(
				"{DisplayName} language server connection for workspace '{Workspace}' is ready while {DocumentCount} tracked document(s) still need reopening; resuming the replay.",
				_providerDisplayName,
				_workspaceRootsDisplayText,
				documentsToReopen.Count
			);
		}

		return documentsToReopen;
	}

	/// <summary>Ensures the client is started, and returns whether it is together with the connection generation of its session.</summary>
	/// <remarks>
	/// A ready client returns at once on its current generation. The generation is read once, right after a successful call,
	/// and is zero otherwise. The settle fences against this value, so a session that is replaced after this read, during the replay for example,
	/// fails the start instead of being accepted.
	/// </remarks>
	private static async Task<(bool Succeeded, long ConnectionGeneration)> EnsureClientStartedAsync(
		ILanguageServerClient client,
		CancellationToken cancellationToken
	)
	{
		bool succeeded = await client.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
		return (succeeded, succeeded ? client.ConnectionGeneration : 0);
	}

	/// <summary>Replaces a resumed partial list with a complete capture when the start replaced the session the list was captured for.</summary>
	/// <remarks>
	/// A partial list holds only the documents an interrupted replay did not deliver, because the session
	/// it was captured for holds the others. A concurrent invalidation between the readiness decision and the start can make the client
	/// replace that session, and replaying the partial list on the replacement would leave the delivered documents missing there, so the complete set
	/// is captured again. A complete list is never captured again, because its session is the one the start creates.
	/// </remarks>
	private IReadOnlyList<DocumentSnapshot> RecaptureReopenList(
		IReadOnlyList<DocumentSnapshot> documentsToReopen,
		bool isReplayListPartial,
		long observedConnectionGeneration,
		long startedConnectionGeneration
	)
	{
		if (!isReplayListPartial || startedConnectionGeneration == observedConnectionGeneration)
			return documentsToReopen;

		IReadOnlyList<DocumentSnapshot> completeDocuments = _documents.PrepareForRestart();
		_pendingReopenDocuments = completeDocuments.Count > 0 ? completeDocuments : null;

		_logger.LogInformation(
			"{DisplayName} language server connection for workspace '{Workspace}' was replaced while resuming a tracked-document replay; reopening {DocumentCount} tracked document(s) on the new session.",
			_providerDisplayName,
			_workspaceRootsDisplayText,
			completeDocuments.Count
		);

		return completeDocuments;
	}

	/// <summary>Reopens the documents on the started session, records which of them remain pending, and reports whether none remain.</summary>
	/// <returns><see langword="true"/> when no document remains to reopen; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The pending list afterwards is exactly the unfinished remainder, or none when the replay finished or nothing was owed, so a later start
	/// resumes only the documents that still need a reopen. A replay that leaves documents fails the start, because the provider must not report
	/// ready while documents the server does not hold are still owed.
	/// </remarks>
	private async Task<bool> ReplayTrackedDocumentsAsync(IReadOnlyList<DocumentSnapshot> documentsToReopen, CancellationToken cancellationToken)
	{
		if (documentsToReopen.Count == 0)
		{
			_pendingReopenDocuments = null;
			return true;
		}

		IReadOnlyList<DocumentSnapshot>? unfinishedDocuments = await Replay
			.ReopenTrackedDocumentsAsync(documentsToReopen, cancellationToken)
			.ConfigureAwait(false);

		_pendingReopenDocuments = unfinishedDocuments;

		if (unfinishedDocuments is null)
			return true;

		_logger.LogWarning(
			"Failed to replay {DocumentCount} tracked document(s) after {DisplayName} language server restart for workspace '{Workspace}'; the remaining documents are retried on the next start.",
			unfinishedDocuments.Count,
			_providerDisplayName,
			_workspaceRootsDisplayText
		);

		return false;
	}

	/// <summary>Completes the start on the generation that the start returned, or counts the attempt as failed.</summary>
	/// <returns><see langword="true"/> when the start completed; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The completion fails when the client does not hold that generation or the generation was reported unavailable, so a session lost or replaced
	/// during the replay fails the start. A completed start resets the timeout tracking for its generation before it restores workspace watching.
	/// A failed start, including one whose replay left documents, counts toward the hard-failure threshold.
	/// </remarks>
	private async Task<bool> SettleStartAsync(bool startupSucceeded, long startedConnectionGeneration, CancellationToken cancellationToken)
	{
		startupSucceeded = startupSucceeded && TryCompleteSuccessfulStart(startedConnectionGeneration);

		if (startupSucceeded)
		{
			_timeouts.Reset(startedConnectionGeneration);
			await WatchRestoration.RestoreWorkspaceWatchingAsync(cancellationToken).ConfigureAwait(false);
		}
		else
		{
			// A failed tracked-document replay counts with the failed start attempts: the provider must not
			// report ready while documents the replay still owes are missing on the server.
			RegisterFailedStartupAttempt();
		}

		return startupSucceeded;
	}

	private bool TryCompleteSuccessfulStart(long connectionGeneration)
	{
		bool started = _startupState.TryCompleteSuccessfulStart(connectionGeneration, out LanguageServerFeatureProviderState previousState);

		if (started && previousState != LanguageServerFeatureProviderState.Ready && _startupState.State == LanguageServerFeatureProviderState.Ready)
			_events.RaiseCapabilitiesChanged();

		return started;
	}

	/// <summary>
	/// Records one failed startup attempt: increments the consecutive-failure counter, transitions the provider state, and reports the failure.
	/// </summary>
	/// <param name="exception">The unexpected startup exception, or <see langword="null"/> for a reported false start result.</param>
	/// <remarks>
	/// A failed tracked-document replay is recorded
	/// here as well, so a start that succeeds but cannot reopen the documents the replay still owes reaches the latch instead of reporting ready.
	/// Once the counter reaches <see cref="LanguageServerFeatureProviderOptions.HardStartupFailureThreshold"/>, the provider enters
	/// <see cref="LanguageServerFeatureProviderState.Failed"/> and stops attempting to start until it is recreated.
	/// </remarks>
	private void RegisterFailedStartupAttempt(Exception? exception = null)
	{
		int consecutiveStartupFailures = _startupState.RegisterStartupFailure();
		bool isPersistentFailure = consecutiveStartupFailures >= _options.HardStartupFailureThreshold;

		SetProviderState(
			isPersistentFailure ? LanguageServerFeatureProviderState.Failed : LanguageServerFeatureProviderState.Unavailable,
			notifyCapabilitiesChanged: true
		);

		_failureReporter.ReportStartupFailure(exception, consecutiveStartupFailures, isPersistentFailure);
	}
}
