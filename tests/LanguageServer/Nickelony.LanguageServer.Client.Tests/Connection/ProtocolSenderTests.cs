using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ProtocolSenderTests
{
	private static readonly object s_parameters = new { value = 1 };

	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public async Task SendRequestAsync_WhenReady_ReturnsTheServerResult()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink();

		host.MakeReady(link.Session);
		link.AnswerRequestsWith("answer");

		string result = await host.Sender.SendRequestAsync<string>("test/request", s_parameters, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual("answer", result);
	}

	[TestMethod]
	public async Task SendRequestAsync_BeforeAnySessionIsReady_ThrowsConnectionUnavailable()
	{
		var host = new ClientCollaboratorTestHost();

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(() =>
				host.Sender.SendRequestAsync<string>("test/request", s_parameters, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_AfterTheClientStartedDisposal_ThrowsObjectDisposed()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink();

		host.MakeReady(link.Session);
		_ = host.Lifetime.BeginDisposeAsync(static () => Task.CompletedTask);

		await Assert
			.ThrowsExactlyAsync<ObjectDisposedException>(() =>
				host.Sender.SendRequestAsync<string>("test/request", s_parameters, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_ServerRejectsTheRequest_ThrowsRejectedAndKeepsTheConnectionReady()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink();

		host.MakeReady(link.Session);
		link.RejectRequestsWith(errorCode: 7);

		LanguageServerRequestRejectedException exception = await Assert
			.ThrowsExactlyAsync<LanguageServerRequestRejectedException>(() =>
				host.Sender.SendRequestAsync<string>("test/request", s_parameters, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);

		Assert.AreEqual(7, exception.ErrorCode);
		Assert.IsTrue(host.State.IsReady, "A JSON-RPC error response is a server outcome and leaves the connection usable.");
		Assert.IsEmpty(host.UnhealthyMarks);
	}

	[TestMethod]
	public async Task SendRequestAsync_ConnectionLost_MarksTheGenerationUnhealthyAndThrowsConnectionUnavailable()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var host = new ClientCollaboratorTestHost(logger);
		using var link = new FakeServerLink(generation: 4);

		host.MakeReady(link.Session);

		link.DropConnection();
		await link.Session.JsonRpc!.Completion.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(() =>
				host.Sender.SendRequestAsync<string>("test/request", s_parameters, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);

		Assert.AreSequenceEqual([4], [.. host.UnhealthyMarks]);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("generation 4", StringComparison.Ordinal),
			logger.Logs
		);
	}

	[TestMethod]
	public async Task SendRequestAsync_SessionSupersededBeforeTheResultArrives_ThrowsSuperseded()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink(generation: 1);
		using var replacement = new FakeServerLink(generation: 2);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		host.MakeReady(link.Session);
		link.AnswerRequestsWith("late", release.Task);

		Task<string> request = host.Sender.SendRequestAsync<string>("test/request", s_parameters, TestContext.CancellationToken);

		host.MakeReady(replacement.Session);
		release.SetResult();

		await Assert.ThrowsExactlyAsync<LanguageServerConnectionSupersededException>(() => request).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_RejectionFromASupersededSession_ThrowsSupersededInsteadOfRejected()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink(generation: 1);
		using var replacement = new FakeServerLink(generation: 2);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		host.MakeReady(link.Session);
		link.RejectRequestsWith(errorCode: 7, release.Task);

		Task<string> request = host.Sender.SendRequestAsync<string>("test/request", s_parameters, TestContext.CancellationToken);

		host.MakeReady(replacement.Session);
		release.SetResult();

		await Assert.ThrowsExactlyAsync<LanguageServerConnectionSupersededException>(() => request).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_CallerCancelsWhileWaiting_ThrowsCanceledWithoutMarkingTheConnection()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		host.MakeReady(link.Session);
		link.AnswerRequestsWith("never", release.Task);

		using var cancellation = new CancellationTokenSource();

		Task<string> request = host.Sender.SendRequestAsync<string>("test/request", s_parameters, cancellation.Token);

		await cancellation.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(() => request).ConfigureAwait(false);

		Assert.IsEmpty(host.UnhealthyMarks);
		Assert.IsTrue(host.State.IsReady);

		release.SetResult();
	}

	[TestMethod]
	public async Task SendRequestCoreAsync_CallerCancelsAndTheRequestFailsLater_LogsTheLateFaultAtDebug()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var host = new ClientCollaboratorTestHost(logger);
		using var link = new FakeServerLink();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		host.MakeReady(link.Session);
		link.RejectRequestsWith(errorCode: 7, release.Task);

		using var cancellation = new CancellationTokenSource();

		Task<string> request = host.Sender.SendRequestCoreAsync<string>(
			session: link.Session,
			method: "test/request",
			parameters: s_parameters,
			disposedAccess: DisposedAccessPolicy.Enforce,
			cancellationToken: cancellation.Token
		);

		await cancellation.CancelAsync().ConfigureAwait(false);
		await Assert.ThrowsAsync<OperationCanceledException>(() => request).ConfigureAwait(false);

		release.SetResult();

		await TestPolling
			.UntilAsync(
				() =>
					logger.Logs.Any(static log =>
						log.StartsWith("Debug|", StringComparison.Ordinal)
						&& log.Contains("abandoned language server connection task", StringComparison.Ordinal)
					),

				TestPolling.DefaultTimeout,
				"The late fault of the abandoned request should be logged."
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestCoreAsync_WithoutAJsonRpcInstance_ThrowsIOException()
	{
		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);

		await Assert
			.ThrowsExactlyAsync<IOException>(() =>
				host.Sender.SendRequestCoreAsync<string>(
					session,
					method: "test/request",
					parameters: s_parameters,
					disposedAccess: DisposedAccessPolicy.Enforce,
					cancellationToken: TestContext.CancellationToken
				)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestCoreAsync_WhenDisposedAccessIsAllowed_StillSendsAfterDisposalStarted()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink();

		link.AnswerRequestsWith("answer");
		_ = host.Lifetime.BeginDisposeAsync(static () => Task.CompletedTask);

		string result = await host
			.Sender.SendRequestCoreAsync<string>(
				session: link.Session,
				method: "test/request",
				parameters: s_parameters,
				disposedAccess: DisposedAccessPolicy.Allow,
				cancellationToken: TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual("answer", result);
	}

	[TestMethod]
	public async Task SendNotificationAsync_WhenReady_DeliversTheNotificationToTheServer()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink();

		host.MakeReady(link.Session);

		await host.Sender.SendNotificationAsync("test/note", new { value = 5 }, TestContext.CancellationToken).ConfigureAwait(false);

		await TestPolling.UntilAsync(() => link.Notes.Count == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual(5, link.Notes[0].GetProperty("value").GetInt32());
	}

	[TestMethod]
	public async Task SendNotificationAsync_BeforeAnySessionIsReady_ThrowsConnectionUnavailable()
	{
		var host = new ClientCollaboratorTestHost();

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(() =>
				host.Sender.SendNotificationAsync("test/note", parameters: null, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendNotificationAsync_DidChangeConfiguration_SendsTheSettingsAndRefreshesTheSnapshotAfterwards()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink();

		host.MakeReady(link.Session);

		await host
			.Sender.SendNotificationAsync(
				LspMethodNames.DidChangeConfiguration,
				new { settings = new { editor = new { fontSize = 20 } } },
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		await TestPolling.UntilAsync(() => link.ConfigurationChanges.Count == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual(20, link.ConfigurationChanges[0].GetProperty("settings").GetProperty("editor").GetProperty("fontSize").GetInt32());

		JsonElement?[] answered = ConfigurationResponseBuilder.Build(
			new WorkspaceConfigurationParams([new WorkspaceConfigurationItemPayload("editor.fontSize")]),
			host.SettingsCache,
			NullLogger.Instance
		);

		Assert.AreEqual(
			20,
			answered[0]!.Value.GetInt32(),
			"The cached snapshot answers configuration requests with the settings the host just sent."
		);
	}

	[TestMethod]
	public async Task SendNotificationAsync_ConnectionLost_MarksTheGenerationUnhealthyAndThrowsConnectionUnavailable()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink(generation: 6);

		host.MakeReady(link.Session);

		link.DropConnection();
		await link.Session.JsonRpc!.Completion.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(() =>
				host.Sender.SendNotificationAsync("test/note", new { value = 1 }, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);

		Assert.AreSequenceEqual([6], [.. host.UnhealthyMarks]);
	}

	[TestMethod]
	public async Task PushSettingsAsync_BeforeAnySessionIsReady_ThrowsBeforeRunningTheSettingsProvider()
	{
		int providerCalls = 0;

		var host = new ClientCollaboratorTestHost(settingsProvider: () =>
		{
			providerCalls++;
			return new JsonObject();
		});

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(() => host.Sender.PushSettingsAsync(TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreEqual(0, providerCalls);
	}

	[TestMethod]
	public async Task PushSettingsAsync_WhenReady_SendsTheSettingsOfTheProvider()
	{
		var host = new ClientCollaboratorTestHost(settingsProvider: static () => new JsonObject { ["editor"] = new JsonObject { ["tabSize"] = 4 } });
		using var link = new FakeServerLink();

		host.MakeReady(link.Session);

		await host.Sender.PushSettingsAsync(TestContext.CancellationToken).ConfigureAwait(false);

		await TestPolling.UntilAsync(() => link.ConfigurationChanges.Count == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual(4, link.ConfigurationChanges[0].GetProperty("settings").GetProperty("editor").GetProperty("tabSize").GetInt32());
	}

	[TestMethod]
	public async Task SendNotificationCoreAsync_WithoutAJsonRpcInstance_ThrowsIOException()
	{
		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);

		await Assert
			.ThrowsExactlyAsync<IOException>(() =>
				host.Sender.SendNotificationCoreAsync(
					session,
					method: "test/note",
					parameters: null,
					disposedAccess: DisposedAccessPolicy.Enforce,
					cancellationToken: TestContext.CancellationToken
				)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendNotificationCoreAsync_WithACanceledToken_ThrowsBeforeSending()
	{
		var host = new ClientCollaboratorTestHost();
		using var link = new FakeServerLink();
		using var cancellation = new CancellationTokenSource();

		await cancellation.CancelAsync().ConfigureAwait(false);

		await Assert
			.ThrowsAsync<OperationCanceledException>(() =>
				host.Sender.SendNotificationCoreAsync(
					session: link.Session,
					method: "test/note",
					parameters: new { value = 1 },
					disposedAccess: DisposedAccessPolicy.Enforce,
					cancellationToken: cancellation.Token
				)
			)
			.ConfigureAwait(false);

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsEmpty(link.Notes);
	}
}
