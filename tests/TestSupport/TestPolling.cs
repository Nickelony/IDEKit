using System.Diagnostics;

namespace Nickelony.Testing;

/// <summary>
/// Provides the shared condition-polling helpers for tests that must wait for an asynchronous effect or observe a live result that has no completion
/// signal to synchronize on.
/// </summary>
/// <remarks>
/// This is the single polling helper for the language-server test suites; every suite that polls links this file instead of declaring its own copy,
/// so the default timeout and poll interval exist once.
/// </remarks>
internal static class TestPolling
{
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(10);

	/// <summary>The shared timeout for tests that wait on an asynchronous notification or poll for a live result.</summary>
	internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	/// <summary>The shared poll interval for <see cref="WaitForAsync{TResult}"/>.</summary>
	internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(150);

	/// <summary>
	/// The deliberate bounded window used by negative-assertion ("absence") proofs: an operation is performed, the window elapses, and the test
	/// asserts that the forbidden effect never happened.
	/// </summary>
	/// <remarks>
	/// An absence has no completion signal to synchronize on, so this window is for flows that expose no quiescence observation. Keeping the budget
	/// in one place means every absence proof states the same deliberate policy instead of drifting between literals.
	/// </remarks>
	internal static readonly TimeSpan AbsenceWindow = TimeSpan.FromMilliseconds(250);

	/// <summary>
	/// Polls <paramref name="condition"/> until it becomes <see langword="true"/> or <paramref name="timeout"/> elapses, then fails the test.
	/// </summary>
	/// <param name="failureMessage">The assertion message used when the timeout elapses.</param>
	public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string? failureMessage = null)
	{
		var stopwatch = Stopwatch.StartNew();

		while (!condition())
		{
			if (stopwatch.Elapsed >= timeout)
				Assert.Fail(failureMessage ?? "Condition was not reached within the timeout.");

			await Task.Delay(s_pollInterval).ConfigureAwait(false);
		}
	}

	/// <summary>Polls <paramref name="predicate"/> at a fixed short interval until it holds or <paramref name="timeout"/> elapses.</summary>
	/// <returns><see langword="true"/> when the condition held; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Wall-clock budgets stay generous so assertions remain stable under load.
	/// </remarks>
	public static async Task<bool> ForConditionAsync(Func<bool> predicate, TimeSpan timeout)
	{
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < timeout)
		{
			if (predicate())
				return true;

			await Task.Delay(s_pollInterval).ConfigureAwait(false);
		}

		return predicate();
	}

	/// <summary>
	/// Repeatedly evaluates <paramref name="action"/> until <paramref name="isSatisfied"/> accepts the result or <paramref name="timeout"/> elapses,
	/// then fails the test with the last observed result.
	/// </summary>
	/// <param name="failureMessage">The failure message used when the predicate is never satisfied.</param>
	/// <param name="describeLastResult">Describes the last observed result for the failure message.</param>
	/// <param name="pollInterval">The delay between observations, or <see langword="null"/> for the default.</param>
	/// <returns>The first result accepted by <paramref name="isSatisfied"/>.</returns>
	internal static async Task<TResult> WaitForAsync<TResult>(
		Func<Task<TResult>> action,
		Func<TResult, bool> isSatisfied,
		TimeSpan timeout,
		string failureMessage,
		Func<TResult, string> describeLastResult,
		TimeSpan? pollInterval = null
	)
	{
		TimeSpan effectivePollInterval = pollInterval ?? DefaultPollInterval;
		var stopwatch = Stopwatch.StartNew();
		TResult lastResult = default!;

		while (stopwatch.Elapsed < timeout)
		{
			lastResult = await action().ConfigureAwait(false);

			if (isSatisfied(lastResult))
				return lastResult;

			await Task.Delay(effectivePollInterval).ConfigureAwait(false);
		}

		Assert.Fail(failureMessage + Environment.NewLine + describeLastResult(lastResult));
		return lastResult;
	}
}
