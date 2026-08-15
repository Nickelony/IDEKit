using System.Diagnostics;

namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Verifies the process-handle adapter over real short-lived processes: capture accessors, redirection
/// requirements, exit observation, cancellation, and the disposal contract.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
public class ProcessHandleTests
{
	private const int WaitTimeoutMilliseconds = 10_000;

	[TestMethod]
	public void StandardOutput_RedirectionEnabled_ReturnsCapturedText()
	{
		using IProcessHandle handle = StartShell(TestCommands.Echo("captured"), redirectStandardOutput: true);

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
		Assert.AreEqual("captured", handle.StandardOutput.Trim());
	}

	[TestMethod]
	public async Task ReadStandardOutputAsync_RedirectionEnabled_ReturnsCapturedText()
	{
		using IProcessHandle handle = StartShell(TestCommands.Echo("captured async"), redirectStandardOutput: true);

		Assert.AreEqual("captured async", (await handle.ReadStandardOutputAsync()).Trim());
	}

	[TestMethod]
	public async Task ReadStandardErrorAsync_RedirectionEnabled_ReturnsCapturedText()
	{
		using IProcessHandle handle = StartShell(TestCommands.WriteToStandardError("captured async error"), redirectStandardError: true);

		Assert.AreEqual("captured async error", (await handle.ReadStandardErrorAsync()).Trim());
	}

	[TestMethod]
	public void WaitForExit_ProcessExits_ObservesTheExit()
	{
		using IProcessHandle handle = StartShell(TestCommands.Echo("blocking"), redirectStandardOutput: true);

		handle.WaitForExit();

		Assert.AreEqual(0, handle.ExitCode);
		Assert.AreEqual("blocking", handle.StandardOutput.Trim());
	}

	[TestMethod]
	public void WaitForExit_ProcessStillRunning_ReturnsFalseWhenTheTimeoutElapses()
	{
		using IProcessHandle handle = StartShell(TestCommands.Sleep(TimeSpan.FromSeconds(30)));

		try
		{
			// The timed wait is a probe: a running process reports false instead of blocking, unlike the
			// parameterless wait, so a caller can observe activity without terminating the child.
			Assert.IsFalse(handle.WaitForExit(200));
		}
		finally
		{
			handle.KillEntireProcessTree();
		}
	}

