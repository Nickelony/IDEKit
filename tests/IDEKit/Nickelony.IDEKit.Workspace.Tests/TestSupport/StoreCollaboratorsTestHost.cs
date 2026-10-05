using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// The collaborators of a store over a scripted file system, wired the way the store wires them, for the tests that drive one of them directly.
/// </summary>
internal class StoreCollaboratorsTestHost : StoreTestHost
{
	internal StoreCollaboratorsTestHost()
	{
		Lifetime = new StoreLifetime();
		Hooks = new WorkspaceDocumentStoreTestHooks(SyncRoot);
		Io = new StampedFileWorker(FileSystem, Lifetime);
		Runner = new AdmittedOperationRunner(OperationGate, Lifetime);
		Opener = new DocumentOpener(SyncRoot, Tracker, OperationGate, FileSystem, Lifetime, testHooks: Hooks);
		Persister = new DocumentPersister(SyncRoot, OperationGate, Runner, FileSystem, Io, Lifetime);
		Relocator = new DocumentRelocator(SyncRoot, Tracker, OperationGate, Runner, FileSystem, Io);
		Replacer = new LogicalContentReplacer(SyncRoot, Tracker, OperationGate);
		Reader = new DocumentSnapshotReader(SyncRoot, Tracker, OperationGate, Options);
		Teardown = new StoreTeardown(SyncRoot, Tracker, stopGate: Gate, Lifetime);
	}

	internal ScriptedWorkspaceFileSystem FileSystem { get; } = new();

	internal StoreLifetime Lifetime { get; }

	internal WorkspaceDocumentStoreTestHooks Hooks { get; }

	internal StampedFileWorker Io { get; }

	internal AdmittedOperationRunner Runner { get; }

	internal DocumentOpener Opener { get; }

	internal DocumentPersister Persister { get; }

	internal DocumentRelocator Relocator { get; }

	internal LogicalContentReplacer Replacer { get; }

	internal DocumentSnapshotReader Reader { get; }

	internal StoreTeardown Teardown { get; }

	/// <summary>The identity a request against the document's current instance and version carries.</summary>
	internal static WorkspaceDocumentRequestIdentity IdentityOf(LogicalDocument document) =>
		new(document.DocumentKey, document.DocumentId, document.Version);

	/// <summary>A stamp for a file that exists, distinguished by its hash.</summary>
	internal static FileStamp StampOf(string hash) => new(Exists: true, Length: 1, LastWriteTimeUtc: DateTime.UnixEpoch, ContentHash: hash);
}
