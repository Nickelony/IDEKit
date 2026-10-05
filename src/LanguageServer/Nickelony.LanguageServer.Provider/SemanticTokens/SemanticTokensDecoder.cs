using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Decodes a raw LSP semantic token integer stream from a server response into the offset-based <see cref="SemanticToken"/> list expected
/// by editor hosts.
/// </summary>
/// <remarks>
/// Malformed tuples and out-of-content tokens are skipped or clamped instead of throwing, and a skipped tuple still advances the delta cursor
/// so the following tuples stay synchronized.
/// </remarks>
internal static class SemanticTokensDecoder
{
	private static readonly IReadOnlyList<string> s_emptyModifiers = [];

	/// <summary>Decodes a raw semantic token integer stream against document content.</summary>
	/// <param name="data">The raw LSP semantic token integer stream.</param>
	/// <param name="content">The document content the token positions refer to.</param>
	/// <param name="tokenTypes">The semantic token types advertised by the server.</param>
	/// <param name="tokenModifiers">The semantic token modifiers advertised by the server.</param>
	/// <returns>
	/// The decoded offset-based tokens; malformed tuples and tokens outside the content are ignored or clamped, while every tuple still advances
	/// the delta cursor.
	/// </returns>
	/// <remarks>
	/// An empty stream, or a missing or empty type legend, degrades to an empty result. A tuple whose line or type index is out of range,
	/// or whose legend entry is <see langword="null"/> or blank, is skipped; a token clamped to zero length is skipped; and modifier bits
	/// with no advertised modifier are dropped. A skip is silent because this decoder has no logger.
	/// </remarks>
	internal static IReadOnlyList<SemanticToken> Decode(
		IReadOnlyList<int> data,
		string content,
		IReadOnlyList<string>? tokenTypes,
		IReadOnlyList<string>? tokenModifiers
	)
	{
		if (data.Count == 0 || tokenTypes is null || tokenTypes.Count == 0)
			return [];

		var lineMap = TextLineMap.Build(content);
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

			if (line < 0 || line >= lineMap.LineCount || tokenTypeIndex < 0 || tokenTypeIndex >= tokenTypes.Count)
				continue;

			// A malformed legend can contain a null or blank entry; such tuples are skipped instead of throwing.
			string? tokenType = tokenTypes[tokenTypeIndex];

			if (string.IsNullOrWhiteSpace(tokenType))
				continue;

			int safeLine = (int)line;
			int lineLength = lineMap.GetLineLength(safeLine);
			int safeCharacter = (int)Math.Max(0, Math.Min(character, lineLength));
			int safeLength = Math.Max(0, Math.Min(length, lineLength - safeCharacter));

			if (safeLength == 0)
				continue;

			int offset = lineMap.GetLineStartOffset(safeLine) + safeCharacter;

			semanticTokens.Add(
				new SemanticToken(new TextRange(offset, safeLength), tokenType, GetOrAddModifiers(ref modifierCache, modifierMask, tokenModifiers))
			);
		}

		return semanticTokens;
	}

	private static IReadOnlyList<string> GetOrAddModifiers(
		ref Dictionary<int, IReadOnlyList<string>>? cache,
		int modifierMask,
		IReadOnlyList<string>? tokenModifiers
	)
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

		// Cache the mask's resolved list once: a mask that repeats across tokens rebuilds nothing, and every
		// token captures its own owned snapshot from the shared template.
		IReadOnlyList<string> resolvedModifiers = Array.AsReadOnly([.. modifiers]);
		cache[modifierMask] = resolvedModifiers;
		return resolvedModifiers;
	}
}
