namespace Nickelony.IDEKit.Core.Text;

/// <summary>Detects the line terminator style that a text uses.</summary>
/// <remarks>
/// LF, CRLF, and lone CR are line terminators, and CRLF counts as one, as in <see cref="LineTerminatorNormalizer"/>.
/// </remarks>
public static class LineTerminatorDetector
{
	/// <summary>Detects the line terminator style used by the text.</summary>
	/// <param name="text">The text to inspect.</param>
	/// <returns>
	/// The style of the text's line terminators: <see cref="LineTerminatorStyle.Mixed"/> when it uses
	/// more than one, and <see cref="LineTerminatorStyle.None"/> when it has none.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static LineTerminatorStyle Detect(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		LineTerminatorStyle style = LineTerminatorStyle.None;
		int start = 0;

		// A second distinct style proves Mixed, so the scan stops at it instead of reading the rest of the text.
		while (true)
		{
			int found = text.AsSpan(start).IndexOfAny('\r', '\n');

			if (found < 0)
				return style;

			int index = start + found;
			int length = LineTerminators.GetTerminatorLength(text, index);

			LineTerminatorStyle current = Classify(text[index], length);

			if (style != LineTerminatorStyle.None && style != current)
				return LineTerminatorStyle.Mixed;

			style = current;
			start = index + length;
		}
	}

	private static LineTerminatorStyle Classify(char first, int length)
	{
		if (length == 2)
			return LineTerminatorStyle.CrLf;

		return first == '\n' ? LineTerminatorStyle.Lf : LineTerminatorStyle.Cr;
	}
}
