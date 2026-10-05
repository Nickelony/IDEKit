using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Provides completion-item filtering based on the word being typed at the caret.</summary>
public static class CompletionFilter
{
	/// <summary>Reduces a completion item set to the items matching the supplied word.</summary>
	/// <param name="items">The candidate completion items; the list must not contain <see langword="null"/> elements.</param>
	/// <param name="word">The word being typed.</param>
	/// <returns>
	/// A fresh caller-owned list containing the matching completion items. An empty word returns a fresh list containing every item, and a word
	/// that matches nothing returns a shared empty array that callers must not mutate.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="word"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="items"/> contains a <see langword="null"/> element.</exception>
	/// <remarks>
	/// The built-in filter performs a case-insensitive, ordinal substring match against each item's <see cref="CompletionItem.FilterText"/>,
	/// which falls back to the label when no filter text was set. The insertion and edit text are never matched, so snippet or commit text
	/// does not widen the filter. A whitespace-only word is matched literally, and matches keep their input order.
	/// </remarks>
	public static IReadOnlyList<CompletionItem> FilterByWord(IReadOnlyList<CompletionItem> items, string word)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentNullException.ThrowIfNull(word);

		for (int index = 0; index < items.Count; index++)
		{
			if (items[index] is null)
				throw new ArgumentException($"The completion items collection contains a null element at index {index}.", nameof(items));
		}

		if (word.Length == 0)
		{
			// The empty word keeps every item; the copy preserves the caller-owned result contract.
			var allItems = new CompletionItem[items.Count];

			for (int i = 0; i < items.Count; i++)
				allItems[i] = items[i];

			return allItems;
		}

		// Single pass; the result list is caller-owned, so it is returned without copying.
		List<CompletionItem>? matches = null;

		for (int i = 0; i < items.Count; i++)
		{
			CompletionItem item = items[i];

			if (item.FilterText.Contains(word, StringComparison.OrdinalIgnoreCase))
				(matches ??= []).Add(item);
		}

		return matches is null ? Array.Empty<CompletionItem>() : matches;
	}
}
