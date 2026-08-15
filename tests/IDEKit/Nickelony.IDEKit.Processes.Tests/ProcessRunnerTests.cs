using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Verifies the run orchestration of <see cref="ProcessRunner"/>: completion, the outcome values, the timeout and
/// cancellation paths, the zero-timeout probe, termination fallback and unobserved exits, bounded output capture,
/// and request pass-through. Real-process behavior is verified in <see cref="ProcessRunnerRealProcessTests"/>.
/// </summary>
[TestClass]
public class ProcessRunnerTests
{
	private static readonly TimeSpan s_grace = TimeSpan.FromMilliseconds(250);
	private static readonly ProcessRunnerOptions s_graceOptions = new()
	{
		TerminationGracePeriod = s_grace,
		OutputDrainGracePeriod = s_grace,
	};

	// The delayed-exit probe proves the runner awaits a slow kill confirmation instead of assuming the exit.
	// Its simulated confirmation delay (100 ms) must stay far below the grace cap, so a loaded machine, where
	// the delay's continuation can be scheduled late, cannot push the confirmation past the cap.
	private static readonly ProcessRunnerOptions s_delayedExitOptions = new()
	{
		TerminationGracePeriod = TimeSpan.FromSeconds(5),
		OutputDrainGracePeriod = TimeSpan.FromSeconds(5),
	};

