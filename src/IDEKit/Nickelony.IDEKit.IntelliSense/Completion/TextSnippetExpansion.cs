namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Represents the result of expanding a completion snippet: the visible text plus its tabstops.
/// </summary>
/// <remarks>
/// <para>
/// The expanded text is the snippet with every default and first-choice text embedded, every
/// plain tabstop removed, and every supported escape resolved. <see cref="Placeholders"/> is
/// never <see langword="null"/>; it is empty for a snippet without placeholders and is ordered by
/// ascending tabstop index (the final stop <c>$0</c> sorts last), then by position for repeated
/// indexes.
/// </para>
/// <para>
/// The expansion is a data model only: hosts decide how tabstops are navigated, rendered, and
/// grouped for undo, and providers may hand the model straight to an editor session.
/// </para>
/// <para>
/// A <see langword="default"/> value has <see langword="null"/> text and placeholders and is not
/// a valid expansion; construct instances through the constructor.
/// </para>
/// </remarks>
public readonly record struct TextSnippetExpansion
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextSnippetExpansion"/> record struct.
	/// </summary>
	/// <param name="text">The expanded visible text.</param>
	/// <param name="placeholders">
	/// The tabstops and placeholders found in the snippet; the constructor takes ownership of the
	/// list, so the caller must not mutate it afterwards. <see cref="TextSnippetExpander.Expand"/>
	/// transfers its own list, so expansion results are never copied.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="text"/> or <paramref name="placeholders"/> is <see langword="null"/>.
	/// </exception>
	public TextSnippetExpansion(string text, IReadOnlyList<TextSnippetPlaceholder> placeholders)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(placeholders);

		Text = text;
		Placeholders = placeholders;
	}

	/// <summary>
	/// Gets the expanded visible text.
	/// </summary>
	public string Text { get; }

	/// <summary>
	/// Gets the tabstops and placeholders found in the snippet.
	/// </summary>
	public IReadOnlyList<TextSnippetPlaceholder> Placeholders { get; }

	/// <summary>
	/// Deconstructs the expansion into its components.
	/// </summary>
	/// <param name="text">Receives the expanded visible text.</param>
	/// <param name="placeholders">Receives the tabstops and placeholders.</param>
	public void Deconstruct(out string text, out IReadOnlyList<TextSnippetPlaceholder> placeholders)
	{
		text = Text;
		placeholders = Placeholders;
	}
}
