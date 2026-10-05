using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class StandardErrorReaderTests
{
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Start_RecordsEveryNonBlankLineInOrderAndEndsAtEndOfStream()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using Process process = ProcessTestHelpers.StartProcessThatWritesToStandardError("first", string.Empty, "second");

		// The lines are still buffered in the pipe when the process has already exited; they are the crash diagnostics the capture exists for.
		await process.WaitForExitAsync().ConfigureAwait(false);

		var fixture = new SessionTeardownTestHost(logger);
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process);

		await fixture.StandardErrorReader.Start(session).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual("first | second", session.GetRecentStandardErrorSummary());
		Assert.Contains(log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("[LS stderr] first"), logger.Logs);
		Assert.Contains(log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("[LS stderr] second"), logger.Logs);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Start_WhenTheClientWasDisposed_ReadsNothing()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using Process process = ProcessTestHelpers.StartProcessThatWritesToStandardError("ignored");

		var fixture = new SessionTeardownTestHost(logger);
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process);

		fixture.MarkClientDisposed();

		await fixture.StandardErrorReader.Start(session).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsNull(session.GetRecentStandardErrorSummary());
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Start_WhenTheLifetimeIsCanceledWhileReading_EndsWithoutLoggingAFailure()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		using Process process = ProcessTestHelpers.StartDisposableProcess();

		try
		{
			var fixture = new SessionTeardownTestHost(logger);
			ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process);

			Task loop = fixture.StandardErrorReader.Start(session);

			await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

			Assert.IsFalse(loop.IsCompleted, "The loop must keep reading while the process holds its standard error open.");

			fixture.Lifetime.Cancel();
			await loop.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

			Assert.IsEmpty(logger.Logs);
		}
		finally
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
	}

	[TestMethod]
	public async Task Start_ForASessionWithoutAProcess_EndsWithoutRecordingAnything()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		var fixture = new SessionTeardownTestHost(logger);
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);

		await fixture.StandardErrorReader.Start(session).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsNull(session.GetRecentStandardErrorSummary());
		Assert.IsEmpty(logger.Logs);
	}

	[TestMethod]
	public void LogRecentContext_WithRecordedLines_WarnsWithTheContextAndTheLines()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		var fixture = new SessionTeardownTestHost(logger);
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);

		session.RecordStandardErrorLine("crash detail");
		fixture.StandardErrorReader.LogRecentContext(session, "Unexpected process exit");

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("Unexpected process exit recent language server stderr: crash detail", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void LogRecentContext_WithoutRecordedLines_LogsNothing()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		var fixture = new SessionTeardownTestHost(logger);

		fixture.StandardErrorReader.LogRecentContext(
			ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null),
			"Context"
		);

		Assert.IsEmpty(logger.Logs);
	}

	[TestMethod]
	public void LogRecentContext_WithoutASession_LogsNothing()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		var fixture = new SessionTeardownTestHost(logger);

		fixture.StandardErrorReader.LogRecentContext(session: null, "Context");

		Assert.IsEmpty(logger.Logs);
	}
}
