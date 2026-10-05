namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class LanguageServerProfileTests
{
	private static LanguageServerProfile CreateProfile()
	{
		return new()
		{
			LanguageId = "test",
			LanguageDisplayName = "Test",
			ServerDisplayName = "test language server",
			WatchSpecifications = [],
		};
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("  ")]
	public void Names_RejectBlankValues(string blank)
	{
		Assert.ThrowsExactly<ArgumentException>(() => CreateProfile() with { LanguageId = blank });
		Assert.ThrowsExactly<ArgumentException>(() => CreateProfile() with { LanguageDisplayName = blank });
		Assert.ThrowsExactly<ArgumentException>(() => CreateProfile() with { ServerDisplayName = blank });
	}

	[TestMethod]
	public void Names_RejectNull()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => CreateProfile() with { LanguageId = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => CreateProfile() with { LanguageDisplayName = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => CreateProfile() with { ServerDisplayName = null! });
	}

	[TestMethod]
	public void WatchSpecifications_AreCopiedOnAssignment()
	{
		var specifications = new List<FileWatchSpecification> { new("*.one", IncludeSubdirectories: true) };

		LanguageServerProfile profile = CreateProfile() with { WatchSpecifications = specifications };

		specifications.Add(new FileWatchSpecification("*.two", IncludeSubdirectories: true));

		Assert.HasCount(1, profile.WatchSpecifications);
		Assert.ThrowsExactly<ArgumentNullException>(() => CreateProfile() with { WatchSpecifications = null! });
	}

	[TestMethod]
	public void Launch_DefaultsToTheDefaultStdioOptionsAndRejectsNull()
	{
		Assert.AreSame(StdioLanguageServerTransportOptions.Default, CreateProfile().Launch);
		Assert.ThrowsExactly<ArgumentNullException>(() => CreateProfile() with { Launch = null! });
	}

	[TestMethod]
	public void Launch_OverridesOneSettingWithAWithExpressionAndKeepsTheOthers()
	{
		LanguageServerProfile packaged = CreateProfile() with
		{
			Launch = StdioLanguageServerTransportOptions.Default with { ServerArguments = ["--stdio"] },
		};

		LanguageServerProfile hosted = packaged with
		{
			Launch = packaged.Launch with { EnvironmentVariables = new Dictionary<string, string> { ["LOG_LEVEL"] = "debug" } },
		};

		Assert.AreSequenceEqual(["--stdio"], hosted.Launch.ServerArguments);
		Assert.AreEqual("debug", hosted.Launch.EnvironmentVariables["LOG_LEVEL"]);
		Assert.IsEmpty(packaged.Launch.EnvironmentVariables);
	}

	[TestMethod]
	public void Hooks_DefaultToTheHooksOfALanguageWithoutConfigurationFiles()
	{
		LanguageServerProfile profile = CreateProfile();
		Assert.IsFalse(profile.Hooks.IsConfigurationPath("any.file"));
	}

	[TestMethod]
	public void Hooks_RejectNull() => Assert.ThrowsExactly<ArgumentNullException>(() => CreateProfile() with { Hooks = null! });

	[TestMethod]
	public void ClientCapabilities_RejectNull() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => CreateProfile() with { ClientCapabilities = null! });

	[TestMethod]
	public void FailureMessages_DefaultToSentencesThatNameTheServerAndTheLanguage()
	{
		LanguageServerProfile profile = CreateProfile();

		Assert.Contains("test language server", profile.TransientStartupFailureMessage ?? string.Empty);
		Assert.Contains("Test language features", profile.TransientStartupFailureMessage ?? string.Empty);
		Assert.Contains("test language server", profile.PersistentStartupFailureMessage ?? string.Empty);
		Assert.Contains("test language server", profile.MissingClientFailureMessage ?? string.Empty);
	}

	[TestMethod]
	public void FailureMessages_AnExplicitValueReplacesTheDefaultAndABlankOneSuppressesTheReport()
	{
		LanguageServerProfile profile = CreateProfile() with { TransientStartupFailureMessage = "custom", PersistentStartupFailureMessage = "" };

		Assert.AreEqual("custom", profile.TransientStartupFailureMessage);
		Assert.AreEqual("", profile.PersistentStartupFailureMessage);
		Assert.IsFalse(string.IsNullOrEmpty(profile.MissingClientFailureMessage));
	}
}
