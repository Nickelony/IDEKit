using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Covers what only the Lua package decides: the language identifier, the display name in the failure reports,
/// the profile the public factory creates, and the files it mirrors to the server. The framework behavior behind them is covered
/// by the Provider suite.
/// </summary>
[TestClass]
public sealed class LuaLanguageServerProfileFactoryTests
{
	private const string Content = "local value = 1";

	[TestMethod]
	public void Create_DescribesTheLuaLanguage()
	{
		LanguageServerProfile profile = LuaLanguageServerProfileFactory.Create();

		Assert.AreEqual("lua", profile.LanguageId);
		Assert.AreEqual("Lua", profile.LanguageDisplayName);
		Assert.AreEqual("Lua language server", profile.ServerDisplayName);
		Assert.AreSame(StdioLanguageServerTransportOptions.Default, profile.Launch);
		Assert.AreSame(LuaLanguageServerProfileFactory.ClientCapabilities, profile.ClientCapabilities);

		DiagnosticMappingPolicy? policy = profile.DiagnosticMappingPolicy;

		Assert.IsNotNull(policy);
		Assert.AreEqual("Unknown Lua diagnostic.", policy.UnknownMessage);

		Assert.IsTrue(profile.Hooks.IsConfigurationPath(Path.Combine(TestPaths.Root, ".luarc.json")));
		Assert.IsFalse(profile.Hooks.IsConfigurationPath(Path.Combine(TestPaths.Root, "main.lua")));
	}

	[TestMethod]
	public async Task Provider_StartsOpensADocumentAndRequestsCompletionThroughItsClient()
	{
		string filePath = TestPaths.Script("smoke.lua");

		using var client = new FakeStatefulLanguageServerClient
		{
			IsReady = false,
			CompletionResponse = JsonSerializer.SerializeToElement(new { isIncomplete = false, items = new[] { new { label = "print", kind = 3 } } }),
		};

		using var provider = LanguageServerFeatureProvider.Create(LuaLanguageServerProfileFactory.Create(), [TestPaths.Root], client);

		Assert.IsFalse(provider.IsAvailable);

		provider.OpenDocument(filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(provider.IsAvailable);

		JsonElement textDocument = client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument");

		Assert.AreEqual(new Uri(filePath).AbsoluteUri, textDocument.GetProperty("uri").GetString());
		Assert.AreEqual("lua", textDocument.GetProperty("languageId").GetString());
		Assert.AreEqual(Content, textDocument.GetProperty("text").GetString());

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, Content, Content.Length),
			TestContext.CancellationToken
		);

