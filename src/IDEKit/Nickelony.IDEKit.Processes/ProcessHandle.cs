using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Adapts <see cref="Process"/> to <see cref="IProcessHandle"/>.
/// </summary>
/// <remarks>
/// Redirected standard output and error are drained asynchronously from the moment the handle is created, so a
/// process that writes more than the operating-system pipe buffer cannot deadlock a caller that waits for it to
/// exit. The capture accessors return the drained text and block (or wait asynchronously) until the stream closes.
/// A configured capture bound is applied while the drain runs: the excess is discarded, the pipe keeps being
/// drained, and the capture reports the truncation.
/// Disposal releases the captured pipes, which unblocks a drain a quiet descendant would otherwise keep parked,
/// and cancels the drain token so an incomplete drain ends by faulting instead of buffering output after the run.
/// A <see cref="DisposalGuard"/> makes disposal a one-time claim and reports the torn-down <see cref="Process"/>
/// failure as <see cref="ObjectDisposedException"/>, while a different failure the race produces keeps its own
/// type.
/// </remarks>
internal sealed class ProcessHandle : IProcessHandle
{
	private const int DrainBufferSize = 4096;

	private readonly Process _process;
	private readonly int _processId;
	private readonly CancellationTokenSource _drainCancellation = new();
	private readonly DisposalGuard _guard = new();
	private readonly Stream? _standardOutputPipe;
	private readonly Stream? _standardErrorPipe;
	private readonly Task<string>? _standardOutputTask;
	private readonly Task<string>? _standardErrorTask;
	private readonly CaptureTruncation _standardOutputTruncation = new();
	private readonly CaptureTruncation _standardErrorTruncation = new();

	public ProcessHandle(
		Process process,
		bool redirectStandardOutput,
		bool redirectStandardError,
		int? maxCapturedCharactersPerStream = null)
	{
		_process = process;

		// The identifier is captured before the process can exit or be disposed, so the handle reports it for
		// its whole lifetime.
		_processId = process.Id;

		if (redirectStandardOutput)
		{
			_standardOutputPipe = process.StandardOutput.BaseStream;
			_standardOutputTask = DrainAsync(
				process.StandardOutput,
				maxCapturedCharactersPerStream,
				_standardOutputTruncation,
				_drainCancellation.Token);
		}

		if (redirectStandardError)
		{
			_standardErrorPipe = process.StandardError.BaseStream;
			_standardErrorTask = DrainAsync(
				process.StandardError,
				maxCapturedCharactersPerStream,
				_standardErrorTruncation,
				_drainCancellation.Token);
		}
	}

	public int ProcessId
	{
		get
		{
			_guard.ThrowIfDisposed(this);
			return _processId;
		}
	}

	// The genuine failure of an exit read - the process has not exited - is meaningful before disposal and is
	// indistinguishable from the released-process shape, so it must not be reported as the disposal result.
	public int ExitCode
		=> _guard.ExecuteUnmasked(this, () => _process.ExitCode);

	public string StandardOutput
		=> _guard.Execute(this, () => RequireStandardOutput(_standardOutputTask).GetAwaiter().GetResult());

	public string StandardError
		=> _guard.Execute(this, () => RequireStandardError(_standardErrorTask).GetAwaiter().GetResult());

	public bool StandardOutputTruncated
		=> _guard.Execute(this, () => _standardOutputTask is not null && _standardOutputTruncation.Truncated);

	public bool StandardErrorTruncated
		=> _guard.Execute(this, () => _standardErrorTask is not null && _standardErrorTruncation.Truncated);

	public void WaitForExit()
		=> _guard.Execute(this, () => _process.WaitForExit());

	public bool WaitForExit(int timeoutMilliseconds)
	{
		// The argument is validated before the disposal guard so an invalid timeout is reported as a range
		// error even when the handle was already disposed.
		ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, Timeout.Infinite);

