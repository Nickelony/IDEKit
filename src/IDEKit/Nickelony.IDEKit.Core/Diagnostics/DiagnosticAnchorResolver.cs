using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Diagnostics;

/// <summary>Resolves offset ranges from line-map positions, including fallback anchors for empty or reversed ranges.</summary>
/// <remarks>
/// A range that maps to zero length or is reversed falls back, in order, to the word at the start position, then the non-whitespace
/// content of that line, then a single character (widened to a full surrogate pair). On an empty line, the anchor comes from the nearest
/// line below that has content, or else the nearest line above. The resolver carries no word policy: the caller supplies the character rules
/// that fit its language, and the anchor never stretches across a line break.
/// </remarks>
public static class DiagnosticAnchorResolver
{
	/// <summary>Resolves the offset range to use for a position range within a document.</summary>
	/// <param name="lineMap">The line map for the document.</param>
	/// <param name="range">The zero-based position range to resolve.</param>
	/// <param name="wordCharacters">
	/// The character rules of a word for the word fallback; its part-character rule decides which characters belong to the word,
	/// so <see cref="IdentifierCharacterPolicy.Default"/> is the identifier-style choice.
	/// </param>
	/// <param name="resolvedRange">Receives the resolved range, or an empty range at offset <c>0</c> when no range could be resolved.</param>
	/// <returns><see langword="true"/> when a usable offset range could be resolved.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="lineMap"/> or <paramref name="wordCharacters"/> is <see langword="null"/>.</exception>
	public static bool TryResolveOffsets(
		TextLineMap lineMap,
		TextPositionRange range,
		IdentifierCharacterPolicy wordCharacters,
		out TextRange resolvedRange
	)
	{
		ArgumentNullException.ThrowIfNull(lineMap);
		ArgumentNullException.ThrowIfNull(wordCharacters);

		// The representable path is the line map's own conversion; the fallbacks below run exactly
		// when that conversion fails (reversed range) or yields a zero-length range.
		if (lineMap.TryGetOffsets(range, out resolvedRange) && resolvedRange.Length > 0)
			return true;

		// The start line is read as a memory slice, not a substring: the span drives the empty/content
		// checks and the anchor, and the same slice feeds the word walk, so the whole path is
		// allocation-free.
		ReadOnlyMemory<char> lineText = lineMap.GetLineMemory(range.Start.Line);
		int lineStartOffset = lineMap.GetLineStartOffset(range.Start.Line);

		if (lineText.IsEmpty)
			return TryGetEmptyLineFallbackOffsets(lineMap, range.Start.Line, out resolvedRange);

		ReadOnlySpan<char> lineSpan = lineText.Span;
		int safeCharacter = Math.Max(0, Math.Min(range.Start.Character, Math.Max(0, lineSpan.Length - 1)));

		if (
			TryGetWordBounds(
				lineText,
				index: safeCharacter,
				isWordCharacter: wordCharacters.PartCharacterPredicate,
				out int wordStart,
				out int wordEnd
			)
		)
		{
			resolvedRange = new(lineStartOffset + wordStart, wordEnd - wordStart);

			if (resolvedRange.Length > 0)
				return true;

			// Every false return reports the documented empty default range, so a caller never observes a
			// range a failed attempt left behind.
			resolvedRange = default;
			return false;
		}

		int trimmedStart = 0;
		int trimmedEnd = lineSpan.Length;

		while (trimmedStart < trimmedEnd && char.IsWhiteSpace(lineSpan[trimmedStart]))
			trimmedStart++;

		while (trimmedEnd > trimmedStart && char.IsWhiteSpace(lineSpan[trimmedEnd - 1]))
			trimmedEnd--;

		if (trimmedEnd > trimmedStart)
		{
			resolvedRange = new(lineStartOffset + trimmedStart, trimmedEnd - trimmedStart);
			return true;
		}

		resolvedRange = CreateAnchorRange(lineText: lineSpan, index: safeCharacter, lineStartOffset, textLength: lineMap.TextLength);

		if (resolvedRange.Length > 0)
			return true;

		resolvedRange = default;
		return false;
	}

