using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class StdioLanguageServerTransportTests
{
	private static readonly LanguageServerConnectRequest s_request = new() { Logger = NullLogger.Instance };

	[TestMethod]
	public void Constructor_WithABlankExecutablePath_Throws()
	{
		ArgumentException blank = Assert.ThrowsExactly<ArgumentException>(() => new StdioLanguageServerTransport("   "));
		ArgumentNullException missing = Assert.ThrowsExactly<ArgumentNullException>(() => new StdioLanguageServerTransport(null!));

		Assert.AreEqual("serverExecutablePath", blank.ParamName);
		Assert.AreEqual("serverExecutablePath", missing.ParamName);
	}

	[TestMethod]
	public void ConnectAsync_WithNullRequest_ThrowsArgumentNullException()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new StdioLanguageServerTransport("example-language-server.exe").ConnectAsync(null!, CancellationToken.None)
		);

		Assert.AreEqual("request", exception.ParamName);
	}

	[TestMethod]
	public void ConnectAsync_WithACanceledToken_ThrowsBeforeSpawning()
	{
		using var startupCancellation = new CancellationTokenSource();
		startupCancellation.Cancel();

		int guardRequests = 0;

		var transport = new StdioLanguageServerTransport(
			"example-language-server.exe",

			new StdioLanguageServerTransportOptions
			{
				ChildProcessLifetimeGuardFactory = () =>
				{
					guardRequests++;
					return new RecordingChildProcessLifetimeGuard();
				},
			}
		);

		Assert.ThrowsExactly<OperationCanceledException>(() => transport.ConnectAsync(s_request, startupCancellation.Token));
		Assert.AreEqual(0, guardRequests);
	}

	[TestMethod]
	public void ToString_NamesTheExecutable() =>
		Assert.Contains("example-language-server.exe", new StdioLanguageServerTransport("example-language-server.exe").ToString());

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task ConnectAsync_WithAStartableExecutable_ReturnsALiveConnection()
	{
		(string fileName, string[] arguments) = CreateLongRunningCommand();
		var transport = new StdioLanguageServerTransport(fileName, new StdioLanguageServerTransportOptions { ServerArguments = arguments });

		// Spawns a long-running child so the transport's success path is exercised on every platform.
		ILanguageServerConnection connection = await transport.ConnectAsync(s_request, CancellationToken.None).ConfigureAwait(false);

		try
		{
			Assert.IsNotNull(connection.Process, "The stdio transport must expose the spawned process.");
			Assert.IsFalse(connection.Process.HasExited, "The spawned process must still be running.");
			Assert.IsNotNull(connection.ReadStream);
			Assert.IsNotNull(connection.WriteStream);
		}
		finally
		{
			await KillAndDisposeAsync(connection).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task ConnectAsync_LaunchesWithTheArgumentsAndEnvironmentVariablesOfTheOptions()
	{
		(string fileName, string[] arguments) = CreateLongRunningCommand();

		var transport = new StdioLanguageServerTransport(
			fileName,

			new StdioLanguageServerTransportOptions
			{
				ServerArguments = arguments,
				EnvironmentVariables = new Dictionary<string, string> { ["LS_TEST_VARIABLE"] = "ls-test-value" },
			}
		);

		ILanguageServerConnection connection = await transport.ConnectAsync(s_request, CancellationToken.None).ConfigureAwait(false);

		try
		{
			Process process = connection.Process ?? throw new AssertFailedException("The stdio transport must expose the spawned process.");

			Assert.AreSequenceEqual(arguments, [.. process.StartInfo.ArgumentList]);
			Assert.AreEqual("ls-test-value", process.StartInfo.Environment["LS_TEST_VARIABLE"]);
		}
		finally
		{
			await KillAndDisposeAsync(connection).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task ConnectAsync_WithoutAWorkingDirectory_StartsInTheExecutableDirectory()
	{
		(string fileName, string[] arguments) = CreateLongRunningCommand();
		var transport = new StdioLanguageServerTransport(fileName, new StdioLanguageServerTransportOptions { ServerArguments = arguments });

		ILanguageServerConnection connection = await transport.ConnectAsync(s_request, CancellationToken.None).ConfigureAwait(false);

		try
		{
			Assert.AreEqual(Path.GetDirectoryName(fileName), connection.Process?.StartInfo.WorkingDirectory);
		}
		finally
		{
			await KillAndDisposeAsync(connection).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task ConnectAsync_WithAnExecutableNameOnly_StartsInTheCurrentDirectory()
	{
		(string fileName, string[] arguments) = CreateLongRunningCommand();

		// The bare name resolves through the search path, and it has no directory part to start in.
		var transport = new StdioLanguageServerTransport(
			Path.GetFileName(fileName),
			new StdioLanguageServerTransportOptions { ServerArguments = arguments }
		);

		ILanguageServerConnection connection = await transport.ConnectAsync(s_request, CancellationToken.None).ConfigureAwait(false);

		try
		{
			Assert.AreEqual(Environment.CurrentDirectory, connection.Process?.StartInfo.WorkingDirectory);
		}
		finally
		{
			await KillAndDisposeAsync(connection).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task ConnectAsync_WithAWorkingDirectory_StartsInIt()
	{
		(string fileName, string[] arguments) = CreateLongRunningCommand();
		string workingDirectory = Path.GetTempPath();

		var transport = new StdioLanguageServerTransport(
			fileName,
			new StdioLanguageServerTransportOptions { ServerArguments = arguments, ServerWorkingDirectory = workingDirectory }
		);

		ILanguageServerConnection connection = await transport.ConnectAsync(s_request, CancellationToken.None).ConfigureAwait(false);

		try
		{
			// A server that resolves configuration relative to the workspace root needs the configured directory, not the executable's own.
			Assert.AreEqual(workingDirectory, connection.Process?.StartInfo.WorkingDirectory);
		}
		finally
		{
			await KillAndDisposeAsync(connection).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task ConnectAsync_BindsEachSpawnedProcessToAFreshGuardFromTheFactory()
	{
		// Pins the transport's binding contract: each connect asks the factory for a guard, and that guard receives the exact process the
		// connection exposes.
		List<RecordingChildProcessLifetimeGuard> guards = [];
		(string fileName, string[] arguments) = CreateLongRunningCommand();

		var transport = new StdioLanguageServerTransport(
			fileName,

			new StdioLanguageServerTransportOptions
			{
				ServerArguments = arguments,

				ChildProcessLifetimeGuardFactory = () =>
				{
					var guard = new RecordingChildProcessLifetimeGuard();
					guards.Add(guard);
					return guard;
				},
			}
		);

		ILanguageServerConnection first = await transport.ConnectAsync(s_request, CancellationToken.None).ConfigureAwait(false);
		ILanguageServerConnection second = await transport.ConnectAsync(s_request, CancellationToken.None).ConfigureAwait(false);

		try
		{
			Assert.HasCount(2, guards);
			Assert.AreSame(first.Process, guards[0].BoundProcess);
			Assert.AreSame(second.Process, guards[1].BoundProcess);
		}
		finally
		{
			await KillAndDisposeAsync(first).ConfigureAwait(false);
			await KillAndDisposeAsync(second).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task ConnectAsync_WhenTheGuardThrows_DoesNotStrandTheSpawnedProcess()
	{
		var guard = new ThrowingChildProcessLifetimeGuard();
		(string fileName, string[] arguments) = CreateLongRunningCommand();

		var transport = new StdioLanguageServerTransport(
			fileName,
			new StdioLanguageServerTransportOptions { ServerArguments = arguments, ChildProcessLifetimeGuardFactory = () => guard }
		);

		InvalidOperationException exception = await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => transport.ConnectAsync(s_request, CancellationToken.None))
			.ConfigureAwait(false);

		Assert.AreEqual("guard", exception.Message);

		// The transport terminates the child a failing guard could not bind, so the spawned process does not outlive
		// the aborted connection.
		Process? spawned = guard.BoundProcess;

		Assert.IsNotNull(spawned);

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(
				() =>
				{
					try
					{
						return spawned.HasExited;
					}
					catch (InvalidOperationException)
					{
						// The process handle was released with the connection, which also means it has exited.
						return true;
					}
				},

				TestPolling.DefaultTimeout
			)
		);
	}

	/// <summary>
	/// Builds a command that stays alive on the current platform, so the transport tests can spawn a real child without depending
	/// on a language-server executable.
	/// </summary>
	private static (string FileName, string[] Arguments) CreateLongRunningCommand()
	{
		return OperatingSystem.IsWindows()
			? (Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/c", "ping 127.0.0.1 -n 30 > nul"])
			: ("/bin/sh", ["-c", "sleep 30"]);
	}

	/// <summary>Terminates the connection's process (a raw connection disposal does not) and then releases the connection.</summary>
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

	/// <summary>Records the process a lifetime guard is asked to bind, so a test can prove the transport bound the process it spawned.</summary>
	private sealed class RecordingChildProcessLifetimeGuard : IChildProcessLifetimeGuard
	{
		public Process? BoundProcess { get; private set; }
		public void Bind(Process process, ILogger? logger) => BoundProcess = process;
	}

	/// <summary>A lifetime guard that throws when bound, standing in for a host guard that violates the "never throws" contract.</summary>
	private sealed class ThrowingChildProcessLifetimeGuard : IChildProcessLifetimeGuard
	{
		/// <summary>The process the transport attempted to bind, captured before the failure.</summary>
		public Process? BoundProcess { get; private set; }

		public void Bind(Process process, ILogger? logger)
		{
			BoundProcess = process;
			throw new InvalidOperationException("guard");
		}
	}
}
