namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// The test language the Roslyn tests serve, so a test controls the language identifier and the watch pattern instead of depending
/// on a shipped language.
/// </summary>
internal static class TestRoslynLanguages
{
	/// <summary>The language identifier the test language opens documents with.</summary>
	internal const string TestLanguageId = "vbtest";

	/// <summary>The display name the test language reports in log and diagnostic text.</summary>
	internal const string TestDisplayName = "Test Roslyn";

	/// <summary>The language-specific watch pattern the test language mirrors.</summary>
	internal static IReadOnlyList<FileWatchSpecification> TestWatchSpecifications { get; } =
	[new FileWatchSpecification("*.vbtest", IncludeSubdirectories: true)];

	/// <summary>The test language.</summary>
	internal static RoslynLanguageDescriptor Test { get; } =
		new(
			LanguageId: TestLanguageId,
			DisplayName: TestDisplayName,
			ProjectFileExtension: ".vbtestproj",
			WatchSpecifications: TestWatchSpecifications
		);

	/// <summary>The profile of the test language, with the default session options.</summary>
	internal static LanguageServerProfile TestProfile { get; } = RoslynLanguageServerProfileFactory.Create(Test);

	/// <summary>The rows of a test that runs once for each shipped language.</summary>
	/// <returns>One row per shipped language, holding the language.</returns>
	internal static IEnumerable<object[]> ShippedLanguageRows()
	{
		yield return new object[] { RoslynLanguage.CSharp };
		yield return new object[] { RoslynLanguage.VisualBasic };
	}
}
