using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.TextMate;

/// <summary>One parsed and validated selector of a token theme, ranked against the other selector rules of the same theme.</summary>
internal sealed class TextMateSelectorRule
{
	internal TextMateSelectorRule(string[] selectorParts, string[] parentScopes, int sequenceIndex, ArgbColor? foreground, TextMateFontTraits traits)
	{
		SelectorParts = selectorParts;
		ParentScopes = parentScopes;
		SequenceIndex = sequenceIndex;
		Foreground = foreground;
		Traits = traits;

		// The scope depth is fully determined by the anchored selector part, so it is computed here
		// instead of being threaded through every construction site.
		ScopeDepth = selectorParts[^1].Split('.').Length;
	}

	/// <summary>The parent selector parts in the order the ranking compares them: index 0 is the deepest parent.</summary>
	internal string[] ParentScopes { get; }

	/// <summary>The selector parts in source order: index 0 is the outermost parent and the last element is the anchored scope.</summary>
	internal string[] SelectorParts { get; }

	/// <summary>Indicates whether the selector anchors a parent chain.</summary>
	internal bool HasParentScopes => ParentScopes.Length > 0;

	/// <summary>The number of dot-separated segments in the rightmost selector part, the specificity component the ranking compares.</summary>
	internal int ScopeDepth { get; }

	/// <summary>The position of the parsed rule in the resolver's rule list; it breaks specificity ties towards the earlier rule.</summary>
	internal int SequenceIndex { get; }

	/// <summary>The foreground of the rule, or <see langword="null"/> when the rule sets none.</summary>
	internal ArgbColor? Foreground { get; }

	/// <summary>The font traits of the rule; an unset trait stays <see langword="null"/>.</summary>
	internal TextMateFontTraits Traits { get; }

	/// <summary>Creates a copy of the rule with a different sequence index.</summary>
	/// <param name="sequenceIndex">The sequence index of the copy.</param>
	/// <returns>The copied rule.</returns>
	internal TextMateSelectorRule WithSequenceIndex(int sequenceIndex) => new(SelectorParts, ParentScopes, sequenceIndex, Foreground, Traits);

	/// <summary>Determines whether the selector matches the token's scope sequence with its rightmost part anchored at the given scope.</summary>
	/// <param name="scopes">The token scopes, ordered from the root scope to the innermost scope.</param>
	/// <param name="anchorIndex">The index of the scope that acts as the innermost scope.</param>
	/// <returns><see langword="true"/> when the selector matches the anchored scope path.</returns>
	/// <remarks>
	/// The anchored scope must match the rightmost selector part. The remaining parent parts must match enclosing scopes in order, and a part
	/// may skip enclosing scopes that do not match it. A child combinator part requires the next part to match the scope that directly encloses
	/// the last matched scope.
	/// </remarks>
	internal bool MatchesAnchor(IReadOnlyList<string> scopes, int anchorIndex)
	{
		if (!MatchesScope(SelectorParts[^1], scopes[anchorIndex]))
			return false;

		return MatchesEnclosingScopes(SelectorParts, scopes, anchorIndex);
	}

	/// <summary>
	/// Matches the parent selector parts against the scopes enclosing the anchored scope, mirroring vscode-textmate's parent-scope matching.
	/// </summary>
	/// <param name="parts">The selector parts, ordered from the outermost part to the anchored part.</param>
	/// <param name="scopes">The token scopes, ordered from the root scope to the innermost scope.</param>
	/// <param name="anchorIndex">The index of the scope that acts as the innermost scope.</param>
	/// <returns><see langword="true"/> when every parent part matched.</returns>
	private static bool MatchesEnclosingScopes(string[] parts, IReadOnlyList<string> scopes, int anchorIndex)
	{
		// Parent parts are matched deepest-first; validation rejects a selector that has a leading, trailing, or adjacent
		// child combinator before matching runs.
		int partIndex = parts.Length - 2;
		int scopeIndex = anchorIndex - 1;

		while (partIndex >= 0)
		{
			string pattern = parts[partIndex];
			bool mustMatchDirectly = false;

			if (pattern == ">")
			{
				pattern = parts[--partIndex];
				mustMatchDirectly = true;
			}

			while (true)
			{
				if (scopeIndex < 0)
					return false;

				if (MatchesScope(pattern, scopes[scopeIndex]))
					break;

				if (mustMatchDirectly)
					return false;

				scopeIndex--;
			}

			scopeIndex--;
			partIndex--;
		}

		return true;
	}

