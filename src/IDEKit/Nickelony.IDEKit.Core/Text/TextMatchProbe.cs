namespace Nickelony.IDEKit.Core.Text;

/// <summary>Matches expected text against a character source one character at a time.</summary>
public static class TextMatchProbe
{
	/// <summary>Determines whether <paramref name="expectedText"/> occurs at <paramref name="offset"/> in the supplied source.</summary>
	/// <typeparam name="TCharSource">The character source type.</typeparam>
	/// <param name="source">The source to match against.</param>
	/// <param name="offset">The zero-based offset to compare at.</param>
	/// <param name="expectedText">The text to match.</param>
	/// <returns><see langword="true"/> when the text matches at the offset; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="expectedText"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// An empty text or a range outside the source matches nothing. The source is passed by its generic type so a struct adapter
	/// is matched without boxing; the comparison reads characters one at a time and never materializes a substring.
	/// </remarks>
	public static bool MatchesAt<TCharSource>(TCharSource source, int offset, string expectedText)
		where TCharSource : ITextCharSource
	{
		// A comparison with null is a no-op for a struct source, so the check allocates nothing on the keystroke path.
		if (source is null)
			throw new ArgumentNullException(nameof(source));

		ArgumentNullException.ThrowIfNull(expectedText);

		if (expectedText.Length == 0 || (uint)offset > (uint)source.TextLength || (uint)expectedText.Length > (uint)(source.TextLength - offset))
			return false;

		for (int index = 0; index < expectedText.Length; index++)
		{
			if (source.GetCharAt(offset + index) != expectedText[index])
				return false;
		}

		return true;
	}
}
