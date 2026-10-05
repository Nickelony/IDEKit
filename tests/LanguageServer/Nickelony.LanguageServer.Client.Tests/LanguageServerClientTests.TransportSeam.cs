using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Covers the pluggable transport seam: the client must open every connection through the transport
/// it was constructed with, adopt the returned connection, and release it during teardown.
/// </summary>
public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task EnsureStartedAsync_WithACustomTransportAndNoExecutable_CompletesTheHandshakeAndDisposesTheConnectionOnTeardown()
	{
		using var serverOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();
		var connection = new RecordingLanguageServerConnection(serverOutputStream, serverInputStream);

		// An in-memory transport names no executable at all: the client hands it only the connect request.
		var transport = new RecordingLanguageServerTransport((_, _) => Task.FromResult<ILanguageServerConnection>(connection));

		var client = new LanguageServerClient(workspaceRootDirectoryPaths: [TestPaths.Root], transport, hooks: null, options: CreateSeamOptions());

		try
		{
			Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

			int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
			serverOutputStream.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(requestId, ReadyCapabilitiesJson));

			Assert.IsTrue(await startTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
			Assert.IsTrue(client.IsReady);
			Assert.AreEqual(1L, client.ConnectionGeneration);

			string writtenText = serverInputStream.GetWrittenText();

			// The handshake must have traveled through the connection the transport returned.
			Assert.IsTrue(writtenText.Contains("\"method\":\"initialize\"", StringComparison.Ordinal), writtenText);
			Assert.AreEqual(1, transport.ConnectCount);
			Assert.IsNotNull(transport.LastRequest?.Logger);

			Assert.AreEqual(0, connection.DisposeCount);
		}
		finally
		{
			await client.DisposeAsync().ConfigureAwait(false);
		}

		// The client owns the connection once the transport returns it, so teardown has to release it exactly once.
		Assert.AreEqual(1, connection.DisposeCount);
	}

	[TestMethod]
	public async Task DisposeAsync_WithAConnectionButNoProcess_SendsTheGracefulShutdownSequence()
	{
		using var responseStream = new GatedReadStream();
		using var recordingStream = new RecordingStream();
		var connection = new RecordingLanguageServerConnection(responseStream, recordingStream);

		// The unanswered shutdown request alone spends most of the seam options' disposal budget, so a loaded run could cut
		// the session teardown short before the exit notification is written; the wider budget leaves the whole sequence inside it.
		LanguageServerClientOptions options = CreateSeamOptions() with
		{
			DisposeWaitTimeout = TestPolling.DefaultTimeout,
		};

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: responseStream,
			serverInputStream: recordingStream,
			startListening: true,
			connection
		);

		SetActiveSession(client, session);

		await client.DisposeAsync().ConfigureAwait(false);

		string writtenPayload = recordingStream.GetWrittenText();

		// A transport-supplied connection is the liveness signal of a session without a process, so the protocol
		// shutdown sequence must still reach the server before the connection is released.
		Assert.IsTrue(writtenPayload.Contains("\"method\":\"shutdown\"", StringComparison.Ordinal), writtenPayload);
		Assert.IsTrue(writtenPayload.Contains("\"method\":\"exit\"", StringComparison.Ordinal), writtenPayload);
		Assert.AreEqual(1, connection.DisposeCount);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheTransportFails_NamesTheTransportInTheStartupFailureLog()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		string missingExecutablePath = Path.Combine(Path.GetTempPath(), $"missing-language-server-{Guid.NewGuid():N}.exe");

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new StdioLanguageServerTransport(missingExecutablePath),
			hooks: null,
			options: CreateSeamOptions(),
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		Assert.IsFalse(await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false));

		// The client knows no executable; the stdio transport names its own, so the failure log still says which server failed.
		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("Failed to start the language server", StringComparison.Ordinal)
				&& log.Contains(missingExecutablePath, StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	/// <summary>Builds client options with the short shutdown budgets the seam tests rely on.</summary>
	/// <returns>The configured options.</returns>
	private static LanguageServerClientOptions CreateSeamOptions()
	{
		return LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
		};
	}
}
