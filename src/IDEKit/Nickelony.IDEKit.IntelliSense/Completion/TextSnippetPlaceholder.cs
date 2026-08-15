using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Represents one tabstop or placeholder within an expanded completion snippet.
/// </summary>
/// <remarks>
/// <para>
/// The model is deliberately flat: nested placeholders are separate entries, and the same tabstop
/// index may occur more than once when the snippet mirrors a tabstop. Whether mirrored occurrences
/// are edited together is host behavior, as are the navigation order and the visual treatment of
/// the placeholder text.
/// </para>
/// <para>
/// Placeholder ranges may nest: a nested placeholder's range lies inside its parent's range, so a
/// host that replaces the text of an outer range invalidates the ranges of the placeholders it
/// contains and must re-resolve them.
/// </para>
/// <para>
/// Index <c>0</c> is the conventional final caret stop and sorts last in
/// <see cref="TextSnippetExpansion.Placeholders"/>; every other index is a numbered tabstop in
/// ascending navigation order.
/// </para>
/// </remarks>
/// <param name="Index">The tabstop index parsed from the snippet.</param>
/// <param name="Range">
/// The range of the placeholder text inside <see cref="TextSnippetExpansion.Text"/>; empty for a
/// tabstop and for an empty default.
/// </param>
/// <param name="Choices">
/// The unescaped choice texts in snippet order for a choice placeholder (<c>${1|a,b|}</c>), or
/// <see langword="null"/> for a tabstop or default-text placeholder. The first choice is the text
/// embedded in <see cref="TextSnippetExpansion.Text"/>; a host offering choice cycling replaces
/// <paramref name="Range"/> with another choice text. The list is part of the expansion snapshot
/// and must not be mutated after construction.
/// </param>
public readonly record struct TextSnippetPlaceholder(int Index, TextRange Range, IReadOnlyList<string>? Choices);
