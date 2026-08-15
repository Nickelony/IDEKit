using System.Diagnostics;

namespace Nickelony.Testing;

/// <summary>
/// Provides the shared condition-polling helpers for tests that must wait for an asynchronous effect or
/// observe a live result that has no completion signal to synchronize on.
/// </summary>
/// <remarks>
/// This is the single polling helper for the language-server test suites; every suite links this file
/// instead of declaring its own copy, so the default timeout and poll interval exist once.
/// </remarks>
internal static class TestPolling
{
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(10);

	/// <summary>
	/// The shared timeout for tests that wait on an asynchronous notification or poll for a live result.
	/// </summary>
	internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	/// <summary>
	/// The shared poll interval for <see cref="WaitForAsync{TResult}"/>.
	/// </summary>
	internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(150);

	/// <summary>
	/// The deliberate bounded window used by negative-assertion ("absence") proofs: an operation is
	/// performed, the window elapses, and the test asserts that the forbidden effect never happened.
	/// </summary>
	/// <remarks>
	/// An absence has no completion signal to synchronize on, so a bounded window is the only
	/// mechanism available while the owning flow exposes no quiescence observation. The Client
	/// diagnostics-router drain seam (plan SP-47) and the Provider's watcher-ordering proof converted
	/// their windows to that observation; the proofs that still use this budget guard flows with no
	/// such seam. Keeping the budget in one place means every absence proof states the same
	/// deliberate policy instead of drifting between literals. A longer window is strictly safer for
	/// the assertion, so the value is chosen as the largest of the previously scattered budgets.
	/// </remarks>
	internal static readonly TimeSpan AbsenceWindow = TimeSpan.FromMilliseconds(250);

	/// <summary>
	/// Polls <paramref name="condition"/> until it becomes <see langword="true"/> or
	/// <paramref name="timeout"/> elapses, then fails the test.
	/// </summary>
	/// <param name="condition">The condition to poll.</param>
	/// <param name="timeout">The maximum time to wait for the condition.</param>
	/// <param name="failureMessage">The assertion message used when the timeout elapses.</param>
	public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string? failureMessage = null)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();

		while (!condition())
		{
			if (stopwatch.Elapsed >= timeout)
				Assert.Fail(failureMessage ?? "Condition was not reached within the timeout.");

			await Task.Delay(s_pollInterval).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Polls <paramref name="predicate"/> every 10 ms until it holds or <paramref name="timeout"/> elapses.
	/// </summary>
	/// <param name="predicate">The condition to observe.</param>
	/// <param name="timeout">The maximum wait time.</param>
	/// <returns><see langword="true"/> when the condition held; otherwise, <see langword="false"/>.</returns>
	/// <remarks>Wall-clock budgets stay generous so assertions remain stable under load.</remarks>
	public static async Task<bool> ForConditionAsync(Func<bool> predicate, TimeSpan timeout)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < timeout)
		{
			if (predicate())
				return true;

			await Task.Delay(10).ConfigureAwait(false);
		}

		return predicate();
	}

	/// <summary>
	/// Repeatedly evaluates <paramref name="action"/> until <paramref name="isSatisfied"/> accepts the
	/// result or <paramref name="timeout"/> elapses, then fails the test with the last observed result.
	/// </summary>
	/// <typeparam name="TResult">The observed result type.</typeparam>
	/// <param name="action">The action that produces one observation.</param>
	/// <param name="isSatisfied">The predicate that accepts a completed observation.</param>
	/// <param name="timeout">The maximum time to keep polling.</param>
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
		TimeSpan? pollInterval = null)
	{
		TimeSpan effectivePollInterval = pollInterval ?? DefaultPollInterval;
		Stopwatch stopwatch = Stopwatch.StartNew();
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
