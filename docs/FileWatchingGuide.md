# File watching guide

`Nickelony.IDEKit.FileWatching` watches the files of a workspace root, coalesces bursts of changes into batches,
and reconciles the changes missed while a watcher was unavailable. This guide owns the debounce and retry
policy, the supervisor and its recovery, the snapshot tracker, and the seams that substitute the engine.

The package references only `Nickelony.IDEKit.Core`, for the local-path comparison, so a host adopts file
watching without an editor or UI-framework dependency. `Nickelony.LanguageServer.Provider` builds its workspace
watching on it, and `Nickelony.IDEKit.Workspace.QuickPick` builds the file list of its quick open on it. The reload coordinator of
`Workspace` takes paths and no batch, so a host connects a batch to it itself, as
[Reloading changed files](#reloading-changed-files) shows.

## Example

```csharp
using Nickelony.IDEKit.FileWatching;

FileWatchSpecification[] specifications = [new("*.cs", IncludeSubdirectories: true)];

using var watcher = new FileWatcher(
    workspaceRootDirectoryPath,
    dispatchAsync: (batch, cancellationToken) =>
    {
        foreach (FileChange change in batch.Entries)
            Console.WriteLine($"{change.Kind}: {change.Path}");

        return Task.CompletedTask;
    },
    specifications);

FileWatcherStartResult result = watcher.Start();

if (!result.Succeeded)
    Console.WriteLine($"The watcher did not start: {result.Outcome}.");
```

A bare `FileWatcher` stops for good after an engine error unless the host passes `onWatcherFailed` and replaces it.
A host that watches for the life of a window uses a [supervisor](#the-supervisor) instead.

## Specifications

A `FileWatchSpecification` is a file-name filter and a flag that recurses into subdirectories. The filter holds
no directory separator and is neither rooted nor `.` or `..`; the watcher and the tracker validate it and throw
`ArgumentException` otherwise. Matching uses the runtime's simple wildcard grammar, where `*` matches any run of
characters and `?` matches one, and the casing rule follows the `LocalPathComparisonPolicy` that the host passes.
An empty specification list is rejected, so a host that wants no watching creates no watcher.

## The watcher

`FileWatcher` creates one `FileSystemWatcher` per specification and queues each event as a `FileChange` with
a `FileChangeKind`. A rename reports a deleted change for the old path and a created change for the new path, each only
when that endpoint still matches the specification.

- Changes within the debounce delay of each other form one batch. A sustained stream still dispatches at least
  once per maximum dispatch delay. Both values are fixed in `FileWatcher`.
- One dispatch runs at a time, and the same path coalesces to one change: a delete followed by a create keeps
  both entries in order, so a replacement is never reported as nothing.
- A failed dispatch retries with bounded exponential backoff that starts at the debounce delay. When the
  attempt limit is reached the batch is dropped, the watchers stop and the owner is told once through the failure
  callback, so it can replace the watcher.
- The dispatch callback has no timeout and observes the cancellation token that disposal cancels, because a timeout
  would abort a callback that is still making progress and drop or duplicate its batch.
- `Start` returns a `FileWatcherStartResult`. A missing root reports `WorkspaceRootMissing` and leaves the
  instance startable once the root exists. A failed start disposes the instance, so a retry uses a replacement.
- Disposal is idempotent, may start from any callback, and does not wait for an in-flight dispatch.

## The supervisor

A `FileWatchSupervisor` owns the watcher and the tracked snapshot of one workspace root, and a failure of that
watcher stays inside the root.

1. `EnsureWatcherStartedAsync` starts the watcher when the owner enables watching, and captures the baseline
   snapshot outside the supervisor lock, so disposal and failure handling stay responsive.
2. `HandleWatcherFailure` replaces a failed watcher: it clones the tracked snapshot, starts a replacement, and then
   captures a fresh snapshot on a background thread and dispatches the delta between the two. A failure callback
   for a watcher that was already replaced is stale and ignored.
3. An unrecoverable failure reaches the owner once through `FileWatchSupervisorHooks.ReportFailure` as a
   `FileWatchFailure` with the code `StartFailed` or `RecoveryFailed`, until a watcher starts again. A root that
   does not exist reports nothing, because nothing can be watched there.

The hooks carry everything the owner decides: the enabled and disposed probes, the failure report, the dispatch of
coalesced batches (which names the delivering supervisor, so a nested root's own watcher stays the single forwarder
for its subtree) and the observation of the supervisor's background tasks. The supervisor never reads the owner's
state itself, so a host decides when watching is wanted and when the owner is gone.

The supervisor runs `IsEnabled`, `IsDisposed` and the watcher factory under its own lock, so they must return
without blocking and never call back into the supervisor. `ReportFailure` and `ObserveBackgroundTask` can run inside
the watcher's failure callback, which holds the watcher's notification lock, so they must return promptly and leave long
work to a background task. `DispatchAsync` runs on a thread of the watcher or the thread pool, never under the
supervisor's lock.

The owner also keeps the recovery baseline current:

- **`ApplyTrackedChanges`.** The owner passes each forwarded batch to it. Without the call, a recovery replays every
  change since the start, because the tracked snapshot never moves.
- **`RescanAsync`.** The owner calls it after it dropped buffered changes of the root, and the supervisor dispatches
  what the tracked snapshot lacks.
- **`IsWatcherActive`.** An owner with nested roots reads it to forward a subtree through the outer watcher while the
  root's own watcher is inactive.

[Reloading changed files](#reloading-changed-files) wires a supervisor in one example.

## The snapshot tracker

`FileSnapshotTracker` tracks the last known file set of a root so that a recovery reports a delta instead of a
full rescan. A capture claims an ordering ticket before the walk, and an apply that claimed a later slot wins, so
a recovery capture cannot resurrect a stale entry over a concurrent apply's commit. A capture against a
briefly-missing root keeps the previous snapshot instead of installing an empty one, which would make the next
delta report every file as created.

`FileSnapshotWalker` reads the files from the file system, and `FileSnapshotDiffer` turns two snapshots into the
changes that reconcile them. The tracker exposes the delta through `IFileSnapshotTracker`, so a substituted tracker
decides which vanished paths count as deleted and the supervisor reads no file itself.

A walk follows the watch specifications and applies these rules:

- It includes hidden and system files, because the file-system watcher observes them.
- It excludes reparse points (symbolic links and junctions), because neither the capture nor the watcher follows
  them consistently.
- It enumerates a wildcard filter with the runtime's simple matching. A literal filter that recurses enumerates
  every file and keeps the names that match, and a literal filter that does not recurse addresses one root-level
  file.
- A file-system fault such as a directory that vanishes mid-walk is logged at debug level and does not fail the
  capture.
- An entry records the last write time, the length, and a content fingerprint.
- A file larger than twice the sample length is fingerprinted from a bounded head and tail sample, so the cost
  stays constant, and a same-length rewrite of only its middle that keeps the same timestamp goes unnoticed.
- Workspace's `FileStamp` hashes the whole file instead of a sample, because the store compares stamps before it
  overwrites a file and a missed change would lose data, whereas a missed rewrite here costs one notification and
  no data.

## Substituting the engine

`IFileWatcher` and `IFileSnapshotTracker` are the two seams. A host that observes a different file view than the real
file system, such as a virtual workspace, returns its own implementations from a `FileWatcherFactory` and a
`FileSnapshotTrackerFactory`. The two must agree on the file view: a tracker that captures from the real file system
produces a delta that does not match the changes of a substituted watcher.

## Reloading changed files

A host that reloads documents queues the created and the changed files of a `FileChangeBatch` on a
`FileReloadCoordinator<TDecision>` of `Nickelony.IDEKit.Workspace`, and skips the entries of kind `Deleted`. The dispatch
queues the paths and starts a pass without awaiting it: a conflict prompt would stall the watcher, and an exception
from the pass would count as a failed dispatch, which the watcher retries and then drops with its batch.

A host that keeps reloading for the life of a window watches through a `FileWatchSupervisor`, so the reloads
survive an engine error that would stop a [bare watcher](#the-watcher) for good. The supervisor
replaces the failed watcher and dispatches the changes it missed, and `ReportFailure` tells the host when it
cannot. The dispatch hands the batch to `ApplyTrackedChanges`, so a later recovery replays only what the reload
did not see:

```csharp
var supervisor = new FileWatchSupervisor(
    new FileWatchSupervisorSpecification(
        LocalPath.Normalize(folder),
        "Editor",
        specifications,
        (root, dispatch, onFailed) => new FileWatcher(root, dispatch, specifications, onFailed),
        (root, watched) => new FileSnapshotTracker(root, watched)),
    new FileWatchSupervisorHooks
    {
        IsEnabled = () => true,
        IsDisposed = () => disposed,
        ReportFailure = failure => ShowStatus($"{failure.Code}: changes on disk are not reloaded."),
        DispatchAsync = (owner, batch, _) =>
        {
            reloads.QueueFiles(batch.Entries.Where(change => change.Kind != FileChangeKind.Deleted).Select(change => change.Path));
            _ = reloads.ProcessQueuedFilesAsync(lifetime);
            owner.ApplyTrackedChanges(batch.Entries);

            return Task.CompletedTask;
        },
        ObserveBackgroundTask = (task, description) => _ = LogFaultAsync(task, description),
    });

await supervisor.EnsureWatcherStartedAsync();
```

## Limitations

- The engine watches files by name pattern, so it reports no directory events.
- Patterns are file-name filters under one root. A host that watches several roots creates one supervisor per root.
- The snapshot fingerprint samples large files, so a same-length rewrite of the middle of one with an unchanged
  timestamp is not detected during recovery.
