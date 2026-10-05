namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers that the calls the host makes on one document reach the scheduler in the order it made them, also while the first of them waits
/// for the connection to start.
/// </summary>
[TestClass]
public sealed class DocumentSynchronizerCallOrderTests
{
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static readonly string s_filePath = TestPaths.Script("a.lua");

	/// <summary>A connection start that parks until the test releases it.</summary>
	private sealed class GatedStartup
	{
		private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Task Entered => _entered.Task;

		internal Func<CancellationToken, Task<bool>> Handler =>
			async _ =>
			{
				_entered.TrySetResult();
				return await _release.Task.ConfigureAwait(false);
			};

		internal void Release(bool started = true) => _release.TrySetResult(started);
	}

	private static Task WaitForFinishedCallsAsync(SynchronizerTestHost fixture) =>
		TestPolling.UntilAsync(() => fixture.Scheduler.ReservedCallPathCount == 0, s_waitTimeout, "The calls did not finish.");

	// An operation queued behind everything the document's lane holds runs only after the earlier operations finished.
	private static Task WaitForTheLaneAsync(SynchronizerTestHost fixture, string filePath) =>
		fixture.Scheduler.EnqueuePerDocumentAsync(filePath, static _ => Task.FromResult(0), CancellationToken.None).WaitAsync(s_waitTimeout);

