using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Exercises the Windows child-lifetime guard directly: the per-call diagnostics routing, the reset seam that
/// restores the process-wide state, and the kill-on-close termination guarantee for a bound process.
/// </summary>
/// <remarks>
/// The guard's state is process-wide and <see cref="ChildProcessLifetime.ResetForTests"/> closes the shared job
/// object, so these tests must never run concurrently with any other test that binds a child process.
/// </remarks>
[TestClass]
[DoNotParallelize]
[SupportedOSPlatform("windows")]
public sealed class ChildProcessLifetimeTests
{
	[TestCleanup]
	public void RestoreJobObjectState()
		=> ChildProcessLifetime.ResetForTests();

	/// <summary>
	/// The job object is process-wide, but its diagnostics are not: each assignment reports through the logger its
	/// own caller supplied, so a later client receives its own outcome instead of being silenced by whichever
	/// client initialized the shared job first. A process handle that was never associated with a process makes
	/// the assignment fail, which routes one diagnostic through the caller's logger.
	/// </summary>
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	[Timeout(30_000)]
	public void Bind_RoutesDiagnosticsToEachCallersOwnLogger()
	{
		ChildProcessLifetime.ResetForTests();
		using var firstLogger = new TestLoggerScope(LogLevel.Debug);
		using var secondLogger = new TestLoggerScope(LogLevel.Debug);

		UseUnassociatedProcessHandle(firstLogger);

		if (ReceivedJobObjectInitializationFailure(firstLogger))
		{
			Assert.Inconclusive(
				"The host could not create the kill-on-close job object, so the logger routing cannot be observed here.");
		}

		Assert.IsTrue(ReceivedAssignmentFailure(firstLogger), "The first caller's logger must receive the diagnostic.");

		UseUnassociatedProcessHandle(secondLogger);

		Assert.IsTrue(ReceivedAssignmentFailure(secondLogger), "A later caller's logger must receive its own diagnostic.");
	}

	/// <summary>
	/// A process assigned to the shared kill-on-close job object is terminated when the job handle is released,
	/// which is the guarantee that prevents a stranded language server after a host crash.
	/// </summary>
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	[Timeout(60_000)]
	public void Bind_AssignedProcess_IsTerminatedWhenTheJobHandleIsReleased()
	{
		ChildProcessLifetime.ResetForTests();
		using var loggerScope = new TestLoggerScope(LogLevel.Debug);

		using Process child = StartLongRunningChild();

		try
		{
			ChildProcessLifetime.DefaultGuard.Bind(child, loggerScope);

			if (ReceivedAssignmentFailure(loggerScope))
			{
				Assert.Inconclusive(
					"The host refused the job-object assignment (for example an unbreakable enclosing job), so the " +
					"kill-on-close guarantee cannot be observed here.");
			}

			// Releasing the shared job handle closes it; a process assigned to a kill-on-close job is terminated.
			ChildProcessLifetime.ResetForTests();

			Assert.IsTrue(child.WaitForExit(10_000), "Closing the job handle must terminate the assigned process.");
		}
		finally
		{
			if (!child.HasExited)
				child.Kill(entireProcessTree: true);
		}
	}

	/// <summary>
	/// Passes the job object a handle that cannot be associated with a process, so the assignment fails without
	/// touching a real child.
	/// </summary>
	private static void UseUnassociatedProcessHandle(ILogger logger)
	{
		using var unassociated = new Process();
		ChildProcessLifetime.DefaultGuard.Bind(unassociated, logger);
	}

	private static bool ReceivedAssignmentFailure(TestLoggerScope loggerScope)
		=> loggerScope.Logs.Any(entry => entry.Contains("Failed to assign the language-server process", StringComparison.Ordinal)
			|| entry.Contains("AssignProcessToJobObject failed", StringComparison.Ordinal));

	private static bool ReceivedJobObjectInitializationFailure(TestLoggerScope loggerScope)
		=> loggerScope.Logs.Any(entry => entry.Contains("CreateJobObject returned NULL", StringComparison.Ordinal)
			|| entry.Contains("SetInformationJobObject failed", StringComparison.Ordinal));

	private static Process StartLongRunningChild()
		=> Process.Start(new ProcessStartInfo
		{
			FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			Arguments = "/c ping -n 60 127.0.0.1 > NUL",
			UseShellExecute = false,
			CreateNoWindow = true
		}) ?? throw new InvalidOperationException("The long-running child process could not be started.");
}
