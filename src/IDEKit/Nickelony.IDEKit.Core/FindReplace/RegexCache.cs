using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>
/// Provides the bounded regex cache that backs the <see cref="FindReplaceText"/> helpers.
/// </summary>
/// <remarks>
/// The cache is keyed by pattern, options, the effective timeout, and - for case-insensitive patterns
/// that do not opt into <see cref="RegexOptions.CultureInvariant"/> - the culture captured at creation,
/// so repeated searches over one pattern do not re-parse it. It is bounded and insertion-ordered: when
/// it is full the oldest entry is evicted, so a burst of distinct patterns churns the cache predictably
/// instead of evicting an arbitrary entry. The cache is trimmed back to the limit after an insert that
/// exceeds it, so racing misses can briefly hold a few extra entries but the count cannot keep growing
/// under concurrency.
/// </remarks>
internal static class RegexCache
{
	private const int MaximumCachedRegexCount = 64;

	private static readonly ConcurrentDictionary<RegexCacheKey, Regex> s_regexCache = new();

	// Mirror of the cache keys in insertion order; eviction dequeues from the front. A key left
	// behind by a racing re-insert is skipped when its dictionary entry is already gone.
	private static readonly ConcurrentQueue<RegexCacheKey> s_insertionOrder = new();

	private static readonly object s_evictionLock = new();

	/// <summary>
	/// Gets the number of cached regexes. Internal so the test assembly can verify the bound without
	/// exposing the cache.
	/// </summary>
	internal static int CachedRegexCount => s_regexCache.Count;

	/// <summary>
	/// Returns a cached regex for the supplied pattern, options, and timeout, creating it on first
	/// use.
	/// </summary>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <param name="matchTimeout">
	/// The maximum time for a single match attempt. A non-positive value maps to
	/// <see cref="Regex.InfiniteMatchTimeout"/>, matching the public helpers' documented default.
	/// </param>
	/// <returns>The cached or newly created regex.</returns>
	internal static Regex CreateRegex(string pattern, RegexOptions options, TimeSpan matchTimeout)
	{
		// Normalize the timeout before it enters the key so equivalent requests - zero and
		// Regex.InfiniteMatchTimeout both mean unbounded - share one cache slot and one parse.
		TimeSpan effectiveMatchTimeout = matchTimeout > TimeSpan.Zero ? matchTimeout : Regex.InfiniteMatchTimeout;

		// A case-insensitive pattern without CultureInvariant resolves case equivalence with the
		// culture captured at construction, so that culture joins the key; every other pattern shares
		// one culture-independent slot.
		string? capturedCultureName = (options & RegexOptions.IgnoreCase) != 0
			&& (options & RegexOptions.CultureInvariant) == 0
				? CultureInfo.CurrentCulture.Name
				: null;
		var key = new RegexCacheKey(pattern, options, effectiveMatchTimeout, capturedCultureName);

		if (s_regexCache.TryGetValue(key, out Regex? cachedRegex))
			return cachedRegex;

		var regex = new Regex(pattern, options, effectiveMatchTimeout);

		// Only a winning insert is enqueued, so the queue holds exactly the keys that were added; a
		// racing loser's regex is discarded and never enqueued.
		if (s_regexCache.TryAdd(key, regex))
			s_insertionOrder.Enqueue(key);

		// An insert that pushes the cache over the limit trims it back immediately, so racing misses
		// cannot leave the cache permanently above its bound: every overflowing insert drains the
		// overflow, and a lost eviction race cannot leak an entry (unlike a check-then-evict pass,
		// whose loser discards a failed removal and still inserts).
		if (s_regexCache.Count > MaximumCachedRegexCount)
			TrimToMaximumCount();

		return regex;
	}

	/// <summary>
	/// Evicts entries until the cache is at or below the limit. The trim is serialized by
	/// <see cref="s_evictionLock"/> so racing inserts cannot starve one another's evictions, and it
	/// runs only when an insert overflowed the cache, so the read path stays lock-free.
	/// </summary>
	private static void TrimToMaximumCount()
	{
		lock (s_evictionLock)
		{
			while (s_regexCache.Count > MaximumCachedRegexCount)
			{
				if (!EvictOldestCachedRegex())
					return;
			}
		}
	}

	/// <summary>
	/// Evicts the least recently inserted cached regex. The eviction is insertion-ordered rather
	/// than least-recently-used: a hit does not reorder the cache, which keeps the read path
	/// allocation- and write-free.
	/// </summary>
	/// <returns><see langword="true"/> when an entry was evicted, or <see langword="false"/> when the cache was empty.</returns>
	private static bool EvictOldestCachedRegex()
	{
		// Dequeue in insertion order and remove the first key that is still present. A key whose entry
		// was already removed - by a racing re-insert and a later eviction - is skipped, so every
		// enqueued key is dequeued at most once and the total eviction work is amortized O(1).
		while (s_insertionOrder.TryDequeue(out RegexCacheKey oldestKey))
		{
			if (s_regexCache.TryRemove(oldestKey, out _))
				return true;
		}

		return false;
	}

	private readonly record struct RegexCacheKey(string Pattern, RegexOptions Options, TimeSpan MatchTimeout, string? CapturedCultureName);
}
