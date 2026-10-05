namespace Nickelony.IDEKit.Markdown;

/// <summary>
/// Supplies an editor engine's syntax-highlighting definitions to <see cref="MarkdownHighlightingResolver"/>, so the resolution rules stay
/// engine-neutral while the definition lookup stays engine-specific.
/// </summary>
/// <typeparam name="TDefinition">The engine's highlighting-definition type.</typeparam>
public interface IMarkdownHighlightingCatalog<TDefinition>
	where TDefinition : class
{
	/// <summary>Returns the definition registered under the exact given name, or <see langword="null"/> when none is.</summary>
	/// <param name="name">The definition name.</param>
	/// <returns>The matching definition, or <see langword="null"/>.</returns>
	TDefinition? GetDefinition(string name);

	/// <summary>Returns every registered definition, which the case-insensitive name scan reads.</summary>
	/// <returns>The registered definitions.</returns>
	IReadOnlyList<TDefinition> GetDefinitions();

	/// <summary>
	/// Returns the definition registered for the given file extension (including its leading dot), or <see langword="null"/> when none is.
	/// </summary>
	/// <param name="extension">The file extension, including its leading dot.</param>
	/// <returns>The matching definition, or <see langword="null"/>.</returns>
	TDefinition? GetDefinitionByExtension(string extension);

	/// <summary>Returns the display name of a definition, used for the case-insensitive name scan.</summary>
	/// <param name="definition">The definition whose name is read.</param>
	/// <returns>The definition's name.</returns>
	string GetName(TDefinition definition);
}
