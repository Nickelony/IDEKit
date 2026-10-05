# Nickelony.IDEKit.Workspace.Views

UI-framework-free document-view coordination for the `Nickelony.IDEKit.Workspace` document authority, with nullable reference types. It owns the document-view contract (`IWorkspaceDocumentView`), the editor view base that implements it (`WorkspaceDocumentTextViewBase`) and the coordinator (`WorkspaceViewCoordinator`, in the `Coordination` namespace) that synchronizes attached views with tracked documents.

It is the view slice of the workspace family, split out so a host that only needs document authority (a headless server, a tool, a service) can adopt the [document authority](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.Workspace/README.md) without the view contracts. It depends on that package, on [Core](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.Core/README.md) for the text primitives the snapshot content uses, and on the [language-feature contracts](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.LanguageFeatures/README.md) for the `WorkspaceEdit` that `WorkspaceEditApplier` applies and the `IDocumentSynchronizationTarget` that `WorkspaceDocumentSynchronizer` mirrors documents to.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.Workspace.Views --prerelease
```

```csharp
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views;
using Nickelony.IDEKit.Workspace.Views.Coordination;

// Supplied by the host: the dispatcher of the thread that owns the views, such as the one an editor binding
// creates with UiDispatcher.FromCurrent().
IUiDispatcher dispatcher = hostDispatcher;

string scriptDirectory = Path.Combine(Path.GetTempPath(), "workspace", "scripts");
string scriptPath = Path.Combine(scriptDirectory, "main.lua");

// Create the destination directory first; the store stages every commit through a temporary file
// beside the target (https://github.com/Nickelony/LanguageServer/blob/main/docs/WorkspaceGuide.md#the-atomic-write).
Directory.CreateDirectory(scriptDirectory);

IWorkspaceFileSystem fileSystem = new LocalWorkspaceFileSystem();
await using var store = new WorkspaceDocumentStore(fileSystem);
await using var coordinator = new WorkspaceViewCoordinator(store, dispatcher);
// MyDocumentView is the host's editor view, shown below; MyEditor stands for the host's editor control, which
// has a Text, an IsReadOnly flag and a TextChanged event.
var editor = new MyEditor();
using var view = new MyDocumentView("main", editor, dispatcher);

WorkspaceViewCoordinatorOpenResult opened = await coordinator.OpenWithViewAsync(
    scriptPath,
    WorkspaceDocumentOpenOptions.Default,
    view);

if (opened.Outcome == WorkspaceViewCoordinatorOpenOutcome.Opened)
{
    WorkspaceDocumentSnapshot snapshot = opened.Snapshot!;
    WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await coordinator.CommitAsync(
        new WorkspaceDocumentCommitRequest(snapshot.Identity, snapshot.OnDiskStamp));
}
```

A host derives its editor view from `WorkspaceDocumentTextViewBase`, calls `HandleTextChanged` on each change of
the editor text, and implements its abstract members:

```csharp
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Views;

public sealed class MyDocumentView : WorkspaceDocumentTextViewBase
{
    private readonly MyEditor _editor;

    // The constructor passes the view id, the dispatcher and the publish delay to the base.
    public MyDocumentView(string viewId, MyEditor editor, IUiDispatcher dispatcher)
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

    // Replace only the changed middle, so the caret and the scroll position stay.
    protected override void ReplaceText(string text) => _editor.ReplaceText(text);
}
```

The [workspace views guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/WorkspaceViewsGuide.md#an-editor-view)
shows a complete editor view and its publish delay.

## Remarks

- **The coordinator owns no thread or message loop.** Every view member except the `ApplyRequested` subscription
  is dispatched through the dispatcher supplied to the constructor. The dispatcher must run the action before its
  returned task completes and let the action's exceptions propagate, but the task itself may complete
  asynchronously.
- **The dispatcher must serialize view access.** The dispatcher can be entered from arbitrary threads, concurrently, and again from inside a dispatched action, so a dispatcher whose views are not thread-safe or reentrancy-safe must serialize or queue the actions itself.
- **The document authority owns the draft.** A view owns only the interactive buffer it renders and its relationship members (`DocumentKey`, `HasPendingEdits`, `HasConflict`), and it publishes edits by raising `ApplyRequested`.
- **Blocking is per operation.** The document-destroying operations and a host replacement report `Blocked` while a view holds state the operation would lose; the recovery operations ignore a prior synchronization failure so they can be retried; the identity-only operations never block on view state.
- **A file or directory delete closes and detaches the views it captured.**
  A view that implements `IWorkspaceDocumentDeleteLeaseView` gets a barrier around a delete or directory move,
  and closing a view must be idempotent.
- **A language server gets one copy of a document however many views show it.** `WorkspaceDocumentSynchronizer`
  opens a document on its target when the first view attaches and closes it when the last view detaches, so the
  host never calls the provider's document members itself.

The [workspace views guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/WorkspaceViewsGuide.md)
owns implementing a view, the dispatcher, the result shape, the blocking policy, delete leases,
identity changes, reuse and closure, reading documents, applying a language-server edit and mirroring documents
to a language server. The [workspace
guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/WorkspaceGuide.md) owns the document
authority itself.

## License

MIT © 2026 Kewin Kupilas.
