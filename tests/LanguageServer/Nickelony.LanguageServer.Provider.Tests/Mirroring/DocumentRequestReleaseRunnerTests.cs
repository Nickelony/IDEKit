using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers how the release of a request lease completes the close it deferred, trims idle records and follows a moved record.</summary>
[TestClass]
public sealed class DocumentRequestReleaseRunnerTests
{
	private const string DidClose = "textDocument/didClose";

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static int CountSent(FakeLanguageServerClient client, string method) => client.GetSentMethodNames().Count(name => name == method);

	private static async Task<string> OpenAsync(SynchronizerTestHost fixture, string fileName = "a.lua")
	{
		string filePath = TestPaths.Script(fileName);

		fixture.Synchronizer.Open(filePath, "local a = 1");

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => fixture.Signals.Contains($"synchronized:{fileName}:v1"), s_waitTimeout));

		return filePath;
	}

	private static async Task<DocumentRequestLease> AcquireRequestLeaseAsync(SynchronizerTestHost fixture, string filePath)
	{
		var requestLease = new DocumentRequestLease();

		Assert.IsTrue(
			await fixture.Synchronizer.SynchronizeForRequestAsync(
				filePath,
				content: "local a = 1",
				requestLease,
				cancellationToken: CancellationToken.None
			)
		);

		Assert.IsTrue(requestLease.IsAcquired);

		return requestLease;
	}

	private static async Task DeferTheCloseAsync(SynchronizerTestHost fixture, string filePath)
	{
		fixture.Synchronizer.Close(filePath);

		// The request lease keeps the record tracked, so the close is marked pending and waits for the release.
		await TestPolling.UntilAsync(() => fixture.PendingCloses.IsPending(filePath), s_waitTimeout, "The close was never deferred.");
	}

	[TestMethod]
	public async Task ReleaseAsync_CompletingADeferredClose_SignalsTheDocumentInvalidatedOnlyThen()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);
		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await DeferTheCloseAsync(fixture, filePath);

		Assert.DoesNotContain("invalidated:a.lua", fixture.Signals);

		await fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);

		Assert.Contains("invalidated:a.lua", fixture.Signals);
		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
		Assert.IsFalse(fixture.PendingCloses.IsPending(filePath));
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task ReleaseAsync_WhileAnotherRequestLeaseIsActive_KeepsTheCloseDeferredUntilTheLastRelease()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		DocumentRequestLease firstLease = await AcquireRequestLeaseAsync(fixture, filePath);
		DocumentRequestLease secondLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await DeferTheCloseAsync(fixture, filePath);

		await fixture.ReleaseRunner.ReleaseAsync(firstLease, CancellationToken.None);

		// The second request still uses the document, so the close stays pending and nothing reaches the server.
		Assert.IsTrue(fixture.PendingCloses.IsPending(filePath));
		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(filePath));

		await fixture.ReleaseRunner.ReleaseAsync(secondLease, CancellationToken.None);

		Assert.IsFalse(fixture.PendingCloses.IsPending(filePath));
		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task ReleaseAsync_ForTheSameLeaseTwice_CompletesTheDeferredCloseOnce()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);
		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await DeferTheCloseAsync(fixture, filePath);

		await fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);
		await fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);

		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
		Assert.ContainsSingle(signal => signal == "invalidated:a.lua", fixture.Signals);
	}

	[TestMethod]
	public async Task ReleaseAsync_AfterTheDeferredCloseCompleted_LeavesALaterRequestOnlyRecordOpen()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		DocumentRequestLease firstLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await DeferTheCloseAsync(fixture, filePath);
		await fixture.ReleaseRunner.ReleaseAsync(firstLease, CancellationToken.None);

		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));

		// A later request tracks a request-only record at the same path; a marker left behind would close it at the release.
		DocumentRequestLease laterLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await fixture.ReleaseRunner.ReleaseAsync(laterLease, CancellationToken.None);

		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task ReleaseAsync_WhenTheDeferredCloseSendFails_LogsItAsTheDeferredCloseAndDoesNotThrow()
	{
		using var logger = new RecordingLogger();
		using var fixture = new SynchronizerTestHost(logger: logger);

		string filePath = await OpenAsync(fixture);

		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await DeferTheCloseAsync(fixture, filePath);

		fixture.Client.SendNotificationHandler = static (method, _, _) =>
			method == DidClose ? Task.FromException(new IOException("The connection closed.")) : Task.CompletedTask;

		await fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);

		// The failure belongs to the deferred close, so the release around it still finished.
		Assert.Contains(
			static log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("deferred document close", StringComparison.Ordinal),
			logger.Logs
		);

		Assert.DoesNotContain(static log => log.Contains("request-document release", StringComparison.Ordinal), logger.Logs);
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task ReleaseAsync_ForConcurrentReleasesOfDifferentLeases_CompletesTheDeferredCloseOnce()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		DocumentRequestLease firstLease = await AcquireRequestLeaseAsync(fixture, filePath);
		DocumentRequestLease secondLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await DeferTheCloseAsync(fixture, filePath);

		await Task.WhenAll(
			fixture.ReleaseRunner.ReleaseAsync(firstLease, CancellationToken.None),
			fixture.ReleaseRunner.ReleaseAsync(secondLease, CancellationToken.None)
		);

		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task ReleaseAsync_AfterTheDocumentWasOpenedAgain_KeepsTheDocumentOpen()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);
		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await DeferTheCloseAsync(fixture, filePath);

		fixture.Synchronizer.Open(filePath, "local a = 1");

		await TestPolling.UntilAsync(() => !fixture.PendingCloses.IsPending(filePath), s_waitTimeout, "The open never canceled the deferred close.");

		await fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);

		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task ReleaseAsync_AfterTheDocumentWasRenamed_CompletesTheDeferredCloseThroughTheNewPath()
	{
		using var fixture = new SynchronizerTestHost();

		string oldPath = await OpenAsync(fixture, "old.lua");
		string newPath = TestPaths.Script("new.lua");

		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, oldPath);

		await DeferTheCloseAsync(fixture, oldPath);

		fixture.Synchronizer.Rename(oldPath, newPath, "local a = 1");

		await TestPolling.UntilAsync(() => fixture.Signals.Contains("moved:new.lua"), s_waitTimeout, "The move never completed.");

		Assert.IsTrue(fixture.PendingCloses.IsPending(newPath));

		await fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);

		JsonElement lastClose = fixture.Client.GetLastNotificationParameters(DidClose).GetProperty("textDocument");

		Assert.AreEqual(DocumentUriConversion.CreateFileUri(newPath), lastClose.GetProperty("uri").GetString());
		Assert.IsFalse(fixture.PendingCloses.IsPending(newPath));
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(newPath));
	}

	[TestMethod]
	public async Task ReleaseAsync_WhenNoCloseWasDeferred_KeepsTheOpenDocumentOpen()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);

		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);

		// The host still holds the document open, so only a close the host asked for may complete at the release.
		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(filePath));
		Assert.DoesNotContain("invalidated:a.lua", fixture.Signals);
	}

	[TestMethod]
	public async Task ReleaseAsync_WhenTheDocumentRenamesWhileTheReleaseIsQueued_FollowsTheDocumentAndCompletesTheDeferredClose()
	{
		using var fixture = new SynchronizerTestHost();

		string oldPath = await OpenAsync(fixture, "old.lua");
		string newPath = TestPaths.Script("new.lua");

		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, oldPath);

		await DeferTheCloseAsync(fixture, oldPath);

		var holdSlot = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<bool> blocker = fixture.Scheduler.EnqueuePerDocumentAsync(oldPath, _ => holdSlot.Task, CancellationToken.None);

		// Both calls queue behind the blocker in the old path's slot, so the release starts from the old path and runs after the move.
		fixture.Synchronizer.Rename(oldPath, newPath, "local a = 1");

		Task release = fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);

		holdSlot.TrySetResult(true);

		await blocker.WaitAsync(s_waitTimeout);
		await release.WaitAsync(s_waitTimeout);

		JsonElement lastClose = fixture.Client.GetLastNotificationParameters(DidClose).GetProperty("textDocument");

		Assert.AreEqual(DocumentUriConversion.CreateFileUri(newPath), lastClose.GetProperty("uri").GetString());
		Assert.IsFalse(fixture.PendingCloses.IsPending(newPath));
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(newPath));
	}

	[TestMethod]
	public async Task ReleaseAsync_WhileTheClientIsNotReady_ClosesTheRecordWithoutSendingTheClose()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);
		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await DeferTheCloseAsync(fixture, filePath);

		fixture.Client.IsReady = false;

		await fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);

		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));
		Assert.Contains("invalidated:a.lua", fixture.Signals);
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task ReleaseAsync_WhenTheCallerTokenIsCanceled_StillReleasesTheLeaseAndCompletesTheDeferredClose()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = await OpenAsync(fixture);
		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, filePath);

		await DeferTheCloseAsync(fixture, filePath);

		using var cancellation = new CancellationTokenSource();

		cancellation.Cancel();

		await fixture.ReleaseRunner.ReleaseAsync(requestLease, cancellation.Token);

		// A canceled caller must not leave a request-only record pinned, so the release and the close it deferred still run.
		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task ReleaseAsync_ForARequestOnlyRecordWhenNoIdleRecordMayRemain_EvictsItAndClosesItsServerCopyAfterTheRelease()
	{
		using var fixture = new SynchronizerTestHost(providerOptions: new LanguageServerFeatureProviderOptions { MaxTrackedIdleDocuments = 0 });

		string filePath = TestPaths.Script("a.lua");

		Assert.IsTrue(await fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None));

		DocumentRequestLease requestLease = await AcquireRequestLeaseAsync(fixture, filePath);

		bool? trackedWhenClosed = null;

		fixture.Client.SendNotificationHandler = (method, _, _) =>
		{
			if (method == DidClose)
				trackedWhenClosed = fixture.Documents.IsTrackedNormalized(filePath);

			return Task.CompletedTask;
		};

		await fixture.ReleaseRunner.ReleaseAsync(requestLease, CancellationToken.None);

		// The release came first, so the trim saw the record idle and evicted it, and the server copy closed only once it was untracked.
		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
		Assert.IsFalse(trackedWhenClosed);
		Assert.Contains("invalidated:a.lua", fixture.Signals);
	}

	[TestMethod]
	public async Task ReleaseAsync_WhenAnOlderIdleRecordExceedsTheAllowance_ClosesTheOlderRecordAndKeepsTheReleasedOne()
	{
		using var fixture = new SynchronizerTestHost(providerOptions: new LanguageServerFeatureProviderOptions { MaxTrackedIdleDocuments = 1 });

		string olderPath = TestPaths.Script("older.lua");
		string newerPath = TestPaths.Script("newer.lua");

		Assert.IsTrue(await fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None));

		DocumentRequestLease olderLease = await AcquireRequestLeaseAsync(fixture, olderPath);
		DocumentRequestLease newerLease = await AcquireRequestLeaseAsync(fixture, newerPath);

		await fixture.ReleaseRunner.ReleaseAsync(olderLease, CancellationToken.None);

		Assert.AreEqual(0, CountSent(fixture.Client, DidClose));

		await fixture.ReleaseRunner.ReleaseAsync(newerLease, CancellationToken.None);

		JsonElement close = fixture.Client.GetLastNotificationParameters(DidClose).GetProperty("textDocument");

		Assert.AreEqual(1, CountSent(fixture.Client, DidClose));
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(olderPath), close.GetProperty("uri").GetString());
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(olderPath));
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(newerPath));
	}
}
