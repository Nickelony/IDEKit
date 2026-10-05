using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Client;

/// <summary>Owns the disposed state, the cancellation token and the completion of one client's disposal.</summary>
/// <remarks>
/// <para>
/// The first <see cref="BeginDisposeAsync"/> caller claims disposal and runs the teardown; every later caller receives the completion
/// of that teardown, so a completed dispose call means disposal finished. Claiming and completing are inseparable, so no caller can mark the client
/// disposed and leave a later caller waiting.
/// </para>
/// <para>
/// The token is cached, so a lifetime check never reads from a disposed source after <see cref="Release"/>. The disposal budget is one duration
/// shared by every teardown stage, measured by a stopwatch that the teardown starts.
/// </para>
/// <para>
/// <c>ProviderLifetime</c> of the Provider package claims a disposal the same way, and the two stay separate copies on purpose:
/// they differ in who runs the teardown and who releases the token source, and this package references no Core project, as the remarks
/// of <c>ProviderLifetime</c> describe.
/// </para>
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1001:Types that own disposable fields should be disposable",
	Justification = "Release disposes the token source after the last teardown stage, so the type has no Dispose of its own; see the remarks."
)]
internal sealed class ClientLifetime
{
	private readonly CancellationTokenSource _cancellation = new();
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly string _workspaceDisplayText;
	private readonly ILogger _logger;

	private volatile bool _isDisposed;
	private int _disposeStarted;

	/// <param name="disposeWaitTimeout">The disposal budget that every teardown stage shares.</param>
	/// <param name="workspaceDisplayText">The workspace roots that diagnostics name.</param>
	/// <param name="logger">The logger that receives the diagnostics.</param>
	internal ClientLifetime(TimeSpan disposeWaitTimeout, string workspaceDisplayText, ILogger logger)
	{
		DisposeWaitTimeout = disposeWaitTimeout;
		Token = _cancellation.Token;
		_workspaceDisplayText = workspaceDisplayText;
		_logger = logger;
	}

	/// <summary>Whether disposal started.</summary>
	internal bool IsDisposed => _isDisposed;

	/// <summary>Signals client disposal to background work.</summary>
	internal CancellationToken Token { get; }

	/// <summary>The disposal budget that every teardown stage shares.</summary>
	internal TimeSpan DisposeWaitTimeout { get; }

	/// <summary>
	/// Claims disposal for the first caller and returns the teardown it started; every later caller receives the completion of that teardown.
	/// </summary>
	/// <param name="teardown">The teardown to run once.</param>
	/// <returns>A task that completes when disposal finishes.</returns>
	internal Task BeginDisposeAsync(Func<Task> teardown)
	{
		if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
			return _completion.Task;

		_isDisposed = true;
		return RunTeardownAsync(teardown);
	}

	/// <summary>Cancels the token without starting teardown, stopping background loops that observe it.</summary>
	internal void Cancel()
	{
		try
		{
			_cancellation.Cancel();
		}
		catch (ObjectDisposedException) { }
		catch (Exception exception)
		{
			// CancellationTokenSource.Cancel wraps throwing cancellation callbacks in an AggregateException;
			// a callback failure must not abort disposal before the process and session are torn down.
			_logger.LogDebug(
				exception,
				"Language server lifetime cancellation for workspace '{Workspace}' reported one or more callback failures.",
				_workspaceDisplayText
			);
		}
	}

	/// <summary>Releases the cancellation source once the teardown stages have finished consulting the token.</summary>
	internal void Release() => _cancellation.Dispose();

	/// <summary>Returns the part of the disposal budget that the stopwatch has not used.</summary>
	/// <param name="disposeStopwatch">Tracks the elapsed disposal time.</param>
	internal TimeSpan GetRemainingDisposeBudget(Stopwatch disposeStopwatch)
	{
		TimeSpan remainingDisposeBudget = DisposeWaitTimeout - disposeStopwatch.Elapsed;
		return remainingDisposeBudget > TimeSpan.Zero ? remainingDisposeBudget : TimeSpan.Zero;
	}

	private async Task RunTeardownAsync(Func<Task> teardown)
	{
		try
		{
			await teardown().ConfigureAwait(false);
		}
		finally
		{
			_completion.TrySetResult();
		}
	}
}
