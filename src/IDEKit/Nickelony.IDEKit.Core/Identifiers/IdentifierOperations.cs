using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>Provides identifier-extraction utilities for editor features such as completion, hover, and definition navigation.</summary>
public static class IdentifierOperations
{
	/// <summary>
	/// Returns the word that ends at the specified offset, using the default identifier rule.
	/// </summary>
	/// <param name="text">The text to inspect.</param>
	/// <param name="offset">The zero-based offset the word ends at. Values outside the text are clamped.</param>
	/// <returns>
	/// The contiguous sequence of letters, digits, or underscores ending at the offset, or an empty
	/// string when no such characters immediately precede it.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <example>
	/// <code>
	/// string text = "if CONST_";
	/// string word = IdentifierOperations.GetWordEndingAt(text, text.Length); // "CONST_"
	/// </code>
	/// </example>
	public static string GetWordEndingAt(string text, int offset) => GetWordEndingAt(text, offset, IdentifierCharacterPolicy.Default);

	/// <summary>
	/// Returns the word that ends at the specified offset, using the supplied token-character policy.
	/// </summary>
	/// <param name="text">The text to inspect.</param>
	/// <param name="offset">The zero-based offset the word ends at. Values outside the text are clamped.</param>
	/// <param name="policy">The character rules that define token membership.</param>
	/// <returns>
	/// The contiguous sequence of characters accepted by the policy's part-character rule ending at
	/// the offset, or an empty string when no such characters immediately precede it.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> or <paramref name="policy"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The offset is clamped to the text bounds, and the result is the word text of the span <see cref="FindTokenSpan"/> reports with
	/// <see cref="IdentifierSpanMode.EndingAtOffset"/> for the same policy, because both share one boundary walk. When no word ends at the offset,
	/// this method returns an empty string while <see cref="FindTokenSpan"/> reports no token.
	/// </remarks>
	/// <example>
	/// JavaScript allows <c>$</c> within identifiers:
	/// <code>
	/// string text = "let $el = 1";
	/// var policy = IdentifierCharacterPolicy.Create(static c => char.IsLetterOrDigit(c) || c is '_' or '$');
	/// string word = IdentifierOperations.GetWordEndingAt(text, 7, policy); // "$el"
	/// </code>
	/// </example>
	public static string GetWordEndingAt(string text, int offset, IdentifierCharacterPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(policy);

		if (string.IsNullOrEmpty(text))
			return string.Empty;

		int clampedOffset = Math.Clamp(offset, 0, text.Length);

		if (clampedOffset == 0)
			return string.Empty;

		// Share the boundary walk with FindTokenSpan(EndingAtOffset) so both primitives always
		// resolve the same word rule and clamp out-of-range offsets the same way.
		var source = new IdentifierBoundaryWalker.StringSource(text);
		int start = IdentifierBoundaryWalker.FindPrecedingRunStart(source, clampedOffset, policy.PartCharacterPredicate);

		return text[start..clampedOffset];
	}

	/// <summary>Tries to locate an identifier or token span using the specified offset as a probe.</summary>
	/// <param name="snapshot">The text to inspect.</param>
	/// <param name="offset">The zero-based offset to probe.</param>
	/// <param name="policy">The character rules that define token membership.</param>
	/// <param name="mode">How the span is located relative to <paramref name="offset"/>.</param>
	/// <returns>The resolved token span, or <see langword="null"/> when the probe does not identify a token.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="policy"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The offset is clamped to the document bounds. <see cref="IdentifierSpanMode.Containing"/> takes the maximal
	/// run of part-character-rule characters around the clamped offset - so a narrower start rule still resolves a continuation
	/// character into its token. When the probe is not a token character, it prefers the immediately preceding token, provided that token
	/// begins with a start character; otherwise, the result is <see langword="null"/>. <see cref="IdentifierSpanMode.AtOffset"/> takes the same run
	/// but reports no token when the probe is not a part character. <see cref="IdentifierSpanMode.EndingAtOffset"/> ends the span exactly
	/// at the offset and reports no token unless the character before the offset is a part character.
	/// </remarks>
	public static TextRange? FindTokenSpan(
		ITextSnapshot snapshot,
		int offset,
		IdentifierCharacterPolicy policy,
		IdentifierSpanMode mode = IdentifierSpanMode.Containing
	)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(policy);

		if (snapshot.TextLength == 0)
			return null;

		int clampedOffset = Math.Clamp(offset, 0, snapshot.TextLength);

		Func<char, bool> isPartCharacter = policy.PartCharacterPredicate;

		if (mode == IdentifierSpanMode.EndingAtOffset)
		{
			if (clampedOffset == 0)
				return null;

			int probe = clampedOffset - 1;

			// The word being typed must be adjacent to the caret. A token that ends in a
			// continuation character still resolves to its full run.
			if (!isPartCharacter(snapshot.GetCharAt(probe)))
				return null;

			int start = IdentifierBoundaryWalker.FindPrecedingRunStart(snapshot, probe, isPartCharacter);

			return TextRange.FromBounds(start, clampedOffset);
		}

		int containingProbe = clampedOffset;

		if (containingProbe >= snapshot.TextLength)
			containingProbe = snapshot.TextLength - 1;

		if (!isPartCharacter(snapshot.GetCharAt(containingProbe)))
		{
			if (mode == IdentifierSpanMode.AtOffset)
				return null;

			// A probe outside a token prefers the immediately preceding token, but only when that
			// token starts with a start character.
			int precedingProbe = IdentifierBoundaryWalker.FindPrecedingRunStart(snapshot, containingProbe, isPartCharacter);

			if (precedingProbe == containingProbe || !policy.IsStartCharacter(snapshot.GetCharAt(precedingProbe)))
				return null;

			containingProbe = precedingProbe;
		}

		if (
			!IdentifierBoundaryWalker.TryExpandRun(
				source: snapshot,
				seedIndex: containingProbe,
				isPartCharacter,
				start: out int wordStart,
				end: out int wordEnd
			)
		)
		{
			return null;
		}

		return TextRange.FromBounds(wordStart, wordEnd);
	}
}
