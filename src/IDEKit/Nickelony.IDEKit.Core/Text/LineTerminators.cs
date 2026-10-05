namespace Nickelony.IDEKit.Core.Text;

/// <summary>Owns the line-terminator rules the text primitives share: LF, CRLF, and lone CR are line terminators, and CRLF counts as one.</summary>
/// <remarks>
/// A scan that needs the terminator's index may use <c>IndexOfAny('\r', '\n')</c> directly for vectorization.
/// </remarks>
internal static class LineTerminators
{
	/// <summary>
	/// The style of <see cref="Environment.NewLine"/>, which the editor engines insert for the first line break of a text that has none.
	/// </summary>
	internal static LineTerminatorStyle PlatformStyle { get; } = Environment.NewLine == "\r\n" ? LineTerminatorStyle.CrLf : LineTerminatorStyle.Lf;

	/// <summary>Returns the terminator text of a style.</summary>
	/// <param name="style">The style to look up.</param>
	/// <returns>
	/// LF, CRLF or CR; <see langword="null"/> for <see cref="LineTerminatorStyle.Mixed"/> and <see cref="LineTerminatorStyle.None"/>,
	/// which name no single terminator.
	/// </returns>
	internal static string? GetText(LineTerminatorStyle style)
	{
		return style switch
		{
			LineTerminatorStyle.Lf => "\n",
			LineTerminatorStyle.CrLf => "\r\n",
			LineTerminatorStyle.Cr => "\r",
			_ => null,
		};
	}

	/// <summary>Determines whether the character is a CR or LF line terminator.</summary>
	/// <param name="character">The character to inspect.</param>
	/// <returns><see langword="true"/> when the character is <c>\r</c> or <c>\n</c>.</returns>
	internal static bool IsTerminator(char character) => character is '\r' or '\n';

	/// <summary>Determines whether the characters form a CRLF pair, which counts as one line terminator.</summary>
	/// <param name="before">The character before the boundary.</param>
	/// <param name="after">The character after the boundary.</param>
	/// <returns><see langword="true"/> when the pair is <c>\r\n</c>.</returns>
	internal static bool IsCrLfPair(char before, char after) => before == '\r' && after == '\n';

	/// <summary>
	/// Returns the length of the line terminator that starts at the supplied index: 2 for CRLF, 1 for a standalone CR or LF, and 0 when no terminator
	/// starts there.
	/// </summary>
	/// <param name="text">The text to inspect.</param>
	/// <param name="index">The zero-based index to inspect.</param>
	/// <returns>The terminator length in UTF-16 code units.</returns>
	internal static int GetTerminatorLength(ReadOnlySpan<char> text, int index)
	{
		if ((uint)index >= (uint)text.Length || !IsTerminator(text[index]))
			return 0;

		return index + 1 < text.Length && IsCrLfPair(text[index], text[index + 1]) ? 2 : 1;
	}

	/// <summary>
	/// Returns the length of the line terminator that starts at the supplied offset in a snapshot: 2 for CRLF, 1 for a standalone CR or LF,
	/// and 0 when no terminator starts there.
	/// </summary>
	/// <param name="snapshot">The snapshot to inspect.</param>
	/// <param name="offset">The zero-based offset to inspect.</param>
	/// <returns>The terminator length in UTF-16 code units.</returns>
	internal static int GetTerminatorLength(ITextSnapshot snapshot, int offset)
	{
		if ((uint)offset >= (uint)snapshot.TextLength || !IsTerminator(snapshot.GetCharAt(offset)))
			return 0;

		return offset + 1 < snapshot.TextLength && IsCrLfPair(snapshot.GetCharAt(offset), snapshot.GetCharAt(offset + 1)) ? 2 : 1;
	}

	/// <summary>Determines whether the text contains a line terminator.</summary>
	/// <param name="text">The text to inspect.</param>
	/// <returns><see langword="true"/> when the text contains CR or LF.</returns>
	internal static bool ContainsTerminator(ReadOnlySpan<char> text) => text.IndexOfAny('\r', '\n') >= 0;
}
