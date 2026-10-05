using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	/// <summary>
	/// Drives the real JSON-RPC round trip for the server-to-client messages this client deliberately does not service, so the acknowledgment
	/// contract is pinned on the wire instead of only through log assertions.
	/// </summary>
	[TestMethod]
	public async Task ServerMessage_ForUnservicedRequestsAndNotifications_AnswersWithNullAndNeverFaults()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		using var serverFrameStream = new ScriptedServerFrameStream();
		using var hostOutputStream = new RecordingStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_briefTeardownClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 6,
			process: null,
			serverOutputStream: serverFrameStream,
			serverInputStream: hostOutputStream,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// A server request this client deliberately does not service must still be answered - with a null result,
		// not a method-not-found error - so the server's call never deadlocks. The unsupported notifications
		// must be swallowed without a reply frame.
		serverFrameStream.Push(JsonRpcFrameTestHelpers.BuildRequest(1, "window/workDoneProgress/create", """{"token":"task-1"}"""));
		serverFrameStream.Push(JsonRpcFrameTestHelpers.BuildNotification("telemetry/event", """{"event":"startup"}"""));
		serverFrameStream.Push(JsonRpcFrameTestHelpers.BuildNotification("$/progress", """{"token":"task-1"}"""));
		serverFrameStream.Push(JsonRpcFrameTestHelpers.BuildNotification("$/hello", """{"text":"hello"}"""));

		await TestPolling
			.UntilAsync(
				() => JsonRpcFrameTestHelpers.ExtractWrittenMessages(hostOutputStream.GetWrittenText()).Count > 0,
				TimeSpan.FromSeconds(5),
				"The client should answer the unserviced server request."
			)
			.ConfigureAwait(false);

		// Give an erroneous extra reply frame the chance to be written before asserting exactly one response.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		var responses = JsonRpcFrameTestHelpers
			.ExtractWrittenMessages(hostOutputStream.GetWrittenText())
			.Where(message => message.Method is null)
			.ToList();

		Assert.HasCount(1, responses, hostOutputStream.GetWrittenText());

		using var responseDocument = JsonDocument.Parse(responses[0].BodyJson);

		JsonElement response = responseDocument.RootElement;

		Assert.AreEqual(1, response.GetProperty("id").GetInt32());
		Assert.IsFalse(response.TryGetProperty("error", out _));
		Assert.AreEqual(JsonValueKind.Null, response.GetProperty("result").ValueKind);
		Assert.IsFalse(session.JsonRpc!.Completion.IsFaulted);

		// The acknowledgment is logged as an ignored server message rather than surfaced to the host.
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Debug), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public async Task ServerMessage_RequestWithNullParams_ReachesItsRpcMethodAndIsAnswered()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		using var serverFrameStream = new ScriptedServerFrameStream();
		using var hostOutputStream = new RecordingStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_briefTeardownClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		StartServerMessageDispatcher(client);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 7,
			process: null,
			serverOutputStream: serverFrameStream,
			serverInputStream: hostOutputStream,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		var refreshRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		client.SemanticTokensRefreshRequested += (_, _) => refreshRequested.TrySetResult(true);

		// LuaLS sends its parameterless refresh requests with "params": null, which JSON-RPC allows to mean no parameters.
		serverFrameStream.Push(JsonRpcFrameTestHelpers.BuildRequest(5, "workspace/semanticTokens/refresh", "null"));

		await refreshRequested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		await TestPolling
			.UntilAsync(
				() => JsonRpcFrameTestHelpers.ExtractWrittenMessages(hostOutputStream.GetWrittenText()).Any(message => message.Method is null),
				TimeSpan.FromSeconds(5),
				"The client should answer the refresh request."
			)
			.ConfigureAwait(false);

		(string? _, string bodyJson) = JsonRpcFrameTestHelpers
			.ExtractWrittenMessages(hostOutputStream.GetWrittenText())
			.Single(message => message.Method is null);

		using var responseDocument = JsonDocument.Parse(bodyJson);

		Assert.AreEqual(5, responseDocument.RootElement.GetProperty("id").GetInt32());
		Assert.IsFalse(responseDocument.RootElement.TryGetProperty("error", out _), bodyJson);

		Assert.DoesNotContain(
			log => log.Contains("malformed language server frame", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	/// <summary>
	/// Read stream that yields scripted server frames and then blocks, so the JSON-RPC read loop stays alive while the client writes its reply
	/// instead of observing an end of stream.
	/// </summary>
	private sealed class ScriptedServerFrameStream : Stream
	{
		private readonly System.Threading.Channels.Channel<byte[]> _frames = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();

		private byte[]? _currentFrame;
		private int _position;

		/// <summary>Queues one complete frame for the host to read.</summary>
		/// <param name="frame">The framed payload (header and body).</param>
		public void Push(string frame) => _frames.Writer.TryWrite(Encoding.UTF8.GetBytes(frame));

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (_currentFrame is null || _position >= _currentFrame.Length)
			{
				_currentFrame = await _frames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
				_position = 0;
			}

			int bytesToCopy = Math.Min(buffer.Length, _currentFrame.Length - _position);
			_currentFrame.AsMemory(_position, bytesToCopy).CopyTo(buffer);
			_position += bytesToCopy;
			return bytesToCopy;
		}

		public override void Flush() { }

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