	/// <summary>
	/// Determines whether a token scope matches a selector part, following TextMate scope matching: the scope equals the part or extends
	/// it with a dot-separated suffix.
	/// </summary>
	/// <param name="selector">The selector part to match.</param>
	/// <param name="scope">The token scope to test.</param>
	/// <returns><see langword="true"/> when the scope matches the selector part.</returns>
	private static bool MatchesScope(string selector, string scope)
	{
		if (string.Equals(scope, selector, StringComparison.Ordinal))
			return true;

		// The scope extends the selector when it starts with it and the next character is the dot that separates the segments.
		return scope.Length > selector.Length && scope[selector.Length] == '.' && scope.AsSpan(0, selector.Length).SequenceEqual(selector);
	}

	/// <summary>
	/// Orders two matching rules by specificity, mirroring VS Code's token-theme ranking (TextMate's "Ranking Matches"): a deeper scope wins,
	/// then a longer parent scope name (compared from the deepest parent towards the root), then a higher parent part count, where child combinators
	/// are skipped in the name comparison but still count.
	/// </summary>
	/// <param name="left">The first rule to compare.</param>
	/// <param name="right">The second rule to compare.</param>
	/// <returns>
	/// A negative value when <paramref name="left"/> is more specific, a positive value when <paramref name="right"/> is more specific,
	/// and zero when both rank equally.
	/// </returns>
	internal static int CompareBySpecificity(TextMateSelectorRule left, TextMateSelectorRule right)
	{
		if (left.ScopeDepth != right.ScopeDepth)
			return right.ScopeDepth.CompareTo(left.ScopeDepth);

		int leftParentIndex = 0;
		int rightParentIndex = 0;

		while (true)
		{
			// Child combinators do not participate in the parent name comparison.
			if (leftParentIndex < left.ParentScopes.Length && left.ParentScopes[leftParentIndex] == ">")
				leftParentIndex++;

			if (rightParentIndex < right.ParentScopes.Length && right.ParentScopes[rightParentIndex] == ">")
				rightParentIndex++;

			if (leftParentIndex >= left.ParentScopes.Length || rightParentIndex >= right.ParentScopes.Length)
				break;

			int lengthDifference = right.ParentScopes[rightParentIndex].Length - left.ParentScopes[leftParentIndex].Length;

			if (lengthDifference != 0)
				return lengthDifference;

			leftParentIndex++;
			rightParentIndex++;
		}

		// The parent part count includes child combinators, mirroring VS Code's ranking: "b > a"
		// outranks "b a" when both match.
		return right.ParentScopes.Length.CompareTo(left.ParentScopes.Length);
	}

	/// <summary>Orders two matching rules of one scope push by specificity, keeping the selector order when distinct rules tie.</summary>
	/// <param name="left">The first rule to compare.</param>
	/// <param name="right">The second rule to compare.</param>
	/// <returns>
	/// A negative value when <paramref name="left"/> sorts first, a positive value when <paramref name="right"/> sorts first,
	/// and zero when both are the same rule.
	/// </returns>
	internal static int CompareByMatchPrecedence(TextMateSelectorRule left, TextMateSelectorRule right)
	{
		int bySpecificity = CompareBySpecificity(left, right);
		return bySpecificity != 0 ? bySpecificity : left.SequenceIndex.CompareTo(right.SequenceIndex);
	}

	/// <summary>
	/// Orders two parsed rules the way VS Code's theme parser sorts them before ranking: by the anchored scope, then by the parent scopes the way VS
	/// Code's <c>strArrCmp</c> does (by array length first, then element by element from the deepest parent towards the root), then by their position
	/// in the theme.
	/// </summary>
	/// <param name="left">The first rule to compare.</param>
	/// <param name="right">The second rule to compare.</param>
	/// <returns>
	/// A negative value when <paramref name="left"/> sorts first, a positive value when <paramref name="right"/> sorts first,
	/// and zero when both are equal.
	/// </returns>
	/// <remarks>
	/// The sorted position also breaks specificity ties at match time.
	/// </remarks>
	internal static int CompareByParseOrder(TextMateSelectorRule left, TextMateSelectorRule right)
	{
		int byScope = string.CompareOrdinal(left.SelectorParts[^1], right.SelectorParts[^1]);

		if (byScope != 0)
			return byScope;

		// VS Code's strArrCmp compares the array lengths before any element, so a rule with fewer parent
		// scopes sorts before one with more regardless of their contents; only equal-length arrays are
		// compared element-wise.
		if (left.ParentScopes.Length != right.ParentScopes.Length)
			return left.ParentScopes.Length.CompareTo(right.ParentScopes.Length);

		for (int i = 0; i < left.ParentScopes.Length; i++)
		{
			int byParent = string.CompareOrdinal(left.ParentScopes[i], right.ParentScopes[i]);

			if (byParent != 0)
				return byParent;
		}

		return left.SequenceIndex.CompareTo(right.SequenceIndex);
	}
}
