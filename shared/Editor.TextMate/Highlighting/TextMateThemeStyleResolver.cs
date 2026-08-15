using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
#if AVALONIAEDIT
using Avalonia.Media;
using Brush = Avalonia.Media.IBrush;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// Resolves TextMate token scopes into <see cref="TextRunStyle"/> instances using a
/// <see cref="TextMateTokenTheme"/>.
/// </summary>
/// <remarks>
/// <para>
/// Styles are resolved the way VS Code resolves them: one scope push at a time over the token's scope
/// stack. Each push anchors the rightmost selector part at one scope of the stack, and the most
/// specific matching rule of that push overrides the accumulated attributes with the properties it
/// sets. A parent-scoped rule falls back to the theme's bare rules for the properties it omits - the
/// way VS Code bakes their values into the rule when it parses the theme - and any property that no
/// rule of the push sets falls back to shallower pushes and then to the theme defaults. A rule with
/// a blank scope provides the defaults that apply to every token.
/// </para>
/// <para>
/// Selector matching follows TextMate semantics for space-separated selectors: the rightmost part
/// matches the innermost scope of the matched path, and the remaining parent parts must match
/// enclosing scopes in order, where a part may skip enclosing scopes that do not match it and a
/// <c>&gt;</c> child combinator requires the next part to match the scope that directly encloses the
/// previously matched scope. The exclusion (<c>-</c>), wildcard (<c>*</c>), priority (<c>L:</c>), and
/// parenthesised group operators belong to injection selectors and are not supported in theme
/// selectors; a selector that uses any of them, or that places a child combinator without a scope name
/// on each side, never matches and is reported through the constructor logger.
/// </para>
/// <para>
/// The ranking follows VS Code's token-theme ordering (TextMate's "Ranking Matches"): a deeper scope
/// wins, then a longer parent scope name compared from the deepest parent towards the root, then a
/// higher parent part count, where child combinator parts are skipped in the name comparison but still
/// count. Rules that repeat the same selector merge with later values overwriting earlier ones for
/// each property the later rule sets, and distinct rules of equal specificity resolve in the order the
/// theme declares them.
/// </para>
/// <para>
/// Theme values follow the TextMate data conventions: a rule's foreground accepts the six-digit
/// <c>#RRGGBB</c> and three-digit <c>#RGB</c> forms, the eight-digit <c>#RRGGBBAA</c> form, and the
/// four-digit <c>#RGBA</c> form, whose alpha component is normalized to the toolkit order before
/// parsing. The eight- and four-digit forms are always read in TextMate order; the toolkit forms that
/// put the alpha component first (<c>#AARRGGBB</c> and <c>#ARGB</c>) are not recognized. A present
/// <c>fontStyle</c> value - including an
/// empty string - resets the inherited traits before enabling the recognized ones, while an absent
/// value keeps them; the non-standard <c>none</c> keyword is accepted as an explicit reset. Invalid
/// colors, unsupported selectors, misplaced child combinators, unrecognized traits, and null rules are
/// ignored and reported as warnings. Rule backgrounds are not supported and are ignored without a
/// warning.
/// </para>
/// <para>
/// Resolved styles for non-empty scope sequences are cached per resolver instance in a bounded,
/// synchronized cache that trims down to a lower water mark when it fills, so one resolver can be
/// shared by several editors and threads and a burst of new sequences does not evict the whole
/// cache. Cache keys compare scope sequences by value and snapshot the sequence on storage,
/// so a caller that mutates a scope collection after a call only makes a later lookup miss the
/// cache.
/// </para>
/// </remarks>
public sealed class TextMateThemeStyleResolver
{
	internal const int MaxCacheEntryCount = 4096;

	private static readonly TextDecorationCollection s_underlineDecorations = CloneTextDecorations(TextDecorations.Underline);
	private static readonly TextDecorationCollection s_strikethroughDecorations = CloneTextDecorations(TextDecorations.Strikethrough);
	private static readonly TextDecorationCollection s_underlineAndStrikethroughDecorations = CreateCombinedDecorations();

	private readonly TextMateThemeRuleSet _themeRules;
	private readonly ConcurrentDictionary<ScopeCacheKey, TextRunStyle> _cache = new();
	private int _cacheMissCount;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateThemeStyleResolver"/> class.
	/// </summary>
	/// <param name="theme">The token theme whose rules drive style resolution.</param>
	/// <param name="logger">
	/// An optional logger used to report invalid foreground colors, unsupported selectors,
	/// unrecognized font style traits, misplaced child combinators, and null theme rules.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="theme"/> is <see langword="null"/>.
	/// </exception>
	public TextMateThemeStyleResolver(TextMateTokenTheme theme, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(theme);

		_themeRules = TextMateThemeRuleSet.Create(theme, logger);
	}

