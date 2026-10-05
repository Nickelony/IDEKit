using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the client that the public constructor creates from a profile and a server executable path.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderClientCreationTests
{
	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	public async Task Constructor_WithABlankExecutablePath_LeavesTheLanguageServerUnavailable(string? serverExecutablePath)
	{
		using var provider = new LanguageServerFeatureProvider(TestProviders.Profile, [TestPaths.Root], serverExecutablePath);
		var failures = new List<StartupFailedEventArgs>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs);

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: Path.Combine(TestPaths.Root, "a.test"),
					documentText: "local a = 1",
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, provider.State);
		Assert.AreEqual(TestProviders.MissingClientFailureMessage, Assert.ContainsSingle(failures).Failure.Message);
	}

	[TestMethod]
	public void Constructor_WithAnExecutablePath_CreatesAnUnstartedClient()
	{
		using var provider = new LanguageServerFeatureProvider(TestProviders.Profile, [TestPaths.Root], Path.Combine(TestPaths.Root, "server.exe"));

		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
	}

	[TestMethod]
	public void Constructor_RunsNoProfileDelegate()
	{
		int delegateRuns = 0;

		LanguageServerProfile profile = TestProviders.CreateProfile() with
		{
			Hooks = new LanguageServerProfileHooks
			{
				IsConfigurationPath = _ =>
				{
					delegateRuns++;
					return false;
				},

				BuildSettings = () =>
				{
					delegateRuns++;
					return new System.Text.Json.Nodes.JsonObject();
				},

				BuildInitializationOptions = () =>
				{
					delegateRuns++;
					return null;
				},

				BuildSessionStartNotifications = _ =>
				{
					delegateRuns++;
					return [];
				},
			},
		};

		using var provider = new LanguageServerFeatureProvider(profile, [TestPaths.Root], Path.Combine(TestPaths.Root, "server.exe"));

		Assert.AreEqual(0, delegateRuns);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("  ")]
	public void Constructor_WithABlankExecutablePath_CreatesNoClientSpecification(string? serverExecutablePath)
	{
		using var provider = new LanguageServerFeatureProvider(TestProviders.Profile, [TestPaths.Root], serverExecutablePath);
		Assert.IsNull(provider.ClientSpecification);
	}

	[TestMethod]
	public void Constructor_WithANonDefaultLaunch_HandsTheLaunchSettingsToTheClientTransport()
	{
		var launch = new StdioLanguageServerTransportOptions
		{
			ServerArguments = ["--stdio", "--probe"],
			EnvironmentVariables = new Dictionary<string, string> { ["PROBE_VARIABLE"] = "probe" },
		};

		using LanguageServerFeatureProvider provider = CreateProvider(TestProviders.CreateProfile() with { Launch = launch });

		StdioLanguageServerTransportOptions handedOver = RequireSpecification(provider).Launch;

		Assert.AreSame(launch, handedOver);
		Assert.AreSequenceEqual(["--stdio", "--probe"], [.. handedOver.ServerArguments]);
		Assert.AreEqual("probe", handedOver.EnvironmentVariables["PROBE_VARIABLE"]);
	}

	[TestMethod]
	public void Constructor_WithNonDefaultClientCapabilities_HandsTheCapabilitiesPayloadToTheClient()
	{
		var capabilities = new LanguageServerClientCapabilitiesOptions { SupportsPullDiagnostics = true };

		using LanguageServerFeatureProvider provider = CreateProvider(TestProviders.CreateProfile() with { ClientCapabilities = capabilities });

		var payload = RequireSpecification(provider).Options.ClientCapabilitiesProvider([TestPaths.Root]) as JsonNode;

		Assert.IsTrue(JsonNode.DeepEquals(LanguageServerClientCapabilitiesBuilder.Build(capabilities), payload));
		Assert.IsFalse(JsonNode.DeepEquals(LanguageServerClientCapabilitiesBuilder.Build(LanguageServerClientCapabilitiesOptions.Default), payload));
	}

	[TestMethod]
	public void Constructor_WithASettingsHook_HandsTheSettingsToTheClient()
	{
		var settings = new JsonObject { ["probe"] = true };

		using LanguageServerFeatureProvider provider = CreateProvider(CreateProfileWithHooks(buildSettings: () => settings));

		Assert.AreSame(settings, RequireSpecification(provider).Options.SettingsProvider());
	}

	[TestMethod]
	public void Constructor_WithAnInitializationOptionsHook_HandsTheOptionsToTheClient()
	{
		object initializationOptions = new();

		using LanguageServerFeatureProvider provider = CreateProvider(
			CreateProfileWithHooks(buildInitializationOptions: () => initializationOptions)
		);

		Assert.AreSame(initializationOptions, RequireSpecification(provider).Options.InitializationOptionsProvider([TestPaths.Root]));
	}

	[TestMethod]
	public void Constructor_WithASessionStartNotificationsHook_HandsTheNotificationsToTheClient()
	{
		IReadOnlyList<string>? receivedRoots = null;
		IReadOnlyList<SessionStartNotification> notifications = [new("probe/open", null)];

		using LanguageServerFeatureProvider provider = CreateProvider(
			CreateProfileWithHooks(buildSessionStartNotifications: roots =>
			{
				receivedRoots = roots;
				return notifications;
			})
		);

		IReadOnlyList<string> clientRoots = [TestPaths.Root];

		Assert.AreSame(notifications, RequireSpecification(provider).Options.SessionStartNotificationsProvider(clientRoots));
		Assert.AreSame(clientRoots, receivedRoots);
	}

	[TestMethod]
	public void Constructor_WithoutASessionStartNotificationsHook_SendsNoNotifications()
	{
		using LanguageServerFeatureProvider provider = CreateProvider(TestProviders.CreateProfile());
		Assert.IsEmpty(RequireSpecification(provider).Options.SessionStartNotificationsProvider([TestPaths.Root]));
	}

	[TestMethod]
	public void Constructor_WithAnInvalidRootList_NamesTheParameter()
	{
		ArgumentException relative = Assert.ThrowsExactly<ArgumentException>(() =>
			new LanguageServerFeatureProvider(TestProviders.Profile, ["relative/root"], "server.exe")
		);

		ArgumentNullException missing = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new LanguageServerFeatureProvider(TestProviders.Profile, null!, "server.exe")
		);

		Assert.AreEqual("workspaceRootDirectoryPaths", relative.ParamName);
		Assert.AreEqual("workspaceRootDirectoryPaths", missing.ParamName);
	}

	[TestMethod]
	public void Constructor_WithANullProfile_NamesTheParameter()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new LanguageServerFeatureProvider(null!, [TestPaths.Root], "server.exe")
		);

		Assert.AreEqual("profile", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_WithAnEmptyRootList_ModelsAFolderlessSession()
	{
		using var provider = new LanguageServerFeatureProvider(TestProviders.Profile, [], Path.Combine(TestPaths.Root, "server.exe"));
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
	}

	[TestMethod]
	public void Constructor_WithADuplicateRoot_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new LanguageServerFeatureProvider(TestProviders.Profile, [TestPaths.Root, TestPaths.Root], Path.Combine(TestPaths.Root, "server.exe"))
		);
	}

	[TestMethod]
	public void Constructor_WithCaseVariantRootsUnderACaseInsensitiveComparison_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new LanguageServerFeatureProvider(
				profile: TestProviders.Profile,
				workspaceRootDirectoryPaths: [Path.Combine(TestPaths.Root, "Case"), Path.Combine(TestPaths.Root, "CASE")],
				serverExecutablePath: Path.Combine(TestPaths.Root, "server.exe"),
				options: TestOptions.CaseInsensitive
			)
		);
	}

	[TestMethod]
	public async Task Constructor_ForwardsTheLoggerToTheClient()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		string executablePath = Path.Combine(TestPaths.Root, "missing-server.exe");

		using var provider = new LanguageServerFeatureProvider(
			profile: TestProviders.Profile,
			workspaceRootDirectoryPaths: [TestPaths.Root],
			serverExecutablePath: executablePath,
			logger: logger
		);

		// A missing executable fails the start, which the client reports through the logger it was given.
		await provider
			.GetHoverAsync(
				filePath: Path.Combine(TestPaths.Root, "a.test"),
				documentText: "local a = 1",
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.Contains(message => message.Contains("missing-server.exe", StringComparison.Ordinal), logger.Messages);
	}

	private static LanguageServerFeatureProvider CreateProvider(LanguageServerProfile profile) =>
		new(profile, [TestPaths.Root], Path.Combine(TestPaths.Root, "server.exe"));

	private static ClientSpecification RequireSpecification(LanguageServerFeatureProvider provider)
	{
		Assert.IsNotNull(provider.ClientSpecification);
		return provider.ClientSpecification;
	}

	/// <summary>Creates the test profile with the given hooks and the defaults of the test language for the rest.</summary>
	private static LanguageServerProfile CreateProfileWithHooks(
		Func<JsonNode>? buildSettings = null,
		Func<object?>? buildInitializationOptions = null,
		Func<IReadOnlyList<string>, IReadOnlyList<SessionStartNotification>>? buildSessionStartNotifications = null
	)
	{
		LanguageServerProfileHooks defaults = TestProviders.Profile.Hooks;

		return TestProviders.CreateProfile() with
		{
			Hooks = new LanguageServerProfileHooks
			{
				IsConfigurationPath = defaults.IsConfigurationPath,
				BuildSettings = buildSettings ?? defaults.BuildSettings,
				BuildInitializationOptions = buildInitializationOptions ?? defaults.BuildInitializationOptions,
				BuildSessionStartNotifications = buildSessionStartNotifications,
			},
		};
	}

	public TestContext? TestContext { get; set; }
}
