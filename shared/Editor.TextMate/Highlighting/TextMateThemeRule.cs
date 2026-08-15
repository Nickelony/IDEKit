#if AVALONIAEDIT
using Avalonia.Media;
using Brush = Avalonia.Media.IBrush;
#else
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// One parsed and validated theme selector, ranked against the other rules of the same theme.
/// </summary>
/// <remarks>
/// Selector matching follows TextMate semantics: the rightmost selector part must match the innermost
/// scope of the matched path, the remaining parent parts must match enclosing scopes in order, and a
/// <c>&gt;</c> child combinator requires the next part to match the scope that directly encloses the
/// previously matched scope.
/// </remarks>
internal sealed class TextMateThemeRule
{
	private readonly string[] _selectorParts;

	internal TextMateThemeRule(
		string[] selectorParts,
		string[] parentScopes,
		int sequenceIndex,
		Brush? foreground,
		TextMateFontTraits traits)
	{
		_selectorParts = selectorParts;
		ParentScopes = parentScopes;
		SequenceIndex = sequenceIndex;
		Foreground = foreground;
		Traits = traits;

		// The scope depth is fully determined by the anchored selector part, so it is computed here
		// instead of being threaded through every construction site.
		ScopeDepth = selectorParts[^1].Split('.').Length;
	}

	/// <summary>
	/// Gets the parent selector parts in matching order: index 0 is the deepest parent.
	/// </summary>
	internal string[] ParentScopes { get; }

	/// <summary>
	/// Gets the selector parts in matching order: index 0 is the outermost parent and the last element
	/// is the anchored scope.
	/// </summary>
	internal string[] SelectorParts => _selectorParts;

	/// <summary>
	/// Gets a value indicating whether the selector anchors a parent chain.
	/// </summary>
	internal bool HasParentScopes => ParentScopes.Length > 0;

	/// <summary>
	/// Gets the number of dot-separated segments in the rightmost selector part, the specificity
	/// component the ranking compares.
	/// </summary>
	internal int ScopeDepth { get; }

	/// <summary>
	/// Gets the position of the parsed rule in the resolver's rule list; it breaks specificity ties
	/// towards the earlier rule.
	/// </summary>
	internal int SequenceIndex { get; }

	/// <summary>
	/// Gets the foreground of the rule, or <see langword="null"/> when the rule sets none.
	/// </summary>
	internal Brush? Foreground { get; }

	/// <summary>
	/// Gets the font traits of the rule; an unset trait stays <see langword="null"/>.
	/// </summary>
	internal TextMateFontTraits Traits { get; }

	/// <summary>
	/// Creates a copy of the rule with a different sequence index.
	/// </summary>
	/// <param name="sequenceIndex">The sequence index of the copy.</param>
	/// <returns>The copied rule.</returns>
	internal TextMateThemeRule WithSequenceIndex(int sequenceIndex)
		=> new(SelectorParts, ParentScopes, sequenceIndex, Foreground, Traits);

	/// <summary>
	/// Determines whether the selector matches the token's scope sequence with its rightmost part
	/// anchored at the given scope: that scope must match the rightmost selector part, and the
	/// remaining parent parts must match enclosing scopes in order, where a part may skip enclosing
	/// scopes that do not match it and a child combinator part requires the next part to match the
	/// scope that directly encloses the previously matched scope.
	/// </summary>
	/// <param name="scopes">The token scopes, ordered from the root scope to the innermost scope.</param>
	/// <param name="anchorIndex">The index of the scope that acts as the innermost scope.</param>
	/// <returns><see langword="true"/> when the selector matches the anchored scope path.</returns>
	internal bool MatchesAnchor(IReadOnlyList<string> scopes, int anchorIndex)
	{
		if (!MatchesScope(_selectorParts[^1], scopes[anchorIndex]))
			return false;

		return MatchesEnclosingScopes(_selectorParts, scopes, anchorIndex);
	}