	[TestMethod]
	public async Task Close_AfterAnOpenThatWaitsForTheStartup_ClosesTheOpenedDocument()
	{
		var startup = new GatedStartup();
		using var client = new FakeLanguageServerClient { IsReady = false, EnsureStartedAsyncHandler = startup.Handler };
		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Open(s_filePath, "local a = 1");
		await startup.Entered.WaitAsync(s_waitTimeout);

		fixture.Synchronizer.Close(s_filePath);
		startup.Release();

		await WaitForFinishedCallsAsync(fixture);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/didClose"], client.GetSentMethodNames());
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(s_filePath));
	}

	[TestMethod]
	public async Task Close_AfterAnUpdateThatWaitsForTheStartup_LeavesTheDocumentUntracked()
	{
		var startup = new GatedStartup();
		using var client = new FakeLanguageServerClient { IsReady = false, EnsureStartedAsyncHandler = startup.Handler };
		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Update(s_filePath, "local a = 1");
		await startup.Entered.WaitAsync(s_waitTimeout);

		fixture.Synchronizer.Close(s_filePath);
		startup.Release();

		await WaitForFinishedCallsAsync(fixture);
		await WaitForTheLaneAsync(fixture, s_filePath);

		// The close superseded the update that still waited for the connection, so the update cannot recreate the record the close removed.
		Assert.IsEmpty(client.GetSentMethodNames());
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(s_filePath));
	}

	[TestMethod]
	public async Task Update_AfterAnOpenThatWaitsForTheStartup_ChangesTheOpenedDocument()
	{
		var startup = new GatedStartup();
		using var client = new FakeLanguageServerClient { IsReady = false, EnsureStartedAsyncHandler = startup.Handler };
		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Open(s_filePath, "local a = 1");
		await startup.Entered.WaitAsync(s_waitTimeout);

		fixture.Synchronizer.Update(s_filePath, "local a = 2");
		startup.Release();

		await WaitForFinishedCallsAsync(fixture);
		await WaitForTheLaneAsync(fixture, s_filePath);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/didChange"], client.GetSentMethodNames());
		Assert.AreEqual("local a = 2", fixture.Documents.GetDocumentSnapshot(s_filePath)?.Content);
	}

	[TestMethod]
	public async Task Update_TwoUpdatesThatWaitForTheStartup_LeaveTheNewestContent()
	{
		var startup = new GatedStartup();
		using var client = new FakeLanguageServerClient { IsReady = false, EnsureStartedAsyncHandler = startup.Handler };
		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Update(s_filePath, "local a = 1");
		await startup.Entered.WaitAsync(s_waitTimeout);

		fixture.Synchronizer.Update(s_filePath, "local a = 2");
		startup.Release();

		await WaitForFinishedCallsAsync(fixture);
		await WaitForTheLaneAsync(fixture, s_filePath);

		Assert.AreEqual("local a = 2", fixture.Documents.GetDocumentSnapshot(s_filePath)?.Content);
	}

	[TestMethod]
	public async Task Rename_AfterAnOpenThatWaitsForTheStartup_MovesTheOpenedDocument()
	{
		string newFilePath = TestPaths.Script("b.lua");

		var startup = new GatedStartup();
		using var client = new FakeLanguageServerClient { IsReady = false, EnsureStartedAsyncHandler = startup.Handler };
		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Open(s_filePath, "local a = 1");
		await startup.Entered.WaitAsync(s_waitTimeout);

		fixture.Synchronizer.Rename(s_filePath, newFilePath, "local a = 1");
		startup.Release();

		await WaitForFinishedCallsAsync(fixture);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/didClose", "textDocument/didOpen"], client.GetSentMethodNames());
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(s_filePath));
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(newFilePath));
	}

	[TestMethod]
	public async Task Rename_ToADocumentWhoseOpenWaitsForTheStartup_FindsTheDestinationTracked()
	{
		string newFilePath = TestPaths.Script("b.lua");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Open(s_filePath, "local a = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		await WaitForFinishedCallsAsync(fixture);

		// A failure the client never reported clears the startup flag, so the next call starts the connection again.
		var startup = new GatedStartup();
		client.EnsureStartedAsyncHandler = startup.Handler;
		fixture.Lifecycle.MarkConnectionUnavailable();

		fixture.Synchronizer.Open(newFilePath, "local a = 1");
		await startup.Entered.WaitAsync(s_waitTimeout);

		fixture.Synchronizer.Rename(s_filePath, newFilePath, "local a = 1");
		startup.Release();

		await WaitForFinishedCallsAsync(fixture);

		// The rename follows the open of its destination, so it finds the destination tracked and leaves both documents as they are.
		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/didOpen"], client.GetSentMethodNames());
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(s_filePath));
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(newFilePath));
	}

	[TestMethod]
	public async Task Close_OfAnotherDocument_DoesNotWaitForAnOpenThatWaitsForTheStartup()
	{
		string otherFilePath = TestPaths.Script("b.lua");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Open(otherFilePath, "local b = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		await WaitForFinishedCallsAsync(fixture);

		// A failure the client never reported clears the startup flag, so the next call starts the connection again.
		var startup = new GatedStartup();
		client.EnsureStartedAsyncHandler = startup.Handler;
		fixture.Lifecycle.MarkConnectionUnavailable();

		fixture.Synchronizer.Open(s_filePath, "local a = 1");
		await startup.Entered.WaitAsync(s_waitTimeout);

		fixture.Synchronizer.Close(otherFilePath);

		try
		{
			// The close of the other document ends while the open still waits for the start.
			Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout));
			Assert.AreEqual(1, client.GetSentMethodCount("textDocument/didOpen"));
		}
		finally
		{
			startup.Release();
		}

		await WaitForFinishedCallsAsync(fixture);

		Assert.AreEqual(2, client.GetSentMethodCount("textDocument/didOpen"));
	}

	[TestMethod]
	public async Task Close_AfterAnOpenWhoseStartupFaults_IsNotStranded()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var client = new FakeLanguageServerClient
		{
			IsReady = false,

			EnsureStartedAsyncHandler = async _ =>
			{
				entered.TrySetResult();
				await release.Task.ConfigureAwait(false);
				throw new InvalidOperationException("Simulated startup fault.");
			},
		};

		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Open(s_filePath, "local a = 1");
		await entered.Task.WaitAsync(s_waitTimeout);

		fixture.Synchronizer.Close(s_filePath);
		release.SetResult();

		// The open ends with the fault, and the close behind it still runs and ends.
		await WaitForFinishedCallsAsync(fixture);

		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(s_filePath));
	}

	[TestMethod]
	public async Task Close_AfterAnOpenThatRetriesAfterASkippedAttempt_ClosesTheDocumentTheRetryOpened()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var fixture = new SynchronizerTestHost(client);

		var startHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseStart = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var retryAtTheLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool>? recovery = null;
		int decisions = 0;

		fixture.TestHooks.LocalCommitObserver = () =>
		{
			if (Interlocked.Increment(ref decisions) != 1)
				return;

			// A recovery start takes the startup lock and parks inside the connection start, so the first attempt of the open finds the lock held
			// and is skipped.
			client.EnsureStartedAsyncHandler = _ =>
			{
				startHeld.TrySetResult();
				return releaseStart.Task;
			};

			recovery = fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None);
			startHeld.Task.Wait(s_waitTimeout);

			fixture.TestHooks.StartupLockWaitObserver = () => retryAtTheLock.TrySetResult();
		};

		try
		{
			fixture.Synchronizer.Open(s_filePath, "local a = 1");
			await retryAtTheLock.Task.WaitAsync(s_waitTimeout);

			// The first attempt ended and the retry waits for the start, so a close made now must still follow the retry's open.
			fixture.Synchronizer.Close(s_filePath);
		}
		finally
		{
			releaseStart.TrySetResult(true);
		}

		Assert.IsNotNull(recovery);
		Assert.IsTrue(await recovery);

		await WaitForFinishedCallsAsync(fixture);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/didClose"], client.GetSentMethodNames());
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(s_filePath));
	}

	[TestMethod]
	public async Task Update_BurstBehindARunningUpdate_RunsOnlyTheNewest()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Open(s_filePath, "local a = 0");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		await WaitForFinishedCallsAsync(fixture);

		var changeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseChange = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		client.SendNotificationHandler = async (method, _, _) =>
		{
			if (!string.Equals(method, "textDocument/didChange", StringComparison.Ordinal))
				return;

			changeEntered.TrySetResult();
			await releaseChange.Task.ConfigureAwait(false);
		};

		fixture.Synchronizer.Update(s_filePath, "local a = 1");
		await changeEntered.Task.WaitAsync(s_waitTimeout);

		// The first update completed its place when it joined the scheduler, so the next two join while it still runs and the newer replaces the older.
		fixture.Synchronizer.Update(s_filePath, "local a = 2");
		fixture.Synchronizer.Update(s_filePath, "local a = 3");

		await WaitForFinishedCallsAsync(fixture);

		releaseChange.SetResult();

		await WaitForTheLaneAsync(fixture, s_filePath);

		Assert.AreEqual(2, client.GetSentMethodCount("textDocument/didChange"));
		Assert.AreEqual("local a = 3", fixture.Documents.GetDocumentSnapshot(s_filePath)?.Content);
	}

	[TestMethod]
	public async Task Close_WhoseTurnIsCanceledByDisposal_EndsWithoutClosingTheDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Open(s_filePath, "local a = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		await WaitForFinishedCallsAsync(fixture);

		// A call made earlier that has not finished keeps the close waiting for its turn.
		CallOrderTracker.CallReservation earlier = fixture.Scheduler.ReserveCall(s_filePath);

		fixture.Synchronizer.Close(s_filePath);

		fixture.Disposal.TryBegin();
		fixture.Disposal.Signal();

		earlier.Complete();

		await WaitForFinishedCallsAsync(fixture);

		Assert.DoesNotContain("invalidated:a.lua", fixture.Signals);
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(s_filePath));
	}
}
