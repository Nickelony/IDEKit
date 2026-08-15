using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Computes minimal single-range document changes using common-prefix and common-suffix scans.
/// </summary>
public static class TextIncrementalEditCalculator
{
	/// <summary>
	/// Computes the minimal single-range replacement that transforms the old content into the new content.
	/// </summary>
	/// <remarks>
	/// The range start and end never split a UTF-16 surrogate pair or a CRLF sequence: when the
	/// minimal boundary would split one, the range is widened - never narrowed - to include the whole
	/// unit. Widening off a surrogate pair keeps offsets on character boundaries; widening off a CRLF
	/// pair keeps offset-to-position conversions on line boundaries. Only the old-content boundaries
	/// need this guarantee, because the replacement text is not position-mapped.
	/// </remarks>
	/// <param name="oldText">The previously synchronized document content; <see langword="null"/> is treated as empty.</param>
	/// <param name="newText">The updated document content; <see langword="null"/> is treated as empty.</param>
	/// <returns>The replacement range and text. The range is expressed in UTF-16 offsets.</returns>
	public static TextIncrementalEdit Compute(string? oldText, string? newText)
	{
		oldText ??= string.Empty;
		newText ??= string.Empty;

		int prefixLength = ComputeCommonPrefixLength(oldText, newText);
		int suffixLength = ComputeCommonSuffixLength(oldText, newText, prefixLength);
		int oldEnd = oldText.Length - suffixLength;
		int newEnd = newText.Length - suffixLength;

		// Move the start before any text unit that straddles it.
		while (prefixLength > 0 && SplitsTextUnit(oldText, prefixLength))
			prefixLength--;

		// Move both ends past any text unit that straddles them. The ends move in lockstep, so the
		// shared tail stays identical in both texts; the new end moves even though it is not
		// position-mapped, because the tail lengths must stay equal.
		while (oldEnd < oldText.Length && SplitsTextUnit(oldText, oldEnd))
		{
			oldEnd++;
			newEnd++;
		}

		string replacement = newEnd > prefixLength ? newText[prefixLength..newEnd] : string.Empty;

		return new(new TextRange(prefixLength, oldEnd - prefixLength), replacement);
	}

	/// <summary>
	/// Determines whether the boundary between <paramref name="boundary"/> - 1 and
	/// <paramref name="boundary"/> falls inside a UTF-16 surrogate pair or between the two
	/// characters of a CRLF sequence.
	/// </summary>
	private static bool SplitsTextUnit(string text, int boundary)
	{
		if (boundary <= 0 || boundary >= text.Length)
			return false;

		char before = text[boundary - 1];
		char after = text[boundary];

		return LineTerminators.IsCrLfPair(before, after)
			|| (char.IsHighSurrogate(before) && char.IsLowSurrogate(after));
	}

	private static int ComputeCommonPrefixLength(string first, string second)
		=> first.AsSpan().CommonPrefixLength(second.AsSpan());

	private static int ComputeCommonSuffixLength(string first, string second, int prefixLength)
	{
		int maximum = Math.Min(first.Length, second.Length) - prefixLength;
		int index = 0;

		while (index < maximum && first[first.Length - 1 - index] == second[second.Length - 1 - index])
			index++;

		return index;
	}
}
