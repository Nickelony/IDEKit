namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Normalizes the line terminators of text to line feeds.
/// </summary>
/// <remarks>
/// <para>
/// LF, CRLF, and lone CR are line terminators (the same set the other text primitives recognize);
/// normalization replaces every CRLF and lone CR with a single LF, so content produced on another
/// platform is parsed or displayed with one terminator shape.
/// </para>
/// <para>
/// The original instance is returned when the text contains no carriage return, so a no-op is
/// detectable by reference.
/// </para>
/// </remarks>
public static class LineTerminatorNormalizer
{
	/// <summary>
	/// Replaces every CRLF and lone CR line terminator with a single line feed.
	/// </summary>
	/// <param name="text">The text to normalize.</param>
	/// <returns>The normalized text, or <paramref name="text"/> itself when it contains no carriage return.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static string NormalizeToLineFeeds(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		// Most content already uses line feeds; only touch the string when it contains a carriage
		// return, so the common case stays a copy-free no-op.
		if (text.IndexOf('\r') < 0)
			return text;

		return text
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace('\r', '\n');
	}
}
