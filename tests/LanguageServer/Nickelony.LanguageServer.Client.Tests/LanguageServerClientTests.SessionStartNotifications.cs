using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Covers the notifications the host's factory supplies for each session start.</summary>
public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task EnsureStartedAsync_SendsTheSessionStartNotificationsAfterInitializedAndBeforeAnyDocumentNotification()
	{
		List<ScriptedStartupAttempt> attempts = [];
		List<IReadOnlyList<string>> receivedRoots = [];

		LanguageServerClientOptions options = s_defaultClientOptions with
		{
			SessionStartNotificationsProvider = roots =>
			{
				receivedRoots.Add(roots);

				return
				[
					new SessionStartNotification("project/open", new { Projects = new[] { "file:///C:/Workspace/App.csproj" } }),
					new SessionStartNotification("example/ping", parameters: null),
				];
			},
		};

		using LanguageServerClient client = CreateScriptedStartupClient(attempts, options);

		ScriptedStartupAttempt attempt = await CompleteScriptedStartupAsync(
				startTask: client.EnsureStartedAsync(CancellationToken.None),
				attempts,
				attemptIndex: 0,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		// A document notification can only follow readiness, so it lands after every session-start notification.
		await client
			.SendNotificationAsync(
				"textDocument/didOpen",

				new DidOpenTextDocumentParams(
					new TextDocumentItemPayload(Uri: "file:///C:/Workspace/a.cs", LanguageId: "csharp", Version: 1, Text: "")
				),

				CancellationToken.None
			)
			.ConfigureAwait(false);

		List<(string? Method, string BodyJson)> messages = await WaitForWrittenMethodAsync(attempt, "textDocument/didOpen").ConfigureAwait(false);

		Assert.AreSequenceEqual(
			["initialize", "initialized", "project/open", "example/ping", "workspace/didChangeConfiguration", "textDocument/didOpen"],
			[.. messages.Where(message => message.Method is not null).Select(message => message.Method!)]
		);

		// The parameters take the protocol's lower-camel member names.
		using var projectOpen = JsonDocument.Parse(messages.Single(message => message.Method == "project/open").BodyJson);

		Assert.AreEqual("file:///C:/Workspace/App.csproj", projectOpen.RootElement.GetProperty("params").GetProperty("projects")[0].GetString());

		Assert.AreSequenceEqual([TestPaths.Root], [.. Assert.ContainsSingle(receivedRoots)]);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_OnARestart_SendsTheSessionStartNotificationsAgain()
	{
		List<ScriptedStartupAttempt> attempts = [];
		int providerCalls = 0;

		LanguageServerClientOptions options = s_defaultClientOptions with
		{
			SessionStartNotificationsProvider = _ =>
			{
				providerCalls++;
				return [new SessionStartNotification("solution/open", new { Solution = "file:///C:/Workspace/App.sln" })];
			},
		};

		using LanguageServerClient client = CreateScriptedStartupClient(attempts, options);

		await CompleteScriptedStartupAsync(
				startTask: client.EnsureStartedAsync(CancellationToken.None),
				attempts,
				attemptIndex: 0,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		Assert.IsTrue(client.TryMarkConnectionUnhealthy(client.ConnectionGeneration));

		await CompleteScriptedStartupAsync(
				startTask: client.EnsureStartedAsync(CancellationToken.None),
				attempts,
				attemptIndex: 1,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		await WaitForWrittenMethodAsync(attempts[0], "workspace/didChangeConfiguration").ConfigureAwait(false);
		await WaitForWrittenMethodAsync(attempts[1], "workspace/didChangeConfiguration").ConfigureAwait(false);

		Assert.AreEqual(2, providerCalls);
		Assert.AreEqual(1, CountSentMethods(attempts[0].Recording.GetWrittenText(), "solution/open"));
		Assert.AreEqual(1, CountSentMethods(attempts[1].Recording.GetWrittenText(), "solution/open"));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheSessionStartNotificationsFactoryThrows_LogsAndStillStarts()
	{
		List<ScriptedStartupAttempt> attempts = [];
		using var logger = new RecordingLogger(LogLevel.Debug);

		LanguageServerClientOptions options = s_defaultClientOptions with
		{
			SessionStartNotificationsProvider = _ => throw new InvalidOperationException("Simulated factory failure."),
		};

		using LanguageServerClient client = CreateScriptedStartupClient(attempts, options, logger: logger.CreateLogger<LanguageServerClient>());

		ScriptedStartupAttempt attempt = await CompleteScriptedStartupAsync(
				startTask: client.EnsureStartedAsync(CancellationToken.None),
				attempts,
				attemptIndex: 0,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		Assert.IsTrue(client.IsReady);

		List<(string? Method, string BodyJson)> messages = await WaitForWrittenMethodAsync(attempt, "workspace/didChangeConfiguration")
			.ConfigureAwait(false);

		Assert.AreSequenceEqual(
			["initialize", "initialized", "workspace/didChangeConfiguration"],
			[.. messages.Where(message => message.Method is not null).Select(message => message.Method!)]
		);

		Assert.Contains(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("session-start notifications", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void SessionStartNotification_RejectsABlankMethod()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new SessionStartNotification(" ", parameters: null));
		Assert.ThrowsExactly<ArgumentNullException>(() => new SessionStartNotification(null!, parameters: null));
	}

	/// <summary>Waits until the scripted connection recorded a message of one method, then returns every recorded message in order.</summary>
	/// <param name="attempt">The scripted startup attempt whose writes are read.</param>
	/// <param name="method">The method that must have been written.</param>
	/// <returns>The written messages, in write order.</returns>
	private static async Task<List<(string? Method, string BodyJson)>> WaitForWrittenMethodAsync(ScriptedStartupAttempt attempt, string method)
	{
		await TestPolling
			.UntilAsync(
				() => attempt.Recording.GetWrittenText().Contains($"\"method\":\"{method}\"", StringComparison.Ordinal),
				TimeSpan.FromSeconds(5),
				$"The client should write {method}."
			)
			.ConfigureAwait(false);

		return JsonRpcFrameTestHelpers.ExtractWrittenMessages(attempt.Recording.GetWrittenText());
	}
}
