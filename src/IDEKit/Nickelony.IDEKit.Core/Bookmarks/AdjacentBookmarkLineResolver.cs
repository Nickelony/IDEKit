namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>Finds the bookmarked line next to a line, wrapping around at the ends of the document.</summary>
public static class AdjacentBookmarkLineResolver
{
	/// <summary>Finds the next or previous bookmarked line relative to a line.</summary>
	/// <param name="bookmarkedLineNumbers">The one-based bookmarked line numbers in ascending order.</param>
	/// <param name="currentLineNumber">The one-based line the search starts from; it is returned only when it is the sole bookmark.</param>
	/// <param name="searchForward">Whether to search toward the end of the document.</param>
	/// <returns>
	/// The adjacent bookmarked line number, the first one when a forward search runs past the last bookmark, the last one when a backward search runs
	/// past the first, or <see langword="null"/> when there is no bookmark.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="bookmarkedLineNumbers"/> is <see langword="null"/>.</exception>
	public static int? Resolve(IReadOnlyList<int> bookmarkedLineNumbers, int currentLineNumber, bool searchForward)
	{
		ArgumentNullException.ThrowIfNull(bookmarkedLineNumbers);

		if (bookmarkedLineNumbers.Count == 0)
			return null;

		if (searchForward)
		{
			foreach (int lineNumber in bookmarkedLineNumbers)
			{
				if (lineNumber > currentLineNumber)
					return lineNumber;
			}

			return bookmarkedLineNumbers[0];
		}

		for (int index = bookmarkedLineNumbers.Count - 1; index >= 0; index--)
		{
			if (bookmarkedLineNumbers[index] < currentLineNumber)
				return bookmarkedLineNumbers[index];
		}

		return bookmarkedLineNumbers[^1];
	}
}
