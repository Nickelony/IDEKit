using System.Diagnostics;
using System.Globalization;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Wall-clock sampling for the suite's few complexity probes. The fastest-of-N sample is the least
/// noisy in-process estimator, so a probe that compares two input sizes and asserts a scaling bound
/// is not decided by one scheduler hiccup or a collection landing inside a single run.
/// </summary>
internal static class TestMeasurements
{
	/// <summary>
	/// Runs <paramref name="action"/> <paramref name="sampleCount"/> times and returns the fastest
	/// sample.
	/// </summary>
	internal static TimeSpan BestOf(Action action, int sampleCount = 5)
	{
		var best = TimeSpan.MaxValue;

		for (int index = 0; index < sampleCount; index++)
		{
			var stopwatch = Stopwatch.StartNew();
			action();
			stopwatch.Stop();

			if (stopwatch.Elapsed < best)
				best = stopwatch.Elapsed;
		}

		return best;
	}

	/// <summary>
	/// Asserts that the larger workload costs no more than <paramref name="maximumFactor"/> times the
	/// smaller one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The ratio is what detects super-linear work, so it cannot be replaced by an absolute ceiling: a
	/// ceiling wide enough to survive a loaded machine is also wide enough to let a quadratic run
	/// through. A breach is therefore re-sampled rather than widened - both sizes are measured again,
	/// and the bound is only reported as violated when the breach reproduces. Super-linear work
	/// reproduces it on every attempt; a one-off scheduler hiccup, a background collection, or a
	/// momentary burst of load usually does not.
	/// </para>
	/// <para>
	/// A floor on the smaller sample keeps a sub-millisecond baseline from turning timer noise into a
	/// failure, so the compared baseline is <c>max(smaller sample, floor)</c>.
	/// </para>
	/// </remarks>
	/// <param name="smallerWorkload">The smaller of the two input sizes.</param>
	/// <param name="largerWorkload">The larger of the two input sizes.</param>
	/// <param name="failureMessageFormat">
	/// A composite format string for the failure message; argument 0 receives the larger sample and
	/// argument 1 the smaller one.
	/// </param>
	/// <param name="maximumFactor">The largest accepted ratio between the two samples.</param>
	/// <param name="sampleCount">The number of samples taken per input size and per attempt.</param>
	/// <param name="floorMilliseconds">The floor applied to the smaller sample.</param>
	internal static void AssertScalingBound(
		Action smallerWorkload,
		Action largerWorkload,
		string failureMessageFormat,
		int maximumFactor = 10,
		int sampleCount = 5,
		double floorMilliseconds = 5.0)
	{
		long floorTicks = TimeSpan.FromMilliseconds(floorMilliseconds).Ticks;

		TimeSpan smaller = TimeSpan.Zero;
		TimeSpan larger = TimeSpan.Zero;

		for (int attempt = 0; attempt < 2; attempt++)
		{
			smaller = BestOf(smallerWorkload, sampleCount);
			larger = BestOf(largerWorkload, sampleCount);

			if (larger.Ticks <= Math.Max(smaller.Ticks, floorTicks) * maximumFactor)
				return;
		}

		Assert.Fail(string.Format(CultureInfo.InvariantCulture, failureMessageFormat, larger, smaller));
	}
}
