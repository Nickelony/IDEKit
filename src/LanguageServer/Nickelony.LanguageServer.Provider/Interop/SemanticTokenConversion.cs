using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Converts protocol semantic tokens into the shared offset-based semantic-token payloads.
/// </summary>
/// <remarks>
/// A language server's semantic tokens are decoded into <see cref="SemanticToken"/> values in
/// protocol line and character coordinates by <see cref="SemanticTokensDecoder"/> in this package, while
/// editor hosts consume the offset-based <see cref="TextSemanticToken"/> payload from the shared IntelliSense
/// package. This conversion is the bridge between the two models; it runs against the line map of the document
/// the tokens were produced for.
/// </remarks>
public static class SemanticTokenConversion
{
	/// <summary>
	/// Converts a protocol semantic-token sequence into offset-based semantic tokens against a document line map.
	/// </summary>
	/// <remarks>
	/// A token whose line index lies outside the line map no longer identifies document content (for example
	/// after a stale update) and is skipped; a character index beyond a line's length is clamped to the line
	/// end and a token length beyond the remaining line text is clamped as well, so an unusable token that
	/// leaves an empty range is skipped. A token without a type name is skipped because the shared payload
	/// rejects blank types. The returned list preserves the order of the convertible tokens.
	/// <para>
	/// Multi-line tokens are unsupported: a token that spans beyond its line is clamped to the line end and its
	/// remaining lines are not highlighted. The transport advertises <c>multilineTokenSupport = false</c>, so a
	/// conforming server never emits one, and the decoded model cannot represent a span across line breaks.
	/// </para>
	/// </remarks>
	/// <param name="tokens">The protocol tokens to convert.</param>
	/// <param name="lineMap">The line map of the document the tokens were produced for.</param>
	/// <returns>A fresh caller-owned list with one <see cref="TextSemanticToken"/> per convertible token.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="tokens"/> or <paramref name="lineMap"/> is <see langword="null"/>.
	/// </exception>
	public static IReadOnlyList<TextSemanticToken> ToTextSemanticTokens(IReadOnlyList<SemanticToken> tokens, TextLineMap lineMap)
	{
		ArgumentNullException.ThrowIfNull(tokens);
		ArgumentNullException.ThrowIfNull(lineMap);

		var converted = new List<TextSemanticToken>(tokens.Count);

		for (int i = 0; i < tokens.Count; i++)
		{
			SemanticToken token = tokens[i];

			if (token.Line >= lineMap.LineCount || string.IsNullOrWhiteSpace(token.Type))
				continue;

			int lineLength = lineMap.GetLineLength(token.Line);
			int character = Math.Clamp(token.Character, 0, lineLength);
			int length = Math.Min(token.Length, lineLength - character);

			if (length <= 0)
				continue;

			int offset = lineMap.GetLineStartOffset(token.Line) + character;
			converted.Add(new TextSemanticToken(new TextRange(offset, length), token.Type, token.Modifiers));
		}

		return converted;
	}
}
