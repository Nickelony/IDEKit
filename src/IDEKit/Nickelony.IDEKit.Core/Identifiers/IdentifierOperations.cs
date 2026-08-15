using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>
/// Provides identifier-extraction utilities for editor features such as completion, hover, and
/// definition navigation.
/// </summary>
public static class IdentifierOperations
{
	/// <summary>
	/// Gets the word that ends at the specified offset, using the default identifier rule.
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
	public static string GetWordEndingAt(string text, int offset)
		=> GetWordEndingAt(text, offset, IdentifierCharacterPolicy.Default);

	/// <summary>
	/// Gets the word that ends at the specified offset, using the supplied token-character policy.
	/// </summary>
	/// <param name="text">The text to inspect.</param>
	/// <param name="offset">The zero-based offset the word ends at. Values outside the text are clamped.</param>
	/// <param name="policy">The character rules that define token membership.</param>
	/// <returns>
	/// The contiguous sequence of characters accepted by the policy's part-character rule ending at
	/// the offset, or an empty string when no such characters immediately precede it.
	/// </returns>
	/// <remarks>
	/// The offset is clamped to the text bounds and the returned string is the text of the word that
	/// <see cref="FindTokenSpan"/> reports with <see cref="IdentifierSpanMode.EndingAtOffset"/> for
	/// the same policy, because both primitives share one boundary walk. When no word ends at the
	/// offset, this method returns an empty string while <see cref="FindTokenSpan"/> reports no
	/// token. Use this overload when the caller has plain text and only needs the word text; use
	/// <see cref="FindTokenSpan"/> when the caller needs the span itself.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> or <paramref name="policy"/> is <see langword="null"/>.
	/// </exception>
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

	/// <summary>
	/// Tries to locate an identifier or token span using the specified offset as a probe.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The offset is clamped to the document bounds. With <see cref="IdentifierSpanMode.Containing"/>
	/// (the default), the span is the token run around the clamped offset; for a probe that is not a
	/// token character, the immediately preceding token is preferred when it starts with a
	/// token-start character.
	/// </para>
	/// <para>
	/// The span covers the maximal run of characters accepted by the part-character rule, so a policy
	/// whose start-character rule is narrower than its part-character rule resolves a continuation
	/// character as part of its containing token.
	/// </para>
	/// <para>
	/// With <see cref="IdentifierSpanMode.EndingAtOffset"/>, the returned span ends exactly at the offset
	/// and never includes text after it, matching the word-being-typed semantics used by completion
	/// filtering. The character before the offset must be accepted by the part-character rule, so an
	/// offset that follows a delimiter or whitespace resolves no span, while a token that ends in a
	/// continuation character resolves its full run. When only the word text is needed and the text
	/// is a string, <see cref="GetWordEndingAt(string, int, IdentifierCharacterPolicy)"/> returns it
	/// without constructing a snapshot.
	/// </para>
	/// </remarks>
	/// <param name="snapshot">The text to inspect.</param>
	/// <param name="offset">The zero-based offset to probe.</param>
	/// <param name="policy">The character rules that define token membership.</param>
	/// <param name="mode">How the span is located relative to <paramref name="offset"/>.</param>
	/// <returns>
	/// The resolved token span, or <see langword="null"/> when the probe does not identify a token.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="policy"/> is <see langword="null"/>.
	/// </exception>
	public static TextRange? FindTokenSpan(
		ITextSnapshot snapshot,
		int offset,
		IdentifierCharacterPolicy policy,
		IdentifierSpanMode mode = IdentifierSpanMode.Containing)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(policy);

		if (snapshot.TextLength == 0)
			return null;

		int clampedOffset = Math.Clamp(offset, 0, snapshot.TextLength);

		var source = new IdentifierBoundaryWalker.SnapshotSource(snapshot);
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

			int start = IdentifierBoundaryWalker.FindPrecedingRunStart(source, probe, isPartCharacter);

			return new TextRange(start, clampedOffset - start);
		}

		int containingProbe = clampedOffset;

		if (containingProbe >= snapshot.TextLength)
			containingProbe = snapshot.TextLength - 1;

		if (!isPartCharacter(snapshot.GetCharAt(containingProbe)))
		{
			// A probe outside a token prefers the immediately preceding token, but only when that
			// token starts with a start character.
			int precedingProbe = IdentifierBoundaryWalker.FindPrecedingRunStart(source, containingProbe, isPartCharacter);

			if (precedingProbe == containingProbe || !policy.IsStartCharacter(snapshot.GetCharAt(precedingProbe)))
				return null;

			containingProbe = precedingProbe;
		}

		if (!IdentifierBoundaryWalker.TryExpandRun(source, containingProbe, isPartCharacter, out int wordStart, out int wordEnd))
			return null;

		return new TextRange(wordStart, wordEnd - wordStart);
	}
}
