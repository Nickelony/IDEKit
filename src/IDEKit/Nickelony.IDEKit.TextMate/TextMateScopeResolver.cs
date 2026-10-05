using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.TextMate;

/// <summary>Resolves TextMate token scopes into <see cref="TextMateTokenStyle"/> values using a <see cref="TextMateTokenTheme"/>.</summary>
/// <remarks>
/// <para>
/// Styles are resolved the way VS Code resolves them: one scope push at a time over the token's scope stack. Each push anchors the rightmost selector
/// part at one scope of the stack, and the most specific matching rule of that push overrides the accumulated attributes with the properties it sets.
/// A parent-scoped rule falls back to the theme's bare rules for the properties it omits - the way VS Code bakes their values into the rule
/// when it parses the theme - and any property that no rule of the push sets falls back to shallower pushes and then to the theme defaults.
/// A rule with a blank scope provides the defaults that apply to every token.
/// </para>
/// <para>
/// Selector matching follows TextMate semantics for space-separated selectors. The rightmost part matches the innermost scope of the matched path.
/// The remaining parent parts must match enclosing scopes in order, and a part may skip enclosing scopes that do not match it. A <c>&gt;</c> child
/// combinator requires the next part to match the scope that directly encloses the last matched scope. The exclusion (<c>-</c>), wildcard (<c>*</c>),
/// priority (<c>L:</c>), and parenthesized group operators belong to injection selectors and are not supported in theme selectors.
/// A selector that uses any of them, or that places a child combinator without a scope name on each side, never matches and is reported
/// through the constructor logger.
/// </para>
/// <para>
/// The ranking follows VS Code's token-theme ordering (TextMate's "Ranking Matches"): a deeper scope wins, then a longer parent scope name compared
/// from the deepest parent towards the root, then a higher parent part count, where child combinator parts are skipped in the name comparison
/// but still count. Rules that repeat the same selector merge with later values overwriting earlier ones for each property the later rule sets,
/// and distinct rules of equal specificity resolve in selector order, the order VS Code's theme parser sorts them in, not in the order the theme
/// declares them.
/// </para>
/// <para>
/// Theme values follow the TextMate data conventions: a rule's foreground accepts the recognized
/// forms that <see cref="TextMateTokenThemeRule.Foreground"/> lists, and a present <c>fontStyle</c> value - including an empty string -
/// resets the inherited traits before enabling the recognized ones, while an absent value keeps them; the non-standard <c>none</c>
/// keyword is accepted as an explicit reset. Invalid colors, unsupported selectors, misplaced child combinators, unrecognized traits,
/// and <see langword="null"/> rules are ignored and reported as warnings. Rule backgrounds are not supported and are ignored without a warning.
/// </para>
/// <para>
/// Resolved styles for non-empty scope sequences are cached per resolver instance in a bounded, synchronized cache, so one resolver can be shared
/// by several editors and threads; a caller that mutates a scope collection after a call only makes a later lookup miss the cache.
/// </para>
/// </remarks>
public sealed class TextMateScopeResolver
{
	internal const int MaxCacheEntryCount = 4096;

	private readonly TextMateSelectorRuleSet _rules;
	private readonly ConcurrentDictionary<ScopeCacheKey, TextMateTokenStyle> _cache = new();

	private int _cacheMissCount;

	/// <param name="theme">The token theme whose rules drive style resolution.</param>
	/// <param name="logger">
	/// An optional logger that reports invalid foreground colors, unsupported selectors, unrecognized font style traits, misplaced child combinators,
	/// and <see langword="null"/> theme rules.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
	public TextMateScopeResolver(TextMateTokenTheme theme, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(theme);
		_rules = TextMateSelectorRuleSet.Create(theme, logger);
	}

	/// <summary>The number of resolutions that were not served from the cache.</summary>
	internal int CacheMissCount => Volatile.Read(ref _cacheMissCount);

