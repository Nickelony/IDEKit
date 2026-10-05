# Workspace guide

`Nickelony.IDEKit.Workspace` is the UI-framework-free document authority for the editor family. It owns
logical document content, its persistence state, and the file operations behind it, and contains no view
or editor types. It is the authority over the documents of a [workspace](ArchitectureGuide.md#workspace).

This guide covers the [layers](#layers) of the package, the [concurrency model](#concurrency-and-locking),
the path and stamp identity rules, the custom file-system seam, the reload coordinator, and the
document change-set applier.

## Layers

The namespaces depend on each other in one direction, from the root outward, as the
[namespace rule](ArchitectureGuide.md#namespaces) requires:

- `Nickelony.IDEKit.Workspace` holds the failure record, the failure codes, and the options that every other
  namespace shares.
- `Nickelony.IDEKit.Workspace.FileSystem` holds file access, the text codec, and the file-format and stamp
  types.
- `Nickelony.IDEKit.Workspace.Documents` holds the document store, which builds on `FileSystem`.
- `Nickelony.IDEKit.Workspace.Reloading` and `Nickelony.IDEKit.Workspace.Replacement` build on `Documents`, and
  neither references the other.

The file list of a quick open is the separate `Nickelony.IDEKit.Workspace.QuickPick` package, which references
nothing of this one, so a host that only stores documents takes no quick-pick or file-watching package. The
[quick pick guide](QuickPickGuide.md#files) owns it.

## Concepts

- **Document authority.** The store that owns logical document content and its persistence state; it is the
  single point that decides whether a change is written, reloaded, or reported as a conflict.
- **Document id.** The normalized full path that identifies a document; comparison follows the configured
  `LocalPathComparisonPolicy`.
- **Stamp (`FileStamp`).** The captured disk state - existence, length, last-write time, and a SHA-256
  content hash - that requests use as a precondition and results report as an observation.
- **Directory stamp.** `FileStamp.Directory` marks a path that exists as a directory rather than a file, so
  a missing-source rename, a save-as destination, and a write onto an occupied path reject a directory
  instead of treating it as absent.
- **Disk gate.** The per-document semaphore that serializes the operations touching one document's file,
  including the stamp capture of a reload of a document with unsaved changes.
- **Reservation.** The store-internal claim on a path or subtree, held by an open, a destination, or a
  directory operation. How each claim blocks a caller is covered in
  [concurrency and locking](#concurrency-and-locking).
- **Snapshot.** A snapshot captures the logical content as an immutable string and builds line metadata on
  first line-oriented access, so creating one does not build the line index. Creating a snapshot never
  scans the content either, because the dirty comparison runs only when a mutating member changes the content
  or format.
- **Persisted version.** `PersistedVersion` trails `Version` when a logical edit advanced the document after
  an earlier version was persisted, or when a rename or save-as advanced `Version` for the identity change
  while `PersistedVersion` stayed on the version whose content was persisted.

## Operation model

### Optimistic concurrency

Requests carry a `WorkspaceDocumentRequestIdentity` (document key, normalized id, and expected version).
The disk-mutating operations (`commit`, `rename`, `save-as`, `delete`) additionally carry the expected
`FileStamp`. A conflict-resolution request carries the stamp it observed, a reload validates the tracked
stamp, and a directory rename or delete validates each tracked descendant's stamp internally.

A stale expectation produces a result outcome such as `StaleDocument` or `ExternalFileConflict` instead
of mutating a different state; a null or blank document id is an argument error.

The request's document id is validated after the disposal check, so a disposed store reports
`ObjectDisposedException` before an argument error. Path-taking members treat a null, blank, or
unnormalizable path as invalid input reported through the result rather than thrown.

### Path identity

Document ids are normalized full paths compared with the shared
`Nickelony.IDEKit.Core.Paths.LocalPathComparisonPolicy` (case-insensitive on Windows and macOS, ordinal
elsewhere, exposed as `LocalPathComparisonPolicy.ForCurrentPlatform`).

Only fully qualified paths are accepted; a relative path is invalid input because resolving it against
the process current directory would bind document identity to ambient process state.

The platform value is an operating-system assumption, not a probe of the volume. On a case-sensitive macOS
volume, set `PathComparison` to `LocalPathComparisonPolicy.CaseSensitive`; on a case-insensitive Linux
volume, set it to `LocalPathComparisonPolicy.CaseInsensitive`. Pass that one `WorkspaceOptions` value to the
store and the file system, give its `PathComparison` to the quick-pick file list, and give the reload coordinator
the store, so the comparison cannot drift between them.

### Document scope

Documents are path-identified. A missing path opens as a new empty document that can be saved there by
default; set `CreateIfMissing` to `false` in the open options to report
`WorkspaceDocumentOpenOutcome.NotFound` instead.

Saving as onto the document's own file saves in place and reports `SavedAs` instead of a destination
collision. Renaming a document whose expected source stamp is missing retargets the document identity
without a file-system move, so a not-yet-saved document can be renamed and later saved at the new path.
A change of a document's identity is a rename in this package and in the language-server provider, whose
`RenameDocument` receives it.

The package has no untitled or read-only document model, and documents are always text: a file whose bytes
do not decode in any candidate encoding fails to open or reload with the `InvalidEncoding` failure code. A
host with untitled documents supplies a unique synthetic path per document. `Discard` keeps the document
tracked, so the host removes it with `DeleteAsync` and an expected `FileStamp.Missing`, which the local file
system reports as deleted when no file exists at the path.

Opening an already tracked path returns `AlreadyOpen` without reading again. When a delete of that document
is in flight, the call waits and then re-evaluates: a failed delete reports `AlreadyOpen`, and a completed
delete makes the call a fresh open.

The path is normalized for identity, including trailing directory separators, while the supplied path is
retained as `DisplayPath`.

### Directory operations

A directory rename or recursive delete reserves its source subtree (including the directory path itself)
while it runs.

An open for a path in the subtree waits for the operation and then reads the post-operation state (the
vacated path after a rename). A file rename, save-as, or directory rename into the subtree reports a busy
destination. In-flight loads and writes inside the subtree are awaited before the physical move or delete.

The operation validates every tracked descendant against the stamp the store currently tracks; a
descendant whose file changed stops the operation with `ExternalFileConflict`.

`DirectoryOperationRunner` runs a rename and a delete in one order: it publishes the directory reservation,
waits for the loads and writes already in flight, checks the destination (a rename only), takes every
descendant's disk gate without waiting, validates the stamps, and then moves or deletes the tree. A gate that
is held stops the operation with `OperationInProgress` before anything changes.

### Result shape

Every persist-lifecycle result shares one shape: an identity describing what the caller asked for, the
resulting tracked state (null when the document was not found), the stamp the operation closed on when one
was observed, and a failure present only when the operation failed.

A host can therefore write one routine that logs the failure, refreshes its snapshot, and re-baselines its
stamp, and use the concrete result type only where the operation-specific outcome enum matters. The
outcome enums stay per-operation deliberately: each names its own success and rejection states, and a merged
enum would mix one operation's successes with another's. A shared result core would fit only some of the
operations while lengthening every signature.

Directory results differ by design: they report a `Snapshots` list and the directory path instead of one
identity, and logical mutations report neither a stamp nor a failure because they never touch disk.

### Open, replace, commit, and discard

A host opens a document, replaces its logical content, and commits the change in three calls. Each later call
passes the identity from the previous result, so a call against a stale document is rejected with an outcome
instead of changing the document; see [optimistic concurrency](#optimistic-concurrency).

```csharp
await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

var open = await store.OpenAsync(path, WorkspaceDocumentOpenOptions.Default, cancellationToken);
WorkspaceDocumentSnapshot opened = open.Snapshot!;

WorkspaceDocumentSnapshot edited = store.Replace(new(opened.Identity, newText, opened.FileFormat)).Snapshot!;

var committed = await store.CommitAsync(new(edited.Identity, edited.OnDiskStamp), cancellationToken);
```

The example reads `Snapshot` with `!` for brevity. Check `Outcome` before you use a snapshot, because a
failed open has none.

1. `OpenAsync` returns the tracked snapshot. A missing path opens as a new empty document, and nothing
   reaches disk until the commit.
2. `Replace` installs the new content and format and advances `Version`. It never writes to disk, so the
   snapshot keeps the `OnDiskStamp` that the open observed.
3. `CommitAsync` takes the `OnDiskStamp` that the host last observed as its precondition. A write checks that
   stamp against the file before it replaces the file, and a `Committed` result installs the captured content
   as the persisted baseline.

| Call and outcome | Meaning |
| --- | --- |
| `OpenAsync`: `Opened` | The file was loaded, or the path was missing and a new empty document was created. |
| `OpenAsync`: `AlreadyOpen` | The path is tracked, and its snapshot is returned without a reload. |
| `OpenAsync`: `InvalidPath` | The path is null, blank, relative, or cannot be normalized. |
| `OpenAsync`: `NotFound` | The path is missing, and `CreateIfMissing` is `false`. |
| `OpenAsync`: `IsDirectory` | The path is a directory, which cannot be tracked as a document. |
| `OpenAsync`: `LoadFailed` | The load failed; `Failure` says why, for example `InvalidEncoding`. |
| `OpenAsync`: `Canceled` | The token was canceled, or the store was disposed while the open was in flight. |
| `Replace`: `Changed` | The content or format differs, and `Version` advances by one. |
| `Replace`: `NoChange` | The content and format already match, and `Version` stays the same. |
| `Replace`: `StaleDocument` | The version differs from the request; `Snapshot` is current. |
| `Replace`: `StaleDocumentInstance` | The key differs from the tracked instance; `Snapshot` is current. |
| `Replace`: `DocumentNotFound` | No document is tracked at the request's id. |
| `Replace`: `OperationInProgress` | A delete of the document is active. |
| `CommitAsync`: `Committed` | The captured content was written, or a clean document committed as a no-op. |
| `CommitAsync`: `ExternalFileConflict` | The file differs from the expected stamp; nothing was written. |
| `CommitAsync`: `WriteFailed` | The write failed, for example because another process kept the file locked through every [retry](#sharing-and-lock-violations); `Failure` says why. |
| `CommitAsync`: `ReplacementStateUnknown` | The write may have completed, but its final state is unknown. |
| `CommitAsync`: `Canceled` | The commit was canceled, so the file was not written. |
| `CommitAsync`: `StaleDocument` | The version differs from the request; nothing was written. |
| `CommitAsync`: `StaleDocumentInstance` | The key differs from the tracked instance; nothing was written. |
| `CommitAsync`: `DocumentNotFound` | No document is tracked at the request's id. |
| `CommitAsync`: `OperationInProgress` | A delete or another disk operation holds the document. |

A commit that reports `ExternalFileConflict` also replaces the tracked stamp with `ObservedOnDiskStamp` when
one was observed.

`Discard` reports the same mutation outcomes as `Replace`, and it never writes to disk:

| Call and outcome | Meaning |
| --- | --- |
| `Discard`: `Changed` | The unsaved content and format were restored from the persisted baseline, and `Version` advances by one. |
| `Discard`: `NoChange` | The document has no unsaved changes, and `Version` stays the same. |
| `Discard`: `OperationInProgress` | A delete or another disk operation holds the document. |

The stale-version, stale-instance, and not-found outcomes read as they do for `Replace`.

### Cancellation and disposal

Cancellation is observed before a file-system mutation starts, so a `Canceled` result means the file was not
written, moved, or deleted. A token canceled after a completed replacement reports `ReplacementStateUnknown`
rather than `Canceled`, because a completed write must not be reported as canceled.

Store operations are safe to call concurrently; the store does not capture the caller's context, so its
continuations may resume on the thread pool.

An operation that has already started when the store is disposed reports its `Canceled` outcome. That
includes a reload of a document with unsaved changes. Only a call made after disposal throws
`ObjectDisposedException`.

A reload of a document with unsaved changes holds the document's disk gate while it captures the file's stamp,
so it reports `OperationInProgress` while a commit, rename, save-as, or delete of the same document is in
flight, and it makes those report `OperationInProgress` while it runs. The stamp it compares is therefore never
one of a file that another operation of the store is replacing.

### Read-side slice

`IWorkspaceDocumentReader` exposes the read-only surface, so a consumer that only inspects document state can
depend on a smaller contract than the full store. The store composes the reader with its logical-mutation and
disk-persistence members; they stay together because they share the store's version, disk gate, and
conflict state. The store is the public facade: it checks its arguments and disposal and hands each member to an
internal collaborator, described under [Store collaborators](#store-collaborators).

### Deletion policy

The store's delete members follow the policy on [`IWorkspaceFileSystem.DeleteAsync`](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.Workspace/FileSystem/IWorkspaceFileSystem.cs).
A host that needs recoverable deletion (a trash folder or the Windows Shell recycle bin) supplies a
`WorkspaceFileSystemDecorator` instead. A file delete on a directory reports `DeleteFailed` with the
`IsDirectory` failure code, and a directory delete on a file reports `DeleteFailed` with `NotDirectory`.

## Store collaborators

The internal types of `WorkspaceDocumentStore` sit in the `Documents/Store` folder, which keeps the
`Nickelony.IDEKit.Workspace.Documents` namespace. Each collaborator has one job:

- **`DocumentOpener`** loads a file as a tracked document and joins an open of the same path in flight.
- **`DocumentPersister`** commits a document's content, reloads it from disk and resolves an external conflict.
- **`DocumentRelocator`** renames a document, saves it to another path and deletes it.
- **`DirectoryOperationRunner`** renames or deletes a directory with every tracked document under it.
- **`LogicalContentReplacer`** replaces or discards the logical content without touching the disk.
- **`DocumentSnapshotReader`** answers the read-only questions about the tracked documents.
- **`StoreTeardown`** runs the ordered shutdown behind `DisposeAsync`.

`AdmittedOperationRunner` is the skeleton that the persister and the relocator share. It admits the operation
through the `OperationGate`, runs the operation's step under a token linked to the store lifetime, maps a
cancellation or a fault through the operation's own result, releases the operation's reservations, and then
completes the admission. A request the gate refuses never reaches the step.

## Concurrency and locking

One lock, the state lock, guards the tracked documents, the reservations, and the active operations, and it
serializes document mutations and snapshots. The store passes the lock to the internal collaborators that hold
this state: `WorkspaceDocumentTracker` holds the tracked documents with the reservations, because whether a
path is free reads both, and `StopGate` of `Core.Threading` holds the active operations and the disposal task.
They take no lock of their own. A debug build asserts that the caller holds the store's lock in the tracker,
and `StopGate` throws `InvalidOperationException` when it does not.

`OperationGate` admits an operation in the same critical section that checks for disposal, so disposal
waits for every operation it can observe and no later operation starts. A `DisposeAsync` call from inside such
an operation, a file-system callback of it for example, throws `InvalidOperationException`, because the
disposal would wait for that operation. An open that loads a path is admitted as an operation too, so the same
holds for a call from inside its read.

`StampedFileWorker` does the reads of a reload and a conflict resolution, the stamped writes of a commit and a
save-as, the stamp captures that follow a write or a move, and the descendant scan of a directory operation.
It holds no document state, so the store calls it outside the lock, and it captures the stamp of a write only
after the write returned.

An open and the reload reads classify a failed read by one rule, `WorkspaceOperationFailureCodes.ClassifyReadFailure`:
`AccessDenied` for a permission problem, `InvalidEncoding` for content that does not decode, and the
operation's own code otherwise, which is `LoadFailed` for an open and `ReadFailed` for a reload.

`StoreLifetime` owns the cancellation that every operation observes. `DisposeAsync` cancels it after it
releases the state lock, because a callback registered on a linked token can run inline on the canceling thread,
and disposes it once every captured reservation and operation has completed. The disposal cancels before it
disposes also when nothing is in flight, so a reload in flight sees its token canceled. A directory operation
that is not yet admitted is not waited for, so it links with `CreateLinkedCancellationOrNull` and reports
`Canceled` when the disposal already completed.

Each document has a disk gate, a `SemaphoreSlim` with one permit. Commit, rename, save-as, delete, reload, and
conflict resolution hold the gate of their document, and a directory operation holds the gate of every tracked
descendant. The gate is taken without waiting, under the state lock.

A reload of a document with unsaved changes holds it from the stamp capture to the comparison, which runs under the lock, and it reports `Canceled` when the
disposal began meanwhile. `Replace` takes no gate, so only a delete in flight makes it report
`OperationInProgress`, and an edit can still land while a reload captures its stamp: the reload then reports
`StaleDocument`.

Reservations are claims under the same lock. A delete is keyed by document and the others by normalized path:

- **Open.** The first open of a path loads it; a later open waits for that load and re-evaluates.
- **Destination.** A rename, save-as, or directory rename holds its target paths until it completes.
- **Directory.** A directory rename or delete holds its source subtree until it completes.
- **Delete.** A file delete holds its document and a directory delete each tracked descendant, until it
  completes. A replacement and every gated operation on the document report `OperationInProgress`, and an open
  for the path waits.

Only an open and a directory operation wait for a claim. A gated file operation returns `OperationInProgress`
at once. A rename or save-as into a path that a claim covers returns `DestinationBusy` at once, or
`DestinationInUse` when another tracked document holds the path, and neither touches the disk.
[Directory operations](#directory-operations) gives the subtree rules.

Releases run under the state lock, and completions resume waiters asynchronously. A file rename or save-as
completes its destination reservation before it releases its gate. A directory operation completes its
destination reservations, releases its gates in reverse acquisition order, and removes its directory
reservation last. A delete completes its reservation before it releases its gate.

## Custom file systems

`WorkspaceDocumentStore` is the one implementation of `IWorkspaceDocumentStore`. A host that needs other storage
implements `IWorkspaceFileSystem`, which the store calls for every disk operation.

`LocalWorkspaceFileSystem` is the `IWorkspaceFileSystem` for local disk paths; text encoding is provided
separately by the stateless `WorkspaceTextCodec`.

Hosts override individual members with `WorkspaceFileSystemDecorator`, which forwards every member to an
inner implementation. `LocalWorkspaceFileSystem` is the intended inner implementation; a decorator that
changes the forwarding guarantees changes them for the document store too.

Implementations need not run asynchronous work: synchronous I/O in a completed task is fine, and callers must
not assume off-thread completion. An implementation should await with `ConfigureAwait(false)`, because a host
can block its thread on a store call (for example a synchronous open) and a continuation that captured that
thread's context would deadlock.

### The atomic write

`WriteFileAsync` owns the atomic write: it stages the bytes in a temporary file beside the destination,
re-validates against `expectedStamp`, publishes the temporary file, and removes it on every path. It is one
member on purpose. Splitting those steps across members would move their ordering guarantees onto each caller.
The default staging name is `.{guid}.tmp`, which a host watcher ignores; an implementation that differs must
document its pattern. An optional metadata pre-probe rejects a stale destination without staging.

The default implementation preserves a Unix file mode (the executable bit) best-effort on write; ACLs,
extended attributes, and ownership are not preserved because the write publishes a new file, and writes and
moves do not follow symlinks.

- A write whose destination appears after a missing-stamp check, or whose destination is removed between
  the check and the publish, reports `ExternalFileConflict`.
- A directory where a missing file was expected reports `DestinationExists`.
- A write of a not-yet-existing file into a directory that does not exist reports `Failed` with the
  `WriteFailed` failure code, because the library never creates directories; a host creates the directory
  before a commit or a save-as.
- A token canceled before the destination is touched reports `Canceled`.
- A throw after the destination may have been touched reports `ReplacementStateUnknown`.
- A publish that another process keeps locked through every [retry](#sharing-and-lock-violations) reports
  `Failed` with `WriteFailed`, because nothing was published.
- A replace that removed the destination but could not move the temporary file into place is completed by
  moving that file onto the free destination. When that move fails too, the result is `ReplacementStateUnknown`,
  and the temporary file, which then holds the only copy of the new content, is kept and named in the failure.

Nothing sweeps leftovers. A process crash during a write, or a process that holds the temporary file open past
the cleanup's retries, leaves the `.{guid}.tmp` file beside the destination, and a crash or a failed rollback
during a case-only move leaves the file at `<sourcePath>.{guid}.rename`.
A host that needs a clean tree removes them itself.

The write flushes the staged bytes to the device before it publishes them, but it does not fsync the directory
that the publishing replace or move changes. A power loss shortly after a reported write can therefore leave the
previous content at the destination, with the temporary file beside it.

### Stamp cost and consistency

Stamp capture reads and hashes the whole file (SHA-256), so large files and network shares pay that cost
on every commit, rename, save-as, delete, reload, and conflict resolution. A clean no-op commit skips stamp
capture because no write occurs.

A host that needs cheaper stamps keeps one stamp computation across every stamp-producing member, because
the store compares stamps across those sources: overriding a single member mixes stamp vocabularies and
turns cross-source comparisons into spurious external conflicts.

The store compares `FileStamp` values for equality and reads the existence and directory flags; it does not
interpret the hash or timestamp, and it re-captures the stamp after a write when the write result does not
report one. The full-content hash is deliberately not replaced by an mtime-plus-size fast path: it is what
detects a rewrite that keeps the previous last-write time (a coarse timestamp or a network share).

### Case-only moves

Construct the file system from the same `WorkspaceOptions` value as the store. A case-insensitive
policy routes a case-only move through an intermediate path because the destination resolves to the source
file; a case-sensitive policy treats the spellings as distinct paths.

### Sharing and lock violations

On Windows, a scanner or an indexer that opens a file briefly makes a replace, a move or a delete fail with a
sharing or lock violation, which leaves every file under its own name. `LocalWorkspaceFileSystem` runs such an
operation again a bounded number of times after short delays:

- The write's publish, `MoveAsync`, `MoveDirectoryAsync` and `DeleteAsync` check their precondition again
  before each retry, so a path that changed in between reports `ExternalFileConflict` or `DestinationExists`
  instead of being overwritten.
- The steps of a case-only move after the first one, and the removal of the temporary file, retry in place
  without observing the token, because a cancellation there would leave a file under its temporary name.
- `DeleteDirectoryAsync` is not retried, because a recursive delete that fails midway has already removed part
  of the tree.
- An access-denied failure is not retried, because it also means a real permission failure, which a retry
  would only delay.

### Encoding

A document records the `TextEncoding` it was read with in its `TextFileFormat`, and a save writes that encoding.
A `TextEncoding` is a value keyed by code page. The presets, such as `TextEncoding.Utf8`, need no factory;
`TextEncoding.FromCodePage(932)` makes any other, and `FromCodePage(65001)` equals `TextEncoding.Utf8`. UTF-32 and
UTF-7 are rejected.

The stateless `WorkspaceTextCodec` decodes and encodes strictly. An invalid sequence fails a decode, and a
character the encoding cannot represent fails a save, both with the `InvalidEncoding` failure code. A Windows
single-byte code page also refuses the bytes and characters it leaves undefined, which the runtime would otherwise
pass through as a control or private-use character; the remarks of `TextEncoding` state the rule and the set. The
DOS, Mac and multi-byte code pages keep the runtime's own strictness.

A file without a byte-order mark can be in any encoding, so the open options list the candidates:

1. A UTF-8 or UTF-16 byte-order mark decides the encoding whatever the candidates are, and a UTF-32 mark fails
   the decode.
2. Otherwise, the first of `WorkspaceDocumentOpenOptions.NoBomEncodings` that decodes the bytes strictly wins,
   and the document records it. The default list holds UTF-8 alone.
3. When no candidate decodes the bytes, the open or reload fails with `InvalidEncoding`, and nothing is tracked
   or changed.

```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // once, during startup

WorkspaceDocumentOpenOptions options = WorkspaceDocumentOpenOptions.Default with
{
    NoBomEncodings = [TextEncoding.Utf8, TextEncoding.FromCodePage(1250)],
};
```

List only the encodings the host's files use, because every candidate after UTF-8 also claims files that are
really in another code page. A single-byte code page accepts nearly every byte sequence, so it ends the search
and a candidate after it never runs, and a multi-byte code page such as Shift-JIS accepts many sequences that are
Windows-1250 text. With `[Utf8, <code page>]`, a UTF-8 file with one bad byte opens entirely in the code page, and a
save writes it back in that code page.

A recorded format is the format the store reads back with. A commit or a save-as that persists a no-BOM encoding
other than the one the document decoded with makes that encoding the document's only candidate, so an external
rewrite of the file in the same encoding decodes with it.

A save in the encoding the document decoded with leaves the
candidates as they are, and so does the first write of a document opened from a missing path in the encoding of
`NewFileFormat`, so that encoding should be one of the candidates. A plain reload of a file whose stamp the store already tracks, such
as the host's own save, reports `Unchanged` without decoding.

A host reopens a document with another encoding through the reload request, which decodes the file's bytes
with that encoding instead of the candidates:

```csharp
var request = new WorkspaceDocumentReloadRequest(snapshot.Identity, TextEncoding.FromCodePage(1250));
WorkspaceDocumentReloadResult result = await store.ReloadAsync(request, cancellationToken);
```

- The reload re-reads a file that did not change, and reports `Unchanged` only when the text and format it
  decodes match the document's.
- A byte-order mark still decides the encoding, and a reopen never overrides one: a Windows-1252 file that starts
  with the bytes of a UTF-8 mark cannot be reopened as Windows-1252.
- When the requested encoding decoded the bytes, which the recorded format shows by naming it without a mark, it
  becomes the document's only candidate, so a later reload without one, such as a file watcher's, decodes with it
  first.
- A reload that a mark decided leaves the candidates as they were, and another reload with an encoding replaces
  the choice.
- A document with unsaved changes is never re-read, because adopting the disk would discard them. Its reload with an
  encoding reports `UnsavedChanges` even when the file did not change, so the host saves or discards first. A reload
  without an encoding keeps the usual outcomes of a document with unsaved changes.
- The file system must return the file's bytes. A read that returns decoded content fails the reload with
  `ReadFailed` and the `EncodingNotApplicable` failure code, and changes nothing, so a custom file system that
  decodes inside its read cannot reopen a document with another encoding.

When the chosen encoding cannot decode a later version of the file, the reload fails with `InvalidEncoding` and
the document keeps its text. It does not fall back to the candidates of the open, because those would show the file in
an encoding the user did not choose, and a save would write that text back over the file. The host reopens with
another encoding instead.

A host saves a document in another encoding by replacing its format and committing, because a commit writes the
encoding that the document records:

```csharp
TextFileFormat format = snapshot.FileFormat with { Encoding = TextEncoding.Utf16LittleEndian, HasBom = true };

WorkspaceDocumentSnapshot changed = store.Replace(new(snapshot.Identity, snapshot.Content, format)).Snapshot!;
var committed = await store.CommitAsync(new(changed.Identity, changed.OnDiskStamp), cancellationToken);
```

- The replacement marks the document dirty, because the format is part of the logical state, and `Discard` restores
  the format of the file.
- A byte-order mark exists only for UTF-8 and UTF-16, so `HasBom` is `false` for any other encoding.
- A character the encoding cannot represent fails the commit with `WriteFailed` and the `InvalidEncoding` failure
  code. The file stays as it was and the document keeps the new format as an unsaved change, so the host replaces
  the previous format back or discards.
- A host with views publishes their pending edits first, as the [views guide](WorkspaceViewsGuide.md#changing-the-encoding-of-a-document-with-views)
  shows.

`TextFileFormat.EncodingDisplayName` names a format for a status bar: "UTF-8", "UTF-8 with BOM", "UTF-16 LE" and
"UTF-16 BE" with or without the byte-order mark, "Windows-1250" to "Windows-1258" and "Windows-874" for the
Windows single-byte code pages, and the runtime's name for any other encoding, such as "Japanese (Shift-JIS)".

The library never registers the code-page provider, because that is a process-wide side effect: the host calls
`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` once during startup. `FromCodePage` throws a
`NotSupportedException` that names the provider when the process cannot resolve the encoding. An open, a reload, a conflict resolution or a save that needs an encoding the process has not
resolved reports the `EncodingUnavailable` failure code and changes nothing. `TextEncoding.Windows1252` is such an
encoding before the host registers the provider, and registering recovers, because nothing is cached for the
failure. A direct call of `WorkspaceTextCodec.Decode` or `Encode` throws the `NotSupportedException` instead.

### Line terminators

`TextFileFormat.LineTerminatorStyle` records the style that the codec found on read: `Lf`, `CrLf`, `Cr`, `Mixed` for
a text with more than one, and `None` for a text without a line terminator. The store never recomputes it and the
codec never applies it, so a host that changes the terminators replaces the content and the style together:

```csharp
string converted = LineTerminatorNormalizer.NormalizeToLineFeeds(snapshot.Content);
var format = snapshot.FileFormat with { LineTerminatorStyle = LineTerminatorStyle.Lf };

store.Replace(new(snapshot.Identity, converted, format));
```

- A commit writes the content as it is, so the next save writes the terminators of the content.
- The replacement marks the document dirty, whether the content or only the style differs from the file, and
  `Discard` restores both. A replacement with the content and the style the document already has changes
  nothing.
- A view publishes the style of the text it holds, so a host with an editor converts the editor text and calls
  nothing else; the [views guide](WorkspaceViewsGuide.md#an-editor-view) describes the publication.
- `GetDisplayName` of `LineTerminatorStyle` names a style for a status bar: "LF", "CRLF", "CR" and "Mixed". A text
  without a line terminator reads as the terminator that the editors insert for its first new line, which is
  the platform's.

### A trash decorator

```csharp
sealed class TrashFileSystem(IWorkspaceFileSystem inner) : WorkspaceFileSystemDecorator(inner)
{
    public List<string> TrashedPaths { get; } = [];

    public override async Task<WorkspaceFileDeleteResult> DeleteAsync(
        string path,
        FileStamp expectedStamp,
        CancellationToken cancellationToken = default
    )
    {
        FileStamp actual = await Inner.CaptureStampAsync(path, cancellationToken).ConfigureAwait(false);

        if (actual != expectedStamp)
            return new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.ExternalFileConflict, actual);

        TrashedPaths.Add(path); // a host moves the file to its trash or recycle bin here

        return new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted);
    }
}
```

An override must still validate `expectedStamp` before it touches the file, or the trash move loses the
store's conflict guarantee. `DeleteDirectoryAsync` follows the same deletion policy and forwards to the inner
file system until the decorator overrides it as well.

## Reload coordination

`FileReloadCoordinator` reloads the tracked documents whose files changed, one file at a time. `QueueFiles` is
safe on any thread, and `ProcessQueuedFilesAsync` starts a pass on the caller's thread or joins the running one. A
pass drains every path queued before it ends, including a path queued again while it was being reloaded, so a
host needs no loop of its own. A path the store does not track when its turn comes is dropped.

The coordinator asks `DecideReload` only when `ResolveConflict` is also supplied, so a conflict that nothing resolves is not
prompted. A hook must not await the pass it runs in, because that pass waits for the hook.

A reload result other than `Reloaded`, `Unchanged`, or `Canceled` is reported through `ReportReloadFailure`
with the original result, unless a conflict resolution returns `ResolvedWithDisk` or `ResolvedWithLogical`. A
`Canceled` result stops the pass and leaves the canceled and following entries pending; a callback exception
leaves the failing entry and its followers pending.

A conflict is a reload that reports `ExternalFileConflict`: the document has unsaved changes and its file changed.
`ResolveExternalConflictAsync` settles it with the stamp that the reload observed, so a file that changed again
reports `ExternalFileConflict` instead of being overwritten or adopted:

```csharp
using Nickelony.IDEKit.Workspace.Documents;

async Task<WorkspaceDocumentConflictResolutionResult?> ReloadAndResolveAsync(
    IWorkspaceDocumentStore store,
    WorkspaceDocumentSnapshot snapshot,
    WorkspaceDocumentConflictResolutionMode mode,
    CancellationToken cancellationToken)
{
    WorkspaceDocumentReloadResult reload = await store.ReloadAsync(
        new WorkspaceDocumentReloadRequest(snapshot.Identity),
        cancellationToken);

    if (reload.Outcome != WorkspaceDocumentReloadOutcome.ExternalFileConflict)
        return null;

    return await store.ResolveExternalConflictAsync(
        new WorkspaceDocumentConflictResolutionRequest(reload.Snapshot!.Identity, reload.ObservedOnDiskStamp!.Value, mode),
        cancellationToken);
}
```

- `ResolveWithDisk` ends in `ResolvedWithDisk`: the document adopts the text of the file and drops its
  unsaved changes.
- `ResolveWithLogical` ends in `ResolvedWithLogical`: the text of the document is written over the file.

A hook runs on the thread that started the pass until one hook completes asynchronously, and the hooks after
that run on the thread pool, so a hook that touches the UI marshals itself. A host with a view coordinator
takes its hooks from `WorkspaceViewReloadHooksFactory` of `Nickelony.IDEKit.Workspace.Views`: they have the
views publish their held-back edits, reload through the coordinator, resolve a conflict with the host's answer,
and start the host's decision and failure callbacks on the dispatcher thread.

A host that watches files with `Nickelony.IDEKit.FileWatching` queues the created and the changed files of each
`FileChangeBatch` (see the [file watching guide](FileWatchingGuide.md#reloading-changed-files)). The decision
below stands for host code, with `dispatcher` the dispatcher that the view coordinator was constructed with:

```csharp
var reloads = new FileReloadCoordinator<WorkspaceDocumentConflictResolutionMode?>(
    store,
    WorkspaceViewReloadHooksFactory.Create(
        coordinator, dispatcher, (conflict, token) => AskUserAsync(conflict, token)));

reloads.QueueFiles(changedPaths);
_ = reloads.ProcessQueuedFilesAsync(lifetime);
```

A hook that throws faults the task of its pass, so the discarded task above loses the exception. A host that wants to
report it awaits the task inside a method that catches, as `SampleFileReloader` does.

[`SampleFileReloader`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleFileReloader.cs) runs the passes for a file watcher, with the
hooks [`SampleDocuments`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleDocuments.cs) decorates to move the change-marker baseline.

## The document change-set applier

`DocumentReplacementApplier` applies whole-content replacements to existing tracked documents through a
replacement delegate, and it awaits each replacement before it attempts the next target. It is narrower than the
Language Server Protocol's `WorkspaceEdit`: it does not create, rename, or delete resources or compute edits from
scratch.

```csharp
var applier = new DocumentReplacementApplier(
    (request, _) => Task.FromResult(DocumentReplacementAttemptResult.Reached(store.Replace(request))),
    store.Options);
DocumentReplacementResult result = await applier.ApplyAsync(targets);
```

A delegate that refuses before it reaches the document returns `DocumentReplacementAttemptResult.Refused` with a
failure whose code its own package owns, and the target is not applied with that failure. A host whose documents
have views applies a language-server edit through the
[workspace views](WorkspaceViewsGuide.md#language-server-edits) package instead, whose applier replaces through
the view coordinator so the views follow each replacement.

A prepared target names its document once, through the store's `Identity`, so a result reports each document
by the id the store gave it, and the target carries the content and format before and after the
transformation. A target whose content and format
both match is a no-op that never reaches the store, and confirmed changes record both formats.

### Version chaining and rejection

The store advances the document version on each accepted replacement, so every later request for the same
document uses the version confirmed by the earlier result instead of its own stale preflight version. A
replacement that confirms a mutation without returning a snapshot falls back to the version the request was
built with. A rejection carries a current-state snapshot of its own and must not be adopted.

Unless `DocumentReplacementApplierOptions.ContinueOnFailure` is `true`, the applier stops at the first target
that is not applied (a deterministic rejection or an unknown outcome) and marks every remaining target
`NotApplied`, no-op targets included.

With `ContinueOnFailure`, the applier continues past that target, but later targets for the same
document are not attempted, because they were prepared against a state the applier has not confirmed. A
no-op target of that document is still applied, because it never reaches the store.

A store outcome that proves the replacement did not touch the document (a deterministic rejection, returned
before any mutation) is reported as not applied; any other unrecognized outcome leaves mutation undetermined
and is reported as `Unknown`. A target failure carries a code from `DocumentReplacementFailureCodes`; the codes
of the document authority stay in `WorkspaceOperationFailureCodes`.

### Cancellation and validation

The applier passes its token to each replacement and checks it before each target, so a cancellation between
two targets ends the application with a canceled or partial result. A delegate that throws an
`OperationCanceledException` of that token after it was canceled reports that it did not reach the document: the
target is not applied, and the application ends the same way. An `OperationCanceledException` of another token
propagates to the caller rather than becoming a per-target outcome, so the caller can stop the session.

Targets are validated before any application: a null entry would otherwise surface as a
`NullReferenceException` while the per-target results are built, and an unencodable format would surface as
an unknown per-target outcome.

Use the `DocumentReplacementResult` factories so the outcome, failure, and change-set members cannot
contradict each other. Changed and unknown document ids are de-duplicated with the path comparison of the
`WorkspaceOptions` supplied to the factory (the default follows the operating system; supply the options that
the store uses so the comparison cannot drift), preserving first-occurrence order.

A producer that applies changes without format knowledge leaves both format values null.

## Optional architecture

No editor binding package references the document store, so an editor-only host does not take this package.
A host that also wants an editor view kept in sync with the store adds `Nickelony.IDEKit.Workspace.Views` and
derives the view from its `WorkspaceDocumentTextViewBase`, which the
[workspace views guide](WorkspaceViewsGuide.md#an-editor-view) covers. The
[root README](https://github.com/Nickelony/LanguageServer/blob/main/README.md#package-notes-and-non-goals)
states the package boundary.