	[TestMethod]
	public void CaptureAccessors_StandardOutputRedirectionDisabled_ThrowInvalidOperationException()
	{
		using IProcessHandle handle = StartShell(TestCommands.Echo("ignored"));

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = handle.StandardOutput);
		Assert.ThrowsExactly<InvalidOperationException>(() => handle.ReadStandardOutputAsync());
	}

	[TestMethod]
	public void CaptureAccessors_StandardErrorRedirectionDisabled_ThrowInvalidOperationException()
	{
		using IProcessHandle handle = StartShell(TestCommands.Echo("ignored"));

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = handle.StandardError);
		Assert.ThrowsExactly<InvalidOperationException>(() => handle.ReadStandardErrorAsync());
	}

	[TestMethod]
	public void ExitCode_BeforeExit_ThrowsInvalidOperationException()
	{
		using IProcessHandle handle = StartShell(TestCommands.Sleep(TimeSpan.FromSeconds(30)));

		try
		{
			Assert.ThrowsExactly<InvalidOperationException>(() => _ = handle.ExitCode);
		}
		finally
		{
			handle.KillEntireProcessTree();
		}
	}

	[TestMethod]
	public void WaitForExit_ValueBelowInfinite_ThrowsArgumentOutOfRangeException()
	{
		using IProcessHandle handle = StartShell(TestCommands.Sleep(TimeSpan.FromSeconds(30)));

		try
		{
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => handle.WaitForExit(-2));
		}
		finally
		{
			handle.KillEntireProcessTree();
		}

		// The argument is validated before the disposal guard, so an invalid timeout is a range error even on
		// a disposed handle, while a valid timeout still reports the disposal contract.
		handle.Dispose();
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => handle.WaitForExit(-2));
		Assert.ThrowsExactly<ObjectDisposedException>(() => handle.WaitForExit(0));
	}

	[TestMethod]
	public async Task WaitForExitAsync_Canceled_ThrowsAndProcessKeepsRunning()
	{
		using IProcessHandle handle = StartShell(TestCommands.Sleep(TimeSpan.FromSeconds(30)));
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

		// The platform reports a canceled wait as TaskCanceledException, a subclass of OperationCanceledException.
		await Assert.ThrowsAsync<OperationCanceledException>(() => handle.WaitForExitAsync(cancellation.Token));

		// The canceled wait must not have terminated the process or observed its exit.
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = handle.ExitCode);

		handle.KillEntireProcessTree();
		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
	}

	[TestMethod]
	public void ProcessId_ReportsTheOperatingSystemIdentifier()
	{
		using IProcessHandle handle = StartShell(TestCommands.Echo("id"), redirectStandardOutput: true);

		int processId = handle.ProcessId;

		Assert.IsTrue(processId > 0);
		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));

		// The identifier stays available after the process exits.
		Assert.AreEqual(processId, handle.ProcessId);
	}

	[TestMethod]
	public void Dispose_ConcurrentCalls_AreSafe()
	{
		IProcessHandle handle = StartShell(TestCommands.Sleep(TimeSpan.FromSeconds(5)));
		using var start = new ManualResetEventSlim();
		Task[] disposals = [.. Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
		{
			start.Wait();
			handle.Dispose();
		}))];

		start.Set();

		// Disposal is claimed once, so concurrent callers neither fault nor leave the handle usable.
		Assert.IsTrue(Task.WaitAll(disposals, TimeSpan.FromSeconds(10)), "Concurrent disposal did not finish.");
		Assert.ThrowsExactly<ObjectDisposedException>(() => _ = handle.ExitCode);
	}

	[TestMethod]
	public void Dispose_EndsHandleUse()
	{
		IProcessHandle handle = StartShell(TestCommands.Echo("done"), redirectStandardOutput: true);
		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));

		handle.Dispose();

		Assert.ThrowsExactly<ObjectDisposedException>(() => _ = handle.ExitCode);
		Assert.ThrowsExactly<ObjectDisposedException>(() => _ = handle.StandardOutput);
		Assert.ThrowsExactly<ObjectDisposedException>(() => _ = handle.StandardError);
		Assert.ThrowsExactly<ObjectDisposedException>(() => handle.WaitForExit());
		Assert.ThrowsExactly<ObjectDisposedException>(() => handle.WaitForExit(0));
		Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = handle.WaitForExitAsync(); });
		Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = handle.ReadStandardOutputAsync(); });
		Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = handle.ReadStandardErrorAsync(); });
		Assert.ThrowsExactly<ObjectDisposedException>(() => handle.Kill());
		Assert.ThrowsExactly<ObjectDisposedException>(() => handle.KillEntireProcessTree());

		handle.Dispose();
	}

	[TestMethod]
	public async Task Dispose_PendingCapture_EndsTheDrain()
	{
		IProcessHandle handle = StartShell(TestCommands.StartAndDetachBackgroundChild(TimeSpan.FromSeconds(6)), redirectStandardOutput: true);
		Task<string> readTask = handle.ReadStandardOutputAsync();

		Assert.IsFalse(readTask.IsCompleted, "The detached descendant should keep the capture pending.");

		handle.Dispose();

		// Disposal cancels the drain, so the pending read ends by faulting instead of reading (and buffering)
		// output until the descendant exits.
		Exception fault = await Assert.ThrowsAsync<Exception>(() => readTask);
		Assert.IsTrue(
			fault is OperationCanceledException or ObjectDisposedException or IOException,
			$"Unexpected drain fault: {fault.GetType().Name}.");
	}

	[TestMethod]
	public async Task Dispose_PendingCaptureOnQuietProcess_EndsTheDrainPromptly()
	{
		// The child writes nothing, so the drain is blocked on a silent pipe instead of waiting for the next
		// write; disposal must cancel the read instead of waiting for the child to exit.
		IProcessHandle handle = StartShell(TestCommands.Sleep(TimeSpan.FromSeconds(6)), redirectStandardOutput: true);
		Task<string> readTask = handle.ReadStandardOutputAsync();

		Assert.IsFalse(readTask.IsCompleted, "The quiet child should keep the capture pending.");

		var stopwatch = Stopwatch.StartNew();
		handle.Dispose();

		Exception fault = await Assert.ThrowsAsync<Exception>(() => readTask);
		stopwatch.Stop();

		Assert.IsTrue(
			fault is OperationCanceledException or ObjectDisposedException or IOException,
			$"Unexpected drain fault: {fault.GetType().Name}.");
		Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Disposal did not end the blocked capture promptly: {stopwatch.Elapsed}.");
	}

	[TestMethod]
	[Timeout(30_000)]
	public void Kill_RealProcess_TerminatesItAndConfirmsTheExit()
	{
		// The single-process Kill path (as opposed to the tree walk) is exercised on a real child: the forced
		// termination is observed by the exit wait and reports a failure exit code.
		using IProcessHandle handle = StartShell(TestCommands.Sleep(TimeSpan.FromSeconds(5)));

		handle.Kill();

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds), "The exited wait must observe the terminated child.");
		Assert.AreNotEqual(0, handle.ExitCode);
	}

	[TestMethod]
	public void StandardOutput_WrittenBeyondTheCaptureBound_ReportsTruncation()
	{
		using IProcessHandle handle = StartShell(
			TestCommands.Echo("0123456789"),
			redirectStandardOutput: true,
			maxCapturedCharactersPerStream: 4);

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));

		// The drain keeps the first four characters and discards the rest while it keeps reading the pipe,
		// so the capture is bounded and the truncation is reported instead of silently hidden.
		Assert.AreEqual("0123", handle.StandardOutput);
		Assert.IsTrue(handle.StandardOutputTruncated);
		Assert.IsFalse(handle.StandardErrorTruncated);
	}

	[TestMethod]
	public void StandardOutput_WithinTheCaptureBound_IsNotReportedTruncated()
	{
		using IProcessHandle handle = StartShell(
			TestCommands.Echo("abcd"),
			redirectStandardOutput: true,
			maxCapturedCharactersPerStream: 64);

		Assert.IsTrue(handle.WaitForExit(WaitTimeoutMilliseconds));
		Assert.AreEqual("abcd", handle.StandardOutput.Trim());
		Assert.IsFalse(handle.StandardOutputTruncated);
	}

	private static IProcessHandle StartShell(
		string rawArguments,
		bool redirectStandardOutput = false,
		bool redirectStandardError = false,
		int? maxCapturedCharactersPerStream = null)
	{
		var runner = new ProcessRunner(new ProcessRunnerOptions
		{
			MaxCapturedCharactersPerStream = maxCapturedCharactersPerStream,
		});

		return runner.Start(new ProcessRunRequest
		{
			FileName = TestCommands.ShellFileName,
			RawArguments = rawArguments,
			RedirectStandardOutput = redirectStandardOutput,
			RedirectStandardError = redirectStandardError
		});
	}
}
