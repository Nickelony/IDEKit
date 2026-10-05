using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the opener directly: the load, the join of a concurrent open, the rejected paths and the stop.</summary>
[TestClass]
public sealed class DocumentOpenerTests
{
	private static readonly string s_path = Path.Combine(Path.GetTempPath(), "opener-tests", "a.txt");

	private static readonly WorkspaceDocumentOpenOptions s_options = new([TextEncoding.Utf8], TestSnapshots.FileFormat);

	public required TestContext TestContext { get; set; }

	[TestMethod]
	public async Task OpenAsync_ForAnExistingFile_TracksTheDocumentWithTheFileContent()
	{
		var host = new StoreCollaboratorsTestHost();
		FileStamp stamp = StoreCollaboratorsTestHost.StampOf("a");
		host.FileSystem.OnRead = (_, _) => Task.FromResult(WorkspaceFileReadResult.FromContent("text", TestSnapshots.FileFormat, stamp));

		WorkspaceDocumentOpenResult result = await host.Opener.OpenAsync(s_path, s_options, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, result.Outcome);
		Assert.AreEqual("text", result.Snapshot?.Content);
		Assert.AreEqual(stamp, result.Snapshot?.OnDiskStamp);
		Assert.IsTrue(host.Locked(tracker => tracker.TryGetDocument(result.Snapshot!.DocumentId, out _)));
		Assert.IsEmpty(host.Stop());
	}

	[TestMethod]
	public async Task OpenAsync_ForAnOpenDocument_ReportsAlreadyOpenWithoutReadingAgain()
	{
		var host = new StoreCollaboratorsTestHost();

		await host.Opener.OpenAsync(s_path, s_options, TestContext.CancellationToken).ConfigureAwait(false);
		WorkspaceDocumentOpenResult second = await host.Opener.OpenAsync(s_path, s_options, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.AlreadyOpen, second.Outcome);
		Assert.HasCount(1, host.FileSystem.Calls);
	}

	[TestMethod]
	public async Task OpenAsync_ForAMissingFileThatMustExist_ReportsNotFoundAndTracksNothing()
	{
		var host = new StoreCollaboratorsTestHost();
		WorkspaceDocumentOpenOptions options = s_options with { CreateIfMissing = false };

		WorkspaceDocumentOpenResult result = await host.Opener.OpenAsync(s_path, options, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.NotFound, result.Outcome);
		Assert.IsEmpty(host.Locked(tracker => tracker.Documents.ToList()));
	}

	[TestMethod]
	public async Task OpenAsync_ForADirectory_ReportsIsDirectory()
	{
		var host = new StoreCollaboratorsTestHost();
		host.FileSystem.OnRead = (_, _) => Task.FromResult(WorkspaceFileReadResult.Directory);

		WorkspaceDocumentOpenResult result = await host.Opener.OpenAsync(s_path, s_options, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.IsDirectory, result.Outcome);
	}

	[TestMethod]
	public async Task OpenAsync_WhenTheReadFails_ReportsLoadFailedAndReleasesTheReservation()
	{
		var host = new StoreCollaboratorsTestHost();
		host.FileSystem.OnRead = (_, _) => throw new UnauthorizedAccessException("denied");

		WorkspaceDocumentOpenResult result = await host.Opener.OpenAsync(s_path, s_options, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.LoadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure?.Code);
		Assert.IsEmpty(host.Locked(tracker => tracker.GetOpenReservationTasks()));
	}

	[TestMethod]
	public async Task OpenAsync_ForABlankPath_ReportsInvalidPath()
	{
		var host = new StoreCollaboratorsTestHost();

		WorkspaceDocumentOpenResult result = await host.Opener.OpenAsync(" ", s_options, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.InvalidPath, result.Outcome);
		Assert.IsEmpty(host.FileSystem.Calls);
	}

	[TestMethod]
	public async Task OpenAsync_AfterTheStopBegan_ThrowsObjectDisposed()
	{
		var host = new StoreCollaboratorsTestHost();
		host.Stop();

		await Assert
			.ThrowsExactlyAsync<ObjectDisposedException>(() => host.Opener.OpenAsync(s_path, s_options, TestContext.CancellationToken))
			.ConfigureAwait(false);
	}
}