	[TestMethod]
	public void Run_WithoutTimeoutOrCancellation_WaitsUntilExitAndReturnsResult()
	{
		var handle = new FakeProcessHandle { ExitCodeValue = 7 };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(TestRequests.Default);

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(7, result.ExitCode);
		Assert.AreEqual(1, handle.WaitForExitAsyncCalls);
		Assert.AreEqual(0, handle.KillEntireProcessTreeCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public async Task RunAsync_ExitObserved_ReturnsResult()
	{
		var handle = new FakeProcessHandle { ExitCodeValue = 7 };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = await runner.RunAsync(TestRequests.Default);

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(7, result.ExitCode);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_ExitObserved_ReportsTheProcessIdAndSucceeded()
	{
		var handle = new FakeProcessHandle { ExitCodeValue = 0, ProcessId = 4242 };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(TestRequests.Default);

		Assert.AreEqual(4242, result.ProcessId);
		Assert.IsTrue(result.Succeeded);
	}

	[TestMethod]
	public void Run_FailureExitCode_IsNotSucceeded()
	{
		var handle = new FakeProcessHandle { ExitCodeValue = 3 };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(TestRequests.Default);

		Assert.AreEqual(3, result.ExitCode);
		Assert.IsFalse(result.Succeeded);
	}

	[TestMethod]
	public void Run_UnobservedExit_StillReportsTheProcessId()
	{
		var handle = new FakeProcessHandle { ExitsImmediately = false, CompletesExitOnKill = false };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with { Timeout = TimeSpan.FromMilliseconds(50) });

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);
		Assert.IsNull(result.ExitCode);
		Assert.AreEqual(4242, result.ProcessId);
		Assert.IsFalse(result.Succeeded);
	}

	[TestMethod]
	public void Run_Timeout_ProcessExitsInTime_DoesNotTerminate()
	{
		var handle = new FakeProcessHandle();
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with { Timeout = TimeSpan.FromSeconds(1) });

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(0, handle.KillEntireProcessTreeCalls);
		Assert.AreEqual(0, handle.KillCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_TimeoutExceeded_TerminatesProcessTreeAndReportsTimedOut()
	{
		var handle = new FakeProcessHandle { ExitsImmediately = false };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with { Timeout = TimeSpan.FromMilliseconds(50) });

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);
		Assert.AreEqual(0, result.ExitCode);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
		Assert.AreEqual(0, handle.KillCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_TimeoutExceeded_WaitsForTheExitBeforeReportingIt()
	{
		// The exit completes only after the kill delay, so reading the exit code proves that the runner awaited
		// the termination confirmation within the grace period instead of assuming the exit.
		var handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			ExitCodeValue = 7,
			KillExitDelay = TimeSpan.FromMilliseconds(100),
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_delayedExitOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with { Timeout = TimeSpan.FromMilliseconds(50) });

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);
		Assert.AreEqual(7, result.ExitCode);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_TimeoutExceeded_CapturesOutputWhenTheExitIsConfirmed()
	{
		var handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			StandardOutputText = "late output",
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			Timeout = TimeSpan.FromMilliseconds(50),
			RedirectStandardOutput = true,
		});

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);
		Assert.AreEqual(0, result.ExitCode);
		Assert.AreEqual("late output", result.StandardOutput);
	}

	[TestMethod]
	[DynamicData(nameof(TreeKillFailures), DynamicDataDisplayName = nameof(FaultDisplayName))]
	public void Run_TreeKillFails_FallsBackToSingleKill(Exception treeKillFailure)
	{
		var handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			OnKillEntireProcessTree = () => throw treeKillFailure,
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with { Timeout = TimeSpan.FromMilliseconds(50) });

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
		Assert.AreEqual(1, handle.KillCalls);
		Assert.AreEqual(0, result.ExitCode);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_TerminationFails_ReportsUnobservedExit()
	{
		var handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			OnKillEntireProcessTree = () => throw new InvalidOperationException("tree kill failed"),
			OnKill = () => throw new InvalidOperationException("kill failed"),
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			Timeout = TimeSpan.FromMilliseconds(50),
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		});

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);
		Assert.IsNull(result.ExitCode);
		Assert.IsNull(result.StandardOutput);
		Assert.IsNull(result.StandardError);
		Assert.AreEqual(0, handle.StandardOutputReadCalls);
		Assert.AreEqual(0, handle.StandardErrorReadCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public async Task RunAsync_PreCanceledToken_DoesNotStartProcessAndReportsCanceled()
	{
		var launcher = new FakeProcessLauncher(_ => new FakeProcessHandle());
		var runner = new ProcessRunner(launcher);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		ProcessRunResult result = await runner.RunAsync(TestRequests.Default, cancellation.Token);

		Assert.AreEqual(ProcessRunOutcome.CanceledBeforeStart, result.Outcome);
		Assert.IsNull(result.ExitCode);
		Assert.AreEqual(0, launcher.StartCalls);
	}

	[TestMethod]
	public async Task RunAsync_CanceledWhileWaiting_TerminatesProcessTreeAndReportsCanceled()
	{
		FakeProcessHandle handle = null!;
		using var cancellation = new CancellationTokenSource();
		handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			OnWaitForExitAsync = cancellation.Cancel,
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = await runner.RunAsync(TestRequests.Default, cancellation.Token);

		Assert.AreEqual(ProcessRunOutcome.Canceled, result.Outcome);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_ZeroTimeout_ProcessAlreadyExited_ReportsExitWithoutTermination()
	{
		var handle = new FakeProcessHandle { ExitCodeValue = 3 };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with { Timeout = TimeSpan.Zero });

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(3, result.ExitCode);
		Assert.AreEqual(1, handle.TimedWaitForExitCalls);
		Assert.AreEqual(0, handle.WaitForExitAsyncCalls);
		Assert.AreEqual(0, handle.KillEntireProcessTreeCalls);
	}

	[TestMethod]
	public void Run_ZeroTimeout_ProcessStillRunning_TerminatesAndReportsTimedOut()
	{
		var handle = new FakeProcessHandle { ExitsImmediately = false };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with { Timeout = TimeSpan.Zero });

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);
		Assert.AreEqual(0, result.ExitCode);
		Assert.AreEqual(1, handle.TimedWaitForExitCalls);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
	}

	[TestMethod]
	public void Run_LauncherWithoutHandle_ReportsNoProcessHandle()
	{
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => null));

		ProcessRunResult result = runner.Run(TestRequests.Default);

		Assert.AreEqual(ProcessRunOutcome.NoProcessHandle, result.Outcome);
		Assert.IsNull(result.ExitCode);
		Assert.IsNull(result.ProcessId);
		Assert.IsFalse(result.Succeeded);
	}

	[TestMethod]
	public void Run_StartException_Propagates()
	{
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => throw new InvalidOperationException("process start failed")));

		Assert.ThrowsExactly<InvalidOperationException>(() => runner.Run(TestRequests.Default));
	}

	[TestMethod]
	public void Run_WhenWaitThrows_PropagatesTerminatesAndDisposes()
	{
		var handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			OnWaitForExitAsync = () => throw new InvalidOperationException("process wait failed"),
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		Assert.ThrowsExactly<InvalidOperationException>(() => runner.Run(TestRequests.Default));

		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_CapturesStandardOutputAndError()
	{
		var handle = new FakeProcessHandle { StandardOutputText = "output", StandardErrorText = "error" };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		});

		Assert.AreEqual("output", result.StandardOutput);
		Assert.AreEqual("error", result.StandardError);
		Assert.AreEqual(1, handle.StandardOutputReadCalls);
		Assert.AreEqual(1, handle.StandardErrorReadCalls);
	}

	[TestMethod]
	public void Run_OutputNotRequested_DoesNotReadCapturedStreams()
	{
		var handle = new FakeProcessHandle { StandardOutputText = "output", StandardErrorText = "error" };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(TestRequests.Default);

		Assert.IsNull(result.StandardOutput);
		Assert.IsNull(result.StandardError);
		Assert.AreEqual(0, handle.StandardOutputReadCalls);
		Assert.AreEqual(0, handle.StandardErrorReadCalls);
	}

	[TestMethod]
	[TestCategory(TestCategories.Performance)]
	public void Run_OutputDrainStaysPending_ReportsOutputUnavailableWithinGrace()
	{
		var handle = new FakeProcessHandle { HoldsCapturedOutput = true };
		var runner = new ProcessRunner(
			new FakeProcessLauncher(_ => handle),
			new ProcessRunnerOptions
			{
				TerminationGracePeriod = s_grace,
				OutputDrainGracePeriod = TimeSpan.FromMilliseconds(50),
			});
		var stopwatch = Stopwatch.StartNew();

		ProcessRunResult result = runner.Run(TestRequests.Default with { RedirectStandardOutput = true });

		stopwatch.Stop();

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(0, result.ExitCode);
		Assert.IsNull(result.StandardOutput);
		Assert.AreEqual(1, handle.StandardOutputReadCalls);
		Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(40), $"The capture ended before the drain grace period: {stopwatch.Elapsed}.");
		Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"The capture was not bounded by the drain grace period: {stopwatch.Elapsed}.");
	}

	[TestMethod]
	[TestCategory(TestCategories.Performance)]
	public void Run_BothStreamsHeld_BoundsTheCaptureByASingleDrainPeriod()
	{
		var handle = new FakeProcessHandle { HoldsCapturedOutput = true };
		var runner = new ProcessRunner(
			new FakeProcessLauncher(_ => handle),
			new ProcessRunnerOptions
			{
				TerminationGracePeriod = s_grace,
				OutputDrainGracePeriod = TimeSpan.FromMilliseconds(300),
			});
		var stopwatch = Stopwatch.StartNew();

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		});

		stopwatch.Stop();

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.IsNull(result.StandardOutput);
		Assert.IsNull(result.StandardError);
		Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(250), $"The capture ended before the drain grace period: {stopwatch.Elapsed}.");
		Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromMilliseconds(550), $"The captures did not share one drain grace period: {stopwatch.Elapsed}.");
	}

	[TestMethod]
	public void Run_RequestPassedToLauncherUnchanged()
	{
		var launcher = new FakeProcessLauncher(_ => new FakeProcessHandle());
		var runner = new ProcessRunner(launcher);

		ProcessRunRequest request = TestRequests.Default with
		{
			WorkingDirectory = "work-dir",
			EnvironmentVariables = new Dictionary<string, string> { ["KEY"] = "value" },
			StandardOutputEncoding = Encoding.UTF8,
			RedirectStandardOutput = true,
			Timeout = TimeSpan.FromSeconds(5),
		};

		runner.Run(request);

		Assert.AreSame(request, launcher.LastRequest);
		Assert.IsNotNull(launcher.LastRequest);
		Assert.AreEqual("work-dir", launcher.LastRequest.WorkingDirectory);
		Assert.AreEqual("value", launcher.LastRequest.EnvironmentVariables["KEY"]);
		Assert.AreEqual(Encoding.UTF8, launcher.LastRequest.StandardOutputEncoding);
		Assert.IsTrue(launcher.LastRequest.RedirectStandardOutput);
		Assert.AreEqual(TimeSpan.FromSeconds(5), launcher.LastRequest.Timeout);
	}

	[TestMethod]
	public void Start_ReturnsHandleFromLauncher()
	{
		var handle = new FakeProcessHandle();
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		IProcessHandle started = runner.Start(TestRequests.Default);

		Assert.AreSame(handle, started);
	}

	[TestMethod]
	public void Start_LauncherReturnsNull_ThrowsInvalidOperationException()
	{
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => null));

		Assert.ThrowsExactly<InvalidOperationException>(() => runner.Start(TestRequests.Default));
	}

	[TestMethod]
	public void Run_ZeroTimeout_TokenCanceledDuringLaunch_ReportsCanceled()
	{
		using var cancellation = new CancellationTokenSource();
		var handle = new FakeProcessHandle { ExitsImmediately = false };
		var runner = new ProcessRunner(
			new FakeProcessLauncher(_ =>
			{
				cancellation.Cancel();
				return handle;
			}),
			s_graceOptions);

		ProcessRunResult result = runner.Run(
			TestRequests.Default with { Timeout = TimeSpan.Zero },
			cancellation.Token);

		Assert.AreEqual(ProcessRunOutcome.Canceled, result.Outcome);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
	}

	[TestMethod]
	public async Task RunAsync_CanceledWhileWaitingWithTimeout_ReportsCanceled()
	{
		FakeProcessHandle handle = null!;
		using var cancellation = new CancellationTokenSource();
		handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			OnWaitForExitAsync = cancellation.Cancel,
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = await runner.RunAsync(
			TestRequests.Default with { Timeout = TimeSpan.FromSeconds(30) },
			cancellation.Token);

		Assert.AreEqual(ProcessRunOutcome.Canceled, result.Outcome);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
	}

	[TestMethod]
	public void Run_InfiniteTimeout_WaitsUntilExit()
	{
		var handle = new FakeProcessHandle { ExitCodeValue = 4 };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(TestRequests.Default with { Timeout = Timeout.InfiniteTimeSpan });

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(4, result.ExitCode);
		Assert.AreEqual(1, handle.WaitForExitAsyncCalls);
		Assert.AreEqual(0, handle.KillEntireProcessTreeCalls);
	}

	[TestMethod]
	public void Run_WhenWaitAndTerminationFail_PropagatesTheWaitFailure()
	{
		var handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			OnKillEntireProcessTree = () => throw new AggregateException(
				new InvalidOperationException("tree kill failed")),
			OnKill = () => throw new AggregateException(
				new InvalidOperationException("kill failed")),
			OnWaitForExitAsync = () => throw new InvalidOperationException("process wait failed"),
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		Assert.ThrowsExactly<InvalidOperationException>(() => runner.Run(TestRequests.Default));

		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
		Assert.AreEqual(1, handle.KillCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_TerminationSucceedsButExitStaysUnobserved_ReportsUnobservedExit()
	{
		var handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			CompletesExitOnKill = false,
			HoldsCapturedOutput = true,
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			Timeout = TimeSpan.FromMilliseconds(50),
			RedirectStandardOutput = true,
		});

		Assert.AreEqual(ProcessRunOutcome.TimedOut, result.Outcome);
		Assert.IsNull(result.ExitCode);
		Assert.IsNull(result.StandardOutput);
		Assert.AreEqual(1, handle.KillEntireProcessTreeCalls);
		Assert.AreEqual(0, handle.StandardOutputReadCalls);
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	[TestCategory(TestCategories.Performance)]
	public async Task RunAsync_TokenCanceledDuringOutputDrain_OutcomeStaysExited()
	{
		using var cancellation = new CancellationTokenSource();
		var handle = new FakeProcessHandle
		{
			HoldsCapturedOutput = true,
			OnWaitForExitAsync = () => cancellation.CancelAfter(TimeSpan.FromMilliseconds(100)),
		};
		var runner = new ProcessRunner(
			new FakeProcessLauncher(_ => handle),
			new ProcessRunnerOptions
			{
				TerminationGracePeriod = s_grace,
				OutputDrainGracePeriod = TimeSpan.FromMilliseconds(400),
			});
		var stopwatch = Stopwatch.StartNew();

		ProcessRunResult result = await runner.RunAsync(
			TestRequests.Default with { RedirectStandardOutput = true },
			cancellation.Token);

		stopwatch.Stop();

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(0, result.ExitCode);
		Assert.IsNull(result.StandardOutput);
		Assert.AreEqual(0, handle.KillEntireProcessTreeCalls);
		Assert.IsTrue(handle.Disposed);

		// The capture keeps its own drain grace period instead of the caller's token, so the run must outlive the
		// cancellation before it reports the output as unavailable.
		Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(300), $"The capture ended before the drain grace period: {stopwatch.Elapsed}.");
	}

	[TestMethod]
	[DynamicData(nameof(CaptureFaults), DynamicDataDisplayName = nameof(FaultDisplayName))]
	public void Run_OutputDrainFails_ReportsOutputUnavailable(Exception captureFault)
	{
		var handle = new FakeProcessHandle { StandardOutputFault = captureFault };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		ProcessRunResult result = runner.Run(TestRequests.Default with { RedirectStandardOutput = true });

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(0, result.ExitCode);
		Assert.IsNull(result.StandardOutput);
		Assert.AreEqual(1, handle.StandardOutputReadCalls);
	}

	[TestMethod]
	public void Run_OutputDrainFailsWithOutOfMemory_Propagates()
	{
		var handle = new FakeProcessHandle { StandardOutputFault = new OutOfMemoryException() };
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		Assert.ThrowsExactly<OutOfMemoryException>(
			() => runner.Run(TestRequests.Default with { RedirectStandardOutput = true }));
	}

	[TestMethod]
	public void Run_TreeKillFailsWithAnEmptyAggregate_PropagatesInsteadOfFallingBack()
	{
		// An aggregate without inner failures does not describe a partial tree failure, so it is not treated
		// as a termination failure and must not trigger the single-kill fallback.
		var handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			OnKillEntireProcessTree = () => throw new AggregateException("tree kill failed"),
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		Assert.ThrowsExactly<AggregateException>(
			() => runner.Run(TestRequests.Default with { Timeout = TimeSpan.FromMilliseconds(50) }));

		Assert.IsTrue(handle.KillEntireProcessTreeCalls > 0, "The tree kill must have been attempted.");
		Assert.AreEqual(0, handle.KillCalls, "The single-kill fallback must not run for an unrecognized failure.");
		Assert.IsTrue(handle.Disposed);
	}

	[TestMethod]
	public void Run_TreeKillReportsDisposal_PropagatesInsteadOfFallingBack()
	{
		// A disposed handle is a disposal signal, not a termination failure: it must not be swallowed as "the
		// process could not be terminated" and must not fall back to a second kill.
		var handle = new FakeProcessHandle
		{
			ExitsImmediately = false,
			OnKillEntireProcessTree = () => throw new ObjectDisposedException("handle"),
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle), s_graceOptions);

		Assert.ThrowsExactly<ObjectDisposedException>(
			() => runner.Run(TestRequests.Default with { Timeout = TimeSpan.FromMilliseconds(50) }));

		Assert.IsTrue(handle.KillEntireProcessTreeCalls > 0, "The tree kill must have been attempted.");
		Assert.AreEqual(0, handle.KillCalls, "The single-kill fallback must not run for a disposal signal.");
	}

	[TestMethod]
	public void Run_CaptureReachedTheBound_ReportsTruncation()
	{
		var handle = new FakeProcessHandle
		{
			StandardOutputText = "out",
			StandardErrorText = "err",
			StandardOutputTruncated = true,
		};
		var runner = new ProcessRunner(new FakeProcessLauncher(_ => handle));

		ProcessRunResult result = runner.Run(TestRequests.Default with
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		});

		Assert.AreEqual("out", result.StandardOutput);
		Assert.IsTrue(result.StandardOutputTruncated);
		Assert.AreEqual("err", result.StandardError);
		Assert.IsFalse(result.StandardErrorTruncated);
	}

	[TestMethod]
	[TestCategory(TestCategories.Performance)]
	public void Run_CaptureUnavailable_DoesNotReportTruncation()
	{
		// A capture that did not finish within its grace period reports no output, so it has nothing to
		// describe as truncated even though the drain already hit the bound.
		var handle = new FakeProcessHandle { HoldsCapturedOutput = true, StandardOutputTruncated = true };
		var runner = new ProcessRunner(
			new FakeProcessLauncher(_ => handle),
			new ProcessRunnerOptions { OutputDrainGracePeriod = TimeSpan.FromMilliseconds(50) });

		ProcessRunResult result = runner.Run(TestRequests.Default with { RedirectStandardOutput = true });

		Assert.IsNull(result.StandardOutput);
		Assert.IsFalse(result.StandardOutputTruncated);
	}

	[TestMethod]
	public void Run_PassesTheConfiguredOptionsToTheLauncher()
	{
		var launcher = new FakeProcessLauncher(_ => new FakeProcessHandle());
		var options = new ProcessRunnerOptions { MaxCapturedCharactersPerStream = 128 };
		var runner = new ProcessRunner(launcher, options);

		runner.Run(TestRequests.Default);

		Assert.AreSame(options, launcher.LastOptions);
	}

	[TestMethod]
	public void ProcessRunner_NullOptions_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new ProcessRunner((ProcessRunnerOptions)null!));
	}

	private static IEnumerable<object[]> CaptureFaults()
	{
		yield return [new IOException("The pipe has been ended.")];
		yield return [new DecoderFallbackException("The decoder rejected a captured byte sequence.")];
		yield return [new InvalidDataException("The capture failed.")];
	}

	private static IEnumerable<object[]> TreeKillFailures()
	{
		yield return [new InvalidOperationException("tree kill failed")];
		yield return [new NotSupportedException("tree kill is not supported")];
		yield return [new AggregateException("tree kill failed", new InvalidOperationException("tree kill failed"))];
	}

	public static string FaultDisplayName(MethodInfo methodInfo, object[] values)
		=> $"{methodInfo.Name} ({values[0].GetType().Name})";
}
