namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// A controllable process handle used by the runner tests.
/// </summary>
/// <remarks>
/// The handle models the pieces of <see cref="IProcessHandle"/> the runner depends on: an exit source that kill
/// calls complete - immediately or after a delay - capture sources that can stay pending or fault, and counters
/// that pin which operations ran. Configure it with an object initializer.
/// </remarks>
internal sealed class FakeProcessHandle : IProcessHandle
{
	private TaskCompletionSource? _exitSource;
	private TaskCompletionSource<string>? _standardOutputSource;
	private TaskCompletionSource<string>? _standardErrorSource;

	/// <summary>
	/// Gets the exit code reported once the process exited.
	/// </summary>
	public int ExitCodeValue { get; init; }

	/// <summary>
	/// Gets the operating-system process identifier the handle reports.
	/// </summary>
	public int ProcessId { get; init; } = 4242;

	/// <summary>
	/// Gets the text produced by the standard-output capture.
	/// </summary>
	public string StandardOutputText { get; init; } = string.Empty;

	/// <summary>
	/// Gets the text produced by the standard-error capture.
	/// </summary>
	public string StandardErrorText { get; init; } = string.Empty;

	/// <summary>
	/// Gets a value indicating whether the handle reports the captured standard output as truncated.
	/// </summary>
	public bool StandardOutputTruncated { get; init; }

	/// <summary>
	/// Gets a value indicating whether the handle reports the captured standard error as truncated.
	/// </summary>
	public bool StandardErrorTruncated { get; init; }

	/// <summary>
	/// Gets a value indicating whether the process has already exited when the run starts.
	/// </summary>
	public bool ExitsImmediately { get; init; } = true;

	/// <summary>
	/// Gets a value indicating whether the capture sources stay pending until the reading side cancels.
	/// </summary>
	public bool HoldsCapturedOutput { get; init; }

	/// <summary>
	/// Gets a value indicating whether a kill call completes the exit source.
	/// </summary>
	public bool CompletesExitOnKill { get; init; } = true;

	/// <summary>
	/// Gets the delay between a kill call and the completed exit; <see langword="null"/> completes it immediately.
	/// </summary>
	public TimeSpan? KillExitDelay { get; init; }

	/// <summary>
	/// Gets the fault that the standard-output capture produces instead of text.
	/// </summary>
	public Exception? StandardOutputFault { get; init; }

	public Action? OnKill { get; init; }

	public Action? OnKillEntireProcessTree { get; init; }

	public Action? OnWaitForExitAsync { get; init; }

	public int ExitCode
		=> Exited
			? ExitCodeValue
			: throw new InvalidOperationException("The process has not exited.");

	public string StandardOutput
		=> StandardOutputSource.Task.GetAwaiter().GetResult();

	public string StandardError
		=> StandardErrorSource.Task.GetAwaiter().GetResult();

	public bool Exited
		=> ExitSource.Task.IsCompleted;

	private TaskCompletionSource ExitSource
		=> _exitSource ??= CreateExitSource();

	private TaskCompletionSource<string> StandardOutputSource
		=> _standardOutputSource ??= CreateStandardOutputSource();

	private TaskCompletionSource<string> StandardErrorSource
		=> _standardErrorSource ??= CreateStandardErrorSource();

	public int TimedWaitForExitCalls { get; private set; }

	public int WaitForExitAsyncCalls { get; private set; }

	public int KillCalls { get; private set; }

	public int KillEntireProcessTreeCalls { get; private set; }

	public int StandardOutputReadCalls { get; private set; }

	public int StandardErrorReadCalls { get; private set; }

	public bool Disposed { get; private set; }

	public void WaitForExit()
		=> ExitSource.Task.GetAwaiter().GetResult();

	public bool WaitForExit(int timeoutMilliseconds)
	{
		TimedWaitForExitCalls++;

		if (Exited)
			return true;

		// Honor the timeout instead of reporting the exit unconditionally: a zero probe stays non-blocking
		// while a positive bound blocks until the exit source completes or the bound elapses, matching the
		// adapter contract the runner relies on.
		return ExitSource.Task.Wait(timeoutMilliseconds);
	}

	public Task WaitForExitAsync(CancellationToken cancellationToken = default)
	{
		WaitForExitAsyncCalls++;
		OnWaitForExitAsync?.Invoke();
		return ExitSource.Task.WaitAsync(cancellationToken);
	}

	public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken = default)
	{
		StandardOutputReadCalls++;

		return StandardOutputFault is not null
			? Task.FromException<string>(StandardOutputFault)
			: StandardOutputSource.Task.WaitAsync(cancellationToken);
	}

	public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken = default)
	{
		StandardErrorReadCalls++;
		return StandardErrorSource.Task.WaitAsync(cancellationToken);
	}

	public void Kill()
	{
		KillCalls++;
		OnKill?.Invoke();
		CompleteExitOnKill();
	}

	public void KillEntireProcessTree()
	{
		KillEntireProcessTreeCalls++;
		OnKillEntireProcessTree?.Invoke();
		CompleteExitOnKill();
	}

	public void Dispose()
		=> Disposed = true;

	private TaskCompletionSource CreateExitSource()
	{
		var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		if (ExitsImmediately)
			source.TrySetResult();

		return source;
	}

	private TaskCompletionSource<string> CreateStandardOutputSource()
	{
		var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

		if (!HoldsCapturedOutput)
			source.TrySetResult(StandardOutputText);

		return source;
	}

	private TaskCompletionSource<string> CreateStandardErrorSource()
	{
		var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

		if (!HoldsCapturedOutput)
			source.TrySetResult(StandardErrorText);

		return source;
	}

	private void CompleteExitOnKill()
	{
		if (!CompletesExitOnKill)
			return;

		if (KillExitDelay is { } delay && delay > TimeSpan.Zero)
		{
			_ = Task.Delay(delay).ContinueWith(
				static (_, state) => ((TaskCompletionSource)state!).TrySetResult(),
				ExitSource,
				TaskScheduler.Default);
			return;
		}

		ExitSource.TrySetResult();
	}
}
