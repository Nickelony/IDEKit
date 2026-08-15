using System.ComponentModel;
using System.Text;

namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Runs external processes from <see cref="ProcessRunRequest"/> values, handling timeouts, cancellation,
/// output capture, and process-tree termination.
/// </summary>
/// <remarks>
/// The runner holds immutable configuration and is safe for concurrent calls.
/// </remarks>
public sealed class ProcessRunner : IProcessRunner
{
	private readonly IProcessLauncher _launcher;
	private readonly ProcessRunnerOptions _options;

	/// <summary>
	/// Initializes a new instance of the <see cref="ProcessRunner"/> class with the default options.
	/// </summary>
	public ProcessRunner()
		: this(new ProcessLauncher())
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ProcessRunner"/> class with the given options.
	/// </summary>
	/// <param name="options">The run options.</param>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	public ProcessRunner(ProcessRunnerOptions options)
		: this(new ProcessLauncher(), options ?? throw new ArgumentNullException(nameof(options)))
	{
	}

	internal ProcessRunner(IProcessLauncher launcher, ProcessRunnerOptions? options = null)
	{
		_launcher = launcher;
		_options = options ?? ProcessRunnerOptions.Default;
	}

	/// <inheritdoc/>
	public IProcessHandle Start(ProcessRunRequest request)
	{
		ValidateRequest(request);

		// A shell launch resolves the target through the operating system without returning a process handle,
		// so it cannot satisfy this contract; Run and RunAsync report it as NoProcessHandle.
		if (request.UseShellExecute)
			throw new ArgumentException(
				"Shell execution does not produce a process handle; use Run or RunAsync.",
				nameof(request));

		return _launcher.Start(request, _options)
			?? throw new InvalidOperationException("The process could not be started.");
	}

	/// <inheritdoc/>
	public ProcessRunResult Run(ProcessRunRequest request, CancellationToken cancellationToken = default)
		=> RunAsync(request, cancellationToken).GetAwaiter().GetResult();

	/// <inheritdoc/>
	public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
	{
		ValidateRequest(request);

		// A canceled token must not spawn a doomed process or produce its side effects.
		if (cancellationToken.IsCancellationRequested)
			return new ProcessRunResult { Outcome = ProcessRunOutcome.CanceledBeforeStart };

		using IProcessHandle? process = _launcher.Start(request, _options);

		if (process is null)
		{
			// Shell execution can satisfy the request without returning a process handle.
			return new ProcessRunResult
			{
				Outcome = cancellationToken.IsCancellationRequested
					? ProcessRunOutcome.CanceledBeforeStart
					: ProcessRunOutcome.NoProcessHandle,
			};
		}

		ProcessRunOutcome outcome;
		bool exitObserved;

		try
		{
			outcome = await WaitForExitOutcomeAsync(process, request.Timeout, cancellationToken).ConfigureAwait(false);

			exitObserved = outcome == ProcessRunOutcome.Exited
				|| await TerminateProcessTreeAsync(process).ConfigureAwait(false);
		}
		catch
		{
			// A failed wait must not leak a live child. Termination is best-effort and must not replace the
			// failure the caller has to observe, so a termination failure is swallowed here.
			try
			{
				TryTerminateBestEffort(process);
			}
			catch
			{
				// The process could not be terminated; the original failure is what matters.
			}

			throw;
		}

		string? standardOutput = null;
		string? standardError = null;
		bool standardOutputTruncated = false;
		bool standardErrorTruncated = false;

		if (exitObserved)
		{
			Task<string?> standardOutputCapture = request.RedirectStandardOutput
				? ReadCapturedOutputAsync(process.ReadStandardOutputAsync)
				: Task.FromResult<string?>(null);

			Task<string?> standardErrorCapture = request.RedirectStandardError
				? ReadCapturedOutputAsync(process.ReadStandardErrorAsync)
				: Task.FromResult<string?>(null);

			// The captures run concurrently, so the tail is bounded by one output-drain grace period instead
			// of one period per stream.
			await Task.WhenAll(standardOutputCapture, standardErrorCapture).ConfigureAwait(false);

			standardOutput = await standardOutputCapture.ConfigureAwait(false);
			standardError = await standardErrorCapture.ConfigureAwait(false);

			// A truncation is reported only alongside a capture: a capture that failed or exceeded its grace
			// period reports no output, so it has nothing to describe as truncated.
			standardOutputTruncated = standardOutput is not null && process.StandardOutputTruncated;
			standardErrorTruncated = standardError is not null && process.StandardErrorTruncated;
		}

		// An exit that could not be observed leaves the exit code unobservable, and a skipped capture reports
		// no output; the result describes both instead of blocking on either. The process identifier is
		// reported whenever a handle was produced, so a host can still act on a process that may be running.
		return new ProcessRunResult
		{
			Outcome = outcome,
			ExitCode = exitObserved ? process.ExitCode : null,
			ProcessId = process.ProcessId,
			StandardOutput = standardOutput,
			StandardOutputTruncated = standardOutputTruncated,
			StandardError = standardError,
			StandardErrorTruncated = standardErrorTruncated,
		};
	}

	// The request validates its own property values when it is constructed; the runner rejects the combinations
	// that depend on the final property values before anything is launched.
	private static void ValidateRequest(ProcessRunRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.RawArguments.Length > 0 && request.ArgumentList.Count > 0)
			throw new ArgumentException("Set either RawArguments or ArgumentList, not both.", nameof(request));

