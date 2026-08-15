using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the bounded-cache invariant under concurrent misses: an overflowing insert trims the cache
/// back, so racing misses cannot leave it permanently above the limit. The cache is process-global, so
/// the class is excluded from the suite's parallel run; the load it needs is created inside the test.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RegexCacheConcurrencyTests
{
	[TestMethod]
	[Timeout(30_000)]
	public void CreateRegex_ConcurrentDistinctMisses_StayWithinTheBound()
	{
		for (int wave = 0; wave < 4; wave++)
		{
			int waveIndex = wave;

			Parallel.For(0, 512, index => RegexCache.CreateRegex(
				$"cache-race-probe-{waveIndex}-{index}",
				(index & 1) == 0 ? RegexOptions.None : RegexOptions.IgnoreCase,
				TimeSpan.Zero));

			Assert.IsTrue(
				RegexCache.CachedRegexCount <= 64,
				$"The cache must stay bounded, but held {RegexCache.CachedRegexCount} entries after wave {wave}.");
		}
	}

	[TestMethod]
	[Timeout(30_000)]
	public void CreateRegex_AfterConcurrentMisses_TheNewestEntrySurvivesTheTrim()
	{
		Parallel.For(0, 512, index => RegexCache.CreateRegex(
			$"cache-survive-probe-{index}",
			RegexOptions.None,
			TimeSpan.Zero));

		// Every overflowing insert trims the cache back, and the trim evicts the entry with the lowest
		// insertion sequence - never the entry that overflowed the bound. A trim that evicted by
		// dictionary order, or one that evicted the entry it had just added, loses the newest pattern.
		Regex newest = RegexCache.CreateRegex("cache-survive-newest", RegexOptions.None, TimeSpan.Zero);
		Regex newestAgain = RegexCache.CreateRegex("cache-survive-newest", RegexOptions.None, TimeSpan.Zero);

		Assert.AreSame(newest, newestAgain, "The newest entry must survive the trim it triggered.");
	}

	[TestMethod]
	[Timeout(30_000)]
	public void CreateRegex_ConcurrentMissesOnOneKey_ReturnUsableInstances()
	{
		// Racing misses on one key may parse it more than once - the benign race the cache documents - so
		// instance identity is not part of the contract. Usability is: every instance a caller receives
		// must be a working regex, whichever miss lost the insertion race.
		var results = new Regex[512];

		Parallel.For(0, results.Length, index => results[index] = RegexCache.CreateRegex(
			"cache-race-same-key",
			RegexOptions.None,
			TimeSpan.Zero));

		Assert.IsTrue(
			results.All(regex => regex.IsMatch("cache-race-same-key")),
			"Every instance returned by a racing miss must be usable.");
	}
}
