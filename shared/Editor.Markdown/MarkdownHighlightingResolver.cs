using System.Diagnostics.CodeAnalysis;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// Supplies an editor engine's syntax-highlighting definitions to
/// <see cref="MarkdownHighlightingResolver"/>, so the resolution rules stay engine-neutral while the
/// definition lookup stays engine-specific.
/// </summary>
/// <typeparam name="TDefinition">The engine's highlighting-definition type.</typeparam>
internal interface IMarkdownHighlightingRegistry<TDefinition>
	where TDefinition : class
{
	/// <summary>
	/// Gets the definition registered under the exact given name, or <see langword="null"/> when none is.
	/// </summary>
	/// <param name="name">The definition name.</param>
	/// <returns>The matching definition, or <see langword="null"/>.</returns>
	TDefinition? GetDefinition(string name);

	/// <summary>
	/// Gets every registered definition, used for the case-insensitive name scan.
	/// </summary>
	/// <returns>The registered definitions.</returns>
	IReadOnlyList<TDefinition> GetDefinitions();

	/// <summary>
	/// Gets the definition registered for the given file extension (including its leading dot), or
	/// <see langword="null"/> when none is.
	/// </summary>
	/// <param name="extension">The file extension, including its leading dot.</param>
	/// <returns>The matching definition, or <see langword="null"/>.</returns>
	TDefinition? GetDefinitionByExtension(string extension);

	/// <summary>
	/// Gets the display name of a definition, used for the case-insensitive name scan.
	/// </summary>
	/// <param name="definition">The definition whose name is read.</param>
	/// <returns>The definition's name.</returns>
	string GetName(TDefinition definition);
}

/// <summary>
/// Resolves a code-block language to a highlighting definition, so the resolution order is defined once
/// for every editor binding.
/// </summary>
/// <remarks>
/// Resolution tries the language as a definition name with the supplied casing, then case-insensitively
/// against the registered definition names, then as a file extension, and finally through the configured
/// aliases. A language that resolves to nothing reports <see langword="null"/>; the caller decides whether
/// that is worth a diagnostic.
/// </remarks>
internal static class MarkdownHighlightingResolver
{
	/// <summary>
	/// Resolves a code-block language to a highlighting definition.
	/// </summary>
	/// <typeparam name="TDefinition">The engine's highlighting-definition type.</typeparam>
	/// <param name="language">The language taken from the fence info string, or <see langword="null"/>.</param>
	/// <param name="aliases">
	/// The additional language aliases to consult last; the caller supplies a dictionary that already
	/// compares keys case-insensitively.
	/// </param>
	/// <param name="registry">The engine-specific definition lookup.</param>
	/// <returns>The resolved definition, or <see langword="null"/> when the language resolves to nothing.</returns>
	internal static TDefinition? Resolve<TDefinition>(
		string? language,
		IReadOnlyDictionary<string, string> aliases,
		IMarkdownHighlightingRegistry<TDefinition> registry)
		where TDefinition : class
	{
		if (string.IsNullOrWhiteSpace(language))
			return null;

		string trimmedLanguage = language.Trim();

		// Definition names are case-sensitive in the engine, so the supplied casing is tried first and a
		// case-insensitive scan follows before falling back to extensions.
		TDefinition? definition = ResolveByToken(trimmedLanguage, registry);

		if (definition is not null)
			return definition;

		return TryGetAlias(aliases, trimmedLanguage, out string? alias)
			? ResolveByToken(alias.Trim(), registry)
			: null;
	}

	private static TDefinition? ResolveByToken<TDefinition>(string token, IMarkdownHighlightingRegistry<TDefinition> registry)
		where TDefinition : class
	{
		TDefinition? definition = registry.GetDefinition(token);

		if (definition is not null)
			return definition;

		definition = FindDefinitionByName(token, registry);

		if (definition is not null)
			return definition;

		return registry.GetDefinitionByExtension(token.StartsWith('.') ? token : "." + token);
	}

	private static TDefinition? FindDefinitionByName<TDefinition>(string name, IMarkdownHighlightingRegistry<TDefinition> registry)
		where TDefinition : class
	{
		IReadOnlyList<TDefinition> definitions = registry.GetDefinitions();

		for (int i = 0; i < definitions.Count; i++)
		{
			TDefinition definition = definitions[i];

			if (string.Equals(registry.GetName(definition), name, StringComparison.OrdinalIgnoreCase))
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
