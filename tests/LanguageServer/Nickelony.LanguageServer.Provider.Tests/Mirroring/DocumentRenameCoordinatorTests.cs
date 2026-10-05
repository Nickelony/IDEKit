namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the move of a tracked document to a new path, directly and without a provider.</summary>
[TestClass]
public sealed class DocumentRenameCoordinatorTests
{
	public TestContext TestContext { get; set; } = null!;

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static async Task<SynchronizerTestHost> OpenedHostAsync(string oldPath, FakeLanguageServerClient? client = null)
	{
		var fixture = new SynchronizerTestHost(client);

		fixture.Synchronizer.Open(oldPath, "local a = 1");

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => fixture.Signals.Contains("synchronized:old.lua:v1"), s_waitTimeout));

		return fixture;
	}

	[TestMethod]
	public async Task RenameAsync_ForAnUntrackedSource_ReturnsFalseAndSignalsNothing()
	{
		using var fixture = new SynchronizerTestHost();

		bool moved = await fixture.Renamer.RenameAsync(
			oldFilePath: TestPaths.Script("missing.lua"),
			newFilePath: TestPaths.Script("new.lua"),
			content: "local a = 1",
			cancellationToken: CancellationToken.None
		);

		Assert.IsFalse(moved);
		Assert.IsEmpty(fixture.Signals);
		Assert.IsEmpty(fixture.Client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task RenameAsync_ForAServerOpenDocument_ClosesTheOldIdentityAndOpensTheNewOne()
	{
		string oldPath = TestPaths.Script("old.lua");
		string newPath = TestPaths.Script("new.lua");

		using SynchronizerTestHost fixture = await OpenedHostAsync(oldPath);

		Assert.IsTrue(
			await fixture.Renamer.RenameAsync(
				oldFilePath: oldPath,
				newFilePath: newPath,
				content: "local a = 1",
				cancellationToken: CancellationToken.None
			)
		);

		Assert.AreSequenceEqual(
			["textDocument/didOpen", "textDocument/didClose", "textDocument/didOpen"],
			[.. fixture.Client.GetSentMethodNames()],
			"The old identity closes before the new one opens."
		);

		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(oldPath));
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(newPath));
	}

	[TestMethod]
	public async Task RenameAsync_InvalidatesBothPathsThenSignalsTheMoveWithoutARefreshForUnchangedContent()
	{
		string oldPath = TestPaths.Script("old.lua");

		using SynchronizerTestHost fixture = await OpenedHostAsync(oldPath);

		Assert.IsTrue(
			await fixture.Renamer.RenameAsync(
				oldFilePath: oldPath,
				newFilePath: TestPaths.Script("new.lua"),
				content: "local a = 1",
				cancellationToken: CancellationToken.None
			)
		);

		Assert.AreSequenceEqual(
			["invalidated:old.lua", "invalidated:new.lua", "moved:new.lua"],
			[.. fixture.Signals.SkipWhile(signal => signal != "invalidated:old.lua")]
		);
	}

	[TestMethod]
	public async Task RenameAsync_WithChangedContent_AnnouncesTheMoveBeforeTheRefresh()
	{
		string oldPath = TestPaths.Script("old.lua");

		using SynchronizerTestHost fixture = await OpenedHostAsync(oldPath);

		Assert.IsTrue(
			await fixture.Renamer.RenameAsync(
				oldFilePath: oldPath,
				newFilePath: TestPaths.Script("new.lua"),
				content: "local a = 2",
				cancellationToken: CancellationToken.None
			)
		);

		string[] afterOpen = [.. fixture.Signals.SkipWhile(signal => signal != "invalidated:old.lua")];

		Assert.HasCount(4, afterOpen);
		Assert.AreEqual("moved:new.lua", afterOpen[2]);
		Assert.StartsWith("synchronized:new.lua:", afterOpen[3]);
	}

	[TestMethod]
	public async Task RenameAsync_WhenTheClientIsNotReady_RenamesTheRecordWithoutSending()
	{
		string oldPath = TestPaths.Script("old.lua");
		string newPath = TestPaths.Script("new.lua");

		using SynchronizerTestHost fixture = await OpenedHostAsync(oldPath);
		fixture.Client.IsReady = false;

		Assert.IsTrue(
			await fixture.Renamer.RenameAsync(
				oldFilePath: oldPath,
				newFilePath: newPath,
				content: "local a = 1",
				cancellationToken: CancellationToken.None
			)
		);

		Assert.AreEqual(0, fixture.Client.GetSentMethodNames().Count(name => name == "textDocument/didClose"));
		Assert.ContainsSingle(name => name == "textDocument/didOpen", fixture.Client.GetSentMethodNames());
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(newPath));
	}

	[TestMethod]
	public async Task RenameAsync_WhenTheCloseCannotBeSent_InvalidatesTheRenamedDocumentAndReportsFalse()
	{
		string oldPath = TestPaths.Script("old.lua");

		using SynchronizerTestHost fixture = await OpenedHostAsync(oldPath);

		fixture.Client.SendNotificationHandler = (method, _, _) =>
			method == "textDocument/didClose" ? Task.FromException(new IOException("The connection closed.")) : Task.CompletedTask;

		Assert.IsFalse(
			await fixture.Renamer.RenameAsync(
				oldFilePath: oldPath,
				newFilePath: TestPaths.Script("new.lua"),
				content: "local a = 1",
				cancellationToken: CancellationToken.None
			)
		);

		Assert.AreEqual(
			2,
			fixture.Signals.Count(signal => signal == "invalidated:new.lua"),
			"The failed send invalidates the new identity once more."
		);

		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(TestPaths.Script("new.lua")));
	}

	[TestMethod]
	public async Task RenameAsync_CancelsTheUpdateQueuedBehindItForTheVacatedPath()
	{
		string oldPath = TestPaths.Script("old.lua");
		string newPath = TestPaths.Script("new.lua");

		using SynchronizerTestHost fixture = await OpenedHostAsync(oldPath);

		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		fixture.Client.SendNotificationHandler = (method, _, _) => method == "textDocument/didChange" ? gate.Task : Task.CompletedTask;

		// The first update holds the slot of the old path while it waits for its send.
		fixture.Synchronizer.Update(oldPath, "local a = 2");

		await TestPolling.UntilAsync(
			() => fixture.Client.GetAttemptedNotificationMethodNames().Count(name => name == "textDocument/didChange") == 1,
			s_waitTimeout,
			"The first update never reached its send."
		);

		// The move queues behind it, and the second update queues behind the move.
		Task<bool> renameTask = fixture.Renamer.RenameAsync(
			oldFilePath: oldPath,
			newFilePath: newPath,
			content: "local a = 2",
			cancellationToken: CancellationToken.None
		);

		fixture.Synchronizer.Update(oldPath, "local a = 3");

		gate.SetResult();

		Assert.IsTrue(await renameTask.WaitAsync(s_waitTimeout, TestContext.CancellationToken));
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken);

		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(oldPath), "A stale update must not recreate the vacated path.");
	}

	[TestMethod]
	public async Task RenameAsync_RenamesACloseDeferredByARequestLeaseWithTheDocument()
	{
		string oldPath = TestPaths.Script("old.lua");
		string newPath = TestPaths.Script("new.lua");

		using SynchronizerTestHost fixture = await OpenedHostAsync(oldPath);
		fixture.PendingCloses.MarkPending(oldPath);

		Assert.IsTrue(
			await fixture.Renamer.RenameAsync(
				oldFilePath: oldPath,
				newFilePath: newPath,
				content: "local a = 1",
				cancellationToken: CancellationToken.None
			)
		);

		Assert.IsFalse(fixture.PendingCloses.IsPending(oldPath));
		Assert.IsTrue(fixture.PendingCloses.IsPending(newPath));
	}
}
