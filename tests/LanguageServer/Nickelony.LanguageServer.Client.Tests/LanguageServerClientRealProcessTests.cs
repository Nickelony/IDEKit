using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Exercises the client over the real stdio transport against the compiled fake server.</summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
public sealed class LanguageServerClientRealProcessTests
{
	[TestMethod]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task EnsureStartedAsync_WithRealProcess_CompletesHandshakeCapturesStandardErrorAndShutsDown()
	{
		string fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		using var logger = new RecordingLogger(LogLevel.Debug);

		LanguageServerClientOptions options = LanguageServerClientOptions.Default with
		{
			InitializeRequestTimeout = TimeSpan.FromSeconds(30),
			ShutdownRequestTimeout = TimeSpan.FromSeconds(10),
			RequireTextDocumentSynchronization = false,
		};

		var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [Path.GetTempPath()],

			transport: new StdioLanguageServerTransport(
				GetDotnetExecutableName(),
				new StdioLanguageServerTransportOptions { ServerArguments = [fakeServerAssemblyPath] }
			),

			hooks: null,
			options,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		try
		{
			// The dotnet host runs the compiled fake server, so process spawning, stream wiring, framing, and
			// standard-error reader all run on real pipes in this test.
			bool ready = await client
				.EnsureStartedAsync(CancellationToken.None)
				.WaitAsync(TimeSpan.FromSeconds(60), TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.IsTrue(ready, $"The real process handshake should complete. Logs: {string.Join("; ", logger.Logs)}");
			Assert.IsTrue(client.IsReady);
			Assert.AreEqual(1L, client.ConnectionGeneration);
			Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);
			Assert.IsNull(client.LastStartupException);

			await WaitForLogAsync(logger, "fake-server stderr marker", TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationToken).ConfigureAwait(false);
		}

		Assert.IsFalse(client.IsReady);
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => client.EnsureStartedAsync(CancellationToken.None)).ConfigureAwait(false);
	}

	[TestMethod]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task EnsureStartedAsync_WithServerAdvertisedCapabilities_CompletesTheDefaultOptionsHandshake()
	{
		string fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		using var logger = new RecordingLogger(LogLevel.Debug);

		LanguageServerClientOptions options = LanguageServerClientOptions.Default with
		{
			InitializeRequestTimeout = TimeSpan.FromSeconds(30),
			ShutdownRequestTimeout = TimeSpan.FromSeconds(10),
		};

		var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [Path.GetTempPath()],

			transport: new StdioLanguageServerTransport(
				GetDotnetExecutableName(),
				new StdioLanguageServerTransportOptions { ServerArguments = [fakeServerAssemblyPath, "--capabilities", """{"textDocumentSync":2}"""] }
			),

			hooks: null,
			options,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		try
		{
			// The default options require text-document synchronization, so the server-advertised capability payload
			// is what lets a default-options handshake succeed against the compiled fake server.
			bool ready = await client
				.EnsureStartedAsync(CancellationToken.None)
				.WaitAsync(TimeSpan.FromSeconds(60), TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.IsTrue(ready, $"The scripted-capability handshake should complete. Logs: {string.Join("; ", logger.Logs)}");
			Assert.IsTrue(client.IsReady);
			Assert.AreEqual(TextDocumentSyncKind.Incremental, client.NegotiatedCapabilities.TextDocumentSyncKind);
			Assert.IsNull(client.LastStartupException);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationToken).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task SendRequestAsync_BeyondTheInitializeHandshake_RoundTripsThroughTheStdioTransport()
	{
		string fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		using var logger = new RecordingLogger(LogLevel.Debug);

		LanguageServerClientOptions options = LanguageServerClientOptions.Default with
		{
			InitializeRequestTimeout = TimeSpan.FromSeconds(30),
			ShutdownRequestTimeout = TimeSpan.FromSeconds(10),
		};

		var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [Path.GetTempPath()],

			transport: new StdioLanguageServerTransport(
				GetDotnetExecutableName(),
				new StdioLanguageServerTransportOptions { ServerArguments = [fakeServerAssemblyPath, "--capabilities", """{"textDocumentSync":2}"""] }
			),

			hooks: null,
			options,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		try
		{
			Assert.IsTrue(
				await client
					.EnsureStartedAsync(CancellationToken.None)
					.WaitAsync(TimeSpan.FromSeconds(60), TestContext.CancellationToken)
					.ConfigureAwait(false),

				$"The handshake should complete before the round trip. Logs: {string.Join("; ", logger.Logs)}"
			);

			// The fake server answers this request over the real stdio pipes, so this proves a request/response round
			// trip beyond the initialize handshake on the actual transport rather than a scripted one.
			JsonElement result = await client
				.SendRequestAsync<JsonElement>("example/echo", new { value = "ping" }, CancellationToken.None)
				.WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.AreEqual("ping", result.GetProperty("echoed").GetProperty("value").GetString());
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationToken).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(180000, CooperativeCancellation = true)]
	public async Task EnsureStartedAsync_WhenTheServerExitsBeforeTheHandshake_ReturnsFalseAndKeepsTheStandardErrorContext()
	{
		string fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		using var logger = new RecordingLogger(LogLevel.Debug);

		LanguageServerClientOptions options = LanguageServerClientOptions.Default with
		{
			InitializeRequestTimeout = TimeSpan.FromSeconds(30),
			RequireTextDocumentSynchronization = false,
		};

		var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [Path.GetTempPath()],

			transport: new StdioLanguageServerTransport(
				GetDotnetExecutableName(),
				new StdioLanguageServerTransportOptions { ServerArguments = [fakeServerAssemblyPath, "--exit-immediately"] }
			),

			hooks: null,
			options,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		try
		{
			bool ready = await client
				.EnsureStartedAsync(CancellationToken.None)
				.WaitAsync(TimeSpan.FromSeconds(60), TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.IsFalse(ready);
			Assert.IsFalse(client.IsReady);
			Assert.IsNotNull(client.LastStartupException);

			// The crash output written before the exit must still be captured for diagnostics.
			await WaitForLogAsync(logger, "fake-server exiting immediately by request", TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationToken).ConfigureAwait(false);
		}
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WithMissingExecutable_ReturnsFalseAndReportsTheFailure()
	{
		string missingExecutablePath = Path.Combine(Path.GetTempPath(), $"missing-language-server-{Guid.NewGuid():N}.exe");
		using var logger = new RecordingLogger(LogLevel.Debug);

		var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [Path.GetTempPath()],
			transport: new StdioLanguageServerTransport(missingExecutablePath),
			options: LanguageServerClientOptions.Default,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		try
		{
			bool ready = await client
				.EnsureStartedAsync(CancellationToken.None)
				.WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.IsFalse(ready);
			Assert.IsFalse(client.IsReady);
			Assert.IsNotNull(client.LastStartupException);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationToken).ConfigureAwait(false);
		}
	}

	private static Task WaitForLogAsync(RecordingLogger logger, string expectedText, TimeSpan timeout)
	{
		return TestPolling.UntilAsync(
			() => logger.Logs.Any(log => log.Contains(expectedText, StringComparison.Ordinal)),
			timeout,
			$"Expected a log entry containing '{expectedText}'."
		);
	}

	/// <summary>The path of the compiled fake server assembly, injected at build time.</summary>
	/// <returns>The fake server assembly path.</returns>
	/// <exception cref="AssertFailedException">The build output was not found, which means the test project build is broken.</exception>
	private static string GetFakeServerAssemblyPath()
	{
		string? outputDirectory = typeof(LanguageServerClientRealProcessTests)
			.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => attribute.Key == "FakeServerOutputDirectory")
			?.Value;

		if (outputDirectory is { Length: > 0 } && Directory.Exists(outputDirectory))
		{
			// The output is searched recursively so a target-framework change in the fake-server project cannot
			// make the lookup miss the assembly and fail the only real-process tests.
			string? assemblyPath = Directory
				.EnumerateFiles(outputDirectory, "Nickelony.LanguageServer.Client.Tests.FakeServer.dll", SearchOption.AllDirectories)
				.FirstOrDefault();

			if (assemblyPath is not null)
				return assemblyPath;
		}

		throw new AssertFailedException(
			$"The fake server build output was not found under '{outputDirectory}'; the test-project build must produce Nickelony.LanguageServer.Client.Tests.FakeServer.dll."
		);
	}

	/// <summary>Returns the dotnet host command that runs the fake server assembly.</summary>
	/// <returns>The dotnet host command name.</returns>
	private static string GetDotnetExecutableName() => "dotnet";

	public TestContext? TestContext { get; set; }
}
