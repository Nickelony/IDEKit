# Nickelony.IDEKit.Workspace

UI-framework-free workspace document authority and file-system coordination built on
`Nickelony.IDEKit.Core`. The package owns logical document content, its persistence state, and the
file operations behind it; it contains no view, editor, or UI types.

## Getting started

Install the package from your configured feed (the library ships from a local
feed while it is in preview):

```powershell
dotnet add package Nickelony.IDEKit.Workspace
```

```csharp
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

string documentDirectory = Path.Combine(Path.GetTempPath(), "workspace", "documents");
string documentPath = Path.Combine(documentDirectory, "notes.txt");

// A commit writes a temporary file next to the target before replacing it, so the destination
// directory must exist before the first write.
Directory.CreateDirectory(documentDirectory);

IWorkspaceFileSystem fileSystem = new LocalWorkspaceFileSystem();
await using var store = new WorkspaceDocumentStore(fileSystem);

WorkspaceDocumentOpenResult opened = await store.OpenAsync(
    documentPath,
    new WorkspaceDocumentOpenOptions(
        TextEncodingKind.Utf8,
        new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf)));

if (opened.Outcome is WorkspaceDocumentOpenOutcome.Opened or WorkspaceDocumentOpenOutcome.AlreadyOpen)
{
    WorkspaceDocumentSnapshot snapshot = opened.Snapshot!;
    WorkspaceDocumentCommitResult committed = await store.CommitAsync(
        new WorkspaceDocumentCommitRequest(
            new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
            snapshot.OnDiskStamp));
}
```

The view-coordination quick start (attaching an editor view through `WorkspaceDocumentManager`)
lives in the [Views package README](../Nickelony.IDEKit.Workspace.Views/README.md).

## Requirements

.NET 8.0 or later (the package targets `net8.0` and is built with nullable reference types
enabled).

## Dependencies

`Nickelony.IDEKit.Core` only - UI-framework-free. Targets `net8.0`. The license terms are
in the repository's `LICENSE` file.

## What's inside

The package contains four vertical slices:

- `Nickelony.IDEKit.Workspace.Documents` - the document authority: logical
  document ownership, persistence state, internal path normalization, and
  open/destination reservations (`WorkspaceDocumentStore`, split across
  per-operation partial files, and its read-side `IWorkspaceDocumentReader`
  slice, plus the `WorkspaceTextCodec`). `WorkspaceDocumentOptions` carries the
  identity policy the store, the file system, and the reload coordinator share.
  The document-authority failure
  vocabulary (`WorkspaceOperationFailureCodes`) lives in the root namespace;
  view-manager failures use `WorkspaceViewOperationFailureCodes` in the Views package.
- `Nickelony.IDEKit.Workspace.Documents.FileSystem` - the file-system seam and
  its result vocabulary: `IWorkspaceFileSystem`, the `LocalWorkspaceFileSystem`
  default implementation, `WorkspaceFileSystemDecorator` for single-member host
  overrides, and the read/replace/move/delete result contracts.
- `Nickelony.IDEKit.Workspace.Documents.Reloading` - the host-facing external
  reload pass: `FileReloadCoordinator` with its `FileReloadHooks` bundle.
- `Nickelony.IDEKit.Workspace.Editing` - the neutral workspace-edit application
  core (`WorkspaceEditApplier`) together with the multi-file application
  result contracts (`WorkspaceEditApplicationResult`,
  `WorkspaceEditApplicationOutcome`, `WorkspaceEditTargetPreparation`,
  `WorkspaceEditTargetResult`, `WorkspaceEditTargetOutcome`,
  `WorkspaceEditChangeSet`, `WorkspaceDocumentChange`).
  - Targets are identified by host-supplied `TargetId` strings, typically document file paths,
    and a prepared target carries the content and format before and after the transformation. A
    target whose content and format both match is a no-op that never reaches the store, and
    confirmed changes record both formats.
  - Use the `WorkspaceEditApplicationResult` factories (`Completed`, `PartiallyApplied`,
    `ValidationFailed`, `Canceled`) so the outcome, failure, and change-set members cannot
    contradict each other. Changed and unknown target ids are de-duplicated with the comparison
    supplied to the factory (the default follows the operating system; supply the store's
    `PathComparison` value so the comparison cannot drift), preserving first-occurrence order.
  - The applier applies multiple prepared changes to the same document in order by tracking the
    version each accepted replacement returns, applies the targets themselves in list order, stops
    at the first target that is not applied unless it was constructed with `continueOnFailure`, and
    accepts a cancellation token that marks the remaining targets not applied. An
    `OperationCanceledException` thrown by the replacement delegate propagates to the caller.
  - The `WorkspaceEdit` name is narrower than the Language Server Protocol's `WorkspaceEdit`: the
    applier applies whole-content replacements to existing tracked documents and does not create,
    rename, or delete resources.

