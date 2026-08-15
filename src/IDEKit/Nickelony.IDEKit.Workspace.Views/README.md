# Nickelony.IDEKit.Workspace.Views

UI-framework-free document-view coordination for the `Nickelony.IDEKit.Workspace` document
authority: the document-view contract (`IWorkspaceDocumentView`) and the manager
(`WorkspaceDocumentManager`) that synchronizes attached views with tracked documents.

The package is the view slice of the workspace family, split into its own package so a host that
only needs document authority (a headless server, a tool, a service) can adopt
`Nickelony.IDEKit.Workspace` without taking the view contracts. It depends on
`Nickelony.IDEKit.Workspace` and `Nickelony.IDEKit.Core`. The sibling READMEs are the
[document authority](../Nickelony.IDEKit.Workspace/README.md) that the manager coordinates and
[Core](../Nickelony.IDEKit.Core/README.md), the text primitives the snapshot content uses.

## Getting started

Install the package from your configured feed (the library ships from a local
feed while it is in preview):

```powershell
dotnet add package Nickelony.IDEKit.Workspace.Views
```

```csharp
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using Nickelony.IDEKit.Workspace.Views;

// Supplied by the host: the delegate that runs view access where view access is legal - a
// message-loop dispatcher, or the direct inline adapter shown here. The delegate must run the
// action before its returned task completes and propagate the action's exceptions; completion may
// itself be asynchronous.
Func<Action, Task> dispatchViewAction = action =>
{
    action();
    return Task.CompletedTask;
};

string scriptDirectory = Path.Combine(Path.GetTempPath(), "workspace", "scripts");
string scriptPath = Path.Combine(scriptDirectory, "main.lua");

// Create the destination directory first; the store stages every commit through a temporary file
// beside the target (the commit model lives in the Workspace README).
Directory.CreateDirectory(scriptDirectory);

IWorkspaceFileSystem fileSystem = new LocalWorkspaceFileSystem();
await using var store = new WorkspaceDocumentStore(fileSystem);
await using var manager = new WorkspaceDocumentManager(store, dispatchViewAction);
IWorkspaceDocumentView view = new MyDocumentView(scriptPath);

WorkspaceDocumentManagerOpenResult opened = await manager.OpenWithViewAsync(
    scriptPath,
    new WorkspaceDocumentOpenOptions(
        TextEncodingKind.Utf8,
        new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf)),
    view);

if (opened.Outcome == WorkspaceDocumentManagerOpenOutcome.Opened)
{
    WorkspaceDocumentSnapshot snapshot = opened.Snapshot!;
    WorkspaceDocumentManagerCommitResult committed = await manager.CommitAsync(
        new WorkspaceDocumentCommitRequest(
            new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
            snapshot.OnDiskStamp));
}
```

## Requirements

.NET 8.0 or later (the package targets `net8.0` and is built with nullable reference types
enabled).

## Dependencies

`Nickelony.IDEKit.Workspace` (document authority) and `Nickelony.IDEKit.Core` - both
UI-framework-free. Targets `net8.0`.

## What's inside

The package covers:

- Attaching a document view to a tracked document (`OpenWithViewAsync`) and closing/unregistering
  it (`StopAsync`, `UnregisterOpenView`).
- Publishing view edits back to the document authority: the view raises
  `IWorkspaceDocumentView.ApplyRequested` and the manager applies the replacement, acknowledging
  the result to the view.
- Synchronizing views around document operations: identity changes for rename, save-as, and
  directory rename; refreshes after commit, reload, and conflict resolution; and closing views when
  their document is deleted.
- Reporting view outcomes as a composed value (`WorkspaceDocumentViewSynchronizationResult` with
  `Synchronized`, `Blocked`, or `Unsynchronized`, carrying the affected views and the failure each
  reported) next to the store result, so the document-authority outcome and the view outcome stay
  separate.

## Operation model

- **Construction and identity.** The constructor takes the document store and the dispatch delegate.
  The manager tracks view membership with the store's own path-comparison policy
  (`IWorkspaceDocumentReader.PathComparison`), so both slices agree on which paths are the same
  document.
- **Result shape.** Every operation result composes the document-authority outcome (`StoreResult`;
  `null` when attached views blocked the operation before the store was reached) with the
  view-synchronization outcome (`ViewSynchronization`) and the current `Snapshot`. `Outcome` and
  `Snapshot` delegate to the store result, and so does `Failure` whenever the store was reached, so
  `StoreResult` is the single source for the document outcome and a host logs a failure and
  refreshes a snapshot from one shape. Only the logical-mutation result never reaches the store, so it
  alone exposes no `Failure`. Construct results through the `FromStore`/`Blocked`
  factories, which keep the store and view outcomes consistent.