	/// <summary>Resolves the style for a token's scope sequence.</summary>
	/// <param name="scopes">The token scopes to resolve, ordered from the root scope to the innermost scope.</param>
	/// <returns>
	/// The resolved style, or <see cref="TextMateTokenStyle.Empty"/> when <paramref name="scopes"/> is <see langword="null"/> or empty.
	/// </returns>
	/// <exception cref="ArgumentException">The scope list contains a <see langword="null"/> entry.</exception>
	/// <remarks>
	/// The method is safe to call from any thread.
	/// </remarks>
	public TextMateTokenStyle Resolve(IReadOnlyList<string>? scopes)
	{
		if (scopes is null || scopes.Count == 0)
			return TextMateTokenStyle.Empty;

		// The null-entry guard is fused into the cache key: the key's hash walk rejects a null entry in
		// the same pass that produces the hash, so a cache hit costs one walk instead of the separate
		// validation walk plus the hash walk.
		var lookupKey = new ScopeCacheKey(scopes);

		if (_cache.TryGetValue(lookupKey, out TextMateTokenStyle cachedStyle))
			return cachedStyle;

		Interlocked.Increment(ref _cacheMissCount);

		ArgbColor? foreground = _rules.DefaultForeground;

		bool? isBold = _rules.DefaultTraits.Bold;
		bool? isItalic = _rules.DefaultTraits.Italic;
		bool? isUnderline = _rules.DefaultTraits.Underline;
		bool? isStrikethrough = _rules.DefaultTraits.Strikethrough;

		// VS Code parity: the theme is applied scope push by scope push. For every scope of the token's
		// stack, the scope path up to that scope is matched with the rightmost selector part anchored at
		// that innermost scope, and the most specific matching rule of the push overrides the accumulated
		// attributes with its set properties.
		// The two lists serve every push of this resolution; each push starts them empty again.
		List<TextMateSelectorRule> parentScopedMatches = [];
		List<TextMateSelectorRule> bareMatches = [];

		for (int anchorIndex = 0; anchorIndex < scopes.Count; anchorIndex++)
		{
			parentScopedMatches.Clear();
			bareMatches.Clear();

			_rules.CollectMatches(scopes, anchorIndex, parentScoped: parentScopedMatches, bare: bareMatches);

			if (parentScopedMatches.Count == 0 && bareMatches.Count == 0)
				continue;

			// Most specific first; distinct rules of equal specificity keep the selector order, which is
			// the order VS Code's candidate ranking uses. The secondary comparison keeps the order
			// deterministic even though List<T>.Sort is unstable.
			parentScopedMatches.Sort(TextMateSelectorRule.CompareByMatchPrecedence);
			bareMatches.Sort(TextMateSelectorRule.CompareByMatchPrecedence);

			// The bare rules of the push form one merged candidate, most specific first. VS Code bakes
			// these values into a parent-scoped rule while it parses the theme, so the candidate also
			// fills the properties that a winning parent-scoped rule leaves unset.
			ArgbColor? bareForeground = null;

			bool? bareBold = null;
			bool? bareItalic = null;
			bool? bareUnderline = null;
			bool? bareStrikethrough = null;

			for (int i = 0; i < bareMatches.Count; i++)
			{
				TextMateSelectorRule rule = bareMatches[i];

				bareForeground ??= rule.Foreground;
				bareBold ??= rule.Traits.Bold;
				bareItalic ??= rule.Traits.Italic;
				bareUnderline ??= rule.Traits.Underline;
				bareStrikethrough ??= rule.Traits.Strikethrough;

				if (bareForeground.HasValue && bareBold.HasValue && bareItalic.HasValue && bareUnderline.HasValue && bareStrikethrough.HasValue)
					break;
			}

			ArgbColor? pushForeground;

			bool? pushBold;
			bool? pushItalic;
			bool? pushUnderline;
			bool? pushStrikethrough;

			// A deeper bare candidate outranks a matching parent-scoped rule; at equal depth the
			// parent-scoped rule wins the parent-count tiebreaker and inherits the candidate values it
			// does not set.
			if (parentScopedMatches.Count == 0 || (bareMatches.Count > 0 && bareMatches[0].ScopeDepth > parentScopedMatches[0].ScopeDepth))
			{
				pushForeground = bareForeground;
				pushBold = bareBold;
				pushItalic = bareItalic;
				pushUnderline = bareUnderline;
				pushStrikethrough = bareStrikethrough;
			}
			else
			{
				TextMateSelectorRule winner = parentScopedMatches[0];

				pushForeground = winner.Foreground ?? bareForeground;
				pushBold = winner.Traits.Bold ?? bareBold;
				pushItalic = winner.Traits.Italic ?? bareItalic;
				pushUnderline = winner.Traits.Underline ?? bareUnderline;
				pushStrikethrough = winner.Traits.Strikethrough ?? bareStrikethrough;
			}

			if (pushForeground.HasValue)
				foreground = pushForeground;

			if (pushBold.HasValue)
				isBold = pushBold;

			if (pushItalic.HasValue)
				isItalic = pushItalic;

			if (pushUnderline.HasValue)
				isUnderline = pushUnderline;

			if (pushStrikethrough.HasValue)
				isStrikethrough = pushStrikethrough;
		}

		var style = new TextMateTokenStyle(
			foreground,
			IsBold: isBold ?? false,
			IsItalic: isItalic ?? false,
			IsUnderline: isUnderline ?? false,
			IsStrikethrough: isStrikethrough ?? false
		);

		StoreInCache(scopes, style);
		return style;
	}

