namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the replay of the documents a restart captured, directly and without a provider.</summary>
[TestClass]
public sealed class DocumentRestartReplayRunnerTests
{
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static Task<bool> WaitForSignalAsync(SynchronizerTestHost fixture, string signal) =>
		TestPolling.ForConditionAsync(() => fixture.Signals.Contains(signal), s_waitTimeout);

	private static int CountSent(FakeLanguageServerClient client, string method) => client.GetSentMethodNames().Count(name => name == method);

	[TestMethod]
	public async Task ReopenTrackedDocumentsAsync_SendsEachTrackedDocumentAgainAndSignalsIt()
	{
		using var fixture = new SynchronizerTestHost();
		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await WaitForSignalAsync(fixture, "synchronized:a.lua:v1"));

		IReadOnlyList<DocumentSnapshot> snapshots = fixture.Documents.PrepareForRestart();

		IReadOnlyList<DocumentSnapshot>? unfinished = await fixture.Replay.ReopenTrackedDocumentsAsync(snapshots, CancellationToken.None);

		Assert.IsNull(unfinished);
		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout));
		Assert.AreSequenceEqual(["synchronized:a.lua:v1", "synchronized:a.lua:v2"], fixture.Signals);
	}

	[TestMethod]
	public async Task ReopenTrackedDocumentsAsync_WhenReplayedTwice_SendsEachDocumentOnce()
	{
		using var fixture = new SynchronizerTestHost();
		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await WaitForSignalAsync(fixture, "synchronized:a.lua:v1"));

		IReadOnlyList<DocumentSnapshot> snapshots = fixture.Documents.PrepareForRestart();

		Assert.IsNull(await fixture.Replay.ReopenTrackedDocumentsAsync(snapshots, CancellationToken.None));
		Assert.IsNull(await fixture.Replay.ReopenTrackedDocumentsAsync(snapshots, CancellationToken.None));

		// The second replay finds the record already open on the server and skips it.
		Assert.AreEqual(2, CountSent(fixture.Client, "textDocument/didOpen"));
		Assert.AreSequenceEqual(["synchronized:a.lua:v1", "synchronized:a.lua:v2"], fixture.Signals);
	}

	[TestMethod]
	public async Task ReopenTrackedDocumentsAsync_ForADocumentClosedMeanwhile_SendsNothingForIt()
	{
		using var fixture = new SynchronizerTestHost();
		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await WaitForSignalAsync(fixture, "synchronized:a.lua:v1"));

		IReadOnlyList<DocumentSnapshot> snapshots = fixture.Documents.PrepareForRestart();

		// The host closes the document while the restart is in flight, so the replay must not leave a phantom server-open record.
		fixture.Documents.Close(filePath);

		Assert.IsNull(await fixture.Replay.ReopenTrackedDocumentsAsync(snapshots, CancellationToken.None));

		Assert.AreEqual(1, CountSent(fixture.Client, "textDocument/didOpen"));
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
		Assert.AreSequenceEqual(["synchronized:a.lua:v1"], fixture.Signals);
	}

	[TestMethod]
	public async Task ReopenTrackedDocumentsAsync_WhenAReopenFails_StopsThereAndReturnsTheFailedDocumentWithTheOnesAfterIt()
	{
		using var fixture = new SynchronizerTestHost();

		foreach (string fileName in new[] { "a.lua", "b.lua", "c.lua" })
		{
			fixture.Synchronizer.Open(TestPaths.Script(fileName), "local a = 1");
			Assert.IsTrue(await WaitForSignalAsync(fixture, $"synchronized:{fileName}:v1"));
		}

		IReadOnlyList<DocumentSnapshot> snapshots = fixture.Documents.PrepareForRestart();

		int attempts = 0;

		// The second reopen fails, so the third must never be attempted.
		fixture.Client.SendNotificationHandler = (_, _, _) =>
			Interlocked.Increment(ref attempts) == 2 ? Task.FromException(new IOException("The connection closed.")) : Task.CompletedTask;

		IReadOnlyList<DocumentSnapshot>? unfinished = await fixture.Replay.ReopenTrackedDocumentsAsync(snapshots, CancellationToken.None);

		Assert.IsNotNull(unfinished);

		Assert.AreSequenceEqual(
			snapshots.Skip(1).Select(static snapshot => snapshot.FilePath),
			unfinished.Select(static snapshot => snapshot.FilePath)
		);

		Assert.HasCount(2, fixture.Client.GetAttemptedNotificationMethodNames());
		Assert.Contains("invalidated:b.lua", fixture.Signals, "A failed reopen invalidates the tracked server copy.");
	}
}