	/// <summary>
	/// Matches the parent selector parts against the scopes enclosing the anchored scope, mirroring
	/// vscode-textmate's parent-scope matching.
	/// </summary>
	/// <param name="parts">The selector parts, ordered from the outermost part to the anchored part.</param>
	/// <param name="scopes">The token scopes, ordered from the root scope to the innermost scope.</param>
	/// <param name="anchorIndex">The index of the scope that acts as the innermost scope.</param>
	/// <returns><see langword="true"/> when every parent part matched.</returns>
	private static bool MatchesEnclosingScopes(string[] parts, IReadOnlyList<string> scopes, int anchorIndex)
	{
		// Parent parts are matched deepest-first; validation drops leading, trailing, and adjacent
		// child combinators before matching runs.
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
	/// Determines whether a token scope matches a selector part, following TextMate scope matching:
	/// the scope equals the part or extends it with a dot-separated suffix.
	/// </summary>
	/// <param name="selector">The selector part to match.</param>
	/// <param name="scope">The token scope to test.</param>
	/// <returns><see langword="true"/> when the scope matches the selector part.</returns>
	private static bool MatchesScope(string selector, string scope)
	{
		if (string.Equals(scope, selector, StringComparison.Ordinal))
			return true;

		return scope.StartsWith(selector + ".", StringComparison.Ordinal);
	}

	/// <summary>
	/// Orders two matching rules by specificity, mirroring VS Code's token-theme ranking (TextMate's
	/// "Ranking Matches"): a deeper scope wins, then a longer parent scope name (compared from the
	/// deepest parent towards the root), then a higher parent part count, where child combinators are
	/// skipped in the name comparison but still count.
	/// </summary>
	/// <param name="left">The first rule to compare.</param>
	/// <param name="right">The second rule to compare.</param>
	/// <returns>
	/// A negative value when <paramref name="left"/> is more specific, a positive value when
	/// <paramref name="right"/> is more specific, and zero when both rank equally.
	/// </returns>
	internal static int CompareBySpecificity(TextMateThemeRule left, TextMateThemeRule right)
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

	/// <summary>
	/// Orders two matching rules of one scope push by specificity, keeping the selector order when
	/// distinct rules tie.
	/// </summary>
	/// <param name="left">The first rule to compare.</param>
	/// <param name="right">The second rule to compare.</param>
	/// <returns>
	/// A negative value when <paramref name="left"/> sorts first, a positive value when
	/// <paramref name="right"/> sorts first, and zero when both are the same rule.
	/// </returns>
	internal static int CompareByMatchPrecedence(TextMateThemeRule left, TextMateThemeRule right)
	{
		int bySpecificity = CompareBySpecificity(left, right);

		return bySpecificity != 0
			? bySpecificity
			: left.SequenceIndex.CompareTo(right.SequenceIndex);
	}

	/// <summary>
	/// Orders two parsed rules the way VS Code's theme parser sorts them before ranking: by the
	/// anchored scope, then by the parent scopes from the deepest parent towards the root, then by
	/// their position in the theme. The sorted position also breaks specificity ties at match time.
	/// </summary>
	/// <param name="left">The first rule to compare.</param>
	/// <param name="right">The second rule to compare.</param>
	/// <returns>
	/// A negative value when <paramref name="left"/> sorts first, a positive value when
	/// <paramref name="right"/> sorts first, and zero when both are equal.
	/// </returns>
	internal static int CompareByParseOrder(TextMateThemeRule left, TextMateThemeRule right)
	{
		int byScope = string.CompareOrdinal(left.SelectorParts[^1], right.SelectorParts[^1]);

		if (byScope != 0)
			return byScope;

		int sharedParentCount = Math.Min(left.ParentScopes.Length, right.ParentScopes.Length);

		for (int i = 0; i < sharedParentCount; i++)
		{
			int byParent = string.CompareOrdinal(left.ParentScopes[i], right.ParentScopes[i]);

			if (byParent != 0)
				return byParent;
		}

		if (left.ParentScopes.Length != right.ParentScopes.Length)
			return left.ParentScopes.Length.CompareTo(right.ParentScopes.Length);

		return left.SequenceIndex.CompareTo(right.SequenceIndex);
	}
}
