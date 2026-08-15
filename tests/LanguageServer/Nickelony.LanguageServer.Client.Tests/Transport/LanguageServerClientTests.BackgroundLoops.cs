using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task WaitForBackgroundLoopsAsync_WhenLoopFaults_LogsLoopFailureWithoutDuplicateDisposalWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());

		await client.TransportHost.WaitForBackgroundLoopsAsync(
			Task.FromException(new IOException("Simulated loop failure.")),
			Task.CompletedTask).ConfigureAwait(false);

		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("background loop", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated loop failure.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsFalse(logScope.Logs.Any(log => log.Contains("Language server background loop failed during disposal.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task ObserveBackgroundLoop_WhenLoopFaultsBeforeDisposal_LogsImmediateWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());

		client.DiagnosticsRouter.ObserveBackgroundLoop(
			Task.FromException(new IOException("Simulated callback pump failure.")),
			"callback dispatcher",
			true);

		await TestPolling.UntilAsync(
			() => logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("background loop", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("callback dispatcher", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("terminated unexpectedly", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("Simulated callback pump failure.", StringComparison.Ordinal)),
			TimeSpan.FromSeconds(5),
			"The observed callback-pump failure should be logged.").ConfigureAwait(false);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("background loop", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("callback dispatcher", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("terminated unexpectedly", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated callback pump failure.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task ObserveBackgroundLoop_WhenTrackedPumpFaults_MarksReadyClientUnhealthy()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 4, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		client.DiagnosticsRouter.ObserveBackgroundLoop(
			Task.FromException(new IOException("Simulated callback pump failure.")),
			"callback dispatcher",
			true);

		await TestPolling.UntilAsync(() => !client.IsReady, TimeSpan.FromSeconds(5), "The faulted tracked pump should mark the ready client unhealthy.").ConfigureAwait(false);

		Assert.IsFalse(client.IsReady);

		await Assert.ThrowsExactlyAsync<LanguageServerTransportUnavailableException>(async () =>
			await client.SendRequestAsync<JsonElement>(
				"workspace/configuration",
				new WorkspaceConfigurationParams([]),
				CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public void EnsureTransportBackgroundLoopsRunning_WhenCallbackPumpFaulted_ReplacesTrackedCallbackPumpTask()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		Task faultedCallbackPump = Task.FromException(new IOException("Simulated callback pump failure."));

		client.DiagnosticsRouter.CallbackPumpTask = faultedCallbackPump;
		client.DiagnosticsRouter.EnsurePumpsRunning(includeDiagnosticsPump: false);

		Task replacementTask = client.DiagnosticsRouter.CallbackPumpTask;

		Assert.AreNotSame(faultedCallbackPump, replacementTask);
		Assert.IsFalse(replacementTask.IsCompleted, "Restart recovery should recreate the callback pump instead of keeping the faulted task tracked.");
	}

	[TestMethod]
	public async Task EnsureTransportBackgroundLoopsRunning_WhenFaultedPumpIsReplaced_ClearsObservedTermination()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		Task faultedCallbackPump = Task.FromException(new IOException("Simulated callback pump failure."));

		client.DiagnosticsRouter.CallbackPumpTask = faultedCallbackPump;

		client.DiagnosticsRouter.ObserveBackgroundLoop(
			faultedCallbackPump,
			"callback dispatcher",
			true);

		await TestPolling.UntilAsync(
			() => client.DiagnosticsRouter.WasObservedBackgroundLoopTermination(faultedCallbackPump),
			TimeSpan.FromSeconds(5),
			"The observed termination marker should be recorded.").ConfigureAwait(false);

		Assert.IsTrue(client.DiagnosticsRouter.WasObservedBackgroundLoopTermination(faultedCallbackPump));

		client.DiagnosticsRouter.EnsurePumpsRunning(includeDiagnosticsPump: false);

		Assert.IsFalse(client.DiagnosticsRouter.WasObservedBackgroundLoopTermination(faultedCallbackPump));
	}

	[TestMethod]
	public async Task EnsureTransportBackgroundLoopsRunning_WhenDiagnosticsPumpFaulted_RestartRecoveryStillPublishesDiagnostics()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession originalSession = CreateTransportSession(client, 4, process: null, Stream.Null, Stream.Null);
		var publishedMessage = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		Task faultedDiagnosticsPump = Task.FromException(new IOException("Simulated diagnostics pump failure."));

		SetActiveSession(client, originalSession);
		SetReadyState(client, true);

		client.DiagnosticsPublished += (_, eventArgs) => publishedMessage.TrySetResult(eventArgs.Parameters.Diagnostics?[0].Message);

		client.DiagnosticsRouter.DiagnosticsPumpTask = faultedDiagnosticsPump;

		client.DiagnosticsRouter.ObserveBackgroundLoop(
			faultedDiagnosticsPump,
			"diagnostics pump",
			true);

		await TestPolling.UntilAsync(() => !client.IsReady, TimeSpan.FromSeconds(5), "The faulted diagnostics pump should mark the ready client unhealthy.").ConfigureAwait(false);

		Assert.IsFalse(client.IsReady);

		TransportSession restartedSession = CreateTransportSession(client, 5, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, restartedSession);
		client.DiagnosticsRouter.EnsurePumpsRunning(includeDiagnosticsPump: true);
		SetReadyState(client, true);

		client.DiagnosticsRouter.QueueDiagnosticsPublished(
			GetTransportGeneration(restartedSession),
			CreateDiagnosticsParameters("file:///C:/Workspace/test.ext", "Recovered warning."));

		Assert.AreEqual("Recovered warning.",
			await publishedMessage.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task WaitWithDisposeBudgetAsync_WhenLoopAlreadyLogged_DoesNotLogDuplicateDisposalWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		Task faultedLoopTask = Task.FromException(new IOException("Simulated callback pump failure."));

		client.DiagnosticsRouter.CallbackPumpTask = faultedLoopTask;
		client.DiagnosticsRouter.ObserveBackgroundLoop(faultedLoopTask, "callback dispatcher", true);

		await TestPolling.UntilAsync(
			() => logScope.Logs.Any(log => log.Contains("Simulated callback pump failure.", StringComparison.Ordinal)),
			TimeSpan.FromSeconds(5),
			"The observed loop failure should be logged before disposal waits.").ConfigureAwait(false);

		// Driving the real teardown reaches the callback-pump wait stage; an already-observed loop failure
		// must not be reported again as a disposal-stage exception.
		client.Dispose();

		Assert.IsFalse(logScope.Logs.Any(log => log.Contains("teardown stage 'callback dispatcher' raised an exception", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.AreEqual(1, logScope.Logs.Count(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("callback dispatcher", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated callback pump failure.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task WaitForBackgroundLoopsAsync_WhenLoopIsCanceled_LogsDebugWithoutWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());

		await client.TransportHost.WaitForBackgroundLoopsAsync(
			Task.FromCanceled(new CancellationToken(canceled: true)),
			Task.CompletedTask).ConfigureAwait(false);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("was canceled during disposal", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("JSON-RPC completion", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("background loop", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}
}
