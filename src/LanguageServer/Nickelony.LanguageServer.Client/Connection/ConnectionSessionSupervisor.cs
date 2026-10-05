using System.Diagnostics;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client;

/// <summary>Detaches the connection session of <see cref="ILanguageServerClient"/> that fails and hands it to the failed-session disposal.</summary>
/// <remarks>
/// Exactly one session is active at a time: the supervisor handles process exits and JSON-RPC disconnects, detaches the affected session,
/// and queues its cleanup so connection threads are never blocked by teardown. The session lifecycle and its rationale live
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class ConnectionSessionSupervisor
{
	private readonly ConnectionState _connectionState;
	private readonly SessionTeardown _sessionTeardown;
	private readonly StandardErrorReader _standardErrorReader;
	private readonly FailedSessionDispatcher _failedSessionDispatcher;
	private readonly ILogger _logger;
	private readonly string _workspaceRootsDisplayText;
	private readonly ClientLifetime _lifetime;
	private readonly Action<long> _raiseConnectionUnavailable;

	/// <param name="context">The shared connection-collaborator context.</param>
	/// <param name="sessionTeardown">Disposes the active session.</param>
	/// <param name="standardErrorReader">Reports the recent standard error of a session that fails.</param>
	/// <param name="failedSessionDispatcher">Disposes a detached failed session in the background.</param>
	internal ConnectionSessionSupervisor(
		ConnectionCollaboratorContext context,
		SessionTeardown sessionTeardown,
		StandardErrorReader standardErrorReader,
		FailedSessionDispatcher failedSessionDispatcher
	)
	{
		_connectionState = context.State;
		_sessionTeardown = sessionTeardown;
		_standardErrorReader = standardErrorReader;
		_failedSessionDispatcher = failedSessionDispatcher;
		_logger = context.Logger;
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_lifetime = context.Lifetime;
		_raiseConnectionUnavailable = context.RaiseConnectionUnavailable;
	}

	/// <summary>Detaches the active connection session, if any, and disposes it.</summary>
	/// <returns>A task that completes when the session is disposed.</returns>
	internal async Task DisposeActiveSessionAsync()
	{
		ConnectionSession? session = _connectionState.DetachActiveSession();

		if (session is not null)
			await _sessionTeardown.DisposeSessionAsync(session).ConfigureAwait(false);
	}

	/// <summary>Handles unexpected server process exit for one connection session.</summary>
	/// <param name="session">The session whose process exited.</param>
	internal void HandleProcessExited(ConnectionSession session)
	{
		// Read the exit code before the detached-session cleanup can dispose the process, and before the
		// session detach decides whether this exit belongs to the active connection.
		int? exitCode = ReadProcessExitCode(session.Process);

		if (!_connectionState.TryDetachSpecificActiveSession(session, out bool wasReady))
		{
			// The session is not the active one: either it never became active, or a concurrent disconnect,
			// restart, or disposal already detached it. The startup path reports startup failures with standard-error
			// context, so keep this at debug level but preserve the exit code evidence.
			if (exitCode is not null && !_lifetime.IsDisposed)
			{
				_logger.LogDebug(
					"Language server process for connection generation {Generation} in workspace '{Workspace}' exited with code {ExitCode} while its connection session was not the active one (it never became active or was already detached).",
					session.ConnectionGeneration,
					_workspaceRootsDisplayText,
					exitCode.Value
				);
			}

			return;
		}

		if (wasReady)
			_raiseConnectionUnavailable(session.ConnectionGeneration);

		_failedSessionDispatcher.Dispatch(session, "process exit");

		if (!_lifetime.IsDisposed)
		{
			_logger.LogWarning(
				"Language server process for connection generation {Generation} in workspace '{Workspace}' exited unexpectedly{ExitCodeSuffix}; the host will recreate the session on the next startup attempt.",
				session.ConnectionGeneration,
				_workspaceRootsDisplayText,
				exitCode is not null ? $" with code {exitCode.Value}" : string.Empty
			);

			_standardErrorReader.LogRecentContext(session, "Unexpected process exit");
		}
	}

	/// <summary>Handles the disconnection of the active session's JSON-RPC instance.</summary>
	/// <param name="session">The disconnected session.</param>
	/// <param name="eventArgs">The disconnect event arguments.</param>
	internal void HandleJsonRpcDisconnected(ConnectionSession session, JsonRpcDisconnectedEventArgs eventArgs)
	{
		if (!_connectionState.TryDetachSpecificActiveSession(session, out bool wasReady))
		{
			_logger.LogDebug(
				"Ignoring disconnect from stale language server connection generation {Generation}: {Description}",
				session.ConnectionGeneration,
				eventArgs.Description
			);

			return;
		}

		_failedSessionDispatcher.Dispatch(session, "JSON-RPC disconnect");

		if (_lifetime.IsDisposed)
		{
			_logger.LogDebug(
				"The JSON-RPC instance of language server connection generation {Generation} for workspace '{Workspace}' disconnected during client disposal: {Description}",
				session.ConnectionGeneration,
				_workspaceRootsDisplayText,
				eventArgs.Description
			);

			return;
		}

		// Local disposal of a still-active session is only reachable through the handler contract (every production
		// dispose path detaches first), but it is a deliberate state: report it as expected instead of as a loss.
		if (eventArgs.Reason == DisconnectedReason.LocallyDisposed)
		{
			_logger.LogInformation(
				"The JSON-RPC instance of language server connection generation {Generation} for workspace '{Workspace}' disconnected during expected local shutdown: {Description}",
				session.ConnectionGeneration,
				_workspaceRootsDisplayText,
				eventArgs.Description
			);

			return;
		}

		if (wasReady)
			_raiseConnectionUnavailable(session.ConnectionGeneration);

		Exception? exception = eventArgs.Exception;

		if (exception is not null)
		{
			_logger.LogWarning(
				exception,
				"The JSON-RPC instance of language server connection generation {Generation} for workspace '{Workspace}' disconnected unexpectedly (reason={Reason}); the host will recreate the session on the next startup attempt: {Description}",
				session.ConnectionGeneration,
				_workspaceRootsDisplayText,
				eventArgs.Reason,
				eventArgs.Description
			);

			_standardErrorReader.LogRecentContext(session, "Unexpected JSON-RPC disconnect");

			return;
		}

		_logger.LogWarning(
			"The JSON-RPC instance of language server connection generation {Generation} for workspace '{Workspace}' disconnected unexpectedly (reason={Reason}); the host will recreate the session on the next startup attempt: {Description}",
			session.ConnectionGeneration,
			_workspaceRootsDisplayText,
			eventArgs.Reason,
			eventArgs.Description
		);

		_standardErrorReader.LogRecentContext(session, "Unexpected JSON-RPC disconnect");
	}

	/// <summary>Tries to read the exit code for a process that may already be disposed.</summary>
	/// <param name="process">The process to inspect.</param>
	/// <returns>The exit code, or <see langword="null"/> when unavailable.</returns>
	private static int? ReadProcessExitCode(Process? process)
	{
		if (process is null)
			return null;

		try
		{
			return process.HasExited ? process.ExitCode : null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}
}
