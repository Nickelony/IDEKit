# Workspace views guide

`Nickelony.IDEKit.Workspace.Views` is the view slice of the workspace family. Its
`Nickelony.IDEKit.Workspace.Views` namespace holds the document-view contract (`IWorkspaceDocumentView`), and its
`Nickelony.IDEKit.Workspace.Views.Coordination` namespace holds the coordinator (`WorkspaceViewCoordinator`) that
synchronizes attached views with the tracked documents of `Nickelony.IDEKit.Workspace`. A view author imports the
first namespace and a host that runs the coordinator imports both.

It is UI-framework-free and depends on `Nickelony.IDEKit.Workspace`, `Nickelony.IDEKit.LanguageFeatures` and Core.
It is optional: a host that needs only document authority (a headless server, a tool, a service) adopts
`Nickelony.IDEKit.Workspace` without it.

This guide covers implementing a view, the result shape, the dispatcher, the blocking policy, delete leases,
identity changes, reuse and closure, reading documents, applying a language-server edit and mirroring documents to
a language server; the document authority itself is in the [workspace guide](WorkspaceGuide.md). The editor view of the samples,
[`SampleDocumentView`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleDocumentView.cs),
derives from the base that [an editor view](#an-editor-view) describes.

A host passes the coordinator the document store and the dispatcher of the thread that owns its views:

```csharp
IUiDispatcher dispatcher = UiDispatcher.FromCurrent(); // the dispatcher of an editor binding

await using var coordinator = new WorkspaceViewCoordinator(store, dispatcher);
```

## Concepts

- **View** - the interactive buffer a host renders, implementing `IWorkspaceDocumentView`. A view owns only
  the buffer it renders. It also reports its relationship to the document through `DocumentKey`,
  `HasPendingEdits`, and `HasConflict`.
- **Registration** - the coordinator's record of one view attached to one tracked document, identified by the
  view's `ViewId`. The coordinator tracks membership with the store's own path-comparison policy
  (`IWorkspaceDocumentReader.Options`), so both slices agree on which paths are the same document.
- **Attach and detach** - `OpenWithViewAsync` opens the document when it is not tracked yet and attaches the view to
  it, which creates the registration. `DetachView` removes the registration and the event subscription, and it
  does not close the view.
- **Draft** - the unsaved logical content of a document, which the store keeps beside the persisted baseline.
- **Draft ownership** - the document authority owns the draft: `WorkspaceDocumentSnapshot` reports the content
  with the dirty flag and the version pair, and a view publishes edits by raising `ApplyRequested`. The coordinator
  never treats a view as the source of truth for content.
- **Publication point** - a view is where edits originate, not where the draft lives. The view chooses when to
  publish, and the coordinator applies the edit to the store. The store stays free of editor-frequency traffic.
- **The view contracts stay in this optional package**, so a host that needs only document authority does not take
  them.
- **Synchronization outcome** - `WorkspaceDocumentViewSynchronizationOutcome` is `Synchronized`, `Blocked` (an
  attached view prevented the operation before the store was reached), or `Unsynchronized` (the document
  operation completed but a view stayed unsynchronized).

## Implementing a view

A view implements `IWorkspaceDocumentView`. This read-only view has no editable buffer, so it never has
pending edits or a conflict and never raises `ApplyRequested`:

```csharp
public sealed class PreviewView : IWorkspaceDocumentView
{
    public event EventHandler<ApplyRequestedEventArgs> ApplyRequested
    {
        add { }
        remove { }
    }

    public string ViewId => "preview";
    public WorkspaceDocumentKey? DocumentKey { get; private set; }
    public bool HasPendingEdits => false;
    public bool HasConflict => false;

    public WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot)
    {
        DocumentKey = snapshot.DocumentKey; // show snapshot.Content
        return new(WorkspaceDocumentViewOpenOutcome.Opened);
    }

    // Refresh, AcknowledgeIdentity, and AcknowledgeApply adopt a snapshot the same way.

    public void Close() => DocumentKey = null;
}
```

- **`ViewId`** stays the same, and is not blank, while the view is registered; `DocumentKey` is `null` while
  the view is detached.
- **`Refresh`, `AcknowledgeIdentity`, and `AcknowledgeApply`** adopt the snapshot of their argument, of
  `change.Snapshot`, and of `result.Snapshot` (`null` when the document was not found), then return
  `Refreshed`, `Updated`, and `Applied`.
- **`Close`** must be idempotent and safe for a view that never attached, because a delete closes a view its
  owner may already have closed and a rejected attach closes the view. It does not detach the view; the
  owner calls `DetachView`.

### An editor view

An editor view derives from `WorkspaceDocumentTextViewBase`, which implements the view, delete-lease and
publishing contracts, keeps the synchronized snapshot, the pending edits, the delete leases and the dirty state,
and follows a rename. The host reads, replaces and locks the editor text, and reports each change of it:

```csharp
public sealed class EditorView : WorkspaceDocumentTextViewBase
{
    private readonly TextEditor _editor;

    public EditorView(string viewId, TextEditor editor, IUiDispatcher dispatcher)
        : base(viewId, dispatcher, publishDelay: TimeSpan.FromMilliseconds(300))
    {
        _editor = editor;
        _editor.TextChanged += (_, _) => HandleTextChanged();
    }

    protected override bool IsReadOnly
    {
        get => _editor.IsReadOnly;
        set => _editor.IsReadOnly = value;
    }

    protected override string ReadText() => _editor.Text;

    // ReplaceContent of an editor binding replaces only the lines and characters that differ, so the caret, the selection and the scroll stay.
    protected override void ReplaceText(string text) => _editor.ReplaceContent(text);
}
```

The publish delay decides when typed text reaches the store:

| `publishDelay` | The view publishes |
| --- | --- |
| `null` | Only when `PublishPendingEditsAsync` asks. |
| `TimeSpan.Zero` | On each change of the text. |
| A positive delay | Once the text stayed unchanged for the delay, through a `DispatcherDebouncer` on the dispatcher. |

- **Another writer's change merges into the pending edits.** The view merges by line hunks, or reports a
  conflict, as [merging another writer's change](#merging-another-writers-change) describes.
- **A publication carries the line terminator style of its text.** The view detects the style of the text it
  publishes, so converting the terminators of the editor needs no call to the store, and undoing the conversion
  publishes the style of the restored text.
- **A text without a line terminator** keeps the style the document records, and the encoding and the byte-order
  mark are always the document's.
- **One publication is in flight at a time.** A publication asked for before the store acknowledged the last one
  follows that acknowledgment against the new base, and an acknowledgment counts only when it carries the
  identity and, once applied, the text of the publication.
- **An apply that throws** ends the publication without an acknowledgment, so the text stays pending and the next
  change or save publishes it again.
- **The delete leases nest.** The first entry saves `IsReadOnly` and sets it, the last release restores it, and
  a release with none held reports `Released`. A release counts down even when the restore throws, and the next
  release of the last lease or `Close` retries the restore.
- **A `ReplaceText` or `IsReadOnly` that throws** turns into the failed or unavailable outcome of the call, with a
  code of `WorkspaceViewOperationFailureCodes` and the exception attached, and the view keeps the state it had. A
  `ReadText` that throws propagates to the caller.
- **A closed or disposed view** answers the coordinator's refresh, acknowledgments and lease entries with their
  failed outcome and leaves the editor alone. A closed view still releases a lease it entered before the close,
  so the editor does not stay read-only.
- **`DirtyChanged` and `DocumentRenamed`** let a tab follow the unsaved state and the path. The view raises
  `DocumentRenamed` inside the coordinator's acknowledgment, so a handler posts the attach or detach it starts.

#### Merging another writer's change

A view that holds pending edits moves onto text that another writer changed in two cases: the store rejects its
publication as stale, or a rename, a save-as or a directory rename hands it a snapshot whose text changed
meanwhile. The view then merges the two changes by line hunks against the text it last synchronized, and a line
terminator counts as part of its line:

- A hunk of one side that overlaps no hunk of the other side is applied, so typing at the top and at the bottom
  merges with a change in between.
- Two hunks that change the same lines to the same text count once, so a view whose publication the store took
  without an acknowledgment reaching the view does not conflict with its own text.
- An insertion at the edge of a run that the other side replaced lands beside it, and two insertions at the same
  point overlap.
- When no hunks overlap, the view shows the merged text, with the caret where the typing put it, and after a
  rejected publication it publishes that text.
- When hunks overlap with different lines, `HasConflict` turns `true` and `ConflictChanged` is raised. The typed
  text stays pending and unpublished, and the operations that destroy the document block on the view, with an
  issue that carries no `Failure`.

The host ends a conflict with `KeepViewText`, which takes the view's side of each conflicting region, or with
`TakeStoreText`, which takes the store's side. Both keep every other hunk of both sides and publish the result, and
a discard or `Close` ends the conflict too. The view raises `ConflictChanged` inside the coordinator's
acknowledgment, which a UI dispatcher runs inline on its thread, so a handler posts its resolution:

```csharp
view.ConflictChanged += (_, _) => dispatcher.Post(() =>
{
    if (view.HasConflict)
        view.KeepViewText();
});
```

A host that lets the user decide shows the choice from the handler, and each button resolves on the dispatcher:

```csharp
view.ConflictChanged += (_, _) => dispatcher.Post(() => conflictBar.IsVisible = view.HasConflict);

keepButton.Click += (_, _) => view.KeepViewText();
takeButton.Click += (_, _) => view.TakeStoreText();
```

A merged change is a step of the editor's own undo history, as an adopted refresh is. Each editor keeps one
history, so undoing the other writer's change in one view publishes that undo to the store and to every other view.

## Result shape

Every operation result except the open result derives from `WorkspaceViewCoordinatorResult<TStoreResult>`, which
composes the document-authority outcome (`StoreResult`) with the view-synchronization outcome
(`ViewSynchronization`). `StoreResult` is `null` exactly when attached views blocked the operation before the
store was reached, and then `ViewSynchronization` is `Blocked`; a result rejects any other combination when it
is created. These sealed results cover the operations:

- **`WorkspaceViewCoordinatorDocumentResult<TStoreResult>`** is the result of a host replacement, commit, reload,
  conflict resolution and delete, which views can block. A blocked result carries the current snapshot as
  `BlockedSnapshot`.
- **`WorkspaceViewCoordinatorDirectoryResult<TStoreResult>`** is the result of a directory rename and a directory
  delete. A blocked result carries the source snapshots as `BlockedSnapshots`.
- **`WorkspaceViewCoordinatorUnblockedResult<TStoreResult>`** is the result of a discard, a rename and a save-as,
  which views never block, so its `StoreResult` is never `null`.

`StoreResult` is the single source for the document outcome, the failure and the snapshot, so a host logs a failure
and refreshes a snapshot from one shape. Construct these results through their `FromStore` factory, or through
`Blocked` for the operations that can block; both keep the store and view outcomes consistent.

The open result (`WorkspaceViewCoordinatorOpenResult`) has no `ViewSynchronization`, and its `Outcome` uses
the coordinator vocabulary, which can differ from the store outcome. Its `StoreResult` is `null` when view
validation answered before the store was reached.

`WorkspaceDocumentViewSynchronizationResult` has no public constructor: `Synchronized`, `Blocked(issues)` and
`Unsynchronized(issues)` create it, and its `Issues` are de-duplicated by view id with an ordinal comparison and
sorted ordinally, so the reported order does not depend on registration or enumeration order. When a view is
reported more than once, the entry that carries a `Failure` is kept.

An issue's `Failure` is populated when the view reported one: a refresh, acknowledgment, identity update,
delete-lease, close, or state-probe failure, returned or thrown by the view. It is `null` when the view
blocked the operation through its state (pending edits or a conflict), which the host can inspect on the view
itself.

## Dispatcher

The coordinator owns no thread or message loop. Every action that touches a view member runs through
`IUiDispatcher.InvokeAsync` of the dispatcher supplied to the constructor:

- The dispatcher must run the action before its returned task completes and let the action's exceptions propagate;
  the task may complete asynchronously, because the coordinator awaits it instead of blocking.
- The coordinator consumes the results the action captured right after the task completes.
- A UI host passes the dispatcher of its message loop; a headless host passes a dispatcher that runs the action on
  the calling thread.
- A caller on the message-loop thread of a UI dispatcher awaits the coordinator's tasks and never blocks on one
  with `GetAwaiter().GetResult()`, because a view action then waits for the thread the caller holds, and the call
  deadlocks.
- Actions may arrive from arbitrary threads, from concurrent operations, and re-entrantly from inside a
  dispatched action (a view raising `ApplyRequested`). A dispatcher whose views are not thread-safe or
  reentrancy-safe must serialize or queue the actions itself. One coordinator coordinates one store.
- The subscription itself is never dispatched: the add runs on the thread that resumes the attach after its
  dispatched action completes - not necessarily the caller's thread or the dispatch thread, because the store
  open between them is asynchronous.
- A direct `DetachView` removes the handler on the calling thread, and coordinator-initiated removals
  during a stop or delete teardown run on the dispatch thread. Event accessors must be usable from any thread.
- The event-driven apply runs its store mutation on the raising thread; the acknowledgment that follows is
  dispatched.
- A view member that throws is isolated (reported through the operation result or recorded as unsynchronized
  state). A fault raised by the dispatcher itself fails the operation, except during the best-effort lease
  release on exit, which records the affected views as unsynchronized.
- Host-initiated mutations use `ReplaceAsync`; a view that publishes its own edits raises `ApplyRequested` and
  learns the outcome through `AcknowledgeApply`, and its publication is never blocked.

Members that return a task validate their own arguments before any work is dispatched: a `null` request or view
throws from the call itself instead of faulting the returned task. A request is otherwise forwarded unchanged, so
the store's argument errors (an empty document id, an unencodable replacement format, an undefined conflict-resolution
choice) surface as failures of the returned task. `ReplaceAsync` and `DiscardAsync` take a cancellation token like
the other operations; a token canceled before the store is reached cancels the returned task.

## Blocking policy

Operations that destroy the document behind a view consult attached view state first:

- A host replacement (`ReplaceAsync`) reports `Blocked` while a view has pending edits or a conflict, or its state
  probe throws, because the replacement would overwrite that text. A prior synchronization failure does not block,
  and the replacement refreshes the view.
- The check, the store replacement and the refresh of a host replacement run in one dispatched action, so when the
  dispatcher runs the views' own thread, typing cannot land between the check and the replacement.
- Once the store changed the document, a host replacement returns its result even when the dispatcher faults
  afterwards, and it reports the views it could not refresh as `Unsynchronized`.
- Delete and delete-directory report `Blocked` while a view has pending edits, a conflict, or an unsynchronized
  failure.
- The check of a delete runs in the dispatched action that enters the delete leases, after they are entered, so
  typing cannot land between the check and the leases, and a blocked delete releases the leases again.
- That action reads the views again, so a view that detached since the delete started is neither leased nor
  checked.
- The recovery operations - commit, reload, and conflict resolution - block only on pending edits and conflicts
  and deliberately ignore a prior synchronization failure, so they can be retried and their post-operation refresh
  retries the failed synchronization.
- Operations that only change the document identity or path - rename, save-as, and directory rename -
  never block on view state. They cannot lose view edits, and the identity change is acknowledged
  afterwards; a view update failure is reported as `Unsynchronized`.
- A directory rename still reports `Blocked` when a view rejects its delete lease (see
  [delete leases](#delete-leases)).

The post-operation report covers view state, not only failures: a view that has pending edits or a conflict when
the report is composed - including one that acquired them while the operation ran - is reported as
unsynchronized.

## Publishing held-back edits

A view that holds typed text back, for example until typing pauses, implements `IWorkspaceDocumentPublishingView`.
Before an operation that must see the text the user sees, such as a save or a reload, the host calls
`PublishPendingEditsAsync`, and `WorkspaceEditApplier` calls it before a [language-server edit](#language-server-edits):
each such view raises `ApplyRequested` with its text, and the call completes once the store applied it.

```csharp
WorkspaceViewCoordinatorPublicationResult published = await coordinator.PublishPendingEditsAsync(path);

// The publication moved the version on, so the commit takes the snapshot the final check read.
if (published.Snapshot is { } snapshot)
    await coordinator.CommitAsync(new WorkspaceDocumentCommitRequest(snapshot.Identity, snapshot.OnDiskStamp));
```

The result carries a snapshot only when no view blocks, and the coordinator reads it in the dispatched action of
its final check, so a publication that typing scheduled cannot land between the check and the snapshot. A view
that cannot publish, that throws, or that holds a conflict is reported as `Blocked`. Without the publication a
store operation leaves the held-back text behind it: a save writes older text, and the view's next publication
overwrites a language-server edit.

`ApplyRequested` stays on the base contract, and a raise has no completion of its own:
`PublishPendingEditsAsync` is its completion channel. The call also awaits an apply that a view raised before
the call and that is still in flight, and an apply that a view raises inside the acknowledgment of one, so its
report covers every edit the views of the document published.

### Changing the encoding of a document with views

A view cannot choose the encoding of its publication, so the host changes it through the coordinator. A view that
holds pending edits blocks the replacement, so the host publishes first, and a save follows the replacement:

```csharp
WorkspaceViewCoordinatorPublicationResult published = await coordinator.PublishPendingEditsAsync(path);

if (published.Snapshot is { } snapshot)
{
    TextFileFormat format = snapshot.FileFormat with { Encoding = TextEncoding.Utf16LittleEndian, HasBom = true };

    var replaced = await coordinator.ReplaceAsync(new WorkspaceDocumentReplaceRequest(snapshot.Identity, snapshot.Content, format));

    if (replaced.StoreResult is { Applied: true, Snapshot: { } changed })
        await coordinator.CommitAsync(new WorkspaceDocumentCommitRequest(changed.Identity, changed.OnDiskStamp));
}
```

- **The replacement refreshes every view.** The views keep their text and publish the new encoding with their later
  edits, and the document reports unsaved changes until the commit writes it.
- **Typing between the publication and the replacement blocks the replacement.** The result is `Blocked`, and the
  host publishes again.
- **A commit that fails keeps the new encoding on the document.** A character the encoding cannot represent fails
  the commit with the `InvalidEncoding` failure code, so the host replaces the previous format back or discards.
- **A reload with another encoding needs a clean document.** The coordinator's `ReloadAsync` takes the request that
  the [workspace guide](WorkspaceGuide.md#encoding) describes, and a document with unsaved changes answers
  `UnsavedChanges`, so the host saves or discards before it asks again.

## Unsynchronized state

A view whose refresh or acknowledgment failed is retained as unsynchronized, with the failure detail for
reporting, until a successful refresh or identity acknowledgment clears that state. The
[blocking policy](#blocking-policy) states which operations block on it.

## Delete leases

Delete operations (file and directory) and directory moves ask attached views that implement
`IWorkspaceDocumentDeleteLeaseView` to enter a delete lease. A lease is a view-defined barrier, for example
suspending live updates while the move runs. A view without the capability is skipped, because it cannot hold
state that one of those operations would lose.

When a view rejects the lease, the operation is reported as `Blocked` before the store is reached; entered
leases are always released, including when the operation throws.

A lease-release failure is reported as an unsynchronized issue on the result, and it is also recorded as
unsynchronized view state unless the operation closes the view anyway.

The lease is two-phase - the views enter it, the store operation runs, and the views release it - because a
barrier must hold while the operation runs, which a check made before the operation cannot do. VS Code's
file-operation participants (`workspace.onWillDeleteFiles`, mirrored by the LSP's `workspace/willDeleteFiles`)
contribute edits but cannot block a delete, so they do not replace a view-defined barrier.

## Identity changes

Rename, save-as, and directory rename ask attached views to acknowledge the new identity through
`AcknowledgeIdentity`; a view that cannot accept the change is reported as `Unsynchronized` on the result and
is retained as [unsynchronized](#unsynchronized-state).

A view that attached while a directory move was in flight is acknowledged as well, matched to its moved
document by its document key.

## Reuse and closure

- A view that is already registered is reported as `AlreadyOpen` with the snapshot of the document it is attached
  to.
- A different view that duplicates a registered view id is `ViewInUse`.
- A file or directory delete that completes closes and detaches every attached view of the removed
  documents, including a view that attached to a removed document while the delete ran.
- A view whose `Close` throws is still detached, and the result reports it as an `Unsynchronized` issue whose
  `Failure` has the code `ViewCloseFailed` and carries the exception.
- Stopping the coordinator (`StopAsync`/`DisposeAsync`) rejects new operations, waits for active ones, closes and
  detaches every registered view, and releases all view-tracking state.
- A `StopAsync` or `DisposeAsync` call from inside a view member, a dispatched action, or the dispatcher
  of an active operation throws `InvalidOperationException`, because the stop would wait for that
  operation. Request the stop from outside the operation.
- The check follows the operation's flow, so work the member starts, a `Task.Run` call included, and code that
  runs on the member's thread meanwhile, a nested message loop included, throws too.

## Reading documents

`IWorkspaceViewCoordinator.Documents` returns the coordinator's own store as an `IWorkspaceDocumentReader`, so a
consumer that only inspects document state reads snapshots through the coordinator's reader without taking the full
store. Its `Options` carry the path comparison the coordinator uses to match views to documents; the
[workspace guide](WorkspaceGuide.md#read-side-slice) describes the reader.

## Language-server edits

A language server answers a rename or a code action with a `WorkspaceEdit` of line and character edits.
`WorkspaceEditApplier` applies it through the coordinator, so every attached view follows the change:

```csharp
var edits = new WorkspaceEditApplier(
    coordinator,
    new WorkspaceEditApplierOptions { OpenOptions = WorkspaceDocumentOpenOptions.Default with { CreateIfMissing = false } });

WorkspaceEditApplyResult result = await edits.ApplyAsync(edit);
```

The options open a document the store does not track; `CreateIfMissing = false` keeps an edit that names a missing
file from opening it as a new empty document. An apply runs in four steps:

1. It opens each document the store does not track, when `OpenOptions` is set.
2. It has the views of each document publish their pending edits, so the edit addresses the text the user sees, and
   it records the version each publication left.
3. It converts the edits against the snapshot the store holds now into one whole-content replacement per document
   the edit changes.
4. When the plan succeeded, it replaces the documents one at a time.

The plan is all or nothing, and a plan with an issue changes no document. These are its issues:

| Kind | The document |
| --- | --- |
| `DocumentNotTracked` | is not tracked; when the apply tried to open it, `OpenResult` carries the failed open. |
| `InvalidRange` | has an edit whose range ends before it starts. |
| `EditsConflict` | has edits that overlap. |
| `PendingEditsNotPublished` | has a view that kept edits it could not publish, a conflict or a failed synchronization. |
| `DocumentChanged` | changed after its views published and before the plan, such as by a publication that typing scheduled. |

The replacements are not atomic, and `Replacement` reports each document. Each one goes through `ReplaceAsync`,
so a document keeps the user's text in two cases:

- When the user typed after the plan and the publication reached the store, its replacement is stale.
- When the typing is still pending in a view, the replacement is blocked and the target is not applied, with
  `ViewHoldsPendingEdits`.

`ViewSynchronization` maps each document the replacements
reached or the views blocked to its view synchronization, so a host sees a view that held edits or did not follow.
Either way an earlier document keeps its confirmed change. The [change-set applier](WorkspaceGuide.md#the-document-change-set-applier)
owns the replacement rules, from version chaining to cancellation.

A cancellation or a stop of the coordinator before the plan throws `OperationCanceledException` or
`ObjectDisposedException`, and no document changed. From the first replacement on, a cancellation, a stop or a
dispatcher that rejects a replacement ends the apply with a `Canceled` or `PartiallyApplied` replacement result
instead, whose failure carries the fault, so the host learns which documents changed. What
follows an apply is host code, such as saving the documents that no view shows.

## Mirroring documents to a language server

A language server tracks each document once, while a host can show one document in several views. The
coordinator's `AttachedDocumentChanged` event therefore reports documents, not views:

| Kind | Raised when |
| --- | --- |
| `Opened` | The first view of the document attaches. |
| `ContentChanged` | An operation changes the text of the document, such as a publication, a reload or a discard. |
| `Renamed` | A rename, a save-as or a directory rename gives the document another id; `OldDocumentId` names the old one, and the snapshot carries the current text, which can be new. |
| `Closed` | The last view of the document detaches, or a delete or a stop closes it. |

The coordinator raises each change on its dispatcher, in the order the operations made them, and
`AttachedDocuments` holds the newest snapshot of each document that has a view. When the dispatcher serializes its
actions, an operation raises its changes before it completes, unless it runs inside a handler; otherwise, the raise
that is running delivers them, possibly after the operation completed. A handler fault never changes the outcome
of an operation and never costs another handler a change: the coordinator posts the fault to the dispatcher, where
it is rethrown for the toolkit's unhandled-exception handler.

`WorkspaceDocumentSynchronizer` turns the changes into the calls of an `IDocumentSynchronizationTarget`, which every
language-server provider is:

```csharp
using var luaMirror = new WorkspaceDocumentSynchronizer(
    coordinator,
    luaServer,
    includes: documentId => documentId.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
```

- The target holds one copy of a document however many views show it, so closing one of two views keeps it open.
- A rename between two included ids renames the document on the target, a rename to an excluded id closes it, and
  a rename back to an included id opens it.
- The constructor opens the documents that already have a view, and `Dispose` closes every document the
  synchronizer opened, so a host that swaps servers disposes the old synchronizer before the old server.
- The target receives the text the store holds, so typed text that a view holds back reaches it when the view
  publishes; a language-feature request carries the editor text, and the provider synchronizes it first.
- Create and dispose the synchronizer on the dispatcher thread, because it handles the changes there.

A host that mirrors the documents to something else, such as a search index, subscribes first and then reads
`AttachedDocuments`, so a document that opens in between is not missed. A snapshot can repeat a change that the
event raises later, so the consumer keeps the newest version of each document:

```csharp
coordinator.AttachedDocumentChanged += (_, e) => index.Upsert(e.Snapshot);

foreach (WorkspaceDocumentSnapshot snapshot in coordinator.AttachedDocuments)
    index.Upsert(snapshot);
```

## Internals

The coordinator composes internal collaborators behind one lock, and none of them reads or invokes a view
member while it holds that lock. The coordinator validates its arguments and hands each operation that the
[blocking policy](#blocking-policy) names to the runner of that policy. The open and detach paths go to the
`ViewAttacher`, and the publication paths go to the `PublicationRunner`:

- **`ViewTracker`** holds the attached views by instance, view id and document and answers queries about them.
- **`ViewTracker` admits a registration** under the same critical section that checks for a stop, so no view
  registers after the stop drained the tracker.
- **`ViewTracker` queues an attached-document change**, counted by document key, under the lock that changes the
  registrations, so the queue order is the operation order.
- **`AttachedDocumentChangeForwarder`** raises the queued changes through the dispatcher, one raise and one
  handler at a time, so a change that a handler causes is raised after that handler returns.
- **`ViewAttacher`** runs the registration check, the view's open and the registration in one dispatched
  action. It undoes a subscription that lost the race with a detach through the registration's own handler, so
  it never strips the handler of a later attach, and it closes and detaches the views of a removed document.
- **`ViewSynchronizer`** probes, refreshes and acknowledges the views of a document through the dispatcher,
  records a failing view as unsynchronized and reports it as an issue.
- **`DeleteLeaseCoordinator`** runs a leased operation: it enters the delete leases and checks the views in one
  dispatched action, runs the store operation, and releases the leases in the dispatched action that updates the
  views. The set of entered leases is `IAsyncDisposable`, so an exit path that left a lease held still releases it.
- **`DestroyOperationRunner`** runs the operations that destroy the document behind a view. It checks the views
  before the store changes anything and reports `Blocked` when a view would lose state.
- **`RecoverOperationRunner`** runs the recovery operations. It blocks only on pending edits and conflicts, and
  its refresh retries a failed synchronization.
- **`FollowOperationRunner`** runs the operations that no view state blocks, so the views follow the store.
- **`PublicationRunner`** applies the edit that a view publishes without blocking the view, and runs the
  publication that a host asks for. It tracks the applies in flight, so that publication completes once the store
  applied what the views published, and it reports a view that cannot publish as `Blocked`.
- **`CoordinatorOperationRunner`** registers each operation with the `StopGate` of `Core.Threading` under the same
  lock, so a stop waits for it, and raises the attached-document changes the operation queued before it completes.
  The gate memoizes the stop task and rejects a stop requested from inside an active operation.
- **`CoordinatorTeardown`** runs the stop: it waits for the active operations, closes the registered views and
  raises the close of their documents.
