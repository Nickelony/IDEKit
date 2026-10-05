using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>Provides the bounded regex cache that backs the <see cref="FindReplaceOperations"/> helpers.</summary>
/// <remarks>
/// Keyed by pattern, options, the effective timeout, and - for case-insensitive patterns that keep the current culture - the captured culture,
/// so repeated searches do not re-parse the pattern. Bounded and insertion-ordered: a full cache evicts the oldest entry, and an insert
/// that overflows the limit trims it back, so the count cannot grow without bound under concurrency.
/// </remarks>
internal static class RegexCache
{
	private const int MaximumCachedRegexCount = 64;

	private static readonly ConcurrentDictionary<RegexCacheKey, Regex> s_regexCache = new();

	// Mirror of the cache keys in insertion order; eviction dequeues from the front. A key left
	// behind by a racing re-insert is skipped when its dictionary entry is already gone.
	private static readonly ConcurrentQueue<RegexCacheKey> s_insertionOrder = new();

	private static readonly object s_evictionLock = new();

	internal static int CachedRegexCount => s_regexCache.Count;

	/// <summary>Returns a cached regex for a search, creating it on first use.</summary>
	/// <remarks>
	/// A non-positive <paramref name="matchTimeout"/> maps to <see cref="Regex.InfiniteMatchTimeout"/>.
	/// </remarks>
	internal static Regex CreateRegex(string pattern, RegexOptions options, TimeSpan matchTimeout)
	{
		// Normalize the timeout before it enters the key so equivalent requests - zero and
		// Regex.InfiniteMatchTimeout both mean unbounded - share one cache slot and one parse.
		TimeSpan effectiveMatchTimeout = matchTimeout > TimeSpan.Zero ? matchTimeout : Regex.InfiniteMatchTimeout;

		// A case-insensitive pattern without CultureInvariant resolves case equivalence with the
		// culture captured at construction, so that culture joins the key; every other pattern shares
		// one culture-independent slot.
		string? capturedCultureName =
			(options & RegexOptions.IgnoreCase) != 0 && (options & RegexOptions.CultureInvariant) == 0 ? CultureInfo.CurrentCulture.Name : null;

		var key = new RegexCacheKey(pattern, options, MatchTimeout: effectiveMatchTimeout, capturedCultureName);

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

	/// <summary>Evicts entries until the cache is at or below the limit.</summary>
	/// <remarks>
	/// The trim is serialized so racing inserts cannot starve one another's evictions, and runs only after an insert overflowed the cache,
	/// so the read path stays lock-free.
	/// </remarks>
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

	/// <summary>Evicts the earliest inserted cached regex.</summary>
	/// <returns><see langword="true"/> when an entry was evicted, or <see langword="false"/> when the cache was empty.</returns>
	/// <remarks>
	/// Eviction is insertion-ordered rather than least-recently-used: a hit does not reorder the cache, keeping the read path allocation-
	/// and write-free.
	/// </remarks>
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
