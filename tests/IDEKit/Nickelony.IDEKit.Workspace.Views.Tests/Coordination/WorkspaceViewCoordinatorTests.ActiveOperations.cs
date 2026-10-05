using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	// A stop that a member of the operation requests would wait for the operation itself, so the gate refuses it. The refusal is how the test
	// sees that the store is reached from inside an active operation: a call outside one begins the stop instead.
	[TestMethod]
	public async Task EveryOperation_ReachesTheStoreAsAnActiveOperation()
	{
		string missingDirectory = "missing";
		CancellationToken cancellationToken = TestContext.CancellationToken;

		var operations = new (string Name, Func<CoordinatorTestHost, WorkspaceDocumentSnapshot, Task> Invoke)[]
		{
			("OpenAsync", (host, _) => host.Coordinator.OpenAsync(host.DocumentPath, s_openOptions, cancellationToken)),

			(
				"OpenWithViewAsync",
				(host, _) =>
					host.Coordinator.OpenWithViewAsync(
						filePath: host.DocumentPath,
						options: s_openOptions,
						view: new FakeCoordinatorView(host, "second"),
						cancellationToken
					)
			),

			(
				"DiscardAsync",
				(host, snapshot) => host.Coordinator.DiscardAsync(new WorkspaceDocumentDiscardRequest(snapshot.Identity), cancellationToken)
			),

			("ReplaceAsync", (host, snapshot) => host.Coordinator.ReplaceAsync(ReplaceRequest(snapshot), cancellationToken)),

			(
				"RenameAsync",
				(host, snapshot) =>
					host.Coordinator.RenameAsync(
						new WorkspaceDocumentRenameRequest(snapshot.Identity, snapshot.OnDiskStamp, Path.Combine(host.DirectoryPath, "renamed.txt")),
						cancellationToken
					)
			),

			(
				"SaveAsAsync",
				(host, snapshot) =>
					host.Coordinator.SaveAsAsync(
						new WorkspaceDocumentSaveAsRequest(snapshot.Identity, snapshot.OnDiskStamp, Path.Combine(host.DirectoryPath, "saved.txt")),
						cancellationToken
					)
			),

			(
				"DeleteAsync",
				(host, snapshot) =>
					host.Coordinator.DeleteAsync(new WorkspaceDocumentDeleteRequest(snapshot.Identity, snapshot.OnDiskStamp), cancellationToken)
			),

			(
				"RenameDirectoryAsync",
				(host, _) =>
					host.Coordinator.RenameDirectoryAsync(
						new WorkspaceDocumentDirectoryRenameRequest(
							Path.Combine(host.DirectoryPath, missingDirectory),
							Path.Combine(host.DirectoryPath, "moved")
						),

						cancellationToken
					)
			),

			(
				"DeleteDirectoryAsync",
				(host, _) =>
					host.Coordinator.DeleteDirectoryAsync(
						new WorkspaceDocumentDirectoryDeleteRequest(Path.Combine(host.DirectoryPath, missingDirectory)),
						cancellationToken
					)
			),

			(
				"CommitAsync",
				(host, snapshot) =>
					host.Coordinator.CommitAsync(new WorkspaceDocumentCommitRequest(snapshot.Identity, snapshot.OnDiskStamp), cancellationToken)
			),

			(
				"ReloadAsync",
				(host, snapshot) => host.Coordinator.ReloadAsync(new WorkspaceDocumentReloadRequest(snapshot.Identity), cancellationToken)
			),

			(
				"ResolveExternalConflictAsync",
				(host, snapshot) =>
					host.Coordinator.ResolveExternalConflictAsync(
						new WorkspaceDocumentConflictResolutionRequest(
							snapshot.Identity,
							snapshot.OnDiskStamp,
							WorkspaceDocumentConflictResolutionMode.ResolveWithDisk
						),

						cancellationToken
					)
			),

			("PublishPendingEditsAsync", (host, _) => host.Coordinator.PublishPendingEditsAsync(host.DocumentPath, cancellationToken)),

			(
				"ApplyRequested",
				(host, snapshot) =>
				{
					host.View.RaiseApply(ReplaceRequest(snapshot));
					return Task.CompletedTask;
				}
			),
		};

		foreach ((string name, Func<CoordinatorTestHost, WorkspaceDocumentSnapshot, Task> invoke) in operations)
		{
			List<RecordingDocumentStore> recordings = [];

			await using var host = new CoordinatorTestHost(wrapStore: store =>
			{
				recordings.Add(new RecordingDocumentStore(store));
				return recordings[^1];
			});

			WorkspaceDocumentSnapshot snapshot = await host.OpenViewAsync();

			InvalidOperationException? refusal = null;
			bool probed = false;

			recordings.Single().OnMember = member =>
			{
				if (probed)
					return;

				probed = true;

				try
				{
					_ = host.Coordinator.StopAsync();
				}
				catch (InvalidOperationException exception)
				{
					refusal = exception;
				}
			};

			await invoke(host, snapshot);

			Assert.IsTrue(probed, $"{name} never reached the store.");
			Assert.IsNotNull(refusal, $"{name} reached the store outside an active operation.");
		}
	}
}