	private static bool TryGetEmptyLineFallbackOffsets(TextLineMap lineMap, int lineIndex, out TextRange range)
	{
		range = default;

		// Whitespace-only lines are skipped so the anchor lands on content rather than indentation.
		// The lines are scanned as spans: resolving a range in a whitespace-only region must not
		// allocate one substring per scanned line.
		for (int nextLineIndex = lineIndex + 1; nextLineIndex < lineMap.LineCount; nextLineIndex++)
		{
			ReadOnlySpan<char> lineSpan = lineMap.GetLineSpan(nextLineIndex);
			int contentOffset = FindFirstContentOffset(lineSpan);

			if (contentOffset < 0)
				continue;

			TextRange anchor = CreateAnchorRange(
				lineText: lineSpan,
				index: contentOffset,
				lineStartOffset: lineMap.GetLineStartOffset(nextLineIndex),
				textLength: lineMap.TextLength
			);

			if (anchor.Length == 0)
				return false;

			range = anchor;
			return true;
		}

		for (int previousLineIndex = lineIndex - 1; previousLineIndex >= 0; previousLineIndex--)
		{
			ReadOnlySpan<char> lineSpan = lineMap.GetLineSpan(previousLineIndex);
			int contentOffset = FindLastContentOffset(lineSpan);

			if (contentOffset < 0)
				continue;

			TextRange anchor = CreateAnchorRange(
				lineText: lineSpan,
				index: contentOffset,
				lineStartOffset: lineMap.GetLineStartOffset(previousLineIndex),
				textLength: lineMap.TextLength
			);

			if (anchor.Length == 0)
				return false;

			range = anchor;
			return true;
		}

		return false;
	}

	/// <summary>Creates the anchor range of a fallback, widened to two code units for a high-surrogate/low-surrogate pair.</summary>
	/// <remarks>
	/// The range is clamped to the text length, so an anchor at the very end stays valid.
	/// </remarks>
	private static TextRange CreateAnchorRange(ReadOnlySpan<char> lineText, int index, int lineStartOffset, int textLength)
	{
		int length = 1;

		if (char.IsHighSurrogate(lineText[index]) && index + 1 < lineText.Length && char.IsLowSurrogate(lineText[index + 1]))
			length = 2;

		int offset = lineStartOffset + index;
		var anchor = new TextRange(offset, length);

		// An anchor at the very end of the text stays valid, so a range that does not fit is clamped
		// to the text length instead of being rejected.
		return anchor.FitsWithin(textLength) ? anchor : TextRange.FromBounds(offset, textLength);
	}

	private static int FindFirstContentOffset(ReadOnlySpan<char> lineText)
	{
		for (int index = 0; index < lineText.Length; index++)
		{
			if (!char.IsWhiteSpace(lineText[index]))
				return index;
		}

		return -1;
	}

	private static int FindLastContentOffset(ReadOnlySpan<char> lineText)
	{
		for (int index = lineText.Length - 1; index >= 0; index--)
		{
			if (!char.IsWhiteSpace(lineText[index]))
				return index;
		}

		return -1;
	}

	/// <summary>Finds the word boundaries around <paramref name="index"/> using the supplied character rule.</summary>
	/// <remarks>
	/// A probe that is not a word character steps back to the preceding word when one is adjacent; otherwise, it advances to the next word character.
	/// The run is then expanded around that seed. Unlike an identifier lookup that requires the probe to sit inside a token,
	/// a probe outside every token still anchors the preceding word (or the next one).
	/// </remarks>
	private static bool TryGetWordBounds(
		ReadOnlyMemory<char> lineText,
		int index,
		Func<char, bool> isWordCharacter,
		out int wordStart,
		out int wordEnd
	)
	{
		wordStart = 0;
		wordEnd = 0;

		if (lineText.IsEmpty)
			return false;

		var source = new IdentifierBoundaryWalker.MemorySource(lineText);

		ReadOnlySpan<char> lineSpan = lineText.Span;
		int safeIndex = Math.Max(0, Math.Min(index, lineSpan.Length - 1));

		if (!isWordCharacter(lineSpan[safeIndex]))
		{
			if (safeIndex > 0 && isWordCharacter(lineSpan[safeIndex - 1]))
			{
				// Prefer the word that immediately precedes the probe.
				safeIndex--;
			}
			else
			{
				safeIndex = IdentifierBoundaryWalker.FindNextPartCharacter(source, safeIndex, isWordCharacter);

				if (safeIndex < 0)
					return false;
			}
		}

		return IdentifierBoundaryWalker.TryExpandRun(
			source,
			seedIndex: safeIndex,
			isPartCharacter: isWordCharacter,
			start: out wordStart,
			end: out wordEnd
		);
	}
}
