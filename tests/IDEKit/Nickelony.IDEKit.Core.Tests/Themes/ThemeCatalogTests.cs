using System.Globalization;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class ThemeCatalogTests
{
	private sealed record TestTheme(string Name, IReadOnlyList<string>? Aliases = null);

	private static ThemeCatalog<TestTheme> CreateCatalog(IEnumerable<TestTheme> themes, string defaultThemeName)
	{
		return new(
			themes,
			defaultThemeName,
			new ThemeCatalogHooks<TestTheme>(GetName: static theme => theme.Name, GetAliases: static theme => theme.Aliases)
		);
	}

	private static TestTheme Theme(string name, params string[]? aliases) => new(name, aliases);

	[TestMethod]
	public void Themes_AreOrderedWithDefaultFirstThenByName()
	{
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Zulu"), Theme("Alpha"), Theme("Default"), Theme("beta")], "Default");
		Assert.AreSequenceEqual(["Default", "Alpha", "beta", "Zulu"], [.. catalog.Themes.Select(static theme => theme.Name)]);
	}

	[TestMethod]
	public void Constructor_NullThemesCollection_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new ThemeCatalog<TestTheme>(null!, "Alpha", new ThemeCatalogHooks<TestTheme>(GetName: static theme => theme.Name))
		);
	}

	[TestMethod]
	public void Aliases_NullEntriesInsideAList_AreSkipped()
	{
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Alpha", null!, "A")], "Alpha");

		Assert.HasCount(1, catalog.Themes);
		Assert.IsTrue(catalog.TryGetTheme("A", out _));
	}

	[TestMethod]
	public void Constructor_InvokesNameAndAliasAccessorsOncePerNamedTheme()
	{
		int nameCalls = 0;
		int aliasCalls = 0;

		var catalog = new ThemeCatalog<TestTheme>(
			[Theme("Alpha", "A"), Theme("Beta"), Theme(string.Empty)],
			defaultThemeName: "Alpha",

			new ThemeCatalogHooks<TestTheme>(
				GetName: theme =>
				{
					nameCalls++;
					return theme.Name;
				},

				GetAliases: theme =>
				{
					aliasCalls++;
					return theme.Aliases;
				}
			)
		);

		Assert.AreEqual(3, nameCalls);
		Assert.AreEqual(2, aliasCalls);
		Assert.HasCount(2, catalog.Themes);
	}

	[TestMethod]
	public void GetTheme_ResolvesByName()
	{
		TestTheme alpha = Theme("Alpha");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([alpha, Theme("Beta")], "Beta");

		Assert.AreSame(alpha, catalog.GetTheme("Alpha"));
	}

	[TestMethod]
	public void GetTheme_ResolvesByAlias()
	{
		TestTheme alpha = Theme("Alpha", "Al");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([alpha, Theme("Beta")], "Beta");

		Assert.AreSame(alpha, catalog.GetTheme("Al"));
	}

	[TestMethod]
	public void GetTheme_IsCaseInsensitive()
	{
		TestTheme alpha = Theme("Alpha", "Al");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([alpha, Theme("Beta")], "Beta");

		Assert.AreSame(alpha, catalog.GetTheme("aLpHa"));
		Assert.AreSame(alpha, catalog.GetTheme("aL"));
	}

	[TestMethod]
	public void GetTheme_TrimsSurroundingWhitespace()
	{
		TestTheme alpha = Theme("Alpha", "Al");
		TestTheme beta = Theme("Beta");

		ThemeCatalog<TestTheme> catalog = CreateCatalog([alpha, beta], "Beta");

		// A caller-supplied spelling with surrounding whitespace resolves instead of falling back to the
		// default theme, matching the normalization applied to the captured names and aliases.
		Assert.AreSame(alpha, catalog.GetTheme("  Alpha  "));
		Assert.AreSame(alpha, catalog.GetTheme("\tAl "));
		Assert.IsTrue(catalog.TryGetTheme(" Beta ", out TestTheme? resolved));
		Assert.AreSame(beta, resolved);
	}

	[TestMethod]
	[DoNotParallelize]
	public void GetTheme_UnderTurkishCulture_KeepsOrdinalCaseInsensitiveMatching()
	{
		// Ordinal comparison is culture-independent, so 'il' still resolves 'IL' under tr-TR, where
		// culture-aware casing maps 'I' and 'i' to different letters.
		TestTheme theme = Theme("IL");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([theme, Theme("Beta")], "Beta");

		CultureInfo originalCulture = CultureInfo.CurrentCulture;
		CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
			CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

			Assert.AreSame(theme, catalog.GetTheme("il"));
			Assert.IsTrue(catalog.TryGetTheme("il", out TestTheme? resolved));
			Assert.AreSame(theme, resolved);
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
		}
	}

	[TestMethod]
	public void GetTheme_UnknownName_ReturnsDefault()
	{
		TestTheme beta = Theme("Beta");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Alpha"), beta], "Beta");

		Assert.AreSame(beta, catalog.GetTheme("DoesNotExist"));
	}

	[TestMethod]
	public void GetTheme_WhitespaceOrEmptyName_ReturnsDefault()
	{
		TestTheme beta = Theme("Beta");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Alpha"), beta], "Beta");

		Assert.AreSame(beta, catalog.GetTheme(string.Empty));
		Assert.AreSame(beta, catalog.GetTheme("   "));
	}

	[TestMethod]
	public void GetTheme_NullName_ReturnsDefault()
	{
		TestTheme beta = Theme("Beta");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Alpha"), beta], "Beta");

		Assert.AreSame(beta, catalog.GetTheme(null));
	}

	[TestMethod]
	public void DefaultTheme_ResolvesConfiguredSelection()
	{
		TestTheme defaultTheme = Theme("Gamma");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Alpha"), defaultTheme, Theme("Beta")], "Gamma");

		Assert.AreSame(defaultTheme, catalog.DefaultTheme);
	}

	[TestMethod]
	public void DefaultTheme_FallsBackToFirstOrderedWhenSelectionAbsent()
	{
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Zulu"), Theme("Alpha"), Theme("beta")], "Missing");
		Assert.AreSame("Alpha", catalog.DefaultTheme.Name);
	}

	[TestMethod]
	public void Constructor_MalformedEntries_AreIsolatedFromOrderingAndLookup()
	{
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme(string.Empty), Theme("   "), Theme("Valid")], "Valid");

		Assert.HasCount(1, catalog.Themes);
		Assert.AreSame("Valid", catalog.Themes[0].Name);
		Assert.AreSame(catalog.Themes[0], catalog.GetTheme("Valid"));
		Assert.IsFalse(catalog.TryGetTheme("", out _));
	}

	[TestMethod]
	public void Constructor_NullThemeEntries_AreSkipped()
	{
		ThemeCatalog<TestTheme> catalog = CreateCatalog([null!, Theme("Alpha")], "Alpha");

		Assert.HasCount(1, catalog.Themes);
		Assert.AreSame("Alpha", catalog.GetTheme("Alpha").Name);
	}

	[TestMethod]
	public void TryGetTheme_NullName_ReturnsFalse()
	{
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Alpha")], "Alpha");
		Assert.IsFalse(catalog.TryGetTheme(null, out _));
	}

	[TestMethod]
	public void DuplicateLookupName_KeepsFirstOrderedOccurrence()
	{
		TestTheme first = Theme("Alpha", "Shared");
		TestTheme second = Theme("Beta", "Shared");

		ThemeCatalog<TestTheme> catalog = CreateCatalog([first, second], "Gamma");

		Assert.AreSame(first, catalog.GetTheme("Shared"));
		Assert.AreSame(second, catalog.GetTheme("Beta"));
	}

	[TestMethod]
	public void DuplicateThemeName_KeepsFirstOrderedOccurrence()
	{
		TestTheme first = Theme("Alpha");
		TestTheme second = Theme("alpha");

		ThemeCatalog<TestTheme> catalog = CreateCatalog([first, second], "Beta");

		Assert.AreSame(first, catalog.GetTheme("Alpha"));
	}

	[TestMethod]
	public void DuplicateAliasWithinOneTheme_ResolvesCaseInsensitively()
	{
		TestTheme alpha = Theme("Alpha", "Al", "AL", "al");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([alpha], "Alpha");

		Assert.AreSame(alpha, catalog.GetTheme("Al"));
		Assert.AreSame(alpha, catalog.GetTheme("AL"));
		Assert.AreSame(alpha, catalog.GetTheme("al"));
		Assert.HasCount(1, catalog.Themes);
	}

	[TestMethod]
	public void ThemeNameCollidingWithLaterAlias_ResolvesToTheNamedTheme()
	{
		TestTheme alpha = Theme("Alpha");
		TestTheme beta = Theme("Beta", "Alpha");

		ThemeCatalog<TestTheme> catalog = CreateCatalog([alpha, beta], "Alpha");

		// The name of the earlier-ordered theme is indexed before the later theme's alias, so the
		// name keeps the lookup and the alias cannot shadow it.
		Assert.AreSame(alpha, catalog.GetTheme("Alpha"));
		Assert.AreSame(beta, catalog.GetTheme("Beta"));
	}

	[TestMethod]
	public void DefaultTheme_BlankSelection_FallsBackToFirstOrdered()
	{
		// A blank configured default matches no theme, so the catalog uses its first ordered theme.
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Zulu"), Theme("Alpha")], "   ");
		Assert.AreSame("Alpha", catalog.DefaultTheme.Name);
	}

	[TestMethod]
	public void Constructor_BlankAliases_AreSkipped()
	{
		TestTheme alpha = Theme("Alpha", "", "   ", "Al");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([alpha], "Alpha");

		Assert.IsFalse(catalog.TryGetTheme(string.Empty, out _));
		Assert.AreSame(alpha, catalog.GetTheme("Al"));
	}

	[TestMethod]
	public void AliasCollidingWithAnotherThemeName_ResolvesToTheFirstOrderedTheme()
	{
		TestTheme alpha = Theme("Alpha", "Beta");
		TestTheme beta = Theme("Beta");

		ThemeCatalog<TestTheme> catalog = CreateCatalog([alpha, beta], "Alpha");

		// "Beta" is registered as an alias of "Alpha" before the theme named "Beta" is indexed,
		// so the alias wins the lookup.
		Assert.AreSame(alpha, catalog.GetTheme("Beta"));
		Assert.AreSame(alpha, catalog.GetTheme("Alpha"));

		// The shadowed theme stays in the ordered list even though its own name resolves elsewhere.
		Assert.Contains(beta, catalog.Themes);
	}

	[TestMethod]
	public void Constructor_NullAliases_AreIgnored()
	{
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Alpha", null)], "Alpha");

		Assert.AreSame("Alpha", catalog.GetTheme("Alpha").Name);
		Assert.HasCount(1, catalog.Themes);
	}

	[TestMethod]
	public void EmptyCatalog_Throws() => Assert.ThrowsExactly<ArgumentException>(() => CreateCatalog([], "Default"));

	[TestMethod]
	public void CatalogOfOnlyMalformedEntries_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => CreateCatalog([Theme(string.Empty), Theme("  ")], "Default"));

	[TestMethod]
	public void Themes_DefaultSelectedByAlias_IsOrderedFirst()
	{
		TestTheme zulu = Theme("Zulu", "Z");
		ThemeCatalog<TestTheme> catalog = CreateCatalog([Theme("Alpha"), zulu], "Z");

		Assert.AreSame(zulu, catalog.DefaultTheme);
		Assert.AreSame(zulu, catalog.Themes[0]);
	}

	[TestMethod]
	public void Constructor_HooksWithoutNameSelector_Throws()
	{
		TestTheme[] themes = [Theme("One")];
		Assert.ThrowsExactly<ArgumentException>(() => new ThemeCatalog<TestTheme>(themes, "One", new ThemeCatalogHooks<TestTheme>(null!)));
	}

	[TestMethod]
	public void Constructor_NullDefaultThemeName_Throws()
	{
		TestTheme[] themes = [Theme("One")];

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new ThemeCatalog<TestTheme>(themes, null!, new ThemeCatalogHooks<TestTheme>(static theme => theme.Name))
		);
	}
}
