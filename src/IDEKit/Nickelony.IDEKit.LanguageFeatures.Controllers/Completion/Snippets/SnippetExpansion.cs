namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

/// <summary>Represents the result of expanding a completion snippet: the visible text plus its tabstops.</summary>
/// <remarks>
/// The text has every default and first-choice text embedded, every plain tabstop removed, and every supported escape resolved.
/// <see cref="Placeholders"/> is never <see langword="null"/>, is empty for a snippet without placeholders, and is ordered by ascending
/// tabstop index with index 0 last, then by position for repeated indexes. The expansion is a data model only, and a <see langword="default"/> value
/// is not a valid expansion.
/// </remarks>
public readonly record struct SnippetExpansion
{
	/// <param name="text">The expanded visible text.</param>
	/// <param name="placeholders">
	/// The tabstops and placeholders found in the snippet; the constructor takes ownership of the list, so the caller must not mutate it afterwards.
	/// <see cref="SnippetExpander.Expand"/> transfers its own list, so expansion results are never copied.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="placeholders"/> is <see langword="null"/>.</exception>
	public SnippetExpansion(string text, IReadOnlyList<SnippetPlaceholder> placeholders)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(placeholders);

		Text = text;
		Placeholders = placeholders;
	}

	/// <summary>The expanded visible text.</summary>
	public string Text { get; }

	/// <summary>The tabstops and placeholders found in the snippet.</summary>
	public IReadOnlyList<SnippetPlaceholder> Placeholders { get; }

	/// <summary>Deconstructs the expansion into its components.</summary>
	/// <param name="text">Receives the expanded visible text.</param>
	/// <param name="placeholders">Receives the tabstops and placeholders.</param>
	public void Deconstruct(out string text, out IReadOnlyList<SnippetPlaceholder> placeholders)
	{
		text = Text;
		placeholders = Placeholders;
	}
}
