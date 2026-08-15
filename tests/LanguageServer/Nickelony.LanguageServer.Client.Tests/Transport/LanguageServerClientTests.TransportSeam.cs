using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Covers the pluggable transport seam: the client must open every connection through
/// <see cref="LanguageServerClientOptions.Transport"/>, adopt the returned connection, and release it during teardown.
/// </summary>
public partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task StartAsync_WithCustomTransport_ConnectsThroughTheConnectionAndDisposesItOnTeardown()
	{
		using var serverOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();
		var connection = new TestLanguageServerConnection(serverOutputStream, serverInputStream);
		LanguageServerTransportContext? capturedContext = null;

		var options = CreateSeamOptions(new TestLanguageServerTransport((context, _) =>
		{
			capturedContext = context;
			return Task.FromResult<ILanguageServerConnection>(connection);
		}));

		var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", options);

		try
		{
			Task<bool> startTask = client.StartAsync(CancellationToken.None);

			int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
			serverOutputStream.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, ReadyCapabilitiesJson));

			Assert.IsTrue(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
			Assert.IsTrue(client.IsReady);
			Assert.AreEqual(1L, client.TransportGeneration);

			string writtenText = serverInputStream.GetWrittenText();

			Assert.IsTrue(writtenText.Contains("\"method\":\"initialize\"", StringComparison.Ordinal), writtenText);

			// The handshake must have traveled through the connection the transport returned.
			Assert.IsNotNull(capturedContext);
			Assert.AreEqual("example-language-server.exe", capturedContext.ServerExecutablePath);
			CollectionAssert.AreEqual(new[] { @"C:\Workspace" }, capturedContext.WorkspaceRootDirectoryPaths.ToArray());
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
		using var responseStream = new PendingReadStream();
		using var recordingStream = new RecordingStream();
		var connection = new TestLanguageServerConnection(responseStream, recordingStream);

		var options = CreateSeamOptions(StdioLanguageServerTransport.Default);

		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", options);
		TransportSession session = CreateTransportSession(client, 1,
			process: null, responseStream, recordingStream, startListening: true, connection: connection);

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
	public async Task DisposeAsync_WithoutAConnectionOrProcess_DoesNotSendTheShutdownSequence()
	{
		using var responseStream = new PendingReadStream();
		using var recordingStream = new RecordingStream();

		await using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			"example-language-server.exe",
			CreateSeamOptions(StdioLanguageServerTransport.Default));

		TransportSession session = CreateTransportSession(client, 1,
			process: null, responseStream, recordingStream, startListening: true);

		SetActiveSession(client, session);

		await client.DisposeAsync().ConfigureAwait(false);

		// A session built directly from raw streams has no liveness signal, so its teardown must not block on a
		// protocol handshake the channel was never able to answer.
		Assert.AreEqual(string.Empty, recordingStream.GetWrittenText());
	}

	[TestMethod]
	public async Task StartAsync_WithAConfiguredServerWorkingDirectory_HandsItToTheTransportContext()
	{
		using var serverOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();
		var connection = new TestLanguageServerConnection(serverOutputStream, serverInputStream);
		var transport = new TestLanguageServerTransport((_, _) => Task.FromResult<ILanguageServerConnection>(connection));
		string workingDirectory = Path.Combine(Path.GetTempPath(), "ls-server-workdir");

		var options = LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
			ServerWorkingDirectory = workingDirectory,
			Transport = transport
		};

		string serverExecutablePath = Path.Combine(Path.GetTempPath(), "ls-server-home", "example-language-server.exe");

		await using var client = new LanguageServerClient([@"C:\Workspace"], serverExecutablePath, options);

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
		serverOutputStream.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, ReadyCapabilitiesJson));

		Assert.IsTrue(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		// A server that resolves configuration relative to the workspace root needs the host-configured working
		// directory, not the executable's own directory, on the context the transport spawns from.
		Assert.IsNotNull(transport.LastContext);
		Assert.AreEqual(workingDirectory, transport.LastContext.ServerWorkingDirectory);
	}

	[TestMethod]
	public async Task StartAsync_WithoutAConfiguredServerWorkingDirectory_DefaultsToTheServerExecutableDirectory()
	{
		using var serverOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();
		var connection = new TestLanguageServerConnection(serverOutputStream, serverInputStream);
		var transport = new TestLanguageServerTransport((_, _) => Task.FromResult<ILanguageServerConnection>(connection));
		string serverDirectory = Path.Combine(Path.GetTempPath(), "ls-server-home");
		string serverExecutablePath = Path.Combine(serverDirectory, "example-language-server.exe");

		var options = LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
			Transport = transport
		};

		await using var client = new LanguageServerClient([@"C:\Workspace"], serverExecutablePath, options);

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
		serverOutputStream.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, ReadyCapabilitiesJson));

		Assert.IsTrue(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		Assert.IsNotNull(transport.LastContext);
		Assert.AreEqual(serverDirectory, transport.LastContext.ServerWorkingDirectory);
	}

	[TestMethod]
	public void LanguageServerClientOptions_Transport_RejectsNull()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => LanguageServerClientOptions.Default with
		{
			Transport = null!
		});
	}

	[TestMethod]
	public void StdioLanguageServerTransport_ConnectAsync_WithNullContext_ThrowsArgumentNullException()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			StdioLanguageServerTransport.Default.ConnectAsync(null!, CancellationToken.None));

		Assert.AreEqual("context", exception.ParamName);
	}

	[TestMethod]
	public void StdioLanguageServerTransport_ConnectAsync_WithACanceledToken_ThrowsBeforeSpawning()
	{
		using var startupCancellation = new CancellationTokenSource();
		startupCancellation.Cancel();

		var context = new LanguageServerTransportContext
		{
			ServerExecutablePath = "example-language-server.exe",
			ServerWorkingDirectory = Environment.CurrentDirectory,
			EnvironmentVariables = new Dictionary<string, string>(),
			WorkspaceRootDirectoryPaths = [],
			Logger = NullLogger.Instance
		};

		Assert.ThrowsExactly<OperationCanceledException>(() =>
			StdioLanguageServerTransport.Default.ConnectAsync(context, startupCancellation.Token));
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task StdioLanguageServerTransport_ConnectAsync_WithAStartableExecutable_ReturnsALiveConnection()
	{
		(string fileName, string[] arguments) = CreateLongRunningCommand();

		var context = new LanguageServerTransportContext
		{
			ServerExecutablePath = fileName,
			ServerArguments = arguments,
			ServerWorkingDirectory = Path.GetTempPath(),
			EnvironmentVariables = new Dictionary<string, string>(),
			WorkspaceRootDirectoryPaths = [],
			Logger = NullLogger.Instance
		};

		// The production transport's success path is otherwise covered only by Windows-gated tests; this spawns a
		// platform-neutral long-running child so the default transport is exercised on every platform.
		ILanguageServerConnection connection = await StdioLanguageServerTransport.Default
			.ConnectAsync(context, CancellationToken.None)
			.ConfigureAwait(false);

		try
		{
			Assert.IsNotNull(connection.Process, "The stdio transport must expose the spawned process.");
			Assert.IsFalse(connection.Process.HasExited, "The spawned process must still be running.");
			Assert.IsNotNull(connection.ReadStream);
			Assert.IsNotNull(connection.WriteStream);
			Assert.IsNotNull(connection.ErrorStream);
		}
		finally
		{
			await KillAndDisposeAsync(connection).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task StdioLanguageServerTransport_ConnectAsync_BindsTheSpawnedProcessToTheConfiguredGuard()
	{
		// The Windows job-object semantics (the kill-on-close flag and the diagnostics routing) are covered directly
		// by ChildProcessLifetimeTests; this platform-neutral test pins the transport's binding contract instead: the
		// host-configured guard must receive the exact process the connection exposes.
		var guard = new RecordingChildProcessLifetimeGuard();
		(string fileName, string[] arguments) = CreateLongRunningCommand();

		var context = new LanguageServerTransportContext
		{
			ServerExecutablePath = fileName,
			ServerArguments = arguments,
			ServerWorkingDirectory = Path.GetTempPath(),
			EnvironmentVariables = new Dictionary<string, string>(),
			WorkspaceRootDirectoryPaths = [],
			ChildProcessLifetimeGuard = guard,
			Logger = NullLogger.Instance
		};

		ILanguageServerConnection connection = await StdioLanguageServerTransport.Default
			.ConnectAsync(context, CancellationToken.None)
			.ConfigureAwait(false);

		try
		{
			Assert.AreSame(connection.Process, guard.BoundProcess);
		}
		finally
		{
			await KillAndDisposeAsync(connection).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Builds a command that stays alive on the current platform, so the transport tests can spawn a real child
	/// without depending on a language-server executable.
	/// </summary>
	private static (string FileName, string[] Arguments) CreateLongRunningCommand()
		=> OperatingSystem.IsWindows()
			? (Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/c", "ping 127.0.0.1 -n 30 > nul"])
			: ("/bin/sh", ["-c", "sleep 30"]);

	/// <summary>
	/// Terminates the connection's process (a raw connection disposal does not) and then releases the connection.
	/// </summary>
	private static async Task KillAndDisposeAsync(ILanguageServerConnection connection)
	{
		try
		{
			if (connection.Process is { } process && !process.HasExited)
				process.Kill(entireProcessTree: true);
		}
		catch
		{
			// The process may already be gone; the connection still has to be released.
		}

		await connection.DisposeAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Records the process a lifetime guard is asked to bind, so a test can prove the transport bound the process it
	/// spawned.
	/// </summary>
	private sealed class RecordingChildProcessLifetimeGuard : IChildProcessLifetimeGuard
	{
		public Process? BoundProcess { get; private set; }

		public void Bind(Process process, ILogger? logger)
			=> BoundProcess = process;
	}

	/// <summary>
	/// Builds client options with the short transport budgets the seam tests rely on.
	/// </summary>
	/// <param name="transport">The transport to configure.</param>
	/// <returns>The configured options.</returns>
	private static LanguageServerClientOptions CreateSeamOptions(ILanguageServerTransport transport)
		=> LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
			Transport = transport
		};
}
