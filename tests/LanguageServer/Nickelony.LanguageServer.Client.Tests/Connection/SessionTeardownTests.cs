using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SessionTeardownTests
{
	// Tests that do not assert on the written messages shorten the shutdown budget; the ones that do keep the fixture default,
	// because the request must reach the stream before its budget elapses.
	private static readonly TimeSpan s_shortTimeout = TimeSpan.FromMilliseconds(150);

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task DisposeSessionAsync_SendsShutdownThenExitAndTerminatesAProcessThatDoesNotExit()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		using Process process = ProcessTestHelpers.StartDisposableProcess();

		int processId = process.Id;

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		var fixture = new SessionTeardownTestHost(logger);
		ConnectionSession session = SessionTeardownTestHost.CreateSession(generation: 3, process, serverOutputStream, serverInputStream);

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		string written = serverInputStream.GetWrittenText();

		int shutdownIndex = written.IndexOf("\"method\":\"shutdown\"", StringComparison.Ordinal);
		int exitIndex = written.IndexOf("\"method\":\"exit\"", StringComparison.Ordinal);

		Assert.IsGreaterThanOrEqualTo(0, shutdownIndex, written);
		Assert.IsGreaterThan(shutdownIndex, exitIndex, written);
		Assert.Contains(log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("forcing process termination"), logger.Logs);
		Assert.IsTrue(await ProcessTestHelpers.WaitForProcessExitAsync(processId).ConfigureAwait(false));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task DisposeSessionAsync_WhenTheServerExitsOnItsOwn_DoesNotForceTermination()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using Process process = ProcessTestHelpers.StartProcessThatExitsAfterOneInputLine();

		Stream serverOutputStream = process.StandardOutput.BaseStream;
		Stream serverInputStream = process.StandardInput.BaseStream;

		var fixture = new SessionTeardownTestHost(logger);
		ConnectionSession session = SessionTeardownTestHost.CreateSession(generation: 5, process, serverOutputStream, serverInputStream);

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		Assert.IsTrue(process.HasExited);

		Assert.DoesNotContain(
			log => log.Contains("forcing process termination", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.DoesNotContain(
			log => log.Contains("raised exceptions while stopping", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task DisposeSessionAsync_WhenTheProcessAlreadyExited_SendsNoGracefulMessages()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		using Process process = ProcessTestHelpers.StartDisposableProcess();

		process.Kill(entireProcessTree: true);
		await process.WaitForExitAsync().ConfigureAwait(false);

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		var fixture = new SessionTeardownTestHost(logger);
		ConnectionSession session = SessionTeardownTestHost.CreateSession(generation: 1, process, serverOutputStream, serverInputStream);

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		Assert.AreEqual(string.Empty, serverInputStream.GetWrittenText());
		Assert.DoesNotContain(log => log.Contains("graceful shutdown", StringComparison.OrdinalIgnoreCase), logger.Logs);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task DisposeSessionAsync_WithAProcessButNoJsonRpc_TerminatesTheProcessDirectly()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		using Process process = ProcessTestHelpers.StartDisposableProcess();

		int processId = process.Id;

		var fixture = new SessionTeardownTestHost(logger);
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 6, process);

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		Assert.IsTrue(await ProcessTestHelpers.WaitForProcessExitAsync(processId).ConfigureAwait(false));

		Assert.Contains(
			log => log.StartsWith("Info|", StringComparison.Ordinal) && log.Contains("before its JSON-RPC instance was attached"),
			logger.Logs
		);

		Assert.DoesNotContain(log => log.Contains("graceful shutdown", StringComparison.OrdinalIgnoreCase), logger.Logs);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task DisposeSessionAsync_DetachesTheProcessExitedHandlerBeforeStoppingTheProcess()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using Process process = ProcessTestHelpers.StartDisposableProcess();

		int processId = process.Id;
		int exitedCount = 0;

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout: s_shortTimeout);
		ConnectionSession session = SessionTeardownTestHost.CreateSession(generation: 7, process, serverOutputStream, serverInputStream);

		session.ProcessExitedHandler = (_, _) => Interlocked.Increment(ref exitedCount);
		process.Exited += session.ProcessExitedHandler;
		process.EnableRaisingEvents = true;

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		Assert.IsTrue(await ProcessTestHelpers.WaitForProcessExitAsync(processId).ConfigureAwait(false));

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref exitedCount));
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WithAConnectionAndNoProcess_RunsTheGracefulSequenceAndReleasesTheSession()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		var connection = new RecordingLanguageServerConnection(serverOutputStream, serverInputStream);
		var fixture = new SessionTeardownTestHost(logger);

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 2,
			process: null,
			serverOutputStream,
			serverInputStream,
			connection
		);

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		string written = serverInputStream.GetWrittenText();

		Assert.IsGreaterThan(
			written.IndexOf("\"method\":\"shutdown\"", StringComparison.Ordinal),
			written.IndexOf("\"method\":\"exit\"", StringComparison.Ordinal)
		);

		Assert.Contains(log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("did not acknowledge shutdown"), logger.Logs);
		Assert.AreEqual(1, connection.DisposeCount);
		Assert.IsNull(session.JsonRpc);
		Assert.IsNull(session.MessageHandler);
		Assert.IsNull(session.RpcCompletionTask);
		Assert.IsNull(session.StandardErrorLoopTask);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WithAConnectionThatClosesItsStreams_WritesExitBeforeReleasingTheConnection()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new SlowRecordingStream(TimeSpan.FromMilliseconds(50));

		var connection = new FakeStreamClosingConnection(serverOutputStream, serverInputStream);
		var fixture = new SessionTeardownTestHost(logger);

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 12,
			process: null,
			serverOutputStream,
			serverInputStream,
			connection
		);

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		// The snapshot is the text the stream held when the connection closed it, so a message still in the outbound
		// pipe at that moment is missing from it.
		string written = connection.WrittenTextAtDisposal ?? string.Empty;

		int shutdownIndex = written.IndexOf("\"method\":\"shutdown\"", StringComparison.Ordinal);

		Assert.IsGreaterThanOrEqualTo(0, shutdownIndex, written);
		Assert.IsGreaterThan(shutdownIndex, written.IndexOf("\"method\":\"exit\"", StringComparison.Ordinal), written);

		Assert.DoesNotContain(
			log => log.Contains("did not drain", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WhenTheInputStreamWriteFails_EndsTheDrainWithoutWaitingForItsBound()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var failingInputStream = new SlowRecordingStream(TimeSpan.Zero, failWrites: true);

		var connection = new RecordingLanguageServerConnection(serverOutputStream, failingInputStream);

		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout: s_shortTimeout);

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 13,
			process: null,
			serverOutputStream,
			serverInputStream: failingInputStream,
			connection
		);

		await fixture.Teardown.DisposeSessionAsync(session).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		// The failed write ends the drain, so the bounded wait never times out.
		Assert.AreEqual(1, connection.DisposeCount);

		Assert.DoesNotContain(
			log => log.Contains("did not drain", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WhenTheOutboundCopyFaults_StillReleasesTheConnection()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var failingInputStream = new SlowRecordingStream(TimeSpan.Zero, failWrites: true);

		var connection = new RecordingLanguageServerConnection(serverOutputStream, failingInputStream);
		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout: s_shortTimeout);

		// The handler's logger throws when the copy reports the failed write, so the copy's task faults.
		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 14,
			process: null,
			serverOutputStream,
			serverInputStream: failingInputStream,
			connection,
			handlerLogger: new ThrowingLogger()
		);

		await fixture.Teardown.DisposeSessionAsync(session).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual(1, connection.DisposeCount);
		Assert.IsNull(session.MessageHandler);

		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("outbound copy of language server connection generation 14 failed"),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WhenTheExitDispatchSpendsTheBudget_ReleasesTheConnectionWithinTwoShutdownTimeouts()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var blockedInputStream = new GatedWriteStream();

		var connection = new RecordingLanguageServerConnection(serverOutputStream, blockedInputStream);
		var shutdownRequestTimeout = TimeSpan.FromSeconds(1);
		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout);

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 15,
			process: null,
			serverOutputStream,
			serverInputStream: blockedInputStream,
			connection
		);

		Assert.IsNotNull(session.JsonRpc);

		// A message past the outbound pipe's pause threshold blocks the handler's writes while the server reads nothing, so the
		// unanswered shutdown request spends one timeout and the exit dispatch spends the shared exit budget.
		Task blockedNotification = session.JsonRpc.NotifyAsync("example/large", new string('x', 256 * 1024));

		var stopwatch = Stopwatch.StartNew();
		Task<TimeSpan> released = connection.Disposed.ContinueWith(_ => stopwatch.Elapsed, TaskContinuationOptions.ExecuteSynchronously);

		try
		{
			await fixture.Teardown.DisposeSessionAsync(session).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);
		}
		finally
		{
			blockedInputStream.Release();
		}

		TimeSpan releasedAfter = await released.ConfigureAwait(false);

		// Without the shared budget the drain would wait one more timeout after the exit dispatch.
		Assert.IsLessThan(shutdownRequestTimeout * 2.5, releasedAfter, string.Join(Environment.NewLine, logger.Logs));
		Assert.Contains(log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("did not drain"), logger.Logs);

		// The handler's disposal ends the blocked notification, and its outcome is not what this test checks.
		_ = blockedNotification.Exception;
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WhenTheReadStreamIgnoresCancellation_EndsTheLoopsOnceTheConnectionClosesIt()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		var serverOutputStream = new UncancelableReadStream();
		using var serverInputStream = new SlowRecordingStream(TimeSpan.Zero);

		var connection = new FakeStreamClosingConnection(serverOutputStream, serverInputStream);
		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout: s_shortTimeout, disposeWaitTimeout: TimeSpan.FromSeconds(2));

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 16,
			process: null,
			serverOutputStream,
			serverInputStream,
			connection
		);

		await fixture.Teardown.DisposeSessionAsync(session).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.DoesNotContain(
			log => log.Contains("did not complete within", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_CalledTwice_SendsNoSecondGracefulSequence()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout: s_shortTimeout);

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 2,
			process: null,
			serverOutputStream,
			serverInputStream,
			connection: new RecordingLanguageServerConnection(serverOutputStream, serverInputStream)
		);

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		string writtenAfterFirstDisposal = serverInputStream.GetWrittenText();

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		Assert.AreEqual(writtenAfterFirstDisposal, serverInputStream.GetWrittenText());

		Assert.ContainsSingle(
			log => log.Contains("Attempting graceful shutdown", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WhenTheConnectionDisposalFails_LogsTheFailureAndStillClearsTheSession()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout: s_shortTimeout);

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 4,
			process: null,
			serverOutputStream,
			serverInputStream,
			connection: new ThrowingLanguageServerConnection(serverOutputStream, serverInputStream)
		);

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		Assert.Contains(
			log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("Disposing the connection of generation 4"),
			logger.Logs
		);

		Assert.IsNull(session.JsonRpc);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WhenTheServerStopsDrainingItsInput_StillCompletesAndReleasesTheConnection()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var blockedInputStream = new GatedWriteStream();

		var connection = new RecordingLanguageServerConnection(serverOutputStream, blockedInputStream);
		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout: s_shortTimeout);

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 8,
			process: null,
			serverOutputStream,
			serverInputStream: blockedInputStream,
			connection
		);

		try
		{
			await fixture.Teardown.DisposeSessionAsync(session).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);
		}
		finally
		{
			blockedInputStream.Release();
		}

		Assert.IsTrue(blockedInputStream.WriteStarted.IsCompleted, "The shutdown request must have reached the blocked stream.");

		Assert.Contains(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("did not acknowledge shutdown"),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.Contains(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("did not drain"),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.AreEqual(1, connection.DisposeCount);
		Assert.IsNull(session.JsonRpc);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WhenTheShutdownRequestFails_LogsTheFailureAndStillTriesTheExitNotification()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		var connection = new RecordingLanguageServerConnection(serverOutputStream, serverInputStream);
		var fixture = new SessionTeardownTestHost(logger);

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 9,
			process: null,
			serverOutputStream,
			serverInputStream,
			connection
		);

		Assert.IsNotNull(session.JsonRpc);

		session.JsonRpc.Dispose();

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		string logs = string.Join(Environment.NewLine, logger.Logs);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("Sending the language server shutdown request during disposal failed"),

			logger.Logs,
			logs
		);

		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("Sending the language server exit notification during disposal failed"),

			logger.Logs,
			logs
		);

		Assert.AreEqual(1, connection.DisposeCount);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_WhenABackgroundLoopOutlastsTheDisposeTimeout_LogsAWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout: s_shortTimeout, disposeWaitTimeout: s_shortTimeout);

		ConnectionSession session = SessionTeardownTestHost.CreateSession(generation: 10, process: null, serverOutputStream, serverInputStream);

		session.StandardErrorLoopTask = new TaskCompletionSource().Task;

		await fixture.Teardown.DisposeSessionAsync(session).WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.Contains(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("did not complete within 150 ms"),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task DisposeSessionAsync_ReleasesTheConnectionBeforeItWaitsForTheBackgroundLoops()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		var connection = new RecordingLanguageServerConnection(serverOutputStream, serverInputStream);

		var fixture = new SessionTeardownTestHost(logger, shutdownRequestTimeout: s_shortTimeout, disposeWaitTimeout: TimeSpan.FromSeconds(2));

		ConnectionSession session = SessionTeardownTestHost.CreateSession(
			generation: 11,
			process: null,
			serverOutputStream,
			serverInputStream,
			connection
		);

		// A loop that ends only when the connection is released: a wait that ran before the release would time out.
		session.StandardErrorLoopTask = connection.Disposed;

		await fixture.Teardown.DisposeSessionAsync(session).ConfigureAwait(false);

		Assert.DoesNotContain(
			log => log.Contains("did not complete within", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task WaitForBackgroundLoopsAsync_WhenLoopFaults_LogsLoopFailureWithoutDuplicateDisposalWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		var fixture = new SessionTeardownTestHost(logger);

		await fixture
			.Teardown.WaitForBackgroundLoopsAsync(Task.FromException(new IOException("Simulated loop failure.")), Task.CompletedTask)
			.ConfigureAwait(false);

		Assert.Contains(
			log =>
				log.Contains("background loop", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("Simulated loop failure.", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.DoesNotContain(
			log => log.Contains("Language server background loop failed during disposal.", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task WaitForBackgroundLoopsAsync_WhenLoopIsCanceled_LogsDebugWithoutWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		var fixture = new SessionTeardownTestHost(logger);

		await fixture
			.Teardown.WaitForBackgroundLoopsAsync(Task.FromCanceled(new CancellationToken(canceled: true)), Task.CompletedTask)
			.ConfigureAwait(false);

		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("was canceled during disposal", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("JSON-RPC completion", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.DoesNotContain(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("background loop", StringComparison.OrdinalIgnoreCase),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task WaitForBackgroundLoopsAsync_WhenALoopOutlastsTheDisposeTimeout_LogsAWarningAndReturns()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		var fixture = new SessionTeardownTestHost(logger, disposeWaitTimeout: TimeSpan.FromMilliseconds(100));
		var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await fixture
			.Teardown.WaitForBackgroundLoopsAsync(neverCompletes.Task, Task.CompletedTask)
			.WaitAsync(TestPolling.DefaultTimeout)
			.ConfigureAwait(false);

		Assert.Contains(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("did not complete within 100 ms"),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task WaitForBackgroundLoopsAsync_WhenBothLoopsCompleted_LogsNothing()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		var fixture = new SessionTeardownTestHost(logger);

		await fixture.Teardown.WaitForBackgroundLoopsAsync(Task.CompletedTask, null).ConfigureAwait(false);

		Assert.IsEmpty(logger.Logs);
	}

	private sealed class ThrowingLanguageServerConnection : ILanguageServerConnection
	{
		public ThrowingLanguageServerConnection(Stream readStream, Stream writeStream)
		{
			ReadStream = readStream;
			WriteStream = writeStream;
		}

		public Stream ReadStream { get; }

		public Stream WriteStream { get; }

		public Process? Process => null;

		public ValueTask DisposeAsync() => throw new IOException("Simulated connection disposal failure.");
	}

	/// <summary>A connection that closes its streams on disposal, as a stdio transport does, and keeps the text written until then.</summary>
	private sealed class FakeStreamClosingConnection : ILanguageServerConnection
	{
		private readonly SlowRecordingStream _writeStream;

		public FakeStreamClosingConnection(Stream readStream, SlowRecordingStream writeStream)
		{
			ReadStream = readStream;
			_writeStream = writeStream;
		}

		public Stream ReadStream { get; }

		public Stream WriteStream => _writeStream;

		public Process? Process => null;

		public string? WrittenTextAtDisposal { get; private set; }

		public ValueTask DisposeAsync()
		{
			WrittenTextAtDisposal ??= _writeStream.GetWrittenText();

			_writeStream.Dispose();
			ReadStream.Dispose();

			return ValueTask.CompletedTask;
		}
	}

	/// <summary>
	/// A read stream whose reads ignore their token and end only when the stream is disposed, as a pipe read with no cancellation does.
	/// </summary>
	private sealed class UncancelableReadStream : Stream
	{
		private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			await _disposed.Task.ConfigureAwait(false);
			return 0;
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
			ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override void Flush() { }

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			_disposed.TrySetResult();
			base.Dispose(disposing);
		}
	}

	/// <summary>A write stream that records each write after a delay, or fails it, and throws once disposed.</summary>
	private sealed class SlowRecordingStream : Stream
	{
		private readonly TimeSpan _writeDelay;
		private readonly bool _failWrites;
		private readonly RecordingStream _recording = new();

		private volatile bool _disposed;

		public SlowRecordingStream(TimeSpan writeDelay, bool failWrites = false)
		{
			_writeDelay = writeDelay;
			_failWrites = failWrites;
		}

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public string GetWrittenText() => _recording.GetWrittenText();

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (_writeDelay > TimeSpan.Zero)
				await Task.Delay(_writeDelay, cancellationToken).ConfigureAwait(false);

			ObjectDisposedException.ThrowIf(_disposed, this);

			if (_failWrites)
				throw new IOException("Simulated input stream write failure.");

			await _recording.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
		}

		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
			WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override void Flush() { }

		public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			_disposed = true;

			if (disposing)
				_recording.Dispose();

			base.Dispose(disposing);
		}
	}
}
