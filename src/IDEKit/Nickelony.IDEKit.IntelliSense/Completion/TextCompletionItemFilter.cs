namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Reduces a completion item set to the items matching the word being typed.
/// </summary>
/// <param name="items">The candidate completion items.</param>
/// <param name="word">The word being typed; empty when no word precedes the caret.</param>
/// <returns>
/// A non-null list of matching items. The returned list is adopted by the kernel when it builds the
/// session decision and is not copied, so a filter must not mutate a list after returning it; returning
/// a fresh list per call is the simplest way to satisfy this. The filter must be thread-safe when the
/// kernel that uses it is shared between callers. Returning <see langword="null"/> is a contract
/// violation and causes <see cref="TextCompletionSessionKernel"/> to throw
/// <see cref="InvalidOperationException"/>.
/// </returns>
public delegate IReadOnlyList<TextCompletionItem> TextCompletionItemFilter(IReadOnlyList<TextCompletionItem> items, string word);
