using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Reduces a completion item set to the items matching the word being typed.</summary>
/// <param name="items">The candidate completion items.</param>
/// <param name="word">The word being typed; empty when no word precedes the caret.</param>
/// <returns>A non-<see langword="null"/> list of matching items.</returns>
/// <remarks>
/// The returned list is adopted by the kernel without a copy, so a filter must not mutate it after returning; returning a fresh
/// list per call satisfies this. The filter must be thread-safe when the kernel is shared. Returning <see langword="null"/> is a contract violation
/// and causes <see cref="CompletionSessionKernel"/> to throw <see cref="InvalidOperationException"/>.
/// </remarks>
public delegate IReadOnlyList<CompletionItem> CompletionItemFilter(IReadOnlyList<CompletionItem> items, string word);
