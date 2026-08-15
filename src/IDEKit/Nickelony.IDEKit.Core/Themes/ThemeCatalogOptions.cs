namespace Nickelony.IDEKit.Core.Themes;

/// <summary>
/// Configures a <see cref="ThemeCatalog{TTheme}"/>: how to read a theme's name and aliases, and
/// which theme is selected by default.
/// </summary>
/// <remarks>
/// A <c>default(ThemeCatalogOptions&lt;TTheme&gt;)</c> value carries a <see langword="null"/> name
/// selector and a <see langword="null"/> default theme name, which the
/// <see cref="ThemeCatalog{TTheme}"/> constructor rejects.
/// </remarks>
/// <param name="GetName">
/// The selector that returns the canonical name of a theme. The name is the lookup and sort key of
/// the catalog, and the configured default selection is matched against it and against the aliases.
/// </param>
/// <param name="DefaultThemeName">
/// The name or alias of the default theme selection. A blank value matches no theme, so the catalog
/// falls back to its first ordered theme.
/// </param>
/// <param name="GetAliases">
/// The optional selector that returns the additional lookup names of a theme, or
/// <see langword="null"/> when a theme has no aliases. The default is <see langword="null"/>.
/// </param>
/// <typeparam name="TTheme">The reference theme type carried by the catalog.</typeparam>
public readonly record struct ThemeCatalogOptions<TTheme>(
	Func<TTheme, string> GetName,
	string DefaultThemeName,
	Func<TTheme, IReadOnlyList<string>?>? GetAliases = null)
	where TTheme : class;
