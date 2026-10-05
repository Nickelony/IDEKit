using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task Handshake_WithScriptedResponses_PublishesCapabilitiesAndPushesSettingsInOrder()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,

			options: new LanguageServerClientOptions
			{
				SettingsProvider = static () => CreateExampleSettings("4.0"),
				ShutdownRequestTimeout = s_briefShutdownRequestTimeout,
			}
		);

		using var recordingStream = new RecordingStream();
		using var responseStream = new DeferredPersistentJsonRpcResponseStream();

		// The helper's first stream is the stream the client reads from; the second stream is the one it writes to.
		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 12,
			process: null,
			serverOutputStream: responseStream,
			serverInputStream: recordingStream,
			startListening: true
		);

		SetActiveSession(client, session);

		Task<bool> handshakeTask = client.Composition.Handshake.RunAsync(session, CancellationToken.None, CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(recordingStream).ConfigureAwait(false);

		responseStream.SetPayload(
			JsonRpcFrameTestHelpers.BuildResultResponse(
				requestId,

				"""
				{
				  "capabilities": {
				    "textDocumentSync": 2,
				    "referencesProvider": true,
				    "semanticTokensProvider": {
				      "full": { "delta": true },
				      "legend": {
				        "tokenTypes": [ "keyword" ],
				        "tokenModifiers": [ "declaration" ]
				      }
				    }
				  }
				}
				"""
			)
		);

		Assert.IsTrue(await handshakeTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));

		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsReferences);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.AreSequenceEqual(["keyword"], [.. client.NegotiatedCapabilities.SemanticTokenTypes]);
		Assert.AreSequenceEqual(["declaration"], [.. client.NegotiatedCapabilities.SemanticTokenModifiers]);

		await TestPolling
			.UntilAsync(
				() => recordingStream.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
				TimeSpan.FromSeconds(5),
				"The handshake should push the settings notification after initialization completes."
			)
			.ConfigureAwait(false);

		string writtenText = recordingStream.GetWrittenText();
		List<(string? Method, string BodyJson)> writtenMessages = JsonRpcFrameTestHelpers.ExtractWrittenMessages(writtenText);

		Assert.AreSequenceEqual(
			["initialize", "initialized", "workspace/didChangeConfiguration"],
			[.. writtenMessages.Where(message => message.Method is not null).Select(message => message.Method!)]
		);

		(string? Method, string BodyJson) initializeMessage = writtenMessages.Single(message => message.Method == "initialize");
		using var initializeDocument = JsonDocument.Parse(initializeMessage.BodyJson);

		JsonElement initializeParameters = initializeDocument.RootElement.GetProperty("params");

		Assert.IsTrue(initializeParameters.TryGetProperty("processId", out JsonElement processIdElement));
		Assert.AreEqual(JsonValueKind.Number, processIdElement.ValueKind);
		Assert.AreEqual(Environment.ProcessId, processIdElement.GetInt32());
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(TestPaths.Root), initializeParameters.GetProperty("rootUri").GetString());
		Assert.AreEqual(JsonValueKind.Object, initializeParameters.GetProperty("capabilities").ValueKind);

		(string? Method, string BodyJson) configurationMessage = writtenMessages.Single(message =>
			message.Method == "workspace/didChangeConfiguration"
		);

		using var configurationDocument = JsonDocument.Parse(configurationMessage.BodyJson);
		JsonElement settings = configurationDocument.RootElement.GetProperty("params").GetProperty("settings");

		Assert.AreEqual("4.0", settings.GetProperty("example").GetProperty("runtime").GetProperty("version").GetString());
	}

	[TestMethod]
	public async Task Handshake_WithPascalCaseSettingsKeys_PushesTheKeysAsSpelled()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,

			options: new LanguageServerClientOptions
			{
				SettingsProvider = static () =>
					new JsonObject { ["Example"] = new JsonObject { ["Runtime"] = new JsonObject { ["Version"] = "4.0" } } },

				ShutdownRequestTimeout = s_briefShutdownRequestTimeout,
			}
		);

		using var recordingStream = new RecordingStream();
		using var responseStream = new DeferredPersistentJsonRpcResponseStream();

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 12,
			process: null,
			serverOutputStream: responseStream,
			serverInputStream: recordingStream,
			startListening: true
		);

		SetActiveSession(client, session);

		Task<bool> handshakeTask = client.Composition.Handshake.RunAsync(session, CancellationToken.None, CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(recordingStream).ConfigureAwait(false);

		responseStream.SetPayload(
			JsonRpcFrameTestHelpers.BuildResultResponse(
				requestId,

				"""
				{
				  "capabilities": { "textDocumentSync": 2 }
				}
				"""
			)
		);

		Assert.IsTrue(await handshakeTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));

		await TestPolling
			.UntilAsync(
				() => recordingStream.GetWrittenText().Contains("workspace/didChangeConfiguration", StringComparison.Ordinal),
				TimeSpan.FromSeconds(5),
				"The handshake should push the settings notification after initialization completes."
			)
			.ConfigureAwait(false);

		List<(string? Method, string BodyJson)> writtenMessages = JsonRpcFrameTestHelpers.ExtractWrittenMessages(recordingStream.GetWrittenText());

		(string? Method, string BodyJson) configurationMessage = writtenMessages.Single(message =>
			message.Method == "workspace/didChangeConfiguration"
		);

		using var configurationDocument = JsonDocument.Parse(configurationMessage.BodyJson);
		JsonElement settings = configurationDocument.RootElement.GetProperty("params").GetProperty("settings");

		// The client applies no naming policy to a JSON document, so a section such as LuaLS's "Lua" keeps its casing.
		Assert.AreEqual("4.0", settings.GetProperty("Example").GetProperty("Runtime").GetProperty("Version").GetString());
	}

	[TestMethod]
	public async Task Handshake_WhenInitializeResultIsNull_FailsWithTheMissingCapabilitiesDiagnostic()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,

			options: LanguageServerClientOptions.Default with
			{
				ShutdownRequestTimeout = s_briefShutdownRequestTimeout,
			}
		);

		using var recordingStream = new RecordingStream();
		using var responseStream = new DeferredPersistentJsonRpcResponseStream();

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 12,
			process: null,
			serverOutputStream: responseStream,
			serverInputStream: recordingStream,
			startListening: true
		);

		SetActiveSession(client, session);

		Task<bool> handshakeTask = client.Composition.Handshake.RunAsync(session, CancellationToken.None, CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(recordingStream).ConfigureAwait(false);
		responseStream.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(requestId, "null"));

		NotSupportedException exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(() => handshakeTask).ConfigureAwait(false);

		Assert.IsTrue(exception.Message.Contains("server capabilities", StringComparison.OrdinalIgnoreCase));
	}
}
