using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the bounded regex cache's key, eviction and bound contract. The cache is process-global, so
/// these probes assert absolute entry counts and must not overlap another test that searches: the
/// class is excluded from the suite's parallel run.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RegexCacheTests
{
	[TestMethod]
	public void CreateRegex_SameKey_ReturnsTheCachedInstance()
	{
		Regex first = RegexCache.CreateRegex("cache-probe-a", RegexOptions.None, TimeSpan.Zero);
		Regex second = RegexCache.CreateRegex("cache-probe-a", RegexOptions.None, TimeSpan.Zero);

		Assert.AreSame(first, second);
	}

	[TestMethod]
	public void CreateRegex_ZeroAndInfiniteTimeout_ShareOneCachedInstance()
	{
		// Both values mean "unbounded", so they must occupy one cache slot and produce one parse.
		Regex zero = RegexCache.CreateRegex("cache-probe-b", RegexOptions.None, TimeSpan.Zero);
		Regex infinite = RegexCache.CreateRegex("cache-probe-b", RegexOptions.None, Regex.InfiniteMatchTimeout);

		Assert.AreSame(zero, infinite);
		Assert.AreEqual(Regex.InfiniteMatchTimeout, zero.MatchTimeout);
	}

	[TestMethod]
	public void CreateRegex_DistinctKeys_StayWithinTheBound()
	{
		for (int index = 0; index < 96; index++)
			RegexCache.CreateRegex($"cache-bound-probe-{index}", RegexOptions.None, TimeSpan.Zero);

		// 96 distinct keys against the 64-entry bound leave the cache exactly at its bound.
		Assert.AreEqual(64, RegexCache.CachedRegexCount, "The cache must stay at its bound.");
	}

	[TestMethod]
	public void CreateRegex_AtTheBound_EvictsTheOldestEntryFirst()
	{
		// The documented eviction policy is insertion-ordered (oldest first): after the bound is
		// exceeded, the first key must be re-parsed while the newest key stays cached. Inserting 65
		// fresh keys into a cache of at most 64 entries evicts exactly one of them - the oldest.
		Regex? first = null;
		Regex? newest = null;

		for (int index = 0; index <= 64; index++)
		{
			Regex created = RegexCache.CreateRegex($"cache-evict-probe-{index}", RegexOptions.None, TimeSpan.Zero);

			if (index == 0)
				first = created;

			if (index == 64)
				newest = created;
		}

		Regex firstAgain = RegexCache.CreateRegex("cache-evict-probe-0", RegexOptions.None, TimeSpan.Zero);

		Assert.IsFalse(ReferenceEquals(first, firstAgain), "The oldest entry must be evicted first.");

		Regex newestAgain = RegexCache.CreateRegex("cache-evict-probe-64", RegexOptions.None, TimeSpan.Zero);

		Assert.AreSame(newest, newestAgain, "The newest entry must stay cached.");
	}

	[TestMethod]
	public void CreateRegex_OptionsAndTimeoutArePartOfTheKey()
	{
		Regex insensitive = RegexCache.CreateRegex("cache-probe-c", RegexOptions.IgnoreCase, TimeSpan.Zero);
		Regex sensitive = RegexCache.CreateRegex("cache-probe-c", RegexOptions.None, TimeSpan.Zero);
		Regex bounded = RegexCache.CreateRegex("cache-probe-c", RegexOptions.None, TimeSpan.FromSeconds(5));

		Assert.AreNotSame(insensitive, sensitive);
		Assert.AreNotSame(sensitive, bounded);
	}
}
