using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

/// <summary>Reads the standard error of a language-server process into the debug log and the recent-line buffer of its session.</summary>
/// <remarks>
/// The session keeps the recent lines; this type only reads them and reports them when a connection failure path asks.
/// </remarks>
internal sealed class StandardErrorReader
{
	private readonly ILogger _logger;
	private readonly ClientLifetime _lifetime;

	/// <param name="context">The shared connection-collaborator context (logger and lifetime).</param>
	internal StandardErrorReader(ConnectionCollaboratorContext context)
	{
		_logger = context.Logger;
		_lifetime = context.Lifetime;
	}

	/// <summary>Starts reading the standard error of one session in the background.</summary>
	/// <param name="session">The configured session whose process standard error should be read.</param>
	/// <returns>The loop task, which the session keeps for orderly shutdown.</returns>
	internal Task Start(ConnectionSession session) => Task.Run(() => ReadStandardErrorLoopAsync(session), CancellationToken.None);

	/// <summary>Logs recent standard-error context for one session when a connection failure path needs more diagnostics.</summary>
	/// <param name="session">The session whose recent standard error should be reported.</param>
	/// <param name="context">The failure context label.</param>
	internal void LogRecentContext(ConnectionSession? session, string context)
	{
		string? recentStandardError = session?.GetRecentStandardErrorSummary();

		if (string.IsNullOrWhiteSpace(recentStandardError))
			return;

		_logger.LogWarning("{Context} recent language server stderr: {RecentStandardError}", context, recentStandardError);
	}

	/// <summary>Reads standard-error output from the language-server process and logs non-empty lines.</summary>
	/// <param name="session">The connection session whose process standard error should be read.</param>
	private async Task ReadStandardErrorLoopAsync(ConnectionSession session)
	{
		try
		{
			// Read to end-of-stream instead of checking HasExited between reads: when the process exits with lines
			// still buffered in the pipe, those lines are the crash diagnostics the ring exists for. The loop stays
			// bounded by the lifetime token when a grandchild keeps the inherited error handle open.
			while (!_lifetime.IsDisposed)
			{
				Process? process = session.Process;

				if (process is null)
					break;

				string? line = await process.StandardError.ReadLineAsync(_lifetime.Token).ConfigureAwait(false);

				if (line is null)
					break;

				if (!string.IsNullOrWhiteSpace(line))
				{
					session.RecordStandardErrorLine(line);
					_logger.LogDebug("[LS stderr] {Line}", line);
				}
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Language server stderr read loop failed.");
		}
	}
}