	/// <summary>
	/// Gets the number of resolutions that were not served from the cache; used to pin the cache
	/// contract in tests.
	/// </summary>
	internal int CacheMissCount => Volatile.Read(ref _cacheMissCount);

	/// <summary>
	/// Resolves the visual style for a token's scope sequence.
	/// </summary>
	/// <param name="scopes">
	/// The token scopes to resolve, ordered from the root scope to the innermost scope.
	/// </param>
	/// <returns>
	/// The resolved style, or the shared <see cref="TextRunStyle.Empty"/> value when
	/// <paramref name="scopes"/> is <see langword="null"/> or empty.
	/// </returns>
	/// <remarks>
	/// The method is safe to call from any thread and returns an immutable style that is shared by all
	/// tokens whose scope sequence resolves to it.
	/// </remarks>
	/// <exception cref="ArgumentException">The scope list contains a null entry.</exception>
	public TextRunStyle Resolve(IReadOnlyList<string>? scopes)
	{
		if (scopes is null || scopes.Count == 0)
			return TextRunStyle.Empty;

		// The null-entry guard is fused into the cache key: the key's hash walk rejects a null entry in
		// the same pass that produces the hash, so a cache hit costs one walk instead of the separate
		// validation walk plus the hash walk.
		var lookupKey = new ScopeCacheKey(scopes);

		if (_cache.TryGetValue(lookupKey, out TextRunStyle cachedStyle))
			return cachedStyle;

		Interlocked.Increment(ref _cacheMissCount);

		Brush? foreground = _themeRules.DefaultForeground;
		bool? isBold = _themeRules.DefaultTraits.Bold;
		bool? isItalic = _themeRules.DefaultTraits.Italic;
		bool? isUnderline = _themeRules.DefaultTraits.Underline;
		bool? isStrikethrough = _themeRules.DefaultTraits.Strikethrough;

		// VS Code parity: the theme is applied scope push by scope push. For every scope of the token's
		// stack, the scope path up to that scope is matched with the rightmost selector part anchored at
		// that innermost scope, and the most specific matching rule of the push overrides the accumulated
		// attributes with its set properties.
		for (int anchorIndex = 0; anchorIndex < scopes.Count; anchorIndex++)
		{
			List<TextMateThemeRule> parentScopedMatches = [];
			List<TextMateThemeRule> bareMatches = [];

			for (int i = 0; i < _themeRules.Rules.Count; i++)
			{
				TextMateThemeRule rule = _themeRules.Rules[i];

				if (!rule.MatchesAnchor(scopes, anchorIndex))
					continue;

				if (rule.HasParentScopes)
					parentScopedMatches.Add(rule);
				else
					bareMatches.Add(rule);
			}

			if (parentScopedMatches.Count == 0 && bareMatches.Count == 0)
				continue;

			// Most specific first; distinct rules of equal specificity keep the selector order, which is
			// the order VS Code's candidate ranking uses. The secondary comparison keeps the order
			// deterministic even though List<T>.Sort is unstable.
			parentScopedMatches.Sort(TextMateThemeRule.CompareByMatchPrecedence);
			bareMatches.Sort(TextMateThemeRule.CompareByMatchPrecedence);

			// The bare rules of the push form one merged candidate, most specific first. VS Code bakes
			// these values into a parent-scoped rule while it parses the theme, so the candidate also
			// fills the properties that a winning parent-scoped rule leaves unset.
			Brush? bareForeground = null;
			bool? bareBold = null;
			bool? bareItalic = null;
			bool? bareUnderline = null;
			bool? bareStrikethrough = null;

			for (int i = 0; i < bareMatches.Count; i++)
			{
				TextMateThemeRule rule = bareMatches[i];

				bareForeground ??= rule.Foreground;
				bareBold ??= rule.Traits.Bold;
				bareItalic ??= rule.Traits.Italic;
				bareUnderline ??= rule.Traits.Underline;
				bareStrikethrough ??= rule.Traits.Strikethrough;

				if (bareForeground is not null
					&& bareBold.HasValue
					&& bareItalic.HasValue
					&& bareUnderline.HasValue
					&& bareStrikethrough.HasValue)
				{
					break;
				}
			}

			Brush? pushForeground;
			bool? pushBold;
			bool? pushItalic;
			bool? pushUnderline;
			bool? pushStrikethrough;

			// A deeper bare candidate outranks a matching parent-scoped rule; at equal depth the
			// parent-scoped rule wins the parent-count tiebreaker and inherits the candidate values it
			// does not set.
			if (parentScopedMatches.Count == 0
				|| (bareMatches.Count > 0 && bareMatches[0].ScopeDepth > parentScopedMatches[0].ScopeDepth))
			{
				pushForeground = bareForeground;
				pushBold = bareBold;
				pushItalic = bareItalic;
				pushUnderline = bareUnderline;
				pushStrikethrough = bareStrikethrough;
			}
			else
			{
				TextMateThemeRule winner = parentScopedMatches[0];

				pushForeground = winner.Foreground ?? bareForeground;
				pushBold = winner.Traits.Bold ?? bareBold;
				pushItalic = winner.Traits.Italic ?? bareItalic;
				pushUnderline = winner.Traits.Underline ?? bareUnderline;
				pushStrikethrough = winner.Traits.Strikethrough ?? bareStrikethrough;
			}

			if (pushForeground is not null)
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

		TextDecorationCollection? textDecorations = CreateTextDecorations(isUnderline ?? false, isStrikethrough ?? false);

		TextRunStyle style = new(
			foreground,
			isBold ?? false,
			isItalic ?? false,
			textDecorations);

		StoreInCache(scopes, style);
		return style;
	}

	private void StoreInCache(IReadOnlyList<string> scopes, TextRunStyle style)
	{
		// Bounds resolver memory for long sessions that resolve many distinct scope sequences. Cached
		// styles are cheap to rebuild, so an approximate trim is preferable to tracking usage precisely.
		int count = _cache.Count;

		if (count >= MaxCacheEntryCount)
			TrimCache(count);

		_cache[ScopeCacheKey.CreateForStorage(scopes)] = style;
	}

	/// <summary>
	/// Evicts entries down to the lower water mark so a burst of new scope sequences drops a bounded
	/// fraction of the cache instead of every entry every editor shares.
	/// </summary>
	/// <remarks>
	/// The eviction walks the dictionary and removes an arbitrary subset; it does not track recency, so
	/// it is an approximate trim rather than an exact LRU. Removing a fraction keeps the cache's hit rate
	/// for the scope sequences that stay resident, and a sequence that is evicted is rebuilt on its next
	/// resolve. This runs only when the entry count reaches <see cref="MaxCacheEntryCount"/>, so the walk's
	/// cost is amortized over the inserts between trims.
	/// </remarks>
	/// <param name="count">The entry count observed before the trim.</param>
	private void TrimCache(int count)
	{
		int removeCount = count - (MaxCacheEntryCount / 2);

		if (removeCount <= 0)
			return;

		foreach (KeyValuePair<ScopeCacheKey, TextRunStyle> entry in _cache)
		{
			if (removeCount == 0)
				break;

			if (_cache.TryRemove(entry.Key, out _))
				removeCount--;
		}
	}

	private static TextDecorationCollection? CreateTextDecorations(bool isUnderline, bool isStrikethrough)
		=> (isUnderline, isStrikethrough) switch
		{
			(true, true) => s_underlineAndStrikethroughDecorations,
			(true, false) => s_underlineDecorations,
			(false, true) => s_strikethroughDecorations,
			_ => null,
		};

	private static TextDecorationCollection CreateCombinedDecorations()
	{
		var decorations = new TextDecorationCollection();

		foreach (TextDecoration decoration in s_underlineDecorations)
			decorations.Add(decoration);

		foreach (TextDecoration decoration in s_strikethroughDecorations)
			decorations.Add(decoration);

#if !AVALONIAEDIT
		decorations.Freeze();
#endif
		return decorations;
	}

	private static TextDecorationCollection CloneTextDecorations(TextDecorationCollection source)
	{
#if AVALONIAEDIT
		// Avalonia's decoration collection is a plain list with no freeze or clone; copy it so the shared
		// value is owned by this type instead of aliasing the toolkit's static instance.
		var clone = new TextDecorationCollection(source);
#else
		var clone = source.Clone();
		clone.Freeze();
#endif
		return clone;
	}

	/// <summary>
	/// Dictionary key describing a token scope sequence without allocating a combined string per lookup.
	/// Keys created for storage own a snapshot of the sequence, so later mutation of a caller-owned list
	/// cannot corrupt the cache.
	/// </summary>
	private readonly struct ScopeCacheKey : IEquatable<ScopeCacheKey>
	{
		private readonly IReadOnlyList<string> _scopes;
		private readonly int _hashCode;

		public ScopeCacheKey(IReadOnlyList<string> scopes)
			: this(scopes, ownsSnapshot: false)
		{
		}

		private ScopeCacheKey(IReadOnlyList<string> scopes, bool ownsSnapshot)
		{
			_scopes = ownsSnapshot ? [.. scopes] : scopes;
			_hashCode = ComputeHashCode(scopes);
		}

		public static ScopeCacheKey CreateForStorage(IReadOnlyList<string> scopes)
			=> new(scopes, ownsSnapshot: true);

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

		public override bool Equals(object? obj)
			=> obj is ScopeCacheKey other && Equals(other);

		public override int GetHashCode()
			=> _hashCode;

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