Document-view coordination is a separate package, `Nickelony.IDEKit.Workspace.Views`, so a
headless consumer that only needs document authority never takes the view contracts. That package
covers opening a view on a document, applying view edits back to the document, delete guards, and
view synchronization outcomes (`IWorkspaceDocumentView`, `WorkspaceDocumentManager`).

Types are grouped by slice in shared contract files (for example
`Documents/WorkspaceFileContracts.cs`, which holds the file-format and on-disk-state model, and
`Documents/WorkspaceDocumentMutationContracts.cs`) as a deliberate exception to the one-type-per-file
default: the contracts in each group change together and are read together.
`WorkspaceDocumentStore` is additionally split across per-operation partial files
(`WorkspaceDocumentStore.Open.cs`, `.Persistence.cs`, `.FileMoves.cs`,
`.DirectoryOperations.cs`, `.OperationPreamble.cs`, `.Reservations.cs`, `.Lifecycle.cs`).

## Concepts

- **Document authority.** The store that owns logical document content and its persistence state;
  it is the single point that decides whether a change is written, reloaded, or reported as a
  conflict.
- **Document id.** The normalized full path that identifies a document; comparison follows the
  configured `LocalPathComparisonPolicy`.
- **Stamp (`FileStamp`).** The captured disk state (existence, length, last-write time, and SHA-256
  content hash) that requests use as a precondition and results report as an observation.
  `FileStamp.Directory` marks a path that exists as a directory instead of a file, so a
  missing-source rename, a save-as destination, and a write onto an occupied path reject a directory
  instead of treating it as absent.
- **Disk gate.** The per-document semaphore that serializes operations touching one document's
  disk state; a dirty reload deliberately bypasses it.
- **Reservation.** The store-internal claim (open, destination, or directory operation) that makes
  concurrent callers wait for an in-flight operation and re-evaluate afterwards.

## Operation model

- **Optimistic concurrency.** Requests carry a `WorkspaceDocumentRequestIdentity`
  (document key, normalized id, and expected version). The file-oriented disk-mutating
  operations (`commit`, `rename`, `save-as`, `delete`) carry the expected `FileStamp`; a
  conflict-resolution request carries the stamp it observed, a reload validates the tracked
  stamp instead, and a directory rename or delete validates each tracked descendant's stamp
  internally. Stale expectations produce result outcomes such as
  `StaleDocument` or `ExternalFileConflict` instead of mutating a different state; a
  null or blank document id is an argument error.
- **Read-side slice.** `IWorkspaceDocumentReader` exposes the read-only surface,
  so consumers that only inspect document state can depend on a smaller contract
  than the full store.
- **Thread affinity.** Store operations are safe to call concurrently; the store does not
  capture the caller's context, so its continuations may resume on the thread pool. An operation
  that has already started when the store is disposed reports its `Canceled` outcome - a dirty
  reload, which bypasses the per-document disk gate and is not registered as an active operation,
  included - and only a call made after disposal throws `ObjectDisposedException`. A dirty reload
  reports `OperationInProgress` while a delete of the same document is in flight.
- **Path identity.** Document ids are normalized full paths compared with the shared
  `Nickelony.IDEKit.Core.Pathing.LocalPathComparisonPolicy` policy (case-insensitive on Windows and macOS,
  ordinal elsewhere, exposed as `LocalPathComparisonPolicy.ForCurrentPlatform`). Only fully
  qualified paths are accepted; a relative path is invalid input because resolving it
  against the process current directory would bind document identity to ambient process
  state. The platform value is an operating-system assumption, not a probe of the volume:
  create one `WorkspaceDocumentOptions` with `PathComparison =
  LocalPathComparisonPolicy.CaseSensitive` on a case-sensitive macOS or Linux volume, and pass
  that same value to the store, the file system, and the reload coordinator. One shared value is
  what keeps the comparison from drifting between them.
- **Deletion policy.** `LocalWorkspaceFileSystem` deletes permanently, and the store's delete
  members inherit that behavior; deleting through the store is destructive by default. A host
  that needs recoverable deletion (a trash folder or the Windows Shell recycle bin) supplies a
  `WorkspaceFileSystemDecorator` instead of relying on the library. See the delete member
  documentation for the contract, including the path-kind failure a mismatched delete reports.
- **Document scope.** Documents are path-identified. A missing path opens as a new empty
  document that can be saved there by default; pass
  `WorkspaceDocumentOpenOptions.CreateIfMissing: false` to report
  `WorkspaceDocumentOpenOutcome.NotFound` instead. Saving as onto the document's own file saves
  in place and reports `SavedAs` instead of a destination collision. Renaming a document whose
  expected source stamp is missing retargets the document identity without a file-system move,
  so a not-yet-saved document can be renamed and later saved at the new path. The package has
  no untitled or read-only document model, and documents are always text: a binary file fails
  to open or reload with the `InvalidEncoding` failure code. A host with untitled documents
  supplies a unique synthetic path per document and removes the document when it is discarded.
