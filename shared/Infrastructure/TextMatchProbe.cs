namespace Nickelony.IDEKit.Infrastructure;

/// <summary>
/// A read-only character source: the minimal surface the shared text matcher needs. Each text
/// representation (a Core snapshot or an editor document) is adapted to this contract, so the
/// matcher itself has no dependency on either representation.
/// </summary>
internal interface ITextCharSource
{
	/// <summary>
	/// Gets the total number of UTF-16 code units in the text.
	/// </summary>
	int TextLength { get; }

	/// <summary>
	/// Gets the character at the specified zero-based offset.
	/// </summary>
	/// <param name="offset">The zero-based offset of the character to retrieve.</param>
	/// <returns>The character at the offset.</returns>
	char GetCharAt(int offset);
}

/// <summary>
/// Character-level text matching shared by the editor-neutral auto-closing resolver and the
/// editor-binding auto-closing service, so "does this text occur at this offset" has one definition.
/// </summary>
internal static class TextMatchProbe
{
	/// <summary>
	/// Determines whether <paramref name="expectedText"/> occurs at <paramref name="offset"/> in the
	/// supplied source. An empty text or a range outside the source matches nothing.
	/// </summary>
	/// <remarks>
	/// The source is passed by its generic type so a struct adapter is matched without boxing; the
	/// comparison reads characters one at a time and never materializes a substring.
	/// </remarks>
	/// <typeparam name="TCharSource">The character source type.</typeparam>
	/// <param name="source">The source to match against.</param>
	/// <param name="offset">The zero-based offset to compare at.</param>
	/// <param name="expectedText">The text to match.</param>
	/// <returns><see langword="true"/> when the text matches at the offset; otherwise, <see langword="false"/>.</returns>
	internal static bool MatchesAt<TCharSource>(TCharSource source, int offset, string expectedText)
		where TCharSource : ITextCharSource
	{
		if (expectedText.Length == 0
			|| (uint)offset > (uint)source.TextLength
			|| (uint)expectedText.Length > (uint)(source.TextLength - offset))
		{
			return false;
		}

		for (int index = 0; index < expectedText.Length; index++)
		{
			if (source.GetCharAt(offset + index) != expectedText[index])
				return false;
		}

		return true;
	}
}
