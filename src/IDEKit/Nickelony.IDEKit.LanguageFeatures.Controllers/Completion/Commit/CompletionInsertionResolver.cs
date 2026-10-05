using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

/// <summary>Resolves the text a completion item inserts and the caret position inside it.</summary>
public static class CompletionInsertionResolver
{
	/// <summary>Resolves the insertion of an item.</summary>
	/// <param name="item">The item that is committed.</param>
	/// <returns>The text to insert and the caret offset inside it.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The edit payload supersedes the plain insertion text, as the LSP text-edit semantics require, so an item that carries one inserts
	/// its replacement text. A snippet is expanded and the caret lands after the text of its final tabstop when it has one.
	/// </remarks>
	public static CompletionInsertion Resolve(CompletionItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		return ResolveSnippet(item) is { } expansion
			? Resolve(expansion)
			: new CompletionInsertion(item.TextEdit?.NewText ?? item.InsertText, CaretOffset: null);
	}

	/// <summary>Resolves the insertion of an expanded snippet.</summary>
	/// <param name="expansion">The expansion of the snippet that is committed.</param>
	/// <returns>The expanded text and the caret offset after the text of its final tabstop, or no caret offset when it has none.</returns>
	/// <exception cref="ArgumentNullException">The placeholders of <paramref name="expansion"/> are <see langword="null"/>.</exception>
	public static CompletionInsertion Resolve(SnippetExpansion expansion)
	{
		ArgumentNullException.ThrowIfNull(expansion.Placeholders);

		foreach (SnippetPlaceholder placeholder in expansion.Placeholders)
		{
			if (placeholder.Index == 0)
				return new CompletionInsertion(expansion.Text, placeholder.Range.EndOffset);
		}

		return new CompletionInsertion(expansion.Text, CaretOffset: null);
	}

	/// <summary>Resolves the snippet an item inserts.</summary>
	/// <param name="item">The item that is committed.</param>
	/// <returns>The expansion of the item's snippet text, or <see langword="null"/> when the item inserts plain text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The expansion is of the text <see cref="Resolve(CompletionItem)"/> inserts, so its <see cref="SnippetExpansion.Text"/> is that insertion's
	/// text and its placeholder ranges are offsets into it.
	/// </remarks>
	public static SnippetExpansion? ResolveSnippet(CompletionItem item)
	{
		ArgumentNullException.ThrowIfNull(item);
		return item.InsertTextFormat == CompletionInsertTextFormat.Snippet ? SnippetExpander.Expand(item.TextEdit?.NewText ?? item.InsertText) : null;
	}
}
