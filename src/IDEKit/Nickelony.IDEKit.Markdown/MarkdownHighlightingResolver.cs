using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.Markdown;

/// <summary>Resolves a code-block language to a highlighting definition, so the resolution order is defined once for every editor binding.</summary>
/// <remarks>
/// Resolution is an exact name first, then a case-insensitive lookup, then extension and alias fallbacks. A language that resolves
/// to nothing reports <see langword="null"/>; the caller decides whether that is worth a diagnostic.
/// </remarks>
public static class MarkdownHighlightingResolver
{
	/// <summary>Resolves a code-block language to a highlighting definition.</summary>
	/// <typeparam name="TDefinition">The engine's highlighting-definition type.</typeparam>
	/// <param name="language">The language taken from the fence info string, or <see langword="null"/>.</param>
	/// <param name="aliases">
	/// The additional language aliases to consult last; the caller supplies a dictionary that already compares keys case-insensitively.
	/// </param>
	/// <param name="catalog">The engine-specific definition lookup.</param>
	/// <returns>The resolved definition, or <see langword="null"/> when the language resolves to nothing.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="aliases"/> or <paramref name="catalog"/> is <see langword="null"/>.</exception>
	public static TDefinition? Resolve<TDefinition>(
		string? language,
		IReadOnlyDictionary<string, string> aliases,
		IMarkdownHighlightingCatalog<TDefinition> catalog
	)
		where TDefinition : class
	{
		ArgumentNullException.ThrowIfNull(aliases);
		ArgumentNullException.ThrowIfNull(catalog);

		if (string.IsNullOrWhiteSpace(language))
			return null;

		string trimmedLanguage = language.Trim();

		// Definition names are case-sensitive in the engine, so the supplied casing is tried first and a
		// case-insensitive scan follows before falling back to extensions.
		TDefinition? definition = ResolveByToken(trimmedLanguage, catalog);

		if (definition is not null)
			return definition;

		return TryGetAlias(aliases, trimmedLanguage, out string? alias) ? ResolveByToken(alias.Trim(), catalog) : null;
	}

	private static TDefinition? ResolveByToken<TDefinition>(string token, IMarkdownHighlightingCatalog<TDefinition> catalog)
		where TDefinition : class
	{
		TDefinition? definition = catalog.GetDefinition(token);

		if (definition is not null)
			return definition;

		definition = FindDefinitionByName(token, catalog);

		if (definition is not null)
			return definition;

		return catalog.GetDefinitionByExtension(token.StartsWith('.') ? token : "." + token);
	}

	private static TDefinition? FindDefinitionByName<TDefinition>(string name, IMarkdownHighlightingCatalog<TDefinition> catalog)
		where TDefinition : class
	{
		IReadOnlyList<TDefinition> definitions = catalog.GetDefinitions();

		for (int i = 0; i < definitions.Count; i++)
		{
			TDefinition definition = definitions[i];

			if (string.Equals(catalog.GetName(definition), name, StringComparison.OrdinalIgnoreCase))
				return definition;
		}

		return null;
	}

	private static bool TryGetAlias(IReadOnlyDictionary<string, string> aliases, string language, [NotNullWhen(true)] out string? alias)
	{
		// Options copy every assigned map into a case-insensitive frozen dictionary, so the direct
		// lookup already matches any casing the fence uses.
		if (aliases.TryGetValue(language, out alias) && !string.IsNullOrWhiteSpace(alias))
			return true;

		alias = null;
		return false;
	}
}
