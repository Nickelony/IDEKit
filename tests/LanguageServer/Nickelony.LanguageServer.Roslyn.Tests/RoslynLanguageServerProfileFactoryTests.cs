using System.Reflection;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>Covers the profile that the factory creates for a language.</summary>
[TestClass]
public sealed class RoslynLanguageServerProfileFactoryTests
{
	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-roslyn-factory-tests-" + Guid.NewGuid().ToString("N"));

	public static string GetLanguageDisplayName(MethodInfo methodInfo, object[] values) => $"{methodInfo.Name} ({values[0]})";

	[TestMethod]
	public void Create_CopiesTheWatchSpecificationsOfTheDescriptor()
	{
		var specifications = new List<FileWatchSpecification> { new("*.one", IncludeSubdirectories: true) };

		LanguageServerProfile profile = RoslynLanguageServerProfileFactory.Create(
			new RoslynLanguageDescriptor(LanguageId: "one", DisplayName: "One", ProjectFileExtension: ".oneproj", WatchSpecifications: specifications)
		);

		int composedCount = profile.WatchSpecifications.Count;

		specifications.Add(new FileWatchSpecification("*.two", IncludeSubdirectories: true));

		Assert.AreEqual("*.one", profile.WatchSpecifications[0].Filter);
		Assert.HasCount(composedCount, profile.WatchSpecifications);
	}

	[TestMethod]
	[DynamicData(
		nameof(TestRoslynLanguages.ShippedLanguageRows),
		typeof(TestRoslynLanguages),
		DynamicDataDisplayName = nameof(GetLanguageDisplayName)
	)]
	public void Create_FollowsTheLanguagePatternsWithTheSharedBuildInputs(RoslynLanguage language)
	{
		IReadOnlyList<FileWatchSpecification> languagePatterns = RoslynLanguageDescriptor.For(language).WatchSpecifications;

		LanguageServerProfile profile = RoslynLanguageServerProfileFactory.Create(language);

		Assert.AreSequenceEqual(WorkspaceConventions.ComposeWatchSpecifications(languagePatterns), profile.WatchSpecifications);
		Assert.IsGreaterThan(languagePatterns.Count, profile.WatchSpecifications.Count);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("  ")]
	public void Create_WithABlankLanguageIdOrDisplayName_Throws(string blank)
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			RoslynLanguageServerProfileFactory.Create(
				new RoslynLanguageDescriptor(LanguageId: blank, DisplayName: "Name", ProjectFileExtension: ".proj", WatchSpecifications: [])
			)
		);

		Assert.ThrowsExactly<ArgumentException>(() =>
			RoslynLanguageServerProfileFactory.Create(
				new RoslynLanguageDescriptor(LanguageId: "id", DisplayName: blank, ProjectFileExtension: ".proj", WatchSpecifications: [])
			)
		);
	}

	[TestMethod]
	public void Create_WithAnUndefinedLanguage_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RoslynLanguageServerProfileFactory.Create((RoslynLanguage)99));

	[TestMethod]
	[DynamicData(
		nameof(TestRoslynLanguages.ShippedLanguageRows),
		typeof(TestRoslynLanguages),
		DynamicDataDisplayName = nameof(GetLanguageDisplayName)
	)]
	public void Create_NamesTheLanguageInTheFailureMessages(RoslynLanguage language)
	{
		string display = RoslynLanguageDescriptor.For(language).DisplayName;

		LanguageServerProfile profile = RoslynLanguageServerProfileFactory.Create(language);

		Assert.AreEqual(
			$"The configured Roslyn language server failed to start, so {display} language features stay unavailable until a start succeeds. The provider retries automatically when {display} language features are requested again.",
			profile.TransientStartupFailureMessage
		);

		Assert.AreEqual(
			$"The configured Roslyn language server failed to start repeatedly, so {display} language features are now disabled until the provider is recreated.",
			profile.PersistentStartupFailureMessage
		);

		Assert.AreEqual(
			$"The Roslyn language server executable is unavailable, so {display} language features are disabled until the provider is recreated with a valid executable path.",
			profile.MissingClientFailureMessage
		);
	}

	[TestMethod]
	public void Create_NeverTreatsAWorkspaceFileAsAConfigurationFile()
	{
		LanguageServerProfile profile = RoslynLanguageServerProfileFactory.Create(RoslynLanguage.CSharp);

		Assert.IsFalse(profile.Hooks.IsConfigurationPath(Path.Combine(s_workspaceRoot, "Directory.Build.props")));
		Assert.IsFalse(profile.Hooks.IsConfigurationPath(Path.Combine(s_workspaceRoot, "global.json")));
	}
}
