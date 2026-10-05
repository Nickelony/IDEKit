using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nickelony.Infrastructure;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	// The settings document of the form { "example": { "runtime": { "version": ... } } }; the client sends a JSON document's member names as spelled.
	private static JsonObject CreateExampleSettings(string version) =>
		new() { ["example"] = new JsonObject { ["runtime"] = new JsonObject { ["version"] = version } } };

	[TestMethod]
	public async Task SendNotificationAsync_WhenDidChangeConfigurationIsAlreadyCanceled_PreservesCachedSettingsSnapshot()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: new LanguageServerClientOptions { SettingsProvider = static () => CreateExampleSettings("4.0") }
		);

		using var cancellationSource = new CancellationTokenSource();

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 11,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);
		cancellationSource.Cancel();

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		JsonElement?[] initialResponse = rpcTarget.WorkspaceConfiguration(
			new WorkspaceConfigurationParams([new WorkspaceConfigurationItemPayload("Example.runtime")])
		);

		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(initialResponse[0]).GetProperty("version").GetString());

		await Assert
			.ThrowsExactlyAsync<OperationCanceledException>(async () =>
				await client
					.SendNotificationAsync(
						"workspace/didChangeConfiguration",
						new DidChangeConfigurationParams(new { Example = new { Runtime = new { Version = "4.1" } } }),
						cancellationSource.Token
					)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		JsonElement?[] responseAfterFailure = rpcTarget.WorkspaceConfiguration(
			new WorkspaceConfigurationParams([new WorkspaceConfigurationItemPayload("Example.runtime")])
		);

		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(responseAfterFailure[0]).GetProperty("version").GetString());
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(11L, client.ConnectionGeneration);
	}

	[TestMethod]
	public async Task SendNotificationAsync_WhenDidChangeConfigurationPayloadHasNoSettings_WritesThePayloadUnchanged()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: new LanguageServerClientOptions { SettingsProvider = static () => CreateExampleSettings("4.0") }
		);

		using var recordingStream = new RecordingStream();

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: recordingStream
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// A payload that is neither a DidChangeConfigurationParams nor carries a settings member cannot be
		// normalized, so the fallback must forward the host's object untouched instead of wrapping or dropping it.
		await client
			.SendNotificationAsync("workspace/didChangeConfiguration", new { UnrelatedMember = "value" }, CancellationToken.None)
			.ConfigureAwait(false);

		// StreamJsonRpc completes the notification task once local dispatch is handed off, before the connection has
		// necessarily flushed the bytes, so the write is polled for instead of read synchronously.
		await TestPolling
			.UntilAsync(
				() => recordingStream.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
				TestPolling.DefaultTimeout,
				"The fallback notification should reach the connection unchanged."
			)
			.ConfigureAwait(false);

		List<(string? Method, string BodyJson)> messages = JsonRpcFrameTestHelpers.ExtractWrittenMessages(recordingStream.GetWrittenText());
		(string? Method, string BodyJson) message = messages.Single(message => message.Method == "workspace/didChangeConfiguration");

		using var document = JsonDocument.Parse(message.BodyJson);

		JsonElement parameters = document.RootElement.GetProperty("params");

		Assert.IsTrue(parameters.TryGetProperty("UnrelatedMember", out JsonElement value));
		Assert.AreEqual("value", value.GetString());
		Assert.IsFalse(parameters.TryGetProperty("settings", out _));
	}

	[TestMethod]
	public void WorkspaceConfiguration_WhenTheSettingsProviderThrows_ReturnsNullValuesAndLogsWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: new LanguageServerClientOptions { SettingsProvider = static () => throw new InvalidOperationException("settings failure") },
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		JsonElement?[] response = rpcTarget.WorkspaceConfiguration(
			new WorkspaceConfigurationParams([
				new WorkspaceConfigurationItemPayload("Example"),
				new WorkspaceConfigurationItemPayload("Example.runtime"),
			])
		);

		Assert.HasCount(2, response);
		Assert.IsNull(response[0]);
		Assert.IsNull(response[1]);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("workspace/configuration", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("returning null values", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task SendNotificationAsync_WithTypedPascalCaseSettings_PushesAndServesTheSameCasing()
	{
		using var serverInputStream = new RecordingStream();
		using var serverOutputStream = new DeferredPersistentJsonRpcResponseStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_briefTeardownClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream,
			serverInputStream,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		await client
			.SendNotificationAsync(
				"workspace/didChangeConfiguration",
				new DidChangeConfigurationParams(new PascalCaseSettings { Example = new PascalCaseSettingsSection { MaxPreload = 4 } }),
				CancellationToken.None
			)
			.ConfigureAwait(false);

		// The pushed notification and the workspace/configuration answers must use the same member casing.
		string writtenText = await WaitForWrittenTextAsync(serverInputStream, "\"maxPreload\":4").ConfigureAwait(false);

		Assert.IsFalse(writtenText.Contains("\"MaxPreload\"", StringComparison.Ordinal), writtenText);

		JsonElement?[] response = rpcTarget.WorkspaceConfiguration(
			new WorkspaceConfigurationParams([
				new WorkspaceConfigurationItemPayload("example"),
				new WorkspaceConfigurationItemPayload("example.maxPreload"),
			])
		);

		Assert.HasCount(2, response);
		Assert.AreEqual(4, JsonSerializer.SerializeToElement(response[1]).GetInt32());
		Assert.AreEqual(4, JsonSerializer.SerializeToElement(response[0]).GetProperty("maxPreload").GetInt32());
	}

	private sealed class PascalCaseSettings
	{
		public PascalCaseSettingsSection? Example { get; init; }
	}

	private sealed class PascalCaseSettingsSection
	{
		public int MaxPreload { get; init; }
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void WorkspaceFolders_ReturnsDriveRootPathWhenWorkspaceRootIsDriveRoot()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [@"C:\"],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		WorkspaceFolder[] workspaceFolders = rpcTarget.WorkspaceFolders();

		Assert.HasCount(1, workspaceFolders);
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(@"C:\"), workspaceFolders[0].Uri);

		// A drive root has no file name; the folder name is the root path itself, not the misleading "C:".
		Assert.AreEqual(@"C:\", workspaceFolders[0].Name);
	}

	[TestMethod]
	public void WorkspaceConfiguration_StaleConnectionGeneration_ReturnsNullValues()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: new LanguageServerClientOptions { SettingsProvider = static () => CreateExampleSettings("5.4") }
		);

		ConnectionSession staleSession = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession activeSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, activeSession);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(staleSession));

		JsonElement?[] response = rpcTarget.WorkspaceConfiguration(
			new WorkspaceConfigurationParams([
				new WorkspaceConfigurationItemPayload("Example"),
				new WorkspaceConfigurationItemPayload("Example.runtime"),
			])
		);

		Assert.HasCount(2, response);
		Assert.IsNull(response[0]);
		Assert.IsNull(response[1]);
	}

	[TestMethod]
	public void WorkspaceFolders_StaleConnectionGeneration_ReturnsEmptyArray()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession staleSession = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession activeSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, activeSession);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(staleSession));
		WorkspaceFolder[] workspaceFolders = rpcTarget.WorkspaceFolders();

		Assert.IsEmpty(workspaceFolders);
	}

	[TestMethod]
	public void WorkspaceFolders_MultipleRoots_ReturnsEveryAdvertisedFolder()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root, TestPaths.SecondaryRoot],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		WorkspaceFolder[] workspaceFolders = rpcTarget.WorkspaceFolders();

		Assert.HasCount(2, workspaceFolders);
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(LocalPathRules.Normalize(TestPaths.Root)), workspaceFolders[0].Uri);
		Assert.AreEqual("Workspace", workspaceFolders[0].Name);
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(LocalPathRules.Normalize(TestPaths.SecondaryRoot)), workspaceFolders[1].Uri);
		Assert.AreEqual("Secondary", workspaceFolders[1].Name);
	}

	[TestMethod]
	public void Constructor_WithNullWorkspaceRoots_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new LanguageServerClient(workspaceRootDirectoryPaths: null!, transport: s_exampleTransport, hooks: null, options: s_defaultClientOptions)
		);
	}

	[TestMethod]
	public void Constructor_WithWhitespaceWorkspaceRootEntry_ThrowsArgumentException()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new LanguageServerClient(
				workspaceRootDirectoryPaths: ["   "],
				transport: s_exampleTransport,
				hooks: null,
				options: s_defaultClientOptions
			)
		);
	}

	[TestMethod]
	public void Constructor_WithARelativeWorkspaceRootEntry_NamesTheConstructorParameter()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			new LanguageServerClient(
				workspaceRootDirectoryPaths: ["relative/root"],
				transport: s_exampleTransport,
				hooks: null,
				options: s_defaultClientOptions
			)
		);

		Assert.AreEqual("workspaceRootDirectoryPaths", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_WithNormalizedDuplicateWorkspaceRoots_ThrowsArgumentException()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new LanguageServerClient(
				workspaceRootDirectoryPaths: [TestPaths.Root, TestPaths.WithAlternateSeparators(TestPaths.Root)],
				transport: s_exampleTransport,
				hooks: null,
				options: s_defaultClientOptions
			)
		);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WithTheDefaultSettingsProvider_PushesAnEmptyConfigurationPayload()
	{
		List<ScriptedStartupAttempt> attempts = [];

		await using LanguageServerClient client = CreateScriptedStartupClient(attempts);

		Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

		ScriptedStartupAttempt attempt = await CompleteScriptedStartupAsync(
				startTask,
				attempts,
				attemptIndex: 0,
				initializeResultJson: ReadyCapabilitiesJson
			)
			.ConfigureAwait(false);

		// The configuration push is written after the handshake completes, so the recording is waited on rather
		// than sampled: the settings body is the later part of that write, so waiting for it also proves the
		// notification frame was written. Asserting immediately raced the writer under load.
		string writtenText = await WaitForWrittenTextAsync(attempt.Recording, "\"settings\":{}").ConfigureAwait(false);

		// A host that never configures settings must still push a valid (empty) configuration so the server does
		// not have to guess whether the client supports the notification.
		Assert.IsTrue(writtenText.Contains("\"method\":\"workspace/didChangeConfiguration\"", StringComparison.Ordinal), writtenText);
		Assert.IsTrue(writtenText.Contains("\"settings\":{}", StringComparison.Ordinal), writtenText);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WithASettingsProviderThatReturnsNull_FailsStartupWithArgumentNullException()
	{
		List<ScriptedStartupAttempt> attempts = [];

		await using LanguageServerClient client = CreateScriptedStartupClient(
			attempts,
			new LanguageServerClientOptions { SettingsProvider = static () => null! }
		);

		Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

		await TestPolling
			.UntilAsync(() => attempts.Count > 0, TestPolling.DefaultTimeout, "Timed out waiting for the scripted startup attempt to begin.")
			.ConfigureAwait(false);

		ScriptedStartupAttempt attempt = attempts[0];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);

		attempt.Responses.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(requestId, ReadyCapabilitiesJson));

		// The settings push is part of the handshake, so a provider that violates its non-null contract must fail
		// the startup attempt with the contract exception instead of publishing readiness with a null payload.
		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsInstanceOfType<ArgumentNullException>(client.LastStartupException);
		Assert.IsFalse(client.IsReady);
	}
}
