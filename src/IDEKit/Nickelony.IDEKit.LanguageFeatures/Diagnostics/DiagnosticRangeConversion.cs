using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Diagnostics;

/// <summary>Converts the line, character and length that a host analysis reports into the offset range of a <see cref="Diagnostic"/>.</summary>
/// <remarks>
/// A compiler or a linter names a problem by its line and the span within it. A result computed for older text
/// can name a line or a character that an edit removed, and a host can number a problem it has no line for, so the conversion clamps a coordinate
/// instead of rejecting it: one bad coordinate must not fail the diagnostics of a whole project.
/// </remarks>
public static class DiagnosticRangeConversion
{
	/// <summary>Converts a zero-based line, a zero-based character and an optional length into the offset range they cover in a text.</summary>
	/// <param name="lineMap">
	/// The line map of the text the position addresses, which a caller builds once per text with <see cref="TextLineMap.Build(string?)"/>.
	/// </param>
	/// <param name="lineIndex">The zero-based line; a negative value counts as the first line.</param>
	/// <param name="character">
	/// The zero-based character within the line, in UTF-16 code units; a negative value counts as the start of the line.
	/// </param>
	/// <param name="length">
	/// The length in UTF-16 code units, or <see langword="null"/> to cover the rest of the line; a negative value counts as zero.
	/// </param>
	/// <returns>
	/// The range on the line, which excludes the line terminator. A line past the end of the text yields an empty range at the end of the text,
	/// a character past the end of its line yields an empty range at the end of the line, and a length that runs past the line stops at the line end.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="lineMap"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// Character 0 with a <see langword="null"/> length covers the whole line.
	/// </para>
	/// <para>
	/// A line past the end differs from <see cref="TextLineMap.GetOffset(int, int)"/>, which clamps to the last line: an empty range at the end
	/// of the text keeps a compiler's end-of-file diagnostic visible there, where a clamp to the last line would underline text the diagnostic
	/// does not name.
	/// </para>
	/// </remarks>
	public static TextRange FromLineAndCharacter(TextLineMap lineMap, int lineIndex, int character, int? length = null)
	{
		ArgumentNullException.ThrowIfNull(lineMap);

		character = Math.Max(character, 0);

		if (lineIndex >= lineMap.LineCount)
			return new TextRange(lineMap.TextLength, 0);

		// The line map counts a negative line as the first one, as its remarks say.
		int lineLength = lineMap.GetLineLength(lineIndex);
		int lineStart = lineMap.GetLineStartOffset(lineIndex);
		int startCharacter = Math.Min(character, lineLength);
		int endCharacter = length is { } count ? startCharacter + Math.Min(Math.Max(count, 0), lineLength - startCharacter) : lineLength;

		return TextRange.FromBounds(lineStart + startCharacter, lineStart + endCharacter);
	}
}
