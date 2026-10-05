using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed partial class LanguageServerClientTests
{
	// Shared defaults used by process-lifetime tests: shorter timeouts keep the real-process tail cheap without
	// weakening the assertions (a delayed JSON-RPC ack still completes well inside these budgets).
	private static readonly LanguageServerClientOptions s_defaultClientOptions = LanguageServerClientOptions.Default with
	{
		ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
		DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
	};

	// A test whose session comes from CreateConnectionSession and that never looks at teardown messaging shortens the shutdown wait: the session
	// has a connection, so disposal still runs the protocol shutdown against a peer that never answers.
	private static readonly TimeSpan s_briefShutdownRequestTimeout = TimeSpan.FromMilliseconds(50);

	// The transport of the tests that install their session through CreateConnectionSession; its executable does not exist, so a connect fails.
	private static readonly StdioLanguageServerTransport s_exampleTransport = new("example-language-server.exe");

	private static readonly LanguageServerClientOptions s_briefTeardownClientOptions = s_defaultClientOptions with
	{
		ShutdownRequestTimeout = s_briefShutdownRequestTimeout,
	};

	private static bool HasAbandonedNotificationLog(RecordingLogger logger)
	{
		return logger.Logs.Any(log =>
			log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("abandoned language server connection task", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated abandoned notification failure.", StringComparison.Ordinal)
		);
	}

	[TestMethod]
	public async Task DisposeAsync_WaitsForDetachedSessionCleanup()
	{
		var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		var queuedCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		FailedSessionTestHelpers.QueueBlockedDisposal(client.Composition.FailedSessionDispatcher, queuedCleanup.Task);

		Task disposeTask = client.DisposeAsync().AsTask();

		Task completedTask = await Task.WhenAny(disposeTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreNotSame(disposeTask, completedTask);

		queuedCleanup.TrySetResult(true);
		await disposeTask.ConfigureAwait(false);
	}

	private static ConnectionSession CreateConnectionSession(
		LanguageServerClient client,
		long generation,
		Process? process,
		Stream serverOutputStream,
		Stream serverInputStream,
		bool startListening = false,
		ILanguageServerConnection? connection = null
	)
	{
		var session = new ConnectionSession(connectionGeneration: generation, process, readStream: serverOutputStream, writeStream: serverInputStream)
		{
			Connection = connection ?? new RecordingLanguageServerConnection(serverOutputStream, serverInputStream, process),
			MessageHandler = client.Composition.SessionFactory.CreateMessageHandler(serverInputStream, serverOutputStream, generation),
			RpcTarget = client.Composition.SessionFactory.CreateRpcTarget(generation),
		};

		JsonRpc jsonRpc = client.Composition.SessionFactory.CreateJsonRpc(session);
		session.JsonRpc = jsonRpc;
		session.RpcCompletionTask = jsonRpc.Completion;

		if (startListening)
			jsonRpc.StartListening();

		return session;
	}

	private static ClientRpcTarget CreateRpcTarget(LanguageServerClient client, long generation = 0) =>
		client.Composition.SessionFactory.CreateRpcTarget(generation);

	private static void SetActiveSession(LanguageServerClient client, ConnectionSession session) =>
		client.Composition.State.SetActiveSession(session);

	private static void SetReadyState(LanguageServerClient client, bool isReady)
	{
		if (isReady)
			client.Composition.State.MarkCapabilityReadyForGeneration(client.ConnectionGeneration);
		else
			client.Composition.State.MarkCapabilityNotReadyForGeneration(client.ConnectionGeneration);
	}

	private static void CaptureServerCapabilities(LanguageServerClient client, InitializeResponse response) =>
		client.Composition.State.CaptureServerCapabilitiesForGeneration(client.ConnectionGeneration, response);

	private static long GetConnectionGeneration(ConnectionSession session) => session.ConnectionGeneration;

	private static void RecordStandardErrorLine(ConnectionSession session, string line) => session.RecordStandardErrorLine(line);

	private static InitializeResponse DeserializeInitializeResponse(string json)
	{
		return JsonSerializer.Deserialize<InitializeResponse>(json)
			?? throw new InvalidOperationException("Failed to deserialize the initialize response test payload.");
	}

	private static PublishDiagnosticsParams CreateDiagnosticsParameters(string uri, string message)
	{
		return new(
			uri,
			Version: null,

			Diagnostics:
			[
				new DiagnosticPayload(
					Range: new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 1)),
					Severity: null,
					message,
					Source: null,
					Code: null
				),
			]
		);
	}

	private static async Task WaitForStartupCancellationAsync(TaskCompletionSource<bool> sessionActivated, CancellationToken cancellationToken)
	{
		sessionActivated.TrySetResult(true);
		await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
	}

	// Holds a simulated transport until the caller cancels, then reports the cancellation as the plain
	// OperationCanceledException the startup contract surfaces: Task.Delay alone would surface TaskCanceledException,
	// which the exact-type assertions distinguish.
	private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) { }

		cancellationToken.ThrowIfCancellationRequested();
	}

	private static async Task AssertFaultedOrCanceledAsync(Task task)
	{
		try
		{
			await task.ConfigureAwait(false);
			Assert.Fail("Expected the task to fault or be canceled.");
		}
		catch (Exception exception) when (exception is not UnitTestAssertException)
		{
			Assert.IsTrue(task.IsFaulted || task.IsCanceled, $"Expected the task to fault or be canceled, but its status was '{task.Status}'.");
		}
	}

	// Deadline-bounded poll: the request id appears once the connection wrote the outgoing payload, so the wait
	// only fails red when the write never happens.
	private static async Task<int> WaitForRequestIdAsync(RecordingStream stream)
	{
		int requestId = 0;

		bool found = await TestPolling
			.ForConditionAsync(() => JsonRpcFrameTestHelpers.TryExtractRequestId(stream.GetWrittenBytes(), out requestId), TestPolling.DefaultTimeout)
			.ConfigureAwait(false);

		Assert.IsTrue(found, "Timed out waiting for the JSON-RPC request payload to be written.");

		return requestId;
	}

	// Deadline-bounded poll: the text appears once the connection wrote the outgoing payload, so the wait only
	// fails red when the write never happens.
	private static async Task<string> WaitForWrittenTextAsync(RecordingStream stream, string expectedText)
	{
		string writtenText = string.Empty;

		bool found = await TestPolling
			.ForConditionAsync(
				() =>
				{
					writtenText = Encoding.UTF8.GetString(stream.GetWrittenBytes());
					return writtenText.Contains(expectedText, StringComparison.Ordinal);
				},

				TestPolling.DefaultTimeout
			)
			.ConfigureAwait(false);

		Assert.IsTrue(found, $"Timed out waiting for the written payload to contain '{expectedText}'.");

		return writtenText;
	}

	private sealed class DeferredJsonRpcResponseStream : Stream
	{
		private readonly TaskCompletionSource<byte[]> _payloadSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

		private byte[]? _payloadBytes;
		private int _position;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => _payloadBytes?.Length ?? 0;

		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public void SetPayload(string payload) => _payloadSource.TrySetResult(Encoding.UTF8.GetBytes(payload));

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			_payloadBytes ??= await _payloadSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			if (_position >= _payloadBytes.Length)
				return 0;

			int bytesToCopy = Math.Min(buffer.Length, _payloadBytes.Length - _position);
			_payloadBytes.AsMemory(_position, bytesToCopy).CopyTo(buffer);
			_position += bytesToCopy;
			return bytesToCopy;
		}

		public override void Flush() { }

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private sealed class DeferredPersistentJsonRpcResponseStream : Stream
	{
		private readonly TaskCompletionSource<byte[]> _payloadSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

		private byte[]? _payloadBytes;
		private int _position;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => _payloadBytes?.Length ?? 0;

		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public void SetPayload(string payload) => _payloadSource.TrySetResult(Encoding.UTF8.GetBytes(payload));

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			_payloadBytes ??= await _payloadSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			if (_position < _payloadBytes.Length)
			{
				int bytesToCopy = Math.Min(buffer.Length, _payloadBytes.Length - _position);
				_payloadBytes.AsMemory(_position, bytesToCopy).CopyTo(buffer);
				_position += bytesToCopy;
				return bytesToCopy;
			}

			await _completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			return 0;
		}

		public override void Flush() { }

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