- **Dispatch delegate.** The manager owns no thread or message loop; every action that touches a view
  member is dispatched through the delegate supplied to the constructor. The rules:
  - The delegate must run the action before its returned task completes and let the action's
    exceptions propagate; the task may complete asynchronously, because the manager awaits it instead
    of blocking.
  - The manager consumes the results the action captured right after the task completes.
  - A headless host passes an inline adapter (`action => { action(); return Task.CompletedTask; }`);
    a UI host passes a dispatcher that runs the action on its message loop.
  - Actions may arrive from arbitrary threads, from concurrent operations, and re-entrantly from
    inside a dispatched action (a view raising `ApplyRequested`); a delegate whose views are not
    thread-safe or reentrancy-safe must serialize or queue the actions itself, and one manager
    coordinates one store.
  - The subscription itself is never dispatched: the add runs on the thread that resumes the attach
    after its dispatched action completes - not necessarily the caller's thread or the dispatch
    thread, because the store open between them is asynchronous - a direct `UnregisterOpenView`
    removes the handler on the calling thread, and manager-initiated removals during a stop or delete
    teardown run on the dispatch thread; event accessors must be usable from any thread.
  - The event-driven apply runs its store mutation on the raising thread; the acknowledgment that
    follows is dispatched.
  - A view member that throws is isolated (reported through the operation result or recorded as
    unsynchronized state); a fault raised by the delegate itself surfaces as a failure of the
    operation.
  - Host-initiated mutations use `ReplaceAsync`; a view that publishes its own edits raises
    `ApplyRequested` and learns the outcome through `AcknowledgeApply`.
- **Draft ownership.** The document authority owns the draft: logical content, the persisted
  baseline, the dirty flag, and the version pair all live on `WorkspaceDocumentSnapshot` in
  `Nickelony.IDEKit.Workspace`. A view owns only the interactive buffer it renders and its three
  relationship members (`DocumentKey`, `HasPendingEdits`, `HasConflict`), and it publishes edits to
  the authority by raising `ApplyRequested`. The manager therefore never treats a view as the source
  of truth for document content: it blocks the operations that could lose an unpublished edit and
  retains a view that failed to adopt a published change, and a host whose view publishes eagerly
  simply never trips the pending-edit path.
- **Blocking policy.** Operations that destroy the document behind a view consult attached view
  state first:
  - Delete and delete-directory report `Blocked` while a view has pending edits, a conflict, or an
    unsynchronized failure.
  - The recovery operations - commit, reload, and conflict resolution - block only on pending edits
    and conflicts and deliberately ignore a prior synchronization failure, so they can be retried and
    their post-operation refresh retries the failed synchronization.
  - Operations that only change the document identity or path - rename, save-as, and directory
    rename - never block on view state: the move cannot lose view edits, and the identity change is
    acknowledged afterwards, where a view update failure is reported as `Unsynchronized`.
- **Unsynchronized state.** A view whose refresh or acknowledgment failed is retained as
  unsynchronized, with the failure detail for reporting. A successful refresh or identity
  acknowledgment clears that state; delete and delete-directory report `Blocked` while it is set,
  and the recovery operations retry the synchronization instead of blocking on it.
- **Delete guards.** Delete operations (file and directory) and directory moves ask attached views
  that implement `IWorkspaceDocumentDeleteGuardView` to enter a delete guard (a view-defined
  barrier, for example suspending live updates while the move runs); a view without the capability
  is skipped. When a view rejects the guard, the operation is reported as `Blocked` before the store
  is reached; entered guards are always released, including when the operation throws. A
  guard-release failure is reported as an unsynchronized issue on the result, and it is also
  recorded as unsynchronized view state unless the operation closes the view anyway.
- **Identity changes.** Rename, save-as, and directory rename ask attached views to acknowledge the
  new identity through `AcknowledgeIdentity`; a view that cannot accept the change is reported as
  `Unsynchronized` on the result, and the manager keeps reporting the view until a successful
  refresh or identity acknowledgment clears it.
- **Reuse and closure.**
  - A view that is already registered is reported as `AlreadyOpen` with the snapshot of the document
    it is attached to.
  - A different view that duplicates a registered view id is `ViewInUse`.
  - Stopping the manager (`StopAsync`/`DisposeAsync`) rejects new operations, waits for active ones,
    closes and unregisters every registered view, and releases all view-tracking state.
  - Never call `StopAsync` from inside a view member or a dispatched action that belongs to an active
    operation: the stop waits for that operation, which cannot finish while its own thread is blocked
    on the stop.

## License

MIT © 2026 Kewin Kupilas.