- **Directory operations.** A directory rename or recursive delete reserves its source subtree
  (including the directory path itself) while it runs: an open for a path in the subtree waits for
  the operation and then reads the post-operation state (the vacated path after a rename), a file
  rename or save-as into the subtree reports a busy destination, and in-flight loads and writes
  inside the subtree are awaited before the physical move or delete.
- **Result shape.** The persist-lifecycle results (commit, reload, conflict resolution, rename,
  save-as, delete) all carry the same four members: `RequestedIdentity` - what the caller asked
  for; `Snapshot` - the resulting tracked state, or `null` when the document was not found;
  `ObservedOnDiskStamp` - the stamp the operation closed on, when one was observed; and
  `Failure` - present only when the operation failed. A host can therefore write one routine
  that logs the failure, refreshes its snapshot, and re-baselines its stamp, and switch on the
  concrete result type only where the operation-specific outcome enum matters. The outcome
  enums stay per-operation deliberately: each names its own success and rejection states, and
  folding them into one shared enum would trade that meaning for a single switch. Directory
  results differ by design - they report a `Snapshots` list and the directory path instead of
  one identity, and logical mutations report neither a stamp nor a failure because they never
  touch disk.

## Custom file systems

`LocalWorkspaceFileSystem` is the default `IWorkspaceFileSystem`; text encoding is provided
separately by the stateless `WorkspaceTextCodec`. Hosts override individual members with
`WorkspaceFileSystemDecorator`, which forwards every member to an inner implementation:

- **Deletion.** The default deletes permanently. Derive from `WorkspaceFileSystemDecorator`
  and override the delete members to route deletes through a trash or recycle-bin
  implementation instead; an override must still validate `expectedStamp` before it touches
  the file, or the trash move loses the store's conflict guarantee.
- **Stamp cost.** Stamp capture reads and hashes the whole file (SHA-256), so large files
  and network shares pay that cost on every commit, rename, save-as, delete, reload, and
  conflict resolution. A clean no-op commit skips stamp capture because no write occurs.
  A host that needs cheaper stamps keeps one stamp computation across every stamp-producing
  member (`ReadAsync`, `CaptureStampAsync`, and the `WriteFileAsync` result), because the
  store compares stamps across those sources: overriding a single member mixes stamp
  vocabularies and turns cross-source comparisons into spurious external conflicts. The store
  compares `FileStamp` values for equality and reads the existence and directory flags; it does not
  interpret the hash or timestamp, and it re-captures the stamp after a write when the
  write result does not report one. The full-content hash is deliberately not replaced by an
  mtime-plus-size fast path: it is what detects a rewrite that keeps the previous last-write time
  (a coarse timestamp or a network share), and a partial stamp would mix vocabularies with the
  other stamp sources.
- **Case-only renames.** Construct the file system from the same `WorkspaceDocumentOptions`
  value as the store. A case-insensitive policy routes a case-only rename through an intermediate
  path because the destination resolves to the source file; a case-sensitive policy
  treats the two spellings as distinct paths.
- **Encoding.** The supported encodings are fixed (UTF-8, UTF-16 in both byte orders, and
  Windows-1252), and the stateless `WorkspaceTextCodec` is used directly rather than through a
  seam; a host that needs another encoding converts at its own boundary before content reaches
  the store. Windows-1252 is not enabled by the library: a host that selects it must call
  `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` during startup, or resolving the
  encoding throws an `ArgumentException`.

A minimal trash decorator keeps every other member forwarding to the default file system:

```csharp
sealed class TrashFileSystem(IWorkspaceFileSystem inner) : WorkspaceFileSystemDecorator(inner)
{
    public override Task<WorkspaceFileDeleteResult> DeleteAsync(
        string path, FileStamp expectedStamp, CancellationToken cancellationToken = default)
        => HostTrash.MoveToTrash(path, expectedStamp, cancellationToken); // host trash/recycle-bin seam
}
```

## Optional architecture, not a requirement

The workspace document store is an optional part of the editor architecture. A host that only
needs the editor packages (`Nickelony.IDEKit.AvalonEdit` and related packages) does not need this
package at all, and base `Nickelony.IDEKit.AvalonEdit` deliberately does not reference it. The
AvalonEdit-to-Workspace bridge is host code, not a package; adopt this package directly when you
want document authority for your own host, and add
[Nickelony.IDEKit.Workspace.Views](../Nickelony.IDEKit.Workspace.Views/README.md) when you want
the view-coordination manager as well.

## License

MIT © 2026 Kewin Kupilas.
