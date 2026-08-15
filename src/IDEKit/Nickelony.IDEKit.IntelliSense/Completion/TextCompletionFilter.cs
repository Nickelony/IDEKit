namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Provides completion-item filtering based on the word being typed at the caret.
/// </summary>
public static class TextCompletionFilter
{
	/// <summary>
	/// Reduces a completion item set to the items matching the supplied word.
	/// </summary>
	/// <remarks>
	/// The built-in filter performs a case-insensitive, ordinal substring match against each item's
	/// <see cref="TextCompletionItem.FilterText"/>, which falls back to the label when no filter text
	/// was set, so the label stays searchable in that case. The insertion and edit text are never
	/// matched, so snippet or commit text does not widen the filter. An empty word keeps all items. A
	/// whitespace-only word is matched literally rather than treated as empty, so it selects only
	/// items whose filter text contains that whitespace. Matches keep their input order.
	/// </remarks>
	/// <param name="items">The candidate completion items; the list must not contain <see langword="null"/> elements.</param>
	/// <param name="word">The word being typed.</param>
	/// <returns>
	/// A fresh caller-owned list containing the matching completion items. An empty word returns a
	/// fresh list containing every item, and a word that matches nothing returns a shared empty array
	/// that callers must not mutate.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="items"/> or <paramref name="word"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="items"/> contains a <see langword="null"/> element.
	/// </exception>
	public static IReadOnlyList<TextCompletionItem> FilterByWord(IReadOnlyList<TextCompletionItem> items, string word)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentNullException.ThrowIfNull(word);

		PayloadCollections.ValidateElements(items, "completion items", nameof(items));

		if (word.Length == 0)
		{
			// The empty word keeps every item; the copy preserves the caller-owned result contract.
			var allItems = new TextCompletionItem[items.Count];

			for (int i = 0; i < items.Count; i++)
				allItems[i] = items[i];

			return allItems;
		}

		// Single pass; the result list is caller-owned, so it is returned without copying.
		List<TextCompletionItem>? matches = null;

		for (int i = 0; i < items.Count; i++)
		{
			TextCompletionItem item = items[i];

			if (item.FilterText.Contains(word, StringComparison.OrdinalIgnoreCase))
				(matches ??= new List<TextCompletionItem>()).Add(item);
		}

		return matches is null ? Array.Empty<TextCompletionItem>() : matches;
	}
}
