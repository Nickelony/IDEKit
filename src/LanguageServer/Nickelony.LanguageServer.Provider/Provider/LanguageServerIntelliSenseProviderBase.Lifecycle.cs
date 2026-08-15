namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <summary>
	/// Ensures the language-server transport is running and that tracked documents and workspace watching are restored after reconnects.
	/// </summary>
	/// <remarks>
	/// Run this outside a document scheduler slot: a restart replays tracked documents by queueing per-document
	/// scheduler operations, and queueing scheduler work from inside a slot is rejected by the scheduler and would
	/// deadlock the slot's own chain while it waits on the startup lock. Document slots observe
	/// <see cref="IsTransportReady"/> instead of starting the transport themselves.
	/// </remarks>
	private async Task<bool> EnsureTransportStartedAsync(CancellationToken cancellationToken)
	{
		if (_isDisposed || _client is null)
			return false;

		if (_startupState.ConsecutiveStartupFailures >= _options.HardStartupFailureThreshold)
		{
			SetProviderState(LanguageServerProviderState.Failed);
			return false;
		}

		using var fastPathDisposeAwareCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeToken);

		// Fast path: once the client is healthy, keep the workspace watcher alive and avoid taking the startup lock.
		if (_startupState.StartupSucceeded && _client.IsReady)
		{
			await _workspaceChanges.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
			await _workspaceChanges.ReplayDeferredWorkspaceFileChangesAsync(fastPathDisposeAwareCts.Token).ConfigureAwait(false);

			return true;
		}

		SetProviderState(LanguageServerProviderState.Starting);

		bool shieldCancellationForRestart = _startupState.HasCompletedStartupOnce && !_client.IsReady;

		CancellationToken startupCancellationToken = shieldCancellationForRestart
			? CancellationToken.None
			: cancellationToken;

		using var disposeAwareStartupCts = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken, _disposeToken);
		CancellationToken effectiveStartupCancellationToken = disposeAwareStartupCts.Token;
		bool startLockHeld = false;

		try
		{
			// Test-only observation seam (null in production): fires as this caller reaches the startup lock, so a
			// test can deterministically observe a caller parked on a contended shared startup.
			TestHooks.StartupLockWaitObserver?.Invoke();

			// Serialize startup and restart work so concurrent callers share one recovery flow.
			await _startLock.WaitAsync(effectiveStartupCancellationToken).ConfigureAwait(false);
			startLockHeld = true;

			IReadOnlyList<DocumentSnapshot> documentsToReopen = [];

			// The transport generation the replay decision below is made on. A concurrent transport invalidation can
			// make StartAsync replace the session; a partial resume list captured for the previous session must not
			// be replayed against its replacement.
			long observedTransportGeneration = _client.TransportGeneration;
			bool replayListIsPartial = false;

			// Re-check state after taking the lock so concurrent callers share the same restart/startup work.
			if (_startupState.ConsecutiveStartupFailures >= _options.HardStartupFailureThreshold)
			{
				SetProviderState(LanguageServerProviderState.Failed);
				return false;
			}

			if (_startupState.StartupSucceeded && _client.IsReady)
			{
				SetProviderState(LanguageServerProviderState.Ready, notifyCapabilitiesChanged: true);

				await _workspaceChanges.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
				await _workspaceChanges.ReplayDeferredWorkspaceFileChangesAsync(effectiveStartupCancellationToken).ConfigureAwait(false);

				return true;
			}

			if (!_client.IsReady)
			{
				documentsToReopen = _documents.PrepareForRestart();

				// Track the replay so an interrupted reopen is resumed by a later start even when the client stayed
				// ready; the guarded reopen skips documents that were already reopened.
				_pendingReopenDocuments = documentsToReopen.Count > 0 ? documentsToReopen : null;

				if (_startupState.StartupSucceeded)
				{
					_logger.LogInformation("{DisplayName} language server connection dropped for workspace '{Workspace}'; restarting and reopening {DocumentCount} tracked document(s).",
						ProviderDisplayName,
						_workspaceRootsDisplayText,
						documentsToReopen.Count);
				}
			}
			else if (_pendingReopenDocuments is { Count: > 0 } pendingReopenDocuments)
			{
				// The transport stayed ready after an interrupted restart replay (for example a replacement transport
				// that is already usable); resume the reopen instead of reporting a ready provider that silently
				// skipped tracked documents.
				documentsToReopen = pendingReopenDocuments;
				replayListIsPartial = true;

				_logger.LogInformation("{DisplayName} language server transport for workspace '{Workspace}' is ready while {DocumentCount} tracked document(s) still need reopening; resuming the replay.",
					ProviderDisplayName,
					_workspaceRootsDisplayText,
					documentsToReopen.Count);
			}

			// Start the transport, then replay tracked documents when this is a restart rather than a cold start.
			bool startupSucceeded = await _client.StartAsync(effectiveStartupCancellationToken).ConfigureAwait(false);
			long startedTransportGeneration = startupSucceeded ? _client.TransportGeneration : 0;

			// A concurrent invalidation between the readiness decision above and StartAsync can make the client
			// replace the session. The partial resume list was captured for the previous session; replaying it
			// against the replacement would skip the documents the earlier reopen already delivered on that
			// session, so the complete set is captured again for the new session.
			if (startupSucceeded && replayListIsPartial && startedTransportGeneration != observedTransportGeneration)
			{
				documentsToReopen = _documents.PrepareForRestart();
				_pendingReopenDocuments = documentsToReopen.Count > 0 ? documentsToReopen : null;

				_logger.LogInformation("{DisplayName} language server transport for workspace '{Workspace}' was replaced while resuming a tracked-document replay; reopening {DocumentCount} tracked document(s) on the new session.",
					ProviderDisplayName,
					_workspaceRootsDisplayText,
					documentsToReopen.Count);
			}

			if (startupSucceeded && documentsToReopen.Count > 0)
			{
				IReadOnlyList<DocumentSnapshot>? unfinishedDocuments = await ReopenTrackedDocumentsAsync(documentsToReopen, effectiveStartupCancellationToken).ConfigureAwait(false);

				_pendingReopenDocuments = unfinishedDocuments;

				if (unfinishedDocuments is not null)
				{
					startupSucceeded = false;

					_logger.LogWarning("Failed to replay {DocumentCount} tracked document(s) after {DisplayName} language server restart for workspace '{Workspace}'; the remaining documents are retried on the next start.",
						unfinishedDocuments.Count,
						ProviderDisplayName,
						_workspaceRootsDisplayText);
				}
			}
			else if (startupSucceeded)
			{
				_pendingReopenDocuments = null;
			}

			if (startupSucceeded)
				startupSucceeded = TryCompleteSuccessfulStart(startedTransportGeneration);

			if (startupSucceeded)
			{
				_requestDispatcher.ResetTimeoutTracking(startedTransportGeneration);

				await _workspaceChanges.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
				await _workspaceChanges.ReplayDeferredWorkspaceFileChangesAsync(effectiveStartupCancellationToken).ConfigureAwait(false);
			}
			else
			{
				// A failed tracked-document replay counts with the failed start attempts: the provider must not
				// report ready while documents the replay still owes are missing on the server.
				RegisterFailedStartupAttempt();
			}

			return startupSucceeded;
		}
		catch (OperationCanceledException) when (_isDisposed)
		{
			return false;
		}
		catch (OperationCanceledException)
		{
			SetProviderState(LanguageServerProviderState.Unavailable, notifyCapabilitiesChanged: true);
			throw;
		}
		catch (Exception exception)
		{
			// An unexpected startup exception counts like a false start result, so repeated failures still reach
			// the hard-failure latch instead of retrying forever; caller cancellation and disposal are handled
			// above, and a fault observed while disposing is not a startup failure.
			if (!_isDisposed)
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
	/// Records one failed startup attempt: increments the consecutive-failure counter, transitions the provider
	/// state, logs the failure, and reports it through <see cref="StartupFailed"/>.
	/// </summary>
	/// <remarks>
	/// A failed tracked-document replay is recorded here as well, so a start that succeeds but cannot reopen the
	/// documents the replay still owes reaches the latch instead of reporting ready. Once the counter reaches
	/// <see cref="LanguageServerProviderOptions.HardStartupFailureThreshold"/>, the provider enters
	/// <see cref="LanguageServerProviderState.Failed"/> and stops attempting to start until it is recreated.
	/// </remarks>
	/// <param name="exception">The unexpected startup exception, or <see langword="null"/> for a reported false start result.</param>
	private void RegisterFailedStartupAttempt(Exception? exception = null)
	{
		int consecutiveStartupFailures = _startupState.RegisterStartupFailure();
		bool isPersistentFailure = consecutiveStartupFailures >= _options.HardStartupFailureThreshold;

		SetProviderState(
			isPersistentFailure ? LanguageServerProviderState.Failed : LanguageServerProviderState.Unavailable,
			notifyCapabilitiesChanged: true);

		// Record repeated failures so IntelliSense eventually stops advertising availability until restart.
		if (isPersistentFailure)
		{
			_logger.LogError(exception, "{DisplayName} language server failed to start {Count} times consecutively for workspace '{Workspace}'; IntelliSense is now disabled until the provider is recreated.",
				ProviderDisplayName, consecutiveStartupFailures, _workspaceRootsDisplayText);
		}
		else
		{
			_logger.LogWarning(exception, "Failed to start the {DisplayName} language server for workspace '{Workspace}' (attempt {Attempt}/{Threshold}).",
				ProviderDisplayName, _workspaceRootsDisplayText, consecutiveStartupFailures, _options.HardStartupFailureThreshold);
		}

		ReportStartupFailure(isPersistentFailure);
	}

	/// <summary>
	/// Reports a startup failure at most once per persistence: claims the report slot, runs the startup-failure
	/// hook, and raises <see cref="StartupFailed"/> when the hook produced a failure description.
	/// </summary>
	/// <param name="isPersistentFailure">Whether the failure is persistent for this provider instance.</param>
	private void ReportStartupFailure(bool isPersistentFailure)
	{
		if (!_startupState.TryMarkStartupFailureReported(isPersistentFailure))
			return;

		string? message = InvokeContainedHook<string?>(
			() => CreateStartupFailureMessage(isPersistentFailure),
			fallbackValue: null,
			"startup-failure");

		if (!string.IsNullOrWhiteSpace(message))
			RaiseStartupFailed(new LanguageServerStartupFailure(message, isPersistentFailure));
	}

	/// <summary>
	/// Reports a persistent startup failure when no language-server client was configured and enters the failed state.
	/// </summary>
	/// <remarks>
	/// Idempotent: the failure is logged and raised at most once. Derived request paths that discover a missing
	/// client can call this to surface the same persistent failure the framework reports.
	/// </remarks>
	protected void ReportMissingClientFailure()
	{
		if (_isDisposed || _client is not null)
			return;

		SetProviderState(LanguageServerProviderState.Failed, notifyCapabilitiesChanged: true);

		if (!_startupState.TryMarkStartupFailureReported(isPersistentFailure: true))
			return;

		_logger.LogError("{DisplayName} language server is unavailable for workspace '{Workspace}'; IntelliSense is disabled until the provider is recreated.",
			ProviderDisplayName, _workspaceRootsDisplayText);

		string? message = InvokeContainedHook<string?>(
			() => CreateMissingClientFailureMessage(),
			fallbackValue: null,
			"missing-client failure");

		if (!string.IsNullOrWhiteSpace(message))
			RaiseStartupFailed(new LanguageServerStartupFailure(message, isPersistent: true));
	}

	// Completed by the teardown the first dispose caller started. Later callers wait for it instead of returning
	// while the teardown continues, so a completed dispose call means disposal finished.
	private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <inheritdoc/>
	/// <remarks>
	/// See the class remarks for the disposal contract. Language-specific teardown runs through the
	/// <see cref="OnDisposing"/> hook before the owned client is unsubscribed and disposed. A later caller waits for
	/// the teardown the first caller started instead of returning while it continues.
	/// </remarks>
	public void Dispose()
	{
		if (!TryBeginDispose())
		{
			// The first caller owns teardown; wait for it rather than returning a completed disposal while the
			// client is still being torn down.
			_disposeCompletion.Task.GetAwaiter().GetResult();
			return;
		}

		GC.SuppressFinalize(this);

		try
		{
			RunDisposeCoreContained();

			_client?.Dispose();
		}
		finally
		{
			_disposeCompletion.TrySetResult();
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Shares one teardown with <see cref="Dispose"/>: whichever overload runs first performs it, and a later caller
	/// waits for that teardown to finish instead of returning while it continues. This overload awaits the owned
	/// client's asynchronous teardown instead of blocking the calling thread on it, so a host with a synchronization
	/// context can dispose the provider without deadlocking its thread; a later synchronous caller still blocks its
	/// own thread until the shared teardown completes.
	/// </remarks>
	public async ValueTask DisposeAsync()
	{
		if (!TryBeginDispose())
		{
			await _disposeCompletion.Task.ConfigureAwait(false);
			return;
		}

		GC.SuppressFinalize(this);

		try
		{
			RunDisposeCoreContained();

			if (_client is not null)
				await _client.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			_disposeCompletion.TrySetResult();
		}
	}

	/// <summary>
	/// Claims the one-time right to tear the provider down.
	/// </summary>
	/// <returns><see langword="true"/> when the caller should perform the teardown; otherwise, <see langword="false"/>.</returns>
	private bool TryBeginDispose()
		=> Interlocked.Exchange(ref _disposeStarted, 1) == 0;

	/// <summary>
	/// Runs the provider-owned teardown steps, containing a failure so it cannot skip the client disposal that
	/// follows.
	/// </summary>
	private void RunDisposeCoreContained()
	{
		try
		{
			DisposeCore();
		}
		catch (Exception exception)
		{
			Logger.LogWarning(exception,
				"{DisplayName} disposal reported a failure while tearing down provider-owned state; continuing with client disposal.",
				ProviderDisplayName);
		}
	}

	/// <summary>
	/// Performs the provider-owned teardown steps shared by <see cref="Dispose"/> and <see cref="DisposeAsync"/>.
	/// </summary>
	private void DisposeCore()
	{
		CloseCallbackAdmission();
		SetProviderState(LanguageServerProviderState.Disposed);
		InvokeContainedHook(OnDisposing, "disposal");

		if (_client is not null)
		{
			_client.DiagnosticsPublished -= HandleDiagnosticsPublished;
			_client.SemanticTokensRefreshRequested -= HandleSemanticTokensRefreshRequested;
			_client.DiagnosticRefreshRequested -= HandleDiagnosticRefreshRequested;
			_client.TransportUnavailable -= HandleTransportUnavailable;
		}

		// The source is deliberately never disposed (see the constructor).
		_disposeCts.Cancel();

		_workspaceChanges.Dispose();

		CancelAllQueuedDocumentUpdates();
		CancelAllSemanticTokenRequests();
		CancelAllDiagnosticRequests();
	}

	private void CloseCallbackAdmission()
	{
		lock (_callbackAdmissionSyncRoot)
		{
			_callbackAdmissionClosed = true;
			_isDisposed = true;

			_diagnosticsUpdated = null;
			_semanticTokensUpdated = null;
			_capabilitiesChanged = null;
			_startupFailed = null;
			_workspaceWatcherFailed = null;
		}
	}
}
