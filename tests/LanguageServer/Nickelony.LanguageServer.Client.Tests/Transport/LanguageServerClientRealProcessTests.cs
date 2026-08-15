using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Exercises the real process transport: a compiled fake language server, hosted by the dotnet executable,
/// answers the handshake over stdio, writes a stderr marker, and is shut down through the graceful
/// shutdown/exit handshake.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Integration)]
public sealed class LanguageServerClientRealProcessTests
{
	[TestMethod]
	[Timeout(180000)]
	public async Task StartAsync_WithRealProcess_CompletesHandshakeCapturesStderrAndShutsDown()
	{
		string fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		using var loggerScope = new TestLoggerScope(LogLevel.Debug);
		var options = LanguageServerClientOptions.Default with
		{
			InitializeTimeout = TimeSpan.FromSeconds(30),
			ShutdownRequestTimeout = TimeSpan.FromSeconds(10),
			RequireTextDocumentSynchronization = false,
			ServerArguments = [fakeServerAssemblyPath]
		};

		var client = new LanguageServerClient(
			[Path.GetTempPath()],
			GetDotnetExecutableName(),
			options,
			loggerScope.CreateLogger<LanguageServerClient>());

		try
		{
			// The dotnet host runs the compiled fake server, so process spawning, stream wiring, framing, and
			// stderr capture all run on real pipes in this test.
			bool ready = await client.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);

			Assert.IsTrue(ready, $"The real process handshake should complete. Logs: {string.Join("; ", loggerScope.Logs)}");
			Assert.IsTrue(client.IsReady);
			Assert.AreEqual(1L, client.TransportGeneration);
			Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
			Assert.IsNull(client.LastStartupException);

			await WaitForLogAsync(loggerScope, "fake-server stderr marker", TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}

		Assert.IsFalse(client.IsReady);
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => client.StartAsync(CancellationToken.None)).ConfigureAwait(false);
	}

	[TestMethod]
	[Timeout(180000)]
	public async Task StartAsync_WithServerAdvertisedCapabilities_CompletesTheDefaultOptionsHandshake()
	{
		string fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		using var loggerScope = new TestLoggerScope(LogLevel.Debug);
		var options = LanguageServerClientOptions.Default with
		{
			InitializeTimeout = TimeSpan.FromSeconds(30),
			ShutdownRequestTimeout = TimeSpan.FromSeconds(10),
			ServerArguments = [fakeServerAssemblyPath, "--capabilities", """{"textDocumentSync":2}"""]
		};

		var client = new LanguageServerClient(
			[Path.GetTempPath()],
			GetDotnetExecutableName(),
			options,
			loggerScope.CreateLogger<LanguageServerClient>());

		try
		{
			// The default options require text-document synchronization, so the server-advertised capability payload
			// is what lets a default-options handshake succeed against the compiled fake server.
			bool ready = await client.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);

			Assert.IsTrue(ready, $"The scripted-capability handshake should complete. Logs: {string.Join("; ", loggerScope.Logs)}");
			Assert.IsTrue(client.IsReady);
			Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);
			Assert.IsNull(client.LastStartupException);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(180000)]
	public async Task SendRequestAsync_BeyondTheInitializeHandshake_RoundTripsThroughTheStdioTransport()
	{
		string fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		using var loggerScope = new TestLoggerScope(LogLevel.Debug);
		var options = LanguageServerClientOptions.Default with
		{
			InitializeTimeout = TimeSpan.FromSeconds(30),
			ShutdownRequestTimeout = TimeSpan.FromSeconds(10),
			ServerArguments = [fakeServerAssemblyPath, "--capabilities", """{"textDocumentSync":2}"""]
		};

		var client = new LanguageServerClient(
			[Path.GetTempPath()],
			GetDotnetExecutableName(),
			options,
			loggerScope.CreateLogger<LanguageServerClient>());

		try
		{
			Assert.IsTrue(await client.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false),
				$"The handshake should complete before the round trip. Logs: {string.Join("; ", loggerScope.Logs)}");

			// The fake server answers this request over the real stdio pipes, so this proves a request/response round
			// trip beyond the initialize handshake on the actual transport rather than a scripted one.
			JsonElement result = await client
				.SendRequestAsync<JsonElement>("example/echo", new { value = "ping" }, CancellationToken.None)
				.WaitAsync(TimeSpan.FromSeconds(30))
				.ConfigureAwait(false);

			Assert.AreEqual("ping", result.GetProperty("echoed").GetProperty("value").GetString());
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}
	}

	[TestMethod]
	[Timeout(180000)]
	public async Task StartAsync_WhenTheServerExitsBeforeTheHandshake_ReturnsFalseAndKeepsTheStderrContext()
	{
		string fakeServerAssemblyPath = GetFakeServerAssemblyPath();

		using var loggerScope = new TestLoggerScope(LogLevel.Debug);
		var options = LanguageServerClientOptions.Default with
		{
			InitializeTimeout = TimeSpan.FromSeconds(30),
			RequireTextDocumentSynchronization = false,
			ServerArguments = [fakeServerAssemblyPath, "--exit-immediately"]
		};

		var client = new LanguageServerClient(
			[Path.GetTempPath()],
			GetDotnetExecutableName(),
			options,
			loggerScope.CreateLogger<LanguageServerClient>());

		try
		{
			bool ready = await client.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);

			Assert.IsFalse(ready);
			Assert.IsFalse(client.IsReady);
			Assert.IsNotNull(client.LastStartupException);

			// The crash output written before the exit must still be captured for diagnostics.
			await WaitForLogAsync(loggerScope, "fake-server exiting immediately by request", TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}
	}

	[TestMethod]
	public async Task StartAsync_WithMissingExecutable_ReturnsFalseAndReportsTheFailure()
	{
		string missingExecutablePath = Path.Combine(Path.GetTempPath(), $"missing-language-server-{Guid.NewGuid():N}.exe");
		using var loggerScope = new TestLoggerScope(LogLevel.Debug);

		var client = new LanguageServerClient(
			[Path.GetTempPath()],
			missingExecutablePath,
			LanguageServerClientOptions.Default,
			loggerScope.CreateLogger<LanguageServerClient>());

		try
		{
			bool ready = await client.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

			Assert.IsFalse(ready);
			Assert.IsFalse(client.IsReady);
			Assert.IsNotNull(client.LastStartupException);
		}
		finally
		{
			await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}
	}

	private static Task WaitForLogAsync(TestLoggerScope loggerScope, string expectedText, TimeSpan timeout)
		=> TestPolling.UntilAsync(
			() => loggerScope.Logs.Any(log => log.Contains(expectedText, StringComparison.Ordinal)),
			timeout,
			$"Expected a log entry containing '{expectedText}'.");

	/// <summary>
	/// Gets the path of the compiled fake server assembly injected at build time.
	/// </summary>
	/// <returns>The fake server assembly path.</returns>
	/// <exception cref="AssertFailedException">The build output was not found, which means the test project build is broken.</exception>
	private static string GetFakeServerAssemblyPath()
	{
		string? outputDirectory = typeof(LanguageServerClientRealProcessTests).Assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => attribute.Key == "FakeServerOutputDirectory")?.Value;

		if (outputDirectory is { Length: > 0 } && Directory.Exists(outputDirectory))
		{
			// The output is searched recursively so a target-framework change in the fake-server project cannot
			// silently turn the only real-process tests into skips.
			string? assemblyPath = Directory
				.EnumerateFiles(outputDirectory, "Nickelony.LanguageServer.Client.FakeServer.dll", SearchOption.AllDirectories)
				.FirstOrDefault();

			if (assemblyPath is not null)
				return assemblyPath;
		}

		throw new AssertFailedException(
			$"The fake server build output was not found under '{outputDirectory}'; the test-project build must produce Nickelony.LanguageServer.Client.FakeServer.dll.");
	}

	/// <summary>
	/// Gets the dotnet host command used to run the fake server assembly.
	/// </summary>
	/// <returns>The dotnet host command name.</returns>
	private static string GetDotnetExecutableName() => "dotnet";
}