		if (request.UseShellExecute)
		{
			if (request.RedirectStandardOutput || request.RedirectStandardError)
				throw new ArgumentException("Shell execution cannot be combined with standard-output or standard-error redirection.", nameof(request));

			// Only Windows rejects environment overrides combined with shell execution; the runner rejects them
			// on every platform so a request behaves the same everywhere.
			if (request.EnvironmentVariables.Count > 0)
				throw new ArgumentException("Shell execution cannot be combined with environment variable overrides.", nameof(request));
		}

		if (request.StandardOutputEncoding is not null && !request.RedirectStandardOutput)
			throw new ArgumentException("StandardOutputEncoding requires RedirectStandardOutput.", nameof(request));

		if (request.StandardErrorEncoding is not null && !request.RedirectStandardError)
			throw new ArgumentException("StandardErrorEncoding requires RedirectStandardError.", nameof(request));
	}

	// Waits for the exit, the deadline, or cancellation, and reports which ended the wait. The deadline is
	// enforced by a timer instead of a polling loop, so the waited time does not drift with wait granularity.
	private static async Task<ProcessRunOutcome> WaitForExitOutcomeAsync(
		IProcessHandle process,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
	{
		// A null timeout and Timeout.InfiniteTimeSpan both wait indefinitely.
		TimeSpan? deadline = timeout is { } value && value != Timeout.InfiniteTimeSpan ? value : null;

		// A timeout shorter than one millisecond never waits: a single non-blocking probe reports whether the
		// process already exited, and a running process is terminated instead of waited for. The caller's token
		// decides the classification when the probe did not observe an exit.
		if (deadline is { } probeTimeout && probeTimeout < TimeSpan.FromMilliseconds(1))
		{
			if (process.WaitForExit(0))
				return ProcessRunOutcome.Exited;

			return cancellationToken.IsCancellationRequested
				? ProcessRunOutcome.Canceled
				: ProcessRunOutcome.TimedOut;
		}

		if (deadline is null && !cancellationToken.CanBeCanceled)
		{
			await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
			return ProcessRunOutcome.Exited;
		}

		using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		if (deadline is { } waitTimeout)
			waitSource.CancelAfter(waitTimeout);

		try
		{
			await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
			return ProcessRunOutcome.Exited;
		}
		catch (OperationCanceledException)
		{
			// The caller's cancellation is the stronger signal: it wins when it is observed, and a deadline
			// that fired alone is reported as a timeout.
			return cancellationToken.IsCancellationRequested
				? ProcessRunOutcome.Canceled
				: ProcessRunOutcome.TimedOut;
		}
	}

	// Reads a captured stream within the output-drain grace period. Pipes stay open as long as any descendant
	// holds them, so an unfinished drain is reported as unavailable output instead of an unbounded wait. Only the
	// failures the capture can raise are reported that way - a pipe that fails while it is read (IOException or
	// InvalidDataException), a capture against a disposed handle (ObjectDisposedException), a configured encoding
	// that rejects a captured byte sequence (DecoderFallbackException), and the drain grace period
	// (OperationCanceledException) - so the capture cannot change the outcome of an otherwise completed run; every
	// other failure, including an out-of-memory condition, propagates instead of being disguised as unavailable
	// output.
	private async Task<string?> ReadCapturedOutputAsync(Func<CancellationToken, Task<string>> readAsync)
	{
		using var drainSource = new CancellationTokenSource(_options.OutputDrainGracePeriod);

		try
		{
			return await readAsync(drainSource.Token).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ObjectDisposedException or DecoderFallbackException or OperationCanceledException)
		{
			return null;
		}
	}

	// Terminates the process tree best-effort and observes the exit within the termination grace period. A
	// terminated child can outlive the kill call, so the exit is observed for at most the period instead of
	// assumed; a failed kill must not turn into an unbounded wait.
	private async Task<bool> TerminateProcessTreeAsync(IProcessHandle process)
	{
		TryTerminateBestEffort(process);

		try
		{
			using var graceSource = new CancellationTokenSource(_options.TerminationGracePeriod);
			await process.WaitForExitAsync(graceSource.Token).ConfigureAwait(false);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			// The handle can no longer observe the process; the exit stays unobserved.
			return false;
		}
		catch (Win32Exception)
		{
			// Observing the exit can fail for operating-system reasons; the exit stays unobserved.
			return false;
		}
	}

	private static void TryTerminateBestEffort(IProcessHandle process)
	{
		try
		{
			process.KillEntireProcessTree();
		}
		catch (Exception exception) when (IsTerminationFailure(exception))
		{
			// Tree termination can be unsupported, can fail for some descendants, or can find the process
			// gone; fall back to terminating only the launched process.
			try
			{
				process.Kill();
			}
			catch (Exception fallbackException) when (IsTerminationFailure(fallbackException))
			{
				// The process could not be terminated; the bounded wait reports the unobserved exit.
			}
		}
	}

	// Process.Kill(entireProcessTree: true) reports partial tree failures as AggregateException; the other
	// types cover an unsupported tree walk, an unavailable process, and an operating-system refusal. A
	// disposed handle reports its own ObjectDisposedException, which is a disposal signal rather than a
	// termination failure, and an aggregate is one only when it carries at least one inner failure and every
	// inner failure is one too - so an unrelated fault wrapped in an aggregate, and an empty aggregate, are
	// not swallowed as "the process could not be terminated".
	private static bool IsTerminationFailure(Exception exception)
	{
		if (exception is not AggregateException aggregate)
		{
			return exception is InvalidOperationException and not ObjectDisposedException
				or NotSupportedException
				or Win32Exception;
		}

		var innerFailures = aggregate.InnerExceptions;

		if (innerFailures.Count == 0)
			return false;

		foreach (Exception failure in innerFailures)
		{
			if (!IsTerminationFailure(failure))
				return false;
		}

		return true;
	}
}
