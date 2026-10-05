using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

/// <summary>Represents one tabstop or placeholder within an expanded completion snippet.</summary>
/// <param name="Index">The tabstop index parsed from the snippet.</param>
/// <param name="Range">
/// The range of the placeholder text inside <see cref="SnippetExpansion.Text"/>; empty for a tabstop and for an empty default.
/// </param>
/// <param name="Choices">
/// The unescaped choice texts in snippet order for a choice placeholder (<c>${1|a,b|}</c>), or <see langword="null"/> for a tabstop
/// or default-text placeholder. The first choice is the text embedded in <see cref="SnippetExpansion.Text"/>; a host offering choice cycling replaces
/// <paramref name="Range"/> with another choice text. The list is part of the expansion snapshot and must not be mutated after construction.
/// </param>
/// <remarks>
/// The model is deliberately flat: nested placeholders are separate entries, and the same index may occur more than once when the snippet
/// mirrors a tabstop, with whether mirrored occurrences are edited together left to the host. Placeholder ranges may nest, so replacing an outer
/// range invalidates the ranges it contains. Index <c>0</c> is the final caret stop and sorts last in <see cref="SnippetExpansion.Placeholders"/>.
/// </remarks>
public readonly record struct SnippetPlaceholder(int Index, TextRange Range, IReadOnlyList<string>? Choices);
