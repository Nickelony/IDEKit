namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Decodes a raw LSP semantic token integer stream from a server response into the typed
/// <see cref="SemanticToken"/> list expected by editor hosts.
/// </summary>
/// <remarks>
/// <para>
/// Character positions and token lengths are interpreted as UTF-16 code units. The client advertises
/// <c>general.positionEncodings = ["utf-16"]</c> exclusively and rejects a server that selects another encoding, so
/// the coordinate contract is pinned for the whole session. The client also forces <c>multilineTokenSupport</c>
/// and <c>overlappingTokenSupport</c> to <see langword="false"/> in its advertised capabilities, so a token that
/// would cross its line end is clamped to that line (the specification's unsupported-multi-line-token rule) and
/// overlapping tokens are not representable. Modifier lists are frozen once per modifier mask: tokens that decode
/// with the same mask share that list instance instead of copying it per token.
/// </para>
/// <para>
/// Tolerance policy, so a consumer does not have to re-derive it per response family: a null argument fails with
/// <see cref="ArgumentNullException"/>; an empty stream, or a missing or empty type legend, degrades to an empty
/// result; a tuple whose line or type index is out of range, or whose legend entry is null or blank, is skipped; a
/// token that would cross its line end is clamped to that line, and one clamped to zero length is skipped; modifier
/// bits with no advertised modifier are dropped. A skipped tuple still advances the delta cursor, so one malformed
/// tuple cannot desynchronize the tuples that follow it, and a skip is silent because this decoder has no logger.
/// </para>
/// </remarks>
public static class SemanticTokensDecoder
{
	private static readonly IReadOnlyList<string> s_emptyModifiers = [];

	/// <summary>
	/// Decodes a raw semantic token integer stream against document content directly.
	/// </summary>
	/// <param name="data">The raw LSP semantic token integer stream.</param>
	/// <param name="content">The document content the token positions refer to.</param>
	/// <param name="tokenTypes">The semantic token types advertised by the server.</param>
	/// <param name="tokenModifiers">The semantic token modifiers advertised by the server.</param>
	/// <returns>The decoded tokens; malformed tuples and tokens outside the content are ignored or clamped, while every tuple still advances the delta cursor.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="data"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<SemanticToken> Decode(IReadOnlyList<int> data, string content,
		IReadOnlyList<string>? tokenTypes, IReadOnlyList<string>? tokenModifiers)
	{
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(content);

		if (data.Count == 0 || tokenTypes is null || tokenTypes.Count == 0)
			return [];

		int[] lineLengths = GetLineLengths(content);
		var semanticTokens = new List<SemanticToken>(data.Count / 5);
		Dictionary<int, IReadOnlyList<string>>? modifierCache = null;

		// The delta cursor accumulates in 64-bit arithmetic: malformed deltas near int.MaxValue must not wrap
		// 32-bit arithmetic onto a wrong, in-range line (the accumulated magnitude cannot exceed the 64-bit range
		// because every delta is an int and the stream length is bounded by the array limits).
		long line = 0;
		long character = 0;

		for (int tupleStart = 0; tupleStart + 4 < data.Count; tupleStart += 5)
		{
			int deltaLine = data[tupleStart];
			int deltaCharacter = data[tupleStart + 1];
			int length = data[tupleStart + 2];
			int tokenTypeIndex = data[tupleStart + 3];
			int modifierMask = data[tupleStart + 4];

			line += deltaLine;
			character = deltaLine == 0 ? character + deltaCharacter : deltaCharacter;

			if (line < 0 || line >= lineLengths.Length || tokenTypeIndex < 0 || tokenTypeIndex >= tokenTypes.Count)
				continue;

			// A malformed legend can contain a null or blank entry; such tuples are skipped instead of throwing.
			string? tokenType = tokenTypes[tokenTypeIndex];

			if (string.IsNullOrWhiteSpace(tokenType))
				continue;

			int lineLength = lineLengths[line];
			int safeCharacter = (int)Math.Max(0, Math.Min(character, lineLength));
			int safeLength = Math.Max(0, Math.Min(length, lineLength - safeCharacter));

			if (safeLength == 0)
				continue;

			semanticTokens.Add(SemanticToken.CreateWithFrozenModifiers(
				(int)line,
				safeCharacter,
				safeLength,
				tokenType,
				GetOrAddModifiers(ref modifierCache, modifierMask, tokenModifiers)));
		}

		return semanticTokens;
	}

	/// <summary>
	/// Measures every line of the document without its terminator, so a token can be clamped to its line end.
	/// </summary>
	/// <remarks>
	/// The decoder needs line lengths only, not a general line map, and this package carries no editor dependency,
	/// so the scan is local. Line terminators follow the family convention: LF, CRLF, and lone CR, with a final empty
	/// line after a trailing terminator and a single empty line for empty content.
	/// </remarks>
	/// <param name="content">The document content.</param>
	/// <returns>The length of each line in UTF-16 code units, in document order.</returns>
	private static int[] GetLineLengths(string content)
	{
		var lineLengths = new List<int>();
		int lineStart = 0;
		int index = 0;

		while (index < content.Length)
		{
			// Scan to the next terminator instead of inspecting every character: the span search is vectorized on
			// the runtime and the per-character loop only won the dense-terminator case.
			int terminatorIndex = content.AsSpan(index).IndexOfAny('\r', '\n');

			if (terminatorIndex < 0)
				break;

			terminatorIndex += index;

			lineLengths.Add(terminatorIndex - lineStart);
			index = terminatorIndex + 1;

			// A CRLF pair is one terminator; the LF of the pair must not open a second, empty line.
			if (content[terminatorIndex] == '\r' && index < content.Length && content[index] == '\n')
				index++;

			lineStart = index;
		}

		lineLengths.Add(content.Length - lineStart);
		return [.. lineLengths];
	}

	private static IReadOnlyList<string> GetOrAddModifiers(
		ref Dictionary<int, IReadOnlyList<string>>? cache,
		int modifierMask,
		IReadOnlyList<string>? tokenModifiers)
	{
		if (modifierMask == 0 || tokenModifiers is null || tokenModifiers.Count == 0)
			return s_emptyModifiers;

		cache ??= [];

		if (cache.TryGetValue(modifierMask, out IReadOnlyList<string>? cached))
			return cached;

		var modifiers = new List<string>();

		// Only the low 32 bits of the modifier mask are addressable; a legend with more than 32 modifiers
		// cannot be represented by the protocol mask, and C# shift masking would alias higher indices onto
		// the low bits.
		uint modifierBits = (uint)modifierMask;
		int modifierCount = Math.Min(tokenModifiers.Count, 32);

		for (int bitIndex = 0; bitIndex < modifierCount; bitIndex++)
		{
			if ((modifierBits & (1u << bitIndex)) != 0 && !string.IsNullOrWhiteSpace(tokenModifiers[bitIndex]))
				modifiers.Add(tokenModifiers[bitIndex]);
		}

		// Freeze the mask's list once: every token that decodes with this mask adopts this instance through
		// SemanticToken.CreateWithFrozenModifiers instead of copying the list per token.
		IReadOnlyList<string> frozenModifiers = Array.AsReadOnly([.. modifiers]);
		cache[modifierMask] = frozenModifiers;
		return frozenModifiers;
	}
}
