namespace Nickelony.LanguageServer.Client;

/// <summary>Disposes each failed connection session in the background, one session at a time.</summary>
/// <remarks>
/// A disposal never runs on the connection thread that reported the failure, and it starts only after every earlier disposal has finished,
/// so two failures in quick succession never tear down their resources concurrently. The session lifecycle and its rationale live
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class FailedSessionDispatcher
{
	private readonly Func<ConnectionSession, Task> _disposeSession;
	private readonly ILogger _logger;

	private readonly object _syncRoot = new();
	private Task _queuedDisposal = Task.CompletedTask;

	/// <param name="disposeSession">Disposes one detached session.</param>
	/// <param name="logger">The logger that receives the failures of a disposal.</param>
	internal FailedSessionDispatcher(Func<ConnectionSession, Task> disposeSession, ILogger logger)
	{
		_disposeSession = disposeSession;
		_logger = logger;
	}

	/// <summary>The task of the newest queued disposal, which completes after every earlier one.</summary>
	internal Task QueuedDisposalTask
	{
		get
		{
			lock (_syncRoot)
				return _queuedDisposal;
		}
	}

	/// <summary>Disposes a detached failed session without blocking the disconnect or process-exit callback thread.</summary>
	/// <param name="session">The detached failed session.</param>
	/// <param name="reason">The failure reason used for diagnostics when cleanup itself fails.</param>
	internal void Dispatch(ConnectionSession session, string reason)
	{
		lock (_syncRoot)
		{
			Task previousDisposal = _queuedDisposal;
			_queuedDisposal = Task.Run(() => DisposeAfterAsync(previousDisposal, session, reason));
		}
	}

	private async Task DisposeAfterAsync(Task previousDisposal, ConnectionSession session, string reason)
	{
		try
		{
			await previousDisposal.ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			// Later cleanup should still run even if earlier detached cleanup failed; the failure is logged so it is
			// not silently swallowed.
			_logger.LogDebug(exception, "A previously queued detached language server connection cleanup failed.");
		}

		await DisposeSessionAsync(session, reason).ConfigureAwait(false);
	}

	private async Task DisposeSessionAsync(ConnectionSession session, string reason)
	{
		try
		{
			await _disposeSession(session).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(
				exception,
				"Failed to dispose detached language server connection generation {Generation} after {Reason}.",
				session.ConnectionGeneration,
				reason
			);
		}
	}
}
