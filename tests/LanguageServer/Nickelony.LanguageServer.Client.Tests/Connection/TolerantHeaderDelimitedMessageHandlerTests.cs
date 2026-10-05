using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;
using StreamJsonRpc.Reflection;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Covers <see cref="TolerantHeaderDelimitedMessageHandler"/>: a frame the formatter cannot decode is dropped so a single malformed server frame
/// cannot fault the connection, while valid frames continue to be read.
/// </summary>
[TestClass]
public sealed class TolerantHeaderDelimitedMessageHandlerTests
{
	private static readonly LanguageServerClientOptions s_clientOptions = LanguageServerClientOptions.Default with
	{
		ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
		DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
	};

	private static readonly StdioLanguageServerTransport s_exampleTransport = new("example-language-server.exe");

	[TestMethod]
	public async Task ReadAsync_SkipsUndecodableFramesAndContinuesWithTheNextFrame()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_clientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		// Two frames the System.Text.Json formatter cannot decode (a request and a notification, both with a string
		// "params"), followed by a valid frame that must still be read once the malformed frames are dropped.
		string payload =
			JsonRpcFrameTestHelpers.BuildRequest(1, "workspace/configuration", "\"text\"")
			+ JsonRpcFrameTestHelpers.BuildRequest(2, "window/logMessage", "\"text\"")
			+ JsonRpcFrameTestHelpers.BuildRequest(3, "textDocument/diagnostic", """{"textDocument":{"uri":"file:///C:/Workspace/test.ext"}}""");

		using var readStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
		using var writeStream = new MemoryStream();

		using HeaderDelimitedMessageHandler handler = client.Composition.SessionFactory.CreateMessageHandler(
			writeStream,
			readStream,
			connectionGeneration: 1
		);

		JsonRpcMessage? message = await handler.ReadAsync(CancellationToken.None);

		Assert.IsInstanceOfType<JsonRpcRequest>(message);
		Assert.AreEqual("textDocument/diagnostic", ((JsonRpcRequest)message!).Method);

		Assert.IsGreaterThanOrEqualTo(
			2,

			logger.Logs.Count(log =>
				log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("malformed language server frame", StringComparison.Ordinal)
			),

			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task ReadAsync_NullParams_ReadsEachFrameAsAMessageWithoutParameters()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_clientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		// JSON-RPC allows a null "params" to mean no parameters, so every such frame is read, a request and a notification alike.
		string payload =
			JsonRpcFrameTestHelpers.BuildRequest(1, "workspace/semanticTokens/refresh", "null")
			+ JsonRpcFrameTestHelpers.BuildNotification("window/logMessage", "null")
			+ JsonRpcFrameTestHelpers.BuildRequest(2, "workspace/diagnostic/refresh", " null ");

		using var readStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
		using var writeStream = new MemoryStream();

		using HeaderDelimitedMessageHandler handler = client.Composition.SessionFactory.CreateMessageHandler(
			writeStream,
			readStream,
			connectionGeneration: 1
		);

		List<string?> methods = [];

		while (await handler.ReadAsync(CancellationToken.None) is JsonRpcRequest request)
		{
			methods.Add(request.Method);
			Assert.AreEqual(0, request.ArgumentCount);

			// The System.Text.Json formatter defers releasing the read buffer, so a direct reader releases it before the next frame.
			((IJsonRpcMessageBufferManager)handler).DeserializationComplete(request);
		}

		Assert.AreSequenceEqual(["workspace/semanticTokens/refresh", "window/logMessage", "workspace/diagnostic/refresh"], methods);

		Assert.DoesNotContain(
			log => log.Contains("malformed language server frame", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task ReadAsync_ThrowsWhenDecodeKeepsFailingWithoutProgress()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_clientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		// A single malformed frame is dropped, but a run past the consecutive-failure bound cannot be a stream of
		// otherwise-decodable frames, so the handler faults instead of spinning on the reader.
		string payload = string.Empty;

		for (int index = 0; index < 64; index++)
			payload += JsonRpcFrameTestHelpers.BuildRequest(index + 1, "workspace/configuration", "\"text\"");

		using var readStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
		using var writeStream = new MemoryStream();

		using HeaderDelimitedMessageHandler handler = client.Composition.SessionFactory.CreateMessageHandler(
			writeStream,
			readStream,
			connectionGeneration: 1
		);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.ReadAsync(CancellationToken.None).AsTask());
	}

	[TestMethod]
	public async Task WriteAsync_WhenTheStreamWriteFails_EndsTheDrainAndFailsTheNextWrite()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_clientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		using var readStream = new MemoryStream();
		var writeStream = new MemoryStream();

		// A disposed stream fails every write, so the copy of the first message fails.
		writeStream.Dispose();

		using TolerantHeaderDelimitedMessageHandler handler = client.Composition.SessionFactory.CreateMessageHandler(
			writeStream,
			readStream,
			connectionGeneration: 1
		);

		await handler.WriteAsync(new JsonRpcRequest { Method = "first" }, CancellationToken.None);
		await handler.OutboundDrained.WaitAsync(TestPolling.DefaultTimeout);

		await Assert.ThrowsAsync<ObjectDisposedException>(() =>
			handler.WriteAsync(new JsonRpcRequest { Method = "second" }, CancellationToken.None).AsTask()
		);

		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("Writing to the input stream of language server connection generation 1 failed"),

			logger.Logs
		);
	}

	[TestMethod]
	public async Task Dispose_LeavesBothStreamsOpenForTheConnectionToRelease()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_clientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		using var readStream = new MemoryStream();
		using var writeStream = new MemoryStream();

		TolerantHeaderDelimitedMessageHandler handler = client.Composition.SessionFactory.CreateMessageHandler(
			writeStream,
			readStream,
			connectionGeneration: 1
		);

		await handler.WriteAsync(new JsonRpcRequest { Method = "exit" }, CancellationToken.None);
		await handler.DisposeAsync();
		await handler.OutboundDrained.WaitAsync(TestPolling.DefaultTimeout);

		Assert.IsTrue(readStream.CanRead);
		Assert.IsTrue(writeStream.CanWrite);
		Assert.Contains("\"method\":\"exit\"", System.Text.Encoding.UTF8.GetString(writeStream.ToArray()));
	}

	[TestMethod]
	public async Task ReadAsync_ReturnsNullAtEndOfStream()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_clientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		string payload = JsonRpcFrameTestHelpers.BuildRequest(1, "textDocument/diagnostic", "{}");

		using var readStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
		using var writeStream = new MemoryStream();

		using HeaderDelimitedMessageHandler handler = client.Composition.SessionFactory.CreateMessageHandler(
			writeStream,
			readStream,
			connectionGeneration: 1
		);

		JsonRpcMessage? message = await handler.ReadAsync(CancellationToken.None);

		Assert.IsNotNull(message);

		// The System.Text.Json formatter defers releasing the read buffer to the JsonRpc loop, so a caller that
		// reads frames directly must release the buffer before reading the next frame.
		((IJsonRpcMessageBufferManager)handler).DeserializationComplete(message);

		Assert.IsNull(await handler.ReadAsync(CancellationToken.None));
	}
}