	private void StoreInCache(IReadOnlyList<string> scopes, TextMateTokenStyle style)
	{
		// Bounds resolver memory for long sessions that resolve many distinct scope sequences. Cached
		// styles are cheap to rebuild, so an approximate trim is preferable to tracking usage precisely.
		int count = _cache.Count;

		if (count >= MaxCacheEntryCount)
			TrimCache(count);

		_cache[ScopeCacheKey.CreateForStorage(scopes)] = style;
	}

	/// <summary>
	/// Evicts entries down to half the entry limit, so a burst of new scope sequences drops a bounded fraction of the cache instead of all of it.
	/// </summary>
	/// <param name="count">The entry count observed before the trim.</param>
	/// <remarks>
	/// The eviction removes an arbitrary subset rather than tracking recency, so it is an approximate trim; removing a fraction
	/// keeps the hit rate for the sequences that stay resident, and an evicted sequence is rebuilt on its next resolve. It runs only when the entry
	/// count reaches <see cref="MaxCacheEntryCount"/>, so the walk's cost is amortized over the inserts between trims.
	/// </remarks>
	private void TrimCache(int count)
	{
		int removeCount = count - (MaxCacheEntryCount / 2);

		if (removeCount <= 0)
			return;

		foreach (KeyValuePair<ScopeCacheKey, TextMateTokenStyle> entry in _cache)
		{
			if (removeCount == 0)
				break;

			if (_cache.TryRemove(entry.Key, out _))
				removeCount--;
		}
	}

	/// <summary>
	/// Dictionary key describing a token scope sequence without allocating a combined string per lookup. Keys created for storage own a snapshot
	/// of the sequence, so later mutation of a caller-owned list cannot corrupt the cache.
	/// </summary>
	private readonly struct ScopeCacheKey : IEquatable<ScopeCacheKey>
	{
		private readonly IReadOnlyList<string> _scopes;
		private readonly int _hashCode;

		internal ScopeCacheKey(IReadOnlyList<string> scopes)
			: this(scopes, ownsSnapshot: false) { }

		private ScopeCacheKey(IReadOnlyList<string> scopes, bool ownsSnapshot)
		{
			_scopes = ownsSnapshot ? [.. scopes] : scopes;
			_hashCode = ComputeHashCode(scopes);
		}

		internal static ScopeCacheKey CreateForStorage(IReadOnlyList<string> scopes) => new(scopes, ownsSnapshot: true);

		public bool Equals(ScopeCacheKey other)
		{
			if (_scopes.Count != other._scopes.Count)
				return false;

			for (int i = 0; i < _scopes.Count; i++)
			{
				if (!string.Equals(_scopes[i], other._scopes[i], StringComparison.Ordinal))
					return false;
			}

			return true;
		}

		public override bool Equals(object? obj) => obj is ScopeCacheKey other && Equals(other);

		public override int GetHashCode() => _hashCode;

		private static int ComputeHashCode(IReadOnlyList<string> scopes)
		{
			var hashCode = new HashCode();

			for (int i = 0; i < scopes.Count; i++)
			{
				// The null-entry guard rides on the hash walk, so a lookup validates and hashes the
				// sequence in one pass. Key creation is the only entry point, so no scope sequence can
				// reach the cache without this check.
				if (scopes[i] is null)
					throw new ArgumentException("The scope list must not contain null entries.", nameof(scopes));

				hashCode.Add(scopes[i], StringComparer.Ordinal);
			}

			return hashCode.ToHashCode();
		}
	}
}
