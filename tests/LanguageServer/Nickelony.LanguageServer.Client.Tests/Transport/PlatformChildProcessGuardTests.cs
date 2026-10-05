using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Exercises the Windows child-lifetime guard directly.</summary>
/// <remarks>
/// Each test creates its own job object, so closing one never terminates a process that the shared job of a client holds.
/// </remarks>
[TestClass]
[SupportedOSPlatform("windows")]
public sealed class PlatformChildProcessGuardTests
{
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Instance_OnWindows_IsTheSharedJobObject() => Assert.AreSame(WindowsJobObject.Instance, PlatformChildProcessGuard.Instance);

	/// <summary>
	/// The job object is process-wide, but its diagnostics are not: each assignment reports through the logger its own caller supplied, so a later
	/// client receives its own outcome instead of being silenced by whichever client initialized the shared job first.
	/// </summary>
	/// <remarks>
	/// A process handle that was never associated with a process makes the assignment fail, which routes one diagnostic through the caller's logger.
	/// </remarks>
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	[Timeout(30_000, CooperativeCancellation = true)]
	public void Bind_RoutesDiagnosticsToEachCallersOwnLogger()
	{
		using var jobObject = new WindowsJobObject();
		using var firstLogger = new RecordingLogger(LogLevel.Debug);
		using var secondLogger = new RecordingLogger(LogLevel.Debug);

		UseUnassociatedProcessHandle(jobObject, firstLogger);

		if (ReceivedJobObjectInitializationFailure(firstLogger))
			Assert.Inconclusive("The host could not create the kill-on-close job object, so the logger routing cannot be observed here.");

		Assert.IsTrue(ReceivedAssignmentFailure(firstLogger), "The first caller's logger must receive the diagnostic.");

		UseUnassociatedProcessHandle(jobObject, secondLogger);

		Assert.IsTrue(ReceivedAssignmentFailure(secondLogger), "A later caller's logger must receive its own diagnostic.");
	}

	/// <summary>A disposed job object assigns nothing, so a late assignment cannot create a job that nothing releases.</summary>
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	[Timeout(30_000, CooperativeCancellation = true)]
	public void Bind_AfterDispose_AssignsNothing()
	{
		var jobObject = new WindowsJobObject();
		using var logger = new RecordingLogger(LogLevel.Debug);

		jobObject.Dispose();

		UseUnassociatedProcessHandle(jobObject, logger);

		// An assignment would have failed for the unassociated handle and reported it; the disposed job never tried.
		Assert.IsFalse(ReceivedAssignmentFailure(logger));
		Assert.IsFalse(ReceivedJobObjectInitializationFailure(logger));
	}

	/// <summary>
	/// A process assigned to the shared kill-on-close job object is terminated when the job handle is released, which is the guarantee that prevents
	/// a stranded language server after a host crash.
	/// </summary>
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	[Timeout(60_000, CooperativeCancellation = true)]
	public void Bind_AssignedProcess_IsTerminatedWhenTheJobHandleIsReleased()
	{
		using var jobObject = new WindowsJobObject();
		using var logger = new RecordingLogger(LogLevel.Debug);

		using Process child = StartLongRunningChild();

		try
		{
			jobObject.Bind(child, logger);

			if (ReceivedAssignmentFailure(logger))
			{
				Assert.Inconclusive(
					"The host refused the job-object assignment (for example an unbreakable enclosing job), so the "
						+ "kill-on-close guarantee cannot be observed here."
				);
			}

			// Releasing the job handle closes it; a process assigned to a kill-on-close job is terminated.
			jobObject.Dispose();

			Assert.IsTrue(child.WaitForExit(10_000), "Closing the job handle must terminate the assigned process.");
		}
		finally
		{
			if (!child.HasExited)
				child.Kill(entireProcessTree: true);
		}
	}

	/// <summary>
	/// Passes the job object a handle that cannot be associated with a process, so the assignment fails without touching a real child.
	/// </summary>
	private static void UseUnassociatedProcessHandle(WindowsJobObject jobObject, ILogger logger)
	{
		using var unassociated = new Process();
		jobObject.Bind(unassociated, logger);
	}

	private static bool ReceivedAssignmentFailure(RecordingLogger logger)
	{
		return logger.Logs.Any(entry =>
			entry.Contains("Failed to bind the language-server process", StringComparison.Ordinal)
			|| entry.Contains("AssignProcessToJobObject failed", StringComparison.Ordinal)
		);
	}

	private static bool ReceivedJobObjectInitializationFailure(RecordingLogger logger)
	{
		return logger.Logs.Any(entry =>
			entry.Contains("CreateJobObject returned NULL", StringComparison.Ordinal)
			|| entry.Contains("SetInformationJobObject failed", StringComparison.Ordinal)
		);
	}

	private static Process StartLongRunningChild()
	{
		return Process.Start(
				new ProcessStartInfo
				{
					FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
					Arguments = "/c ping -n 60 127.0.0.1 > NUL",
					UseShellExecute = false,
					CreateNoWindow = true,
				}
			) ?? throw new InvalidOperationException("The long-running child process could not be started.");
	}
}
