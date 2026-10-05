using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	private const string DidChangeConfigurationMethod = "workspace/didChangeConfiguration";

	[TestMethod]
	public async Task PushSettingsAsync_WhenTheConnectionIsReady_SendsTheCurrentSettingsAndRefreshesTheSnapshot()
	{
		string version = "1.0";

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: new LanguageServerClientOptions { SettingsProvider = () => CreateExampleSettings(version) }
		);

		using var recordingStream = new RecordingStream();

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 3,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: recordingStream
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		await client.PushSettingsAsync(CancellationToken.None).ConfigureAwait(false);

		version = "2.0";

		// A host change alone does not reach the answers; only a push reads the host's settings again.
		Assert.AreEqual("1.0", ReadExampleRuntimeVersion(rpcTarget));

		await client.PushSettingsAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual("2.0", ReadExampleRuntimeVersion(rpcTarget));

		List<(string? Method, string BodyJson)> pushes = await WaitForWrittenMessagesAsync(recordingStream, DidChangeConfigurationMethod, count: 2)
			.ConfigureAwait(false);

		Assert.AreSequenceEqual(["1.0", "2.0"], [.. pushes.Select(static push => ReadPushedRuntimeVersion(push.BodyJson))]);
	}

	[TestMethod]
	public async Task PushSettingsAsync_WhenTheSettingsProviderThrows_LogsAWarningAndSendsNothing()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: new LanguageServerClientOptions { SettingsProvider = static () => throw new InvalidOperationException("settings failure") },
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		using var recordingStream = new RecordingStream();

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 4,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: recordingStream
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		await client.PushSettingsAsync(CancellationToken.None).ConfigureAwait(false);

		// The connection writes in send order, so once the sentinel is written a push that had been sent would be written too.
		await client.SendNotificationAsync("test/sentinel", parameters: null, CancellationToken.None).ConfigureAwait(false);
		await WaitForWrittenMessagesAsync(recordingStream, "test/sentinel", count: 1).ConfigureAwait(false);

		Assert.IsEmpty(
			JsonRpcFrameTestHelpers
				.ExtractWrittenMessages(recordingStream.GetWrittenText())
				.Where(static message => message.Method == DidChangeConfigurationMethod)
		);

		Assert.IsTrue(client.IsReady);

		Assert.Contains(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("settings provider failed", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task PushSettingsAsync_WhenClientIsNotReady_ThrowsConnectionUnavailableWithoutRunningTheSettingsProvider()
	{
		int settingsProviderCallCount = 0;

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,

			options: new LanguageServerClientOptions
			{
				SettingsProvider = () =>
				{
					Interlocked.Increment(ref settingsProviderCallCount);
					return new JsonObject();
				},
			}
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		Assert.IsFalse(client.IsReady);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () =>
				await client.PushSettingsAsync(CancellationToken.None).ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		Assert.AreEqual(0, settingsProviderCallCount);
	}

	[TestMethod]
	public async Task PushSettingsAsync_AfterDisposal_ThrowsObjectDisposedException()
	{
		var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		client.Dispose();

		await Assert
			.ThrowsExactlyAsync<ObjectDisposedException>(async () => await client.PushSettingsAsync(CancellationToken.None).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	private static string? ReadExampleRuntimeVersion(ClientRpcTarget rpcTarget)
	{
		JsonElement?[] response = rpcTarget.WorkspaceConfiguration(
			new WorkspaceConfigurationParams([new WorkspaceConfigurationItemPayload("example.runtime")])
		);

		return JsonSerializer.SerializeToElement(response[0]).GetProperty("version").GetString();
	}

	private static string? ReadPushedRuntimeVersion(string messageBodyJson)
	{
		using var document = JsonDocument.Parse(messageBodyJson);

		return document
			.RootElement.GetProperty("params")
			.GetProperty("settings")
			.GetProperty("example")
			.GetProperty("runtime")
			.GetProperty("version")
			.GetString();
	}

	/// <summary>Waits until the connection has written at least <paramref name="count"/> messages of one method and returns them.</summary>
	private static async Task<List<(string? Method, string BodyJson)>> WaitForWrittenMessagesAsync(
		RecordingStream recordingStream,
		string method,
		int count
	)
	{
		List<(string? Method, string BodyJson)> messages = [];

		await TestPolling
			.UntilAsync(
				() =>
				{
					messages =
					[
						.. JsonRpcFrameTestHelpers
							.ExtractWrittenMessages(recordingStream.GetWrittenText())
							.Where(message => message.Method == method),
					];

					return messages.Count >= count;
				},

				TestPolling.DefaultTimeout,
				$"The connection should have written {count} '{method}' message(s)."
			)
			.ConfigureAwait(false);

		return messages;
	}
}