		Assert.AreEqual("print", Assert.ContainsSingle(items).Label);
	}

	[TestMethod]
	[DataRow(".luarc.json")]
	[DataRow(".luarc.jsonc")]
	public async Task WatchedWorkspace_MirrorsLuaFilesAndRefreshesTheSettingsOnlyForALuaLsConfigurationFile(string configurationFileName)
	{
		using var workspace = new TemporaryDirectory("LuaWatch_");
		using var client = new FakeStatefulLanguageServerClient { IsReady = false };
		using var provider = LanguageServerFeatureProvider.Create(LuaLanguageServerProfileFactory.Create(), [workspace.Path], client);

		// The hover awaits the startup flow, so the workspace watcher runs once it returns.
		await provider
			.GetHoverAsync(
				filePath: workspace.GetFilePath("main.lua"),
				documentText: Content,
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		File.WriteAllText(workspace.GetFilePath("other.lua"), Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));
		Assert.AreEqual(0, client.GetSentMethodCount("workspace/didChangeConfiguration"));

		File.WriteAllText(workspace.GetFilePath(configurationFileName), "{}");

		Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeConfiguration", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task MissingServerExecutable_RaisesOnePersistentStartupFailureThatNamesTheLuaLanguageServer()
	{
		using var provider = new LanguageServerFeatureProvider(
			LuaLanguageServerProfileFactory.Create(),
			[TestPaths.Root],
			serverExecutablePath: null
		);

		var failures = new List<StartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		for (int attempt = 0; attempt < 2; attempt++)
		{
			Assert.IsNull(
				await provider
					.GetHoverAsync(
						filePath: TestPaths.Script("smoke.lua"),
						documentText: Content,
						position: new TextPosition(0, 0),
						cancellationToken: TestContext.CancellationToken
					)
					.ConfigureAwait(false)
			);
		}

		Assert.IsFalse(provider.IsAvailable);
		Assert.HasCount(1, failures);
		Assert.IsTrue(failures[0].IsPersistent);

		Assert.AreEqual(
			"The Lua language server executable is unavailable, so Lua language features are disabled until the provider is recreated with a valid executable path.",
			failures[0].Message
		);
	}

	[TestMethod]
	public async Task ServerThatFailsToStart_RaisesATransientThenAPersistentFailureThatNamesTheLuaLanguageServer()
	{
		using var client = new FakeStatefulLanguageServerClient { IsReady = false, StartResult = false };
		using var provider = LanguageServerFeatureProvider.Create(LuaLanguageServerProfileFactory.Create(), [TestPaths.Root], client);
		var failures = new List<StartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		for (int attempt = 0; attempt < 4; attempt++)
		{
			await provider
				.GetHoverAsync(
					filePath: TestPaths.Script("smoke.lua"),
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);
		}

		Assert.HasCount(2, failures);
		Assert.IsFalse(failures[0].IsPersistent);
		Assert.IsTrue(failures[1].IsPersistent);

		Assert.AreEqual(
			"The configured Lua language server failed to start, so Lua language features stay unavailable until a start succeeds. The provider retries automatically when Lua language features are requested again.",
			failures[0].Message
		);

		Assert.AreEqual(
			"The configured Lua language server failed to start repeatedly, so Lua language features are now disabled until the provider is recreated.",
			failures[1].Message
		);
	}

	[TestMethod]
	public async Task Provider_LaunchesTheServerWithTheProcessSettingsTheHostSetsOnTheProfile()
	{
		using var directory = new TemporaryDirectory("lua-launch-");
		string probePath = directory.GetFilePath("launch-probe.txt");

		// A shell stands in for LuaLS: it writes the variable to a file named relative to its working directory and exits without speaking
		// the protocol, so the file proves the argument, the variable and the working directory reached the process.
		(string executable, string[] arguments) = OperatingSystem.IsWindows()
			? (Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/c", "echo %NICKELONY_LUA_LAUNCH_PROBE%>launch-probe.txt" })
			: ("/bin/sh", ["-c", "printf '%s' \"$NICKELONY_LUA_LAUNCH_PROBE\" > launch-probe.txt"]);

		LanguageServerProfile packaged = LuaLanguageServerProfileFactory.Create();

		LanguageServerProfile profile = packaged with
		{
			Launch = packaged.Launch with
			{
				ServerArguments = arguments,
				ServerWorkingDirectory = directory.Path,
				EnvironmentVariables = new Dictionary<string, string> { ["NICKELONY_LUA_LAUNCH_PROBE"] = "probe-value" },
			},
		};

		using var provider = new LanguageServerFeatureProvider(profile, [directory.Path], executable);

		// The request starts the server; the stand-in fails the handshake, so the hover has no answer.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: directory.GetFilePath("smoke.lua"),
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(() => ReadProbe(probePath) == "probe-value", TestPolling.DefaultTimeout).ConfigureAwait(false),
			$"The stand-in server should have written the launch probe to {probePath}."
		);
	}

	/// <summary>Reads the launch probe the stand-in server writes, or <see langword="null"/> while it is missing or still being written.</summary>
	private static string? ReadProbe(string probePath)
	{
		try
		{
			return File.Exists(probePath) ? File.ReadAllText(probePath).Trim() : null;
		}
		catch (IOException)
		{
			return null;
		}
	}

	public required TestContext TestContext { get; set; }
}
