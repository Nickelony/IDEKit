using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	// The gate that decides whether a queued payload may still be delivered throws for connection generation 4, so a payload queued for
	// that generation makes the diagnostics coalescer fail for real; every other generation is accepted.
	private static readonly Func<long, bool> s_diagnosticsCoalescerFaultGate = static generation =>
		generation == 4 ? throw new IOException("Simulated diagnostics coalescer failure.") : true;

	[TestMethod]
	public async Task DiagnosticsCoalescer_WhenItFaultsWhileTheClientIsReady_MarksTheClientUnhealthy()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,
			serverMessageGate: s_diagnosticsCoalescerFaultGate
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 4,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		client.Composition.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();
		client.Composition.Router.QueueDiagnosticsPublished(4, CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Fatal warning."));

		await TestPolling
			.UntilAsync(
				() => !client.IsReady,
				TestPolling.DefaultTimeout,
				"The faulted diagnostics coalescer should mark the ready client unhealthy."
			)
			.ConfigureAwait(false);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () =>
				await client
					.SendRequestAsync<JsonElement>("workspace/configuration", new WorkspaceConfigurationParams([]), CancellationToken.None)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task DiagnosticsCoalescer_AfterARestart_PublishesDiagnosticsAgain()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,
			serverMessageGate: s_diagnosticsCoalescerFaultGate
		);

		ConnectionSession originalSession = CreateConnectionSession(
			client,
			generation: 4,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var publishedMessage = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, originalSession);
		SetReadyState(client, true);

		client.DiagnosticsPublished += (_, eventArgs) => publishedMessage.TrySetResult(eventArgs.Parameters.Diagnostics?[0].Message);

		client.Composition.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();
		client.Composition.Router.QueueDiagnosticsPublished(4, CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Fatal warning."));

		await TestPolling
			.UntilAsync(
				() => !client.IsReady,
				TestPolling.DefaultTimeout,
				"The faulted diagnostics coalescer should mark the ready client unhealthy."
			)
			.ConfigureAwait(false);

		ConnectionSession restartedSession = CreateConnectionSession(
			client,
			generation: 5,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, restartedSession);
		client.Composition.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();
		SetReadyState(client, true);

		client.Composition.Router.QueueDiagnosticsPublished(
			GetConnectionGeneration(restartedSession),
			CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Recovered warning.")
		);

		Assert.AreEqual(
			"Recovered warning.",
			await publishedMessage.Task.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false)
		);
	}

	[TestMethod]
	public async Task Dispose_WhenALoopAlreadyReportedItsFailure_DoesNotReportItAgain()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>(),
			serverMessageGate: s_diagnosticsCoalescerFaultGate
		);

		client.Composition.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();
		client.Composition.Router.QueueDiagnosticsPublished(4, CreateDiagnosticsParameters(TestPaths.FileUri("test.ext"), "Fatal warning."));

		await TestPolling
			.UntilAsync(
				() => logger.Logs.Any(log => log.Contains("Simulated diagnostics coalescer failure.", StringComparison.Ordinal)),
				TestPolling.DefaultTimeout,
				"The loop failure should be logged before disposal waits."
			)
			.ConfigureAwait(false);

		// Driving the real teardown reaches the diagnostics-coalescer wait stage; a failure that the observer already reported
		// must not be reported again as a disposal-stage exception.
		client.Dispose();

		Assert.DoesNotContain(
			log => log.Contains("teardown stage 'diagnostics coalescer' raised an exception", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.ContainsSingle(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("diagnostics coalescer", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("Simulated diagnostics coalescer failure.", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	public TestContext? TestContext { get; set; }
}
