namespace Nickelony.IDEKit.Core.Text;

/// <summary>Provides language-independent helpers over the text of one line and over text that holds several lines.</summary>
/// <remarks>
/// The leading-whitespace helpers recognize only spaces and tabs, the same indentation alphabet the comment and indentation primitives use.
/// </remarks>
public static class LineTextOperations
{
	/// <summary>Returns the leading whitespace prefix (spaces and tabs) of a line.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="lineText"/> is <see langword="null"/>.</exception>
	public static string GetLeadingWhitespace(string lineText)
	{
		ArgumentNullException.ThrowIfNull(lineText);
		return lineText[..GetLeadingWhitespaceLength(lineText)];
	}

	/// <summary>Returns the number of leading indentation characters (spaces and tabs) in a line.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="lineText"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// Only spaces and tabs count as indentation; any other whitespace character (for example a non-breaking space or form feed) is content and ends
	/// the scan.
	/// </remarks>
	public static int GetLeadingWhitespaceLength(string lineText)
	{
		ArgumentNullException.ThrowIfNull(lineText);

		int length = 0;

		while (length < lineText.Length && WhitespaceScan.IsSpaceOrTab(lineText[length]))
			length++;

		return length;
	}
}
