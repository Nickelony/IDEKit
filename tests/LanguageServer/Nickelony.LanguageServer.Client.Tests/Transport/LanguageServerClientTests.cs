using StreamJsonRpc;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public partial class LanguageServerClientTests
{
	// Shared defaults used by process-lifetime tests: shorter timeouts keep the real-process tail cheap without
	// weakening the assertions (a delayed JSON-RPC ack still completes well inside these budgets).
	private static readonly LanguageServerClientOptions s_defaultClientOptions = LanguageServerClientOptions.Default with
	{
		ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
		DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000)
	};

	private static bool HasAbandonedNotificationLog(TestLoggerScope logScope)
		=> logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("abandoned language server transport task", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated abandoned notification failure.", StringComparison.Ordinal));

	[TestMethod]
	public async Task DisposeAsync_WaitsForDetachedSessionCleanup()
	{
		var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		var queuedCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.TransportHost.QueuedFailedSessionCleanupTask = queuedCleanup.Task;

		Task disposeTask = client.DisposeAsync().AsTask();
		Task completedTask = await Task.WhenAny(disposeTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);

		Assert.AreNotSame(disposeTask, completedTask);

		queuedCleanup.TrySetResult(true);
		await disposeTask.ConfigureAwait(false);
	}

	private static Process StartDisposableProcess()
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			Arguments = "/c ping 127.0.0.1 -n 10 > nul",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		return Process.Start(startInfo)
			?? throw new InvalidOperationException("Unable to start the disposable test process.");
	}

	private static TransportSession CreateTransportSession(LanguageServerClient client, long generation, Process? process, Stream serverOutputStream, Stream serverInputStream, bool startListening = false, ILanguageServerConnection? connection = null)
	{
		var session = new TransportSession(generation, process, serverOutputStream, serverInputStream)
		{
			Connection = connection
		};

		session.MessageHandler = client.TransportHost.CreateMessageHandler(serverInputStream, serverOutputStream);
		session.RpcTarget = client.TransportHost.CreateRpcTarget(generation);

		JsonRpc jsonRpc = client.TransportHost.CreateJsonRpc(session);
		session.JsonRpc = jsonRpc;
		session.RpcCompletionTask = jsonRpc.Completion;

		if (startListening)
			jsonRpc.StartListening();

		return session;
	}

	private static ClientRpcTarget CreateRpcTarget(LanguageServerClient client, long generation = 0)
		=> client.TransportHost.CreateRpcTarget(generation);

	private static void SetActiveSession(LanguageServerClient client, TransportSession session)
		=> client.CapabilityStore.SetActiveSession(session);

	private static void SetReadyState(LanguageServerClient client, bool isReady)
		=> client.CapabilityStore.SetCapabilityReadinessForGeneration(client.TransportGeneration, isReady);

	private static void CaptureServerCapabilities(LanguageServerClient client, InitializeResponse response)
		=> client.CapabilityStore.CaptureServerCapabilitiesForGeneration(client.TransportGeneration, response);

	private static long GetTransportGeneration(TransportSession session)
		=> session.Generation;

	private static void RecordStandardErrorLine(TransportSession session, string line)
		=> session.RecordStandardErrorLine(line);

	private static InitializeResponse DeserializeInitializeResponse(string json)
	{
		return JsonSerializer.Deserialize<InitializeResponse>(json)
			?? throw new InvalidOperationException("Failed to deserialize the initialize response test payload.");
	}

	private static PublishDiagnosticsParams CreateDiagnosticsParameters(string uri, string message) => new(
		uri,
		Version: null,
		Diagnostics:
		[
			new DiagnosticPayload(
				new ProtocolRangePayload(
					new ProtocolPosition(0, 0),
					new ProtocolPosition(0, 1)),
				Severity: null,
				Message: message,
				Source: null,
				Code: null)
		]);

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
		catch (OperationCanceledException)
		{ }

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
			Assert.IsTrue(task.IsFaulted || task.IsCanceled,
				$"Expected the task to fault or be canceled, but its status was '{task.Status}'.");
		}
	}

	// Deadline-bounded wait: a fixed iteration count can expire under load even though the process still exits
	// a moment later, while the deadline can only fail red when the process never exits at all.
	private static Task<bool> WaitForProcessExitAsync(int processId)
		=> TestPolling.ForConditionAsync(() => HasProcessExited(processId), TestPolling.DefaultTimeout);

	private static bool HasProcessExited(int processId)
	{
		try
		{
			using Process process = Process.GetProcessById(processId);
			return process.HasExited;
		}
		catch (ArgumentException)
		{
			return true;
		}
	}

	// Deadline-bounded poll: the request id appears once the transport wrote the outgoing payload, so the wait
	// only fails red when the write never happens.
	private static async Task<int> WaitForRequestIdAsync(RecordingStream stream)
	{
		int requestId = 0;
		bool found = await TestPolling.ForConditionAsync(
			() => JsonRpcFrameTestHelper.TryExtractRequestId(stream.GetWrittenBytes(), out requestId),
			TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsTrue(found, "Timed out waiting for the JSON-RPC request payload to be written.");

		return requestId;
	}

	// Deadline-bounded poll: the text appears once the transport wrote the outgoing payload, so the wait only
	// fails red when the write never happens.
	private static async Task<string> WaitForWrittenTextAsync(RecordingStream stream, string expectedText)
	{
		string writtenText = string.Empty;
		bool found = await TestPolling.ForConditionAsync(
			() =>
			{
				writtenText = Encoding.UTF8.GetString(stream.GetWrittenBytes());
				return writtenText.Contains(expectedText, StringComparison.Ordinal);
			},
			TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsTrue(found, $"Timed out waiting for the written payload to contain '{expectedText}'.");

		return writtenText;
	}

	private sealed class RecordingStream : Stream
	{
		private readonly object _syncRoot = new();
		private readonly MemoryStream _innerStream = new();

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => _innerStream.Length;

		public override long Position
		{
			get
			{
				lock (_syncRoot)
					return _innerStream.Position;
			}
			set
			{
				lock (_syncRoot)
					_innerStream.Position = value;
			}
		}

		public byte[] GetWrittenBytes()
		{
			lock (_syncRoot)
				return _innerStream.ToArray();
		}

		public string GetWrittenText()
			=> Encoding.UTF8.GetString(GetWrittenBytes());

		public override void Flush()
		{
			lock (_syncRoot)
				_innerStream.Flush();
		}

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
		{
			lock (_syncRoot)
				_innerStream.SetLength(value);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			lock (_syncRoot)
				_innerStream.Write(buffer, offset, count);
		}

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			lock (_syncRoot)
				_innerStream.Write(buffer);
		}

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			lock (_syncRoot)
			{
				_innerStream.Write(buffer.Span);
				return ValueTask.CompletedTask;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				lock (_syncRoot)
					_innerStream.Flush();
			}

			base.Dispose(disposing);
		}
	}

	private sealed class BlockingWriteStream : Stream
	{
		private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <summary>
		/// Gets a task that completes when the first transport write reaches this stream.
		/// </summary>
		public Task WriteStarted => _writeStarted.Task;

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public void Release()
			=> _release.TrySetResult(true);

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override void Write(ReadOnlySpan<byte> buffer)
			=> throw new NotSupportedException();

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
			=> new(WaitForReleaseAsync(cancellationToken));

		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
			=> WaitForReleaseAsync(cancellationToken);

		public override void Flush()
		{ }

		public override Task FlushAsync(CancellationToken cancellationToken)
			=> Task.CompletedTask;

		private async Task WaitForReleaseAsync(CancellationToken cancellationToken)
		{
			_writeStarted.TrySetResult(true);

			if (_release.Task.IsCompleted)
				return;

			if (!cancellationToken.CanBeCanceled)
			{
				await _release.Task.ConfigureAwait(false);
				return;
			}

			await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	private sealed class PendingReadStream : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{ }

			return 0;
		}

		public override void Flush()
		{ }

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();
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

		public void SetPayload(string payload)
			=> _payloadSource.TrySetResult(Encoding.UTF8.GetBytes(payload));

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

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

		public override void Flush()
		{ }

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();
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

		public void SetPayload(string payload)
			=> _payloadSource.TrySetResult(Encoding.UTF8.GetBytes(payload));

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

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

		public override void Flush()
		{ }

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();
	}

	private sealed class TestLanguageServerConnection : ILanguageServerConnection
	{
		private int _disposeCount;

		public TestLanguageServerConnection(Stream readStream, Stream writeStream, Stream? errorStream = null, Process? process = null)
		{
			ReadStream = readStream;
			WriteStream = writeStream;
			ErrorStream = errorStream;
			Process = process;
		}

		public Stream ReadStream { get; }

		public Stream WriteStream { get; }

		public Stream? ErrorStream { get; }

		public Process? Process { get; }

		public int DisposeCount => Volatile.Read(ref _disposeCount);

		public ValueTask DisposeAsync()
		{
			// The test streams stay owned by the test that created them, so only the disposal itself is recorded.
			Interlocked.Increment(ref _disposeCount);
			return ValueTask.CompletedTask;
		}
	}

	private sealed class TestLanguageServerTransport : ILanguageServerTransport
	{
		private readonly Func<LanguageServerTransportContext, CancellationToken, Task<ILanguageServerConnection>> _connect;

		public TestLanguageServerTransport(Func<LanguageServerTransportContext, CancellationToken, Task<ILanguageServerConnection>> connect)
			=> _connect = connect;

		public LanguageServerTransportContext? LastContext { get; private set; }

		public Task<ILanguageServerConnection> ConnectAsync(LanguageServerTransportContext context, CancellationToken cancellationToken)
		{
			LastContext = context;
			return _connect(context, cancellationToken);
		}
	}

	private sealed class TestConfigurationRoot
	{
		public TestSectionConfiguration? Section { get; init; }
	}

	private sealed class TestSectionConfiguration
	{
		public TestNestedSectionConfiguration? Runtime { get; init; }
	}

	private sealed class TestNestedSectionConfiguration
	{
		public string? Version { get; init; }
	}
}