		return _guard.Execute(this, () => _process.WaitForExit(timeoutMilliseconds));
	}

	public Task WaitForExitAsync(CancellationToken cancellationToken = default)
		=> _guard.Execute(this, () => _process.WaitForExitAsync(cancellationToken));

	public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken = default)
		=> _guard.Execute(this, () => RequireStandardOutput(_standardOutputTask).WaitAsync(cancellationToken));

	public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken = default)
		=> _guard.Execute(this, () => RequireStandardError(_standardErrorTask).WaitAsync(cancellationToken));

	public void Kill()
		=> _guard.Execute(this, () => _process.Kill());

	public void KillEntireProcessTree()
		=> _guard.Execute(this, () => _process.Kill(entireProcessTree: true));

	public void Dispose()
	{
		if (!_guard.TryBeginDispose())
			return;

		try
		{
			// Releasing the captured pipes frees the redirected streams and unblocks a read that a quiet
			// descendant would otherwise keep parked for an unbounded time; the drain token is canceled so a
			// drain that has not completed ends by faulting.
			_drainCancellation.Cancel();
			_process.Dispose();
			_standardOutputPipe?.Dispose();
			_standardErrorPipe?.Dispose();
		}
		finally
		{
			_drainCancellation.Dispose();

			// The faults of the ended drains are observed so they cannot surface as unobserved exceptions.
			ObserveDrainFault(_standardOutputTask);
			ObserveDrainFault(_standardErrorTask);
		}
	}

	// Reads the redirected stream into a string until the writer closes it or the handle cancels the drain. The
	// token is best-effort: a read parked on the redirected pipe is unblocked by the pipe disposal in Dispose,
	// and the token only bounds a drain that can observe cancellation.
	private static async Task<string> DrainAsync(
		StreamReader reader,
		int? maxCapturedCharactersPerStream,
		CaptureTruncation truncation,
		CancellationToken cancellationToken)
	{
		var builder = new StringBuilder();
		char[] buffer = ArrayPool<char>.Shared.Rent(DrainBufferSize);

		try
		{
			while (true)
			{
				int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

				if (read == 0)
					return builder.ToString();

				AppendBounded(builder, buffer, read, maxCapturedCharactersPerStream, truncation);
			}
		}
		finally
		{
			ArrayPool<char>.Shared.Return(buffer);
		}
	}

	// The excess beyond the bound is discarded while the read continues, so a flooding process still has its
	// pipe drained and never blocks on a full buffer while the captured text stays within the bound.
	private static void AppendBounded(
		StringBuilder builder,
		char[] buffer,
		int read,
		int? maxCapturedCharactersPerStream,
		CaptureTruncation truncation)
	{
		if (maxCapturedCharactersPerStream is not { } maximum)
		{
			builder.Append(buffer, 0, read);
			return;
		}

		int remaining = maximum - builder.Length;

		if (remaining <= 0)
		{
			truncation.Mark();
			return;
		}

		if (read > remaining)
		{
			truncation.Mark();
			read = remaining;
		}

		builder.Append(buffer, 0, read);
	}

	private static Task<string> RequireStandardOutput(Task<string>? captureTask)
		=> captureTask ?? throw new InvalidOperationException("The process was started without standard-output redirection.");

	private static Task<string> RequireStandardError(Task<string>? captureTask)
		=> captureTask ?? throw new InvalidOperationException("The process was started without standard-error redirection.");

	private static void ObserveDrainFault(Task<string>? drainTask)
	{
		if (drainTask is null)
			return;

		if (drainTask.IsCompleted)
		{
			_ = drainTask.Exception;
			return;
		}

		_ = drainTask.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
	}

	/// <summary>
	/// Records whether a drain discarded output beyond the configured capture bound.
	/// </summary>
	private sealed class CaptureTruncation
	{
		private int _truncated;

		internal bool Truncated => Volatile.Read(ref _truncated) != 0;

		internal void Mark() => Volatile.Write(ref _truncated, 1);
	}
}
