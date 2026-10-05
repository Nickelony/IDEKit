namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

/// <summary>Answers the questions about snippet syntax that the expansion path and the brace scans must answer the same way.</summary>
/// <remarks>
/// <see cref="SnippetExpander"/> expands a snippet and also scans ahead for the end of a construct. Both read the same escape, brace,
/// digit-run and choice-terminator rules through this type, so a scan cannot disagree with the expansion about where a construct ends.
/// Every probe reads inside the half-open range that ends at <c>endIndex</c> and never throws for an index inside that range.
/// </remarks>
internal static class SnippetSyntaxProbe
{
	/// <summary>
	/// Determines whether a backslash at <paramref name="index"/> has a character after it inside the range, so it escapes that character.
	/// </summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="index">The index to inspect; it is inside the range.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <returns>
	/// <see langword="true"/> when <c>snippet[index]</c> is a backslash and <c>index + 1</c> is inside the range; otherwise, <see langword="false"/>.
	/// A trailing backslash is not an escape.
	/// </returns>
	internal static bool IsEscapeAt(string snippet, int index, int endIndex) => snippet[index] == '\\' && index + 1 < endIndex;

	/// <summary>Determines whether the escape at <paramref name="index"/> escapes a close brace.</summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="index">The index of the backslash of an escape; <see cref="IsEscapeAt"/> holds there.</param>
	/// <returns><see langword="true"/> when the character after the backslash is <c>}</c>; otherwise, <see langword="false"/>.</returns>
	internal static bool EscapesCloseBrace(string snippet, int index) => snippet[index + 1] == '}';

	/// <summary>Determines whether a <c>${</c> construct opens at <paramref name="index"/>.</summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="index">The index to inspect; it is inside the range.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <returns>
	/// <see langword="true"/> when <c>snippet[index]</c> is <c>$</c> and the next character
	/// inside the range is <c>{</c>; otherwise, <see langword="false"/>.
	/// </returns>
	internal static bool IsBraceOpenAt(string snippet, int index, int endIndex) =>
		snippet[index] == '$' && index + 1 < endIndex && snippet[index + 1] == '{';

	/// <summary>
	/// Determines whether the text from <paramref name="contentStartIndex"/> is the head of a choice: a digit run followed by <c>|</c>.
	/// </summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="contentStartIndex">The index right after <c>${</c>.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <param name="digitEndIndex">Receives the exclusive end of the digit run.</param>
	/// <returns>
	/// <see langword="true"/> when the digit run is not empty
	/// and the character after it, inside the range, is <c>|</c>; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// The run is not parsed here, so a head whose index does not fit <see cref="int"/> still answers <see langword="true"/>.
	/// </remarks>
	internal static bool IsChoiceHeadAt(string snippet, int contentStartIndex, int endIndex, out int digitEndIndex)
	{
		digitEndIndex = ScanDigits(snippet, contentStartIndex, endIndex);
		return digitEndIndex > contentStartIndex && digitEndIndex < endIndex && snippet[digitEndIndex] == '|';
	}

	/// <summary>Determines whether the <c>|}</c> that ends a choice list starts at <paramref name="index"/>.</summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="index">The index to inspect; it is inside the range.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <returns>
	/// <see langword="true"/> when <c>snippet[index]</c> is <c>|</c> and the next character
	/// inside the range is <c>}</c>; otherwise, <see langword="false"/>.
	/// </returns>
	internal static bool IsChoiceTerminatorAt(string snippet, int index, int endIndex) =>
		snippet[index] == '|' && index + 1 < endIndex && snippet[index + 1] == '}';

	/// <summary>Reads a tabstop index: a non-empty run of ASCII digits that fits in <see cref="int"/>.</summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="startIndex">The index of the first character to inspect.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <param name="digitEndIndex">Receives the exclusive end of the digit run.</param>
	/// <param name="value">Receives the parsed index when the method returns <see langword="true"/>.</param>
	/// <returns>
	/// <see langword="true"/> when the run is not empty and fits in <see cref="int"/>; otherwise, <see langword="false"/> and the run stays literal.
	/// </returns>
	internal static bool TryReadIndex(string snippet, int startIndex, int endIndex, out int digitEndIndex, out int value)
	{
		digitEndIndex = ScanDigits(snippet, startIndex, endIndex);

		if (digitEndIndex == startIndex)
		{
			value = 0;
			return false;
		}

		return TryParseIndex(snippet, startIndex, endIndex: digitEndIndex, out value);
	}

	/// <summary>Returns the exclusive end of the digit run that starts at the supplied index.</summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="startIndex">The index of the first character to inspect.</param>
	/// <param name="endIndex">The exclusive end of the enclosing range.</param>
	/// <returns>
	/// The index of the first character that is not an ASCII decimal digit; non-ASCII digits are not part of the snippet grammar and stay plain text.
	/// </returns>
	internal static int ScanDigits(string snippet, int startIndex, int endIndex)
	{
		int index = startIndex;

		while (index < endIndex && snippet[index] is >= '0' and <= '9')
			index++;

		return index;
	}

	/// <summary>Parses a decimal tabstop index from a digit run.</summary>
	/// <param name="snippet">The full snippet text.</param>
	/// <param name="startIndex">The index of the first digit.</param>
	/// <param name="endIndex">The exclusive end of the digit run.</param>
	/// <param name="value">Receives the parsed index when the run fits.</param>
	/// <returns>
	/// <see langword="true"/> when the digit run fits in <see cref="int"/>; otherwise, <see langword="false"/> and the digit run stays literal.
	/// </returns>
	internal static bool TryParseIndex(string snippet, int startIndex, int endIndex, out int value)
	{
		int result = 0;

		for (int index = startIndex; index < endIndex; index++)
		{
			int digit = snippet[index] - '0';

			if (result > (int.MaxValue - digit) / 10)
			{
				value = 0;
				return false;
			}

			result = (result * 10) + digit;
		}

		value = result;
		return true;
	}
}
