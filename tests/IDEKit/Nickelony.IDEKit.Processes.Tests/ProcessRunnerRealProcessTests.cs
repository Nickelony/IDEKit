using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Verifies the runner against real child processes: a complete run with captured output, the terminal outcomes
/// for timeouts and cancellation, encoded output, the drain regression for output larger than the operating-system
/// pipe buffer, the bounded drain when a detached descendant holds the redirected pipe, and the process-tree
/// termination that reaps a background descendant.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
public class ProcessRunnerRealProcessTests
{
	// The descendant's work delay doubles as the absence window's lower bound; the start deadline is generous
	// because a cold descendant shell takes a moment to launch on a loaded machine.
	private static readonly TimeSpan DescendantWorkDelay = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan DescendantStartDeadline = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan DescendantAbsenceMargin = TimeSpan.FromSeconds(2);

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_RealProcess_CompletesAndCapturesOutput()
	{
		var runner = new ProcessRunner();

		ProcessRunResult result = await runner.RunAsync(TestRequests.Default with
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.Echo("hello from a real process"),
			RedirectStandardOutput = true,
			Timeout = TimeSpan.FromSeconds(30),
		});

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(0, result.ExitCode);
		Assert.IsNotNull(result.StandardOutput);
		Assert.AreEqual("hello from a real process", result.StandardOutput.Trim());
	}

	[TestMethod]
	[Timeout(30_000)]
	public void Run_RealProcessOutputBeyondTheCaptureBound_ReportsBoundedTruncatedOutput()
	{
		var runner = new ProcessRunner(new ProcessRunnerOptions { MaxCapturedCharactersPerStream = 8 });

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.Echo("0123456789"),
			RedirectStandardOutput = true,
			Timeout = TimeSpan.FromSeconds(30),
		});

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual("01234567", result.StandardOutput);
		Assert.IsTrue(result.StandardOutputTruncated);
		Assert.IsFalse(result.StandardErrorTruncated);
	}

	[TestMethod]
	[Timeout(30_000)]
	public void Run_RealProcessTimeout_TerminatesAndConfirmsTheExit()
	{
		var runner = new ProcessRunner();

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.Sleep(TimeSpan.FromSeconds(30)),
			Timeout = TimeSpan.FromMilliseconds(300),
		});

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);

		// The termination confirmation observes the killed child and reports the failure exit code of the
		// forced termination instead of leaving the exit unobserved.
		Assert.AreNotEqual(0, RequireObservedExitCode(result), "A stored process must report a failure exit code.");
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_RealProcessCancellation_TerminatesAndConfirmsTheExit()
	{
		var runner = new ProcessRunner();
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

		ProcessRunResult result = await runner.RunAsync(
			TestRequests.Default with
			{
				FileName = TestCommands.ShellFileName,
				RawArguments = TestCommands.Sleep(TimeSpan.FromSeconds(30)),
			},
			cancellation.Token);

		Assert.AreEqual(ProcessRunOutcome.Canceled, result.Outcome);
		Assert.AreNotEqual(0, RequireObservedExitCode(result), "A stored process must report a failure exit code.");
	}

	[TestMethod]
	[Timeout(30_000)]
	public void Run_ExplicitStandardOutputEncoding_DecodesNonAsciiOutput()
	{
		// The child copies raw UTF-8 bytes, so the decoding under test is the runner's explicit encoding and not a
		// code-page side effect of the shell.
		const string Text = "compilation caf\u00E9";
		string payloadFile = Path.Combine(
			Path.GetTempPath(),
			"NickelonyProcessesEncoding_" + Guid.NewGuid().ToString("N") + ".txt");
		File.WriteAllBytes(payloadFile, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Text));

		try
		{
			var runner = new ProcessRunner();

			ProcessRunResult result = runner.Run(TestRequests.Default with
			{
				FileName = TestCommands.ShellFileName,
				RawArguments = TestCommands.PrintFile(payloadFile),
				RedirectStandardOutput = true,
				StandardOutputEncoding = Encoding.UTF8,
				Timeout = TimeSpan.FromSeconds(30),
			});

			Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
			Assert.IsNotNull(result.StandardOutput);
			Assert.AreEqual(Text, result.StandardOutput.Trim());
		}
		finally
		{
			File.Delete(payloadFile);
		}
	}

	[TestMethod]
	[Timeout(60_000)]
	public void Run_RedirectedOutputExceedingPipeBuffer_DrainsWhileRunningAndReturnsFullOutput()
	{
		// A child writing more than the operating-system pipe buffer (4-64 KB) deadlocks when the
		// runner waits for exit before reading the redirected streams.
		const int LineCount = 20_000;
		const string Payload = "0123456789012345678901234567890123456789012345678901234567890123";

		string rawArguments = OperatingSystem.IsWindows()
			? $"/c \"for /L %i in (1,1,{LineCount}) do @echo {Payload}\""
			: "-c \"i=0; while [ $i -lt " + LineCount + " ]; do echo " + Payload + "; i=$((i+1)); done\"";

		var runner = new ProcessRunner();

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = rawArguments,
			RedirectStandardOutput = true,
			Timeout = TimeSpan.FromSeconds(30),
		});

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome, "The run did not exit; the child most likely blocked on a full output pipe.");
		Assert.AreEqual(0, result.ExitCode);
		Assert.IsNotNull(result.StandardOutput);
		Assert.IsTrue(result.StandardOutput.Length > 100_000, $"Expected more than 100,000 captured characters but got {result.StandardOutput.Length}.");
	}

	[TestMethod]
	[Timeout(30_000)]
	public void Run_DescendantHoldsRedirectedPipe_CompletesWithinDrainGraceAndReportsOutputUnavailable()
	{
		// The shell exits immediately, but the detached child inherits the redirected pipe and keeps it open
		// for several seconds. The run must not wait for the pipe to close; the output is reported as unavailable.
		var runner = new ProcessRunner(
			new ProcessLauncher(),
			new ProcessRunnerOptions { OutputDrainGracePeriod = TimeSpan.FromMilliseconds(500) });
		var stopwatch = Stopwatch.StartNew();

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.StartAndDetachBackgroundChild(TimeSpan.FromSeconds(5)),
			RedirectStandardOutput = true,
		});

		stopwatch.Stop();

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(0, result.ExitCode);
		Assert.IsNull(result.StandardOutput);
		Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(4), $"The run outlived the drain grace period: {stopwatch.Elapsed}.");
	}

	[TestMethod]
	[Timeout(30_000)]
	public void Run_StrictEncodingRejectsCapturedBytes_ReportsOutputUnavailable()
	{
		// The child copies a raw byte that a strict UTF-8 decoder rejects, so the completed run must report the
		// capture as unavailable instead of failing with the decoder exception.
		string payloadFile = Path.Combine(
			Path.GetTempPath(),
			"NickelonyProcessesStrictEncoding_" + Guid.NewGuid().ToString("N") + ".bin");
		File.WriteAllBytes(payloadFile, [0x41, 0xFF, 0x42]);

		try
		{
			var runner = new ProcessRunner();

			ProcessRunResult result = runner.Run(TestRequests.Default with
			{
				FileName = TestCommands.ShellFileName,
				RawArguments = TestCommands.PrintFile(payloadFile),
				RedirectStandardOutput = true,
				StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
				Timeout = TimeSpan.FromSeconds(30),
			});

			Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
			Assert.AreEqual(0, result.ExitCode);
			Assert.IsNull(result.StandardOutput);
		}
		finally
		{
			File.Delete(payloadFile);
		}
	}

	[TestMethod]
	[Timeout(30_000)]
	public void Run_SubMillisecondTimeout_RealProcess_TerminatesInsteadOfWaiting()
	{
		// A timeout shorter than one millisecond never waits: the runner probes once and terminates the running
		// child instead of waiting for it. The zero-grace path is exercised against a real process so it is not
		// pinned by a fake whose ordering may differ from the operating system's wait.
		var runner = new ProcessRunner();

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.Sleep(TimeSpan.FromSeconds(30)),
			Timeout = TimeSpan.FromTicks(10),
		});

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);
		Assert.AreNotEqual(0, RequireObservedExitCode(result), "A stored process must report a failure exit code.");
	}

	[TestMethod]
	[Timeout(30_000)]
	public void Run_RealProcessFloodingBothStreams_DrainsBothWithoutDeadlock()
	{
		// Both redirected streams carry more than the operating-system pipe buffer. A runner that drained the
		// streams one after another would deadlock here: the child blocks writing the second stream while the
		// first capture waits for a pipe the child never closes, so the run never exits.
		const string Payload = "0123456789012345678901234567890123456789012345678901234567890123";
		const int LineCount = 6_000;

		string standardOutputFile = WritePayloadFile("stdout", Payload, LineCount);
		string standardErrorFile = WritePayloadFile("stderr", Payload, LineCount);

		try
		{
			var runner = new ProcessRunner();

			ProcessRunResult result = runner.Run(TestRequests.Default with
			{
				FileName = TestCommands.ShellFileName,
				RawArguments = TestCommands.PrintFilesToBothStreams(standardOutputFile, standardErrorFile),
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				Timeout = TimeSpan.FromSeconds(30),
			});

			Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome, "The run did not exit; the child most likely blocked on a full output pipe.");
			Assert.AreEqual(0, result.ExitCode);
			Assert.IsNotNull(result.StandardOutput);
			Assert.IsNotNull(result.StandardError);
			Assert.IsTrue(result.StandardOutput.Length > 100_000, $"Expected a flooded standard output but got {result.StandardOutput.Length} characters.");
			Assert.IsTrue(result.StandardError.Length > 100_000, $"Expected a flooded standard error but got {result.StandardError.Length} characters.");
		}
		finally
		{
			File.Delete(standardOutputFile);
			File.Delete(standardErrorFile);
		}
	}

	[TestMethod]
	[Timeout(60_000)]
	public async Task KillEntireProcessTree_RealGrandchild_IsTerminatedWithTheTree()
	{
		// The launched shell starts a background descendant that records its start, sleeps for the work delay,
		// and would then record its completion; the shell stays alive itself. Terminating the whole tree must
		// kill both, so the descendant can never record its completion.
		string startedMarkerPath = CreateMarkerPath("started");
		string finishedMarkerPath = CreateMarkerPath("finished");

		var runner = new ProcessRunner();
		IProcessHandle handle = runner.Start(TestRequests.Default with
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = TestCommands.StartAndHoldBackgroundGrandchild(DescendantWorkDelay, startedMarkerPath, finishedMarkerPath),
		});

		try
		{
			await TestPolling.UntilAsync(
				() => File.Exists(startedMarkerPath),
				DescendantStartDeadline,
				"The descendant process did not start within the deadline.");

			handle.KillEntireProcessTree();

			// The work delay is the absence window's lower bound: a descendant that survived the tree
			// termination records its completion within the window, so an absence proves it was reaped.
			bool survived = await TestPolling.ForConditionAsync(
				() => File.Exists(finishedMarkerPath),
				DescendantWorkDelay + DescendantAbsenceMargin);

			Assert.IsFalse(survived, "The descendant survived the process-tree termination.");
		}
		finally
		{
			TerminateQuietly(handle);
			handle.Dispose();
			DeleteMarkerQuietly(startedMarkerPath);
			DeleteMarkerQuietly(finishedMarkerPath);
		}
	}

	/// <summary>
	/// Requires an observed exit code and returns it.
	/// </summary>
	/// <param name="result">The run result.</param>
	/// <returns>The observed exit code.</returns>
	/// <exception cref="AssertFailedException">The run did not observe an exit code.</exception>
	private static int RequireObservedExitCode(ProcessRunResult result)
		=> result.ExitCode ?? throw new AssertFailedException("The run must report the observed exit code of the terminated child.");

	/// <summary>
	/// Creates a temporary marker path with the given role in its name.
	/// </summary>
	/// <param name="role">The role used in the file name.</param>
	/// <returns>The marker file path.</returns>
	private static string CreateMarkerPath(string role)
		=> Path.Combine(Path.GetTempPath(), $"NickelonyProcessesTree_{role}_{Guid.NewGuid():N}.txt");

	/// <summary>
	/// Terminates the handle's process tree as best-effort cleanup.
	/// </summary>
	/// <param name="handle">The handle to terminate.</param>
	private static void TerminateQuietly(IProcessHandle handle)
	{
		try
		{
			handle.KillEntireProcessTree();
		}
		catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
		{
			// The cleanup is best-effort; the descendant assertion has already run.
		}
	}

	/// <summary>
	/// Deletes a temporary marker file as best-effort cleanup.
	/// </summary>
	/// <param name="markerPath">The marker file to delete.</param>
	private static void DeleteMarkerQuietly(string markerPath)
	{
		try
		{
			File.Delete(markerPath);
		}
		catch (IOException)
		{
			// A transient file lock must not replace the assertion the test already made.
		}
	}

	/// <summary>
	/// Writes a payload file of the given line count and returns its path.
	/// </summary>
	/// <param name="name">The role used in the file name.</param>
	/// <param name="line">The repeated line.</param>
	/// <param name="lineCount">The number of repetitions.</param>
	/// <returns>The payload file path.</returns>
	private static string WritePayloadFile(string name, string line, int lineCount)
	{
		string filePath = Path.Combine(
			Path.GetTempPath(),
			$"NickelonyProcessesPayload_{name}_{Guid.NewGuid():N}.txt");

		var builder = new StringBuilder(lineCount * (line.Length + Environment.NewLine.Length));

		for (int index = 0; index < lineCount; index++)
			builder.AppendLine(line);

		File.WriteAllText(filePath, builder.ToString());
		return filePath;
	}
}
