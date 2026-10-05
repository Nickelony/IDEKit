using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	/// <summary>
	/// Pins the detached-session cleanup chain: a second failed-session cleanup must not start until the first has finished, so two near-simultaneous
	/// connection failures never tear down their resources concurrently.
	/// </summary>
	[TestMethod]
	public async Task DetachedSessionCleanup_WhenTwoSessionsFail_SerializesTheSecondBehindTheFirst()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstDisposeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondDisposeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var firstConnection = new GatedLanguageServerConnection(releaseFirst.Task, firstDisposeStarted);
		var secondConnection = new GatedLanguageServerConnection(Task.CompletedTask, secondDisposeStarted);

		var firstSession = new ConnectionSession(connectionGeneration: 1, process: null, readStream: Stream.Null, writeStream: Stream.Null)
		{
			Connection = firstConnection,
		};

		var secondSession = new ConnectionSession(connectionGeneration: 2, process: null, readStream: Stream.Null, writeStream: Stream.Null)
		{
			Connection = secondConnection,
		};

		SetActiveSession(client, firstSession);

		client.Composition.Supervisor.HandleJsonRpcDisconnected(
			firstSession,
			new JsonRpcDisconnectedEventArgs("first detached session", DisconnectedReason.StreamError)
		);

		// The first cleanup is parked inside its connection disposal, so the second cleanup has a prior task to wait on.
		Assert.IsTrue(await firstDisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken));

		SetActiveSession(client, secondSession);

		client.Composition.Supervisor.HandleJsonRpcDisconnected(
			secondSession,
			new JsonRpcDisconnectedEventArgs("second detached session", DisconnectedReason.StreamError)
		);

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsFalse(secondDisposeStarted.Task.IsCompleted, "The second cleanup must wait for the first to finish.");

		releaseFirst.TrySetResult(true);

		Assert.IsTrue(await secondDisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken));

		await client
			.Composition.FailedSessionDispatcher.QueuedDisposalTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(1, firstConnection.DisposeCount);
		Assert.AreEqual(1, secondConnection.DisposeCount);
	}
}
