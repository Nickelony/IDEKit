namespace Nickelony.IDEKit.Core.Themes;

/// <summary>The delegates through which a <see cref="ThemeCatalog{TTheme}"/> reads the names of a theme.</summary>
/// <typeparam name="TTheme">The reference theme type carried by the catalog.</typeparam>
/// <param name="GetName">
/// Returns a theme's canonical name, which is the catalog's lookup and sort key; the default selection is matched against it and the aliases.
/// </param>
/// <param name="GetAliases">Returns a theme's additional lookup names, or <see langword="null"/> when it has none.</param>
/// <remarks>
/// A <c>default(ThemeCatalogHooks&lt;TTheme&gt;)</c> value carries a <see langword="null"/> name selector,
/// which the <see cref="ThemeCatalog{TTheme}"/> constructor rejects.
/// </remarks>
public readonly record struct ThemeCatalogHooks<TTheme>(Func<TTheme, string> GetName, Func<TTheme, IReadOnlyList<string>?>? GetAliases = null)
	where TTheme : class;
