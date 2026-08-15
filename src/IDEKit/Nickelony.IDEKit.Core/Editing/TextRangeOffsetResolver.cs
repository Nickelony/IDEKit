using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Resolves offset ranges from line-map positions, including fallback anchors for empty or reversed
/// ranges.
/// </summary>
/// <remarks>
/// <para>
/// When the requested range maps to a zero-length offset range or is reversed, the resolver first
/// tries the word at the start position, then the non-whitespace content of that line, and finally
/// a single character (widened to a full surrogate pair when the anchor is a high surrogate followed
/// by a low surrogate). For an empty line it searches forward for the first line with non-whitespace
/// content before searching backward for the last non-whitespace character of a preceding content
/// line. A whitespace-only start line takes neither path: the word and content fallbacks find
/// nothing on it, so the anchor is the single whitespace character at the start position (widened
/// to a surrogate pair the same way), and the search runs only when the line itself is empty.
/// </para>
/// <para>
/// The resolver carries no word policy of its own: callers pass the rule that fits their language
/// (for identifier-style tokens, <see cref="Identifiers.IdentifierCharacterPolicy.Default"/> is the
/// identifier-style rule).
/// </para>
/// <para>
/// The representable path is <see cref="TextLineMap.TryGetOffsets"/>; the resolver falls back
/// exactly when that conversion fails or yields a range of zero length.
/// </para>
/// <para>
/// The fallback cascade deliberately spans the neighboring lines: a word-only or single-character-only
/// shape cannot produce a selectable range for an empty range that lands on an empty line, which is
/// the position a host reports when a definition or search result points at a blank line. Each step
/// keeps the anchor local to the document line that carries it, so the resolved range never stretches
/// across a line break.
/// </para>
/// </remarks>
public static class TextRangeOffsetResolver
{
	/// <summary>
	/// Resolves the offset range to use for a position range within a document.
	/// </summary>
	/// <param name="lineMap">The line map for the document.</param>
	/// <param name="range">The zero-based position range to resolve.</param>
	/// <param name="isWordCharacter">
	/// Determines whether a character is part of a word for the word fallback. The resolver carries
	/// no default word policy, so callers supply the rule that fits their language;
	/// <see cref="Identifiers.IdentifierCharacterPolicy.Default"/> is the identifier-style rule.
	/// </param>
	/// <param name="resolvedRange">Receives the resolved range, or an empty range at offset <c>0</c> when no range could be resolved.</param>
	/// <returns><see langword="true"/> when a usable offset range could be resolved.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="lineMap"/> or <paramref name="isWordCharacter"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryResolveOffsets(TextLineMap lineMap, TextPositionRange range,
		Func<char, bool> isWordCharacter, out TextRange resolvedRange)
	{
		ArgumentNullException.ThrowIfNull(lineMap);
		ArgumentNullException.ThrowIfNull(isWordCharacter);

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

		if (TryGetWordBounds(lineText, safeCharacter, isWordCharacter, out int wordStart, out int wordEnd))
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

		resolvedRange = CreateAnchorRange(
			lineSpan,
			safeCharacter,
			lineStartOffset,
			lineMap.TextLength);

		if (resolvedRange.Length > 0)
			return true;

		resolvedRange = default;
		return false;
	}

	/// <summary>
	/// Resolves the offset range to use for a position range within a document, using an
	/// identifier-style word policy for the word fallback.
	/// </summary>
	/// <remarks>
	/// This overload is the policy-typed convenience over
	/// <see cref="TryResolveOffsets(TextLineMap, TextPositionRange, Func{char, bool}, out TextRange)"/>:
	/// it uses <see cref="IdentifierCharacterPolicy.IsPartCharacter"/> as the word rule, so a caller
	/// cannot accidentally pass <see cref="IdentifierCharacterPolicy.IsStartCharacter"/> and lose
	/// interior word characters.
	/// </remarks>
	/// <param name="lineMap">The line map for the document.</param>
	/// <param name="range">The zero-based position range to resolve.</param>
	/// <param name="wordPolicy">The identifier-style word policy for the word fallback.</param>
	/// <param name="resolvedRange">Receives the resolved range, or an empty range at offset <c>0</c> when no range could be resolved.</param>
	/// <returns><see langword="true"/> when a usable offset range could be resolved.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="lineMap"/> or <paramref name="wordPolicy"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryResolveOffsets(TextLineMap lineMap, TextPositionRange range,
		IdentifierCharacterPolicy wordPolicy, out TextRange resolvedRange)
	{
		ArgumentNullException.ThrowIfNull(wordPolicy);

		return TryResolveOffsets(lineMap, range, wordPolicy.IsPartCharacter, out resolvedRange);
	}

	private static bool TryGetEmptyLineFallbackOffsets(TextLineMap lineMap, int lineIndex,
		out TextRange range)
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
				lineSpan,
				contentOffset,
				lineMap.GetLineStartOffset(nextLineIndex),
				lineMap.TextLength);

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
				lineSpan,
				contentOffset,
				lineMap.GetLineStartOffset(previousLineIndex),
				lineMap.TextLength);

			if (anchor.Length == 0)
				return false;

			range = anchor;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Creates the anchor range of a fallback: the anchored character, widened to two code units when
	/// it is a high surrogate followed by a low surrogate, so the anchor never splits a surrogate
	/// pair. The range is clamped to the text length, so an anchor at the very end stays valid.
	/// </summary>
	/// <param name="lineText">The text of the line that carries the anchor.</param>
	/// <param name="index">The index of the anchored character within <paramref name="lineText"/>.</param>
	/// <param name="lineStartOffset">The offset at which the line begins in the document.</param>
	/// <param name="textLength">The total length of the document text.</param>
	/// <returns>The anchor range.</returns>
	private static TextRange CreateAnchorRange(
		ReadOnlySpan<char> lineText,
		int index,
		int lineStartOffset,
		int textLength)
	{
		int length = 1;

		if (char.IsHighSurrogate(lineText[index])
			&& index + 1 < lineText.Length
			&& char.IsLowSurrogate(lineText[index + 1]))
		{
			length = 2;
		}

		int offset = lineStartOffset + index;
		var anchor = new TextRange(offset, length);

		// An anchor at the very end of the text stays valid, so a range that does not fit is clamped
		// to the text length instead of being rejected.
		return anchor.FitsWithin(textLength)
			? anchor
			: new TextRange(offset, textLength - offset);
	}

	/// <summary>
	/// Returns the index of the first non-whitespace character in <paramref name="lineText"/>, or
	/// <c>-1</c> when the line is empty or whitespace-only.
	/// </summary>
	private static int FindFirstContentOffset(ReadOnlySpan<char> lineText)
	{
		for (int index = 0; index < lineText.Length; index++)
		{
			if (!char.IsWhiteSpace(lineText[index]))
				return index;
		}

		return -1;
	}

	/// <summary>
	/// Returns the index of the last non-whitespace character in <paramref name="lineText"/>, or
	/// <c>-1</c> when the line is empty or whitespace-only.
	/// </summary>
	private static int FindLastContentOffset(ReadOnlySpan<char> lineText)
	{
		for (int index = lineText.Length - 1; index >= 0; index--)
		{
			if (!char.IsWhiteSpace(lineText[index]))
				return index;
		}

		return -1;
	}

	/// <summary>
	/// Finds the word boundaries around <paramref name="index"/> using the supplied character rule.
	/// </summary>
	/// <remarks>
	/// A probe that is not a word character steps back to the preceding word first; otherwise, the
	/// walk advances to the next word character. The returned bounds then cover the maximal run of
	/// word characters around that seed. The run walk is shared with
	/// <see cref="Identifiers.IdentifierOperations.FindTokenSpan"/>, but the probe policy
	/// deliberately differs: the resolver anchors the nearest word for an empty range (preferring
	/// the preceding run even when it does not start with a start character, and searching forward
	/// when there is none), while <c>FindTokenSpan</c> with
	/// <see cref="Identifiers.IdentifierSpanMode.Containing"/> reports no token when the probe lies
	/// outside a token and no valid preceding token exists.
	/// </remarks>
	private static bool TryGetWordBounds(
		ReadOnlyMemory<char> lineText,
		int index,
		Func<char, bool> isWordCharacter,
		out int wordStart,
		out int wordEnd)
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

		return IdentifierBoundaryWalker.TryExpandRun(source, safeIndex, isWordCharacter, out wordStart, out wordEnd);
	}
}
