# Nickelony.IDEKit.Workspace

UI-framework-free workspace document authority and file-system coordination built on
`Nickelony.IDEKit.Core`, with nullable reference types. The package owns logical
document content, its persistence state, and the file operations behind it; it contains no view, editor, or
UI types.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.Workspace --prerelease
```

```csharp
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

string documentDirectory = Path.Combine(Path.GetTempPath(), "workspace", "documents");
string documentPath = Path.Combine(documentDirectory, "notes.txt");

// A commit writes a temporary file next to the target before replacing it, so the destination
// directory must exist before the first write.
Directory.CreateDirectory(documentDirectory);

IWorkspaceFileSystem fileSystem = new LocalWorkspaceFileSystem();
await using var store = new WorkspaceDocumentStore(fileSystem);

WorkspaceDocumentOpenResult opened = await store.OpenAsync(documentPath, WorkspaceDocumentOpenOptions.Default);

if (opened.Outcome is WorkspaceDocumentOpenOutcome.Opened or WorkspaceDocumentOpenOutcome.AlreadyOpen)
{
    WorkspaceDocumentSnapshot snapshot = opened.Snapshot!;
    WorkspaceDocumentCommitResult committed = await store.CommitAsync(
        new WorkspaceDocumentCommitRequest(snapshot.Identity, snapshot.OnDiskStamp));
}
```

The view-coordination quick start lives in the [views guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/WorkspaceViewsGuide.md).

## Remarks

- The change-set applier applies whole-content replacements to existing tracked documents and does not create,
  rename, or delete resources; those operations stay with the store.
- A target names its document through the store's `Identity`, and a result reports the document by that id.
- Use the `DocumentReplacementResult` factories so the outcome, failure, and change-set members cannot
  contradict each other, and supply the store's `WorkspaceOptions` to the factory so document-id de-duplication
  cannot drift.
- A cancellation of the applier's own token, thrown by the replacement delegate too, ends the application with a
  canceled or partial result; an `OperationCanceledException` of another token propagates to the caller.
- The file list of a quick open is the separate
  [`Nickelony.IDEKit.Workspace.QuickPick`](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.Workspace.QuickPick/README.md)
  package, so a host that only needs the store takes no quick-pick or file-watching package.
- The [Workspace guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/WorkspaceGuide.md) owns
  the concurrency model, the path and stamp identity rules, the custom file system seam, and the
  change-set applier.

## License

MIT © 2026 Kewin Kupilas.
