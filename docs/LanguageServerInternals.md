# LanguageServer internals

The framework-internal machinery of `Nickelony.LanguageServer.Client` and `Nickelony.LanguageServer.Provider`
manages connection sessions, provider state, requests, documents, and workspace watching behind the public
contracts. This guide gives the design rationale for that machinery. The types it describes are `internal`
composition details, not host API, and the [provider guide](ProviderGuide.md) lists the public surface.

Host-facing contracts are documented in the package `README.md` files, the
[consumer integration guide](ConsumerIntegration.md), and the [provider authoring guide](ProviderAuthoring.md).
This guide is only about what happens behind those contracts.

## Client connection machinery

### `ClientComposition`, `StartupCoordinator` and `HandshakeRunner`

`ClientComposition` builds the collaborators of one client from the values that the `LanguageServerClient` constructor
validated, and the client reaches them through it. A collaborator test builds the same composition around its own
owner callbacks, so the wiring exists once. The collaborators call back into the client through delegates that read the
client when they run, not while the composition is built.

`StartupCoordinator` sequences one startup attempt. `EnsureStartedAsync` returns `true` at once for a ready client and
starts no process. Otherwise it takes the single-permit start lock, and a caller that waits for the lock checks
readiness again. When an earlier start succeeded, the waiting caller reuses that session. When the earlier start
failed, the waiting caller starts its own attempt. An attempt that fails, is canceled or times out disposes the session
it created, and the failure of the most recent attempt is `LastStartupException`. A caller that is canceled while it
waits for the lock has no attempt, so it throws `OperationCanceledException` and leaves the active session and
`LastStartupException` alone, and disposal ends such a wait with `false`.

`HandshakeRunner` completes the handshake of an activated session: the initialize request, the capability
capture, the `initialized` notification, the host's session-start notifications, the settings push and the
readiness publication. Each publication is fenced on the connection generation, so a session that was
invalidated meanwhile is not reported as ready. `SessionStartNotificationBuilder` runs the host's factory and
serializes the payloads before anything is sent, so a factory or payload fault is logged and the start goes on
without those notifications.

### `ConnectionSessionFactory`

`ConnectionSessionFactory` assembles a connection session around the connection that the client's transport returns:

1. `Create` makes the session with the next connection generation.
2. `Configure` adds the message handler, the RPC target and the JSON-RPC instance,
   and subscribes to the exit of the server process.
3. `Start` starts the JSON-RPC listener and the standard-error loop.

`StartupCoordinator` runs these steps itself, because it checks for disposal after creating the session
and for an early process exit after activating it.
The factory reports a process exit or a disconnect only by calling the handler it receives for that event.

### `ConnectionSessionSupervisor` and `FailedSessionDispatcher`

`ConnectionSessionSupervisor` handles the failure of the active connection session.
It reports an unexpected process exit or JSON-RPC disconnect and detaches the affected session.
`FailedSessionDispatcher` then disposes it in the background after every earlier disposal,
so a slow teardown cannot block the connection thread that reported the failure,
and two failures never tear down their resources at the same time.
A session that fails during startup is disposed before `EnsureStartedAsync` returns.

### `SessionTeardown`

`SessionTeardown` disposes one connection session. For a session whose channel the client can observe (a
running process, or a connection that has no process) it sends the `shutdown` request and then the `exit`
notification, each bounded by the configured shutdown request timeout. It waits a short grace period for a
voluntary exit and forces termination only when the server did not exit. A session disposed before its
JSON-RPC instance was attached has no graceful channel, so its live process is terminated directly.

A sent message has only reached the outbound pipe of the client's message handler, which an
`OutboundStreamForwarder` copies to the connection's write stream. The handler and the forwarder leave both
streams open for the connection to release, and the release closes them, so after disposing the JSON-RPC
instance and the handler, teardown waits for that copy to finish before it releases the connection.

The exit dispatch and this wait share one shutdown request timeout, which starts when the exit notification is
dispatched, so the graceful sequence costs at most two timeouts plus the exit grace period. A write stream
that stops draining loses what the copy has not written yet.

A failure in one stage is logged and never skips the release of the connection or the wait for the background
loops, which the dispose timeout bounds. A second disposal of the same session sends no further messages and
disposes the connection again, which `ILanguageServerConnection` requires `DisposeAsync` to tolerate.

### `ClientLifetime` and `ClientTeardown`

`ClientLifetime` owns the disposed flag, the cancellation token, the disposal budget and the completion of one client's
disposal. The first `Dispose` or `DisposeAsync` call claims disposal, sets the flag and runs the teardown. A later
caller receives the completion of that teardown instead of returning while it continues, and the completion is set
even when the teardown throws. The collaborators read the flag and the token through the lifetime, so one value serves
the facade, the sender, the supervisor, the standard-error reader and the background loops.

`ClientTeardown` runs the ordered shutdown: the signal channels and subscribers complete, the lifetime is
canceled, the active session and the queued cleanup of failed sessions are disposed, the server-message loops
finish, and the start lock is disposed last, while it is held. Every stage spends from the one disposal budget
(`LanguageServerClientOptions.DisposeWaitTimeout`). A stage that does not finish within it, or that finds the budget
already used up, is reported and observed, so a later fault is logged instead of surfacing as an unobserved task
exception. The loop records and the cancellation source are released even when a stage throws.

### Standard error

`StandardErrorReader` reads the server's standard error line by line for a session that has a process.
Each non-blank line goes to the debug log as `[LS stderr]`, and the session keeps the last five lines. A kept
line is trimmed and cut to 200 characters plus an ellipsis. A startup, initialization, shutdown, or unexpected
exit or disconnect failure logs the kept lines in one warning, joined with ` | `.

No public member returns these lines. `ConnectionSession` is internal, so a host reads them only in its logs.

### `ConnectionState`

`ConnectionState` publishes the immutable capability snapshot the public surface reads. The snapshot carries a
connection generation. Publishing a capability update is fenced on that generation, so a stale session cannot
republish readiness. `IsReady`, `ConnectionGeneration`, and `NegotiatedCapabilities` all read a single snapshot
reference, so a reader never observes a half-updated capability set.

`NegotiatedServerCapabilitiesConversion` turns the initialize response into that capability set before
`ConnectionState` publishes it. It rejects a response without a capabilities payload, a position encoding other than
UTF-16, and a server without text synchronization when the client requires it, each with a `NotSupportedException`
that startup reports as an initialization failure.

Marking a generation unhealthy republishes the default snapshot for that generation and records an invalidation
marker, which makes a second mark a no-op and stops a late startup completion from republishing the generation as
ready.

Each new connection session takes the next generation from `NextConnectionGeneration`, an interlocked counter,
so a restart never reuses a generation. The first session is generation 1, and generation 0 means that no
connection exists. `TryMarkConnectionUnhealthy` returns `false` for a generation that does not own the published
snapshot, so a stale value cannot invalidate a newer connection.

### `ServerMessageRouter`

`ServerMessageRouter` routes the diagnostics payloads and refresh requests (semantic tokens and pull
diagnostics) that the server sends to the client's subscribers through a chain of background loops.

- `DiagnosticsCoalescer` keeps the newest payload per connection generation and document identity, which is the
  normalized local path for a file URI and the raw URI text for any other document, and it forwards a payload
  only while its generation still accepts server messages.
- `ServerMessageDispatcher` delivers the semantic-token refresh, then the diagnostic refresh, then the pending
  diagnostics of every document, whose generation it checks again before delivery.
- Repeated refresh requests collapse into one flag per kind, and a newer payload for a document replaces the
  queued one.
- A subscriber set drains each subscriber on its own background work item, so a subscriber sees one callback
  at a time, and a handler that throws is logged without stopping the later subscribers.
- A `DrainWorker` runs each loop and wakes it on a bounded single-slot channel,
  so bursty notifications collapse to one wake-up instead of an ever-growing queue.
- A pass takes its snapshot of the pending payloads through the dictionary's own `ToArray`, which stays correct while
  payloads are queued from other threads. A copy that sizes its array from the count would throw when a payload
  arrives in between, and the exception would end the loop.
- Both are observed background loops; an unexpected termination marks the connection unhealthy rather
  than recreating locally, because only a fresh session can guarantee that no queued delivery was lost.

Disposal completes the server-message dispatcher and then the diagnostics coalescer, which drops what is still
queued, and then completes the subscriber sets, which drops the deliveries that have not started. A handler that
already started runs to completion, and a server message that arrives after completion is dropped.

### `ProtocolSender`

`ProtocolSender` owns request/notification dispatch to the active session.

### `SettingsSnapshotCache`

`SettingsSnapshotCache` holds the host settings as one serialized snapshot that the
`workspace/didChangeConfiguration` push and the server's `workspace/configuration` requests share. The
settings factory runs synchronously on the JSON-RPC read loop under the snapshot lock, so the host factory
must be cheap, non-blocking, and must not re-enter the client. `ProtocolSender.PushSettingsAsync` refreshes
the snapshot from the factory before it sends, and contains a factory fault by logging it and skipping the
push, whereas the initialize handshake lets the same fault fail the startup attempt.

### `BackgroundTaskObserver`

`BackgroundTaskObserver` logs the late fault of a task whose caller stopped waiting, at debug level, so the
fault does not surface as an unobserved exception. It also reports a background loop that ends while the
client is active: one warning per loop, and the connection is marked unhealthy. It records the reported
loops, so disposal logs no second warning for them.

### Handshake payloads

`WorkspaceRoots` normalizes the host's root directories once, so the host providers, the `rootUri`, the
workspace folders, and the log text all read the same normalized paths. `InitializeParamsBuilder` builds the
`InitializeParams` record from the roots and the providers, and `ClientCapabilitiesBuilder` rewrites the
host's capabilities into the payload; the [client guide](ClientGuide.md#capability-negotiation) lists the
flags it forces.

### `ProtocolSerialization`

`ProtocolSerialization` builds the serializer options of the client. The message options register the
tolerant converters of every server message with the client's logger, so the session factory builds them once per
client. The payload options are one shared instance with lower-camel member names, for the payloads the client
serializes itself, and the public `CreateProtocolSerializerOptions` returns a copy of it.

### `WindowsJobObject` / `IChildProcessLifetimeGuard`

The Windows child-process guard places the server process in a job object with kill-on-close, so a host crash does
not strand the server. The guard is injectable
(`StdioLanguageServerTransportOptions.ChildProcessLifetimeGuardFactory`); non-Windows platforms are a documented
no-op.

## Provider machinery

### Provider composition

`LanguageServerFeatureProvider` builds one set of collaborators per session. Each reads what it needs
from `ProviderContext` or from a context record of its own, so no collaborator reads a field of the provider. A
public member of the provider guards its arguments, applies the disposal and path checks that belong to it, and
passes the call on.

- Each request-and-response feature has an internal `{Feature}Provider`, such as `HoverProvider`, that builds a
  `DocumentRequestDescriptor` and sends it through `DocumentRequestPipeline`. The pipeline ensures the
  connection, applies the capability gate, synchronizes the document, sends the request, and releases the
  temporary request lease.
- A position-scoped descriptor carries the caret offset, which the pipeline clamps to the document and converts to
  the protocol position, and the response parser receives the `RequestedDocument`: its normalized path and its
  line map, built once for the request.
- `DocumentSynchronizer` mirrors the tracked documents to the server: open, change and close. It
  reports what it did to a document through `DocumentSignals` and calls no feature.
- `DocumentNotificationSender` sends its notifications, `DocumentCloser` completes its closes, and
  `DocumentRequestReleaseRunner` releases the request leases. `DocumentRenameCoordinator` renames a tracked
  document and its server copy to a new path, and `DocumentRestartReplayRunner` reopens the tracked documents
  after a restart.
- A failed send invalidates the tracked server copy through `TrackedSynchronization`, so the next synchronization
  reopens the document.
- `DiagnosticsSource` and `SemanticTokensSource` react to those signals through the
  `IServerPayloadRefresher` contract, and each runs its refreshes through a `PerDocumentRequestCoordinator`. The
  diagnostics source keeps the mapping of a published payload and hands its pull side to a
  `DiagnosticsPullCoordinator`, which also threads each document's last result id.

The composition drives the sources in one order per signal: a synchronized document refreshes its semantic tokens
before it pulls its diagnostics, an invalidated document cancels both in that order, and a renamed document announces
its diagnostics before its tokens.

### Provider folder order

The provider has one flat namespace, so its folders carry the structure and `FolderCycleGuardTests` fails a
cycle between them. Each line below is a layer: a folder names only folders on the lines above it, and a
reference in a doc comment counts as a name. The package root is two layers of its own: `(root)` holds the
types that the folders name, and `(composition)` holds the facade, the provider partials and the types that
compose them. `Watching` stands for the files directly in that folder, and `Watching/Changes` is a folder of its own.

```text
ClientCapabilities, Markup, Ranges
Editing
(root)
Scheduling, Tracking
Session
Lifecycle, Watching/Changes
Mirroring, Watching
Pipeline
Completion, Diagnostics, DocumentSymbols, Formatting, Hover, Navigation, Rename, SemanticTokens, SignatureHelp
CodeActions, WorkspaceSymbols
(composition)
```

### `StartupState`

`StartupState` holds the startup flags, the consecutive start-failure count, and the connection generations that
fence stale connection events. It serializes them with every write to the provider state under one lock.
When a connection is lost, the startup-succeeded flag is cleared before the provider state changes, and the
readiness check requires that flag. The provider therefore reports itself unavailable as soon as the connection is
gone, even while its state still reads ready.

### `ProviderLifecycleCoordinator`

`ProviderLifecycleCoordinator` runs the provider's startup and restart replay. One startup lock serializes
startup and restart, so concurrent callers share one recovery flow. It never starts from inside a document's
scheduler slot: a restart replays the tracked documents through per-document scheduler operations, and a slot
that queued one from inside itself would deadlock its own lane while it waits on the startup lock.

While the client is down, a document slot records a document locally only through the coordinator, which
decides and commits under the startup lock. It skips the commit while a start holds the lock, the client is
ready, the provider is starting or failed, or a replay owns the tracked records.

A start holds the lock from
its replay snapshot to its completion, so it never snapshots between the decision and the commit. Only a start
that finds the client down takes the snapshot, so a commit made while the client is ready would never be
replayed. The slot only tries the lock and never waits on it.

The startup lock is never disposed. The semaphore allocates a kernel handle only when its wait handle is
requested, so skipping the disposal avoids racing a start that still releases it.

A start replays the documents that were tracked before it through `IDocumentRestartReplay` (the replay runner),
then completes on the generation it started, resets the request timeout tracker for that generation and restores
workspace watching through `IWorkspaceWatchRestoration` (the workspace change dispatcher). Both call back into
the coordinator, so the composition attaches them once, after it built them and before it hands the provider out.

A start runs these phases in order, and each member documents the state it fences:

1. `TryLatchHardFailure` refuses a provider whose consecutive start failures reached the threshold, before the
   caller queues on the startup lock and again under it.
2. `TryReuseHealthyConnectionAsync` restores workspace watching on a started, ready connection without taking
   the lock.
3. `ResolveStartupCancellationToken` makes a restart ignore its caller's cancellation, and disposal still ends it.
4. `TryJoinCompletedStartAsync` joins a start that completed while the caller waited for the lock.
5. `PrepareReopen` captures the documents a restart owes, or resumes the remainder of an interrupted replay.
6. `EnsureClientStartedAsync` starts the client if needed and reads the connection generation of its session.
7. `RecaptureReopenList` replaces a resumed remainder with the complete list when the start replaced the session
   the remainder was captured for.
8. `ReplayTrackedDocumentsAsync` reopens the captured documents and keeps the unfinished remainder pending.
9. `SettleStartAsync` completes the start on the generation it read, resets the timeout tracker and restores
   workspace watching, or counts the attempt as failed.

A caller writes provider state only for an attempt it owns, which begins after the join check, where the caller
enters the `Starting` state. A caller canceled earlier writes nothing, so a start that completed while it waited
stays ready. A canceled owner marks the provider unavailable unless its own start had already completed.

A restart reopens every tracked document itself instead of waiting for each document's next request, because
the server pushes diagnostics for open documents that no request touches. The next start resumes an interrupted
replay even when the client stayed ready.

### `StartupFailureReporter`

`StartupFailureReporter` logs every failed start, a persistent failure as an error and a transient one as a
warning. It reports a failure to the host through the startup-failed event only for the first transient failure
and the first persistent failure since the last successful start, because a successful start re-arms both. The
report is claimed before the language package's message hook runs, so a blank message or a fault in the hook
uses up the report of its persistence. A provider with no client reports its persistent failure through the
same claim.

### `ProviderLifetime` and `ProviderEvents`

`ProviderLifetime` claims the provider's one teardown. The first `Dispose` or `DisposeAsync` call wins
`TryBegin`, and a later caller waits for the teardown instead of returning while it continues. `Signal` cancels
the dispose token, and the disposed flag reads that token, so a collaborator that sees one sees the other. The
provider signals first in its teardown, so every collaborator stops admitting work from that point on.

The token source is never disposed: a request that links its timeout token against a disposed source would throw
`ObjectDisposedException` where the documented disposal fallback belongs. `ClientLifetime` claims a client's
disposal the same way, and the two stay separate copies, because the Client package references no Core project;
the remarks of both types give the rest of the reason.

`ProviderEvents` holds the provider's event subscribers, and one lock orders subscribing, raising and closing.
After disposal began, it drops a handler that is added later, and a raise stops at the next handler boundary. A
raise copies the subscribers under the lock and invokes them outside it, so no subscriber code runs while the lock
is held and a handler that throws does not stop the later ones. The provider closes the events right after it
signals the disposal, which releases every subscriber.

### `BackgroundFailurePolicy`

`BackgroundFailurePolicy` decides the log level of a failure in the provider's own background work, so every site
that logs one agrees: detached tasks, the best-effort notifications of the document closes and the per-document
background requests all ask it. Each site classifies cancellation before it asks, because only the caller knows
who owns the token.

| Failure | Level | Why |
| --- | --- | --- |
| `ObjectDisposedException` after the provider started disposing | none | The provider is going away, so nobody reads the outcome. |
| `IOException`, or `ObjectDisposedException` while the provider is live | debug | The connection is lost, and the next call recovers or falls back. |
| Anything else | warning | Nothing expected it. |

### `PerDocumentRequestCoordinator`

`PerDocumentRequestCoordinator` coordinates the background requests of a push-model feature, such as semantic
tokens or pulled diagnostics. A newer request for a path cancels the previous one, and a refresh of every open
document runs its requests concurrently up to a fixed bound, with a fresh bound for each refresh. A failed
request keeps the cached payload. `RequestRunner` turns a connection failure, a server rejection, a timeout and
an unprocessable response into its fallback value, so the coordinator logs only what the runner lets through, at
the level that `BackgroundFailurePolicy` gives it.

A request that ends without a result, because the server rejected it, answered nothing or timed out, runs
again after each wait of `PerDocumentRequestCoordinator.RetryDelays` while the document keeps the version it
was requested for. LuaLS, for one, rejects every pending semantic-token request with `ContentModified` when
any workspace file changes, so without the retry a change to another document would leave the cached tokens
describing older text until the next edit.

### Provider state

The table lists how the provider enters each `LanguageServerFeatureProviderState` value, and which methods
write it. `ProviderLifecycleCoordinator` owns every listed method except
`StartupState.TryCompleteSuccessfulStart` and `DisposeCore`, which belong to `StartupState` and
`LanguageServerFeatureProvider`.

| State | Entered when | Written by |
| --- | --- | --- |
| `Unavailable` | The provider is constructed. A start attempt fails below the failure threshold, or its owner is canceled before the start completed. The connection is lost or marked unhealthy, including by a failed workspace-change send. A document or request call fails with an `IOException` or `ObjectDisposedException` outside disposal. | `RegisterFailedStartupAttempt`, `EnsureConnectionStartedAsync`, `HandleClientConnectionUnavailable`, `MarkConnectionUnavailable` |
| `Starting` | A call holds the startup lock and finds no start that succeeded on the current generation, as on a cold start or a restart. | `EnsureConnectionStartedAsync` |
| `Ready` | A start completes on the current connection generation. | `StartupState.TryCompleteSuccessfulStart` |
| `Failed` | The consecutive start failures reach `HardStartupFailureThreshold`, or a call finds no configured client. The provider stops starting until it is recreated. | `RegisterFailedStartupAttempt`, `ReportMissingClientFailure`, `TryLatchHardFailure` |
| `Disposed` | The first `Dispose` or `DisposeAsync` call begins teardown. Later transitions are ignored. | `DisposeCore` |

### `DocumentOperationScheduler`

`DocumentOperationScheduler` serializes per-document operations, coalesces pending latest-only updates, runs
exclusive operations that temporarily take ownership of one or two document paths, and keeps the order of the calls
that wait for the connection. It queues each operation on the lanes of an `OperationLaneTracker`, which holds one
ordered lane per normalized document path plus one lane that every exclusive operation joins, and it shares its
lock with that tracker, with the `LatestUpdateTracker` and with the `CallOrderTracker`.

- A **per-document** operation joins its path's lane.
- An **exclusive** operation (a rename) joins the shared exclusive lane and its affected paths' lanes, so
  exclusive operations run one at a time.
- A **latest-only** update appends a coalescable node that is skipped when it was superseded before it started.
  `LatestUpdateTracker` keeps the newest and the running update of each path, so a cancellation reaches a
  running update that a newer one superseded.

Enqueuing captures the tails of the lanes the node joins, waits for them, and installs the node as their new tail,
so later work on a lane never overtakes it.

A document call that has to wait for the connection reaches its lane late, and a call the host made after it must
not overtake it. `DocumentSynchronizer` therefore reserves the call's place in the `CallOrderTracker` when the host
makes the call, before the call waits for the connection, and the call joins its lane only after every reservation
made before it on its paths is complete:

- A call completes its reservation on every exit path. A reservation counts as complete for the calls behind it
  only when the reservations before it are complete too, so a call that fails early never lets a later call
  overtake an earlier one.
- An update completes its reservation when it has joined the scheduler, so a later update can still supersede it
  before it starts. An open, a close and a rename complete theirs when they have finished, and an open keeps its
  reservation through the retries that follow a skipped attempt.
- `CancelLatestUpdate` supersedes the updates that reserved a place and still wait for the connection, as it
  cancels the queued and the running ones, so an open, a close or a completed rename drops them.
- The check that skips a superseded update and the update's join of the lane are one critical section of the
  scheduler's lock.
- A reservation blocks only the calls on its own paths, a rename holds both of its paths, and disposal ends the
  wait of a call that waits for its turn.
- A request does not reserve a place: it carries the content it needs and synchronizes it for itself.

Queued delegates must not call back into the scheduler while they execute: every enqueue method asks the
`OperationReentrancyGate` and rejects flow-local re-entrancy with an `InvalidOperationException` instead of
risking a deadlock. The rule stays fail-fast: a delegate that enqueues work for its own path and waits for it
waits on its own lane, and a wait on another path can close a cycle with that path's lane. A delegate inlines
the nested work, and a caller queues follow-up work after the operation completed.

Provider-owned work that must be detached from the operation that started it runs through
`RunDetachedFromActiveContext`, which hides the operation from the gate, so it does not inherit that operation's
re-entrancy scope (this is how the semantic-token refresh and diagnostics pull run without inheriting their
document slot).

### Newest wins and admit until closed

A newer request replaces an older one in several places, and the older one meets a different fate in each:

- `DocumentOperationScheduler` keeps the latest update of a document path in its task queue. The older update
  is not canceled: it stays in the path's lane and is skipped when its turn comes, so the work around it
  keeps its order.
- `KeyedLatestRequestGate` in `Core.Requests` keeps one cancellation source per key, and the provider keys it by
  document path through `PerDocumentRequestCoordinator`. A newer request cancels the older one while it runs.
- `LatestRequestGate` in `Core.Requests` keeps one slot with no key. A newer request cancels the older one, and
  only the newest request may publish. It has no per-path ordering, path exclusivity or coalescing, so it is
  not a substitute for the scheduler.

A task queue and a cancellation differ in what the older request does, so the scheduler stays apart from the
two gates. The two gates share the cancel-the-older-request behavior and differ in whether a key picks the slot.

Two kinds of gate admit work until they close, and they differ in what a close does with work in flight:

- A **close** never waits for work in flight. `KeyedLatestRequestGate` cancels its requests, and
  `ProviderEvents` checks the disposal before each handler of a raise in flight, so a close requested from
  inside a callback completes. The keyed gate closes through the stop of a `StopGate` that admits no operation.
- A **drain** waits for every operation in flight. `StopGate` of `Core.Threading` is the shared drain gate: it
  tracks the admitted operations, so a stop refuses to run from inside one of them.
- `WorkspaceChangeForwarder` builds its gate for a stop that does not wait: its disposal returns at once, also when
  an operation requests it, and releases the synchronization gate when the last operation in flight leaves, which
  it learns from a completion source per operation.

`LatestRequestGate` decides which result may publish and `StopGate` decides whether new work is admitted at all,
so a component that needs both holds one of each: the latest-wins gate supersedes and cancels the request, and the
stop gate refuses a request once its owner stops.

`StopGate` and `DocumentOperationScheduler` each keep a flow-local mark (an `AsyncLocal`) of the operation that
the current call runs inside. Each uses it to refuse, with an `InvalidOperationException`, a call that would wait
on its own flow: a stop requested from inside an operation, unless the owner built the gate for a stop that does not
wait, or an enqueue from inside a queued delegate. The two share the idea and no code, because one guards a drain and
the other an ordered lane.

### `RequestRunner`

`RequestRunner` applies the timeout and restart policy to provider requests. It owns the single retry across a
connection boundary, `RequestAttemptRunner` sends one attempt under its own timeout and classifies how it settled, and
`RequestTimeoutCounter` counts the consecutive timeouts of each connection generation.

- A request that exceeds the timeout returns its fallback, and consecutive timeouts on one generation mark that
  connection unhealthy once the restart threshold is reached.
- A request whose connection is replaced or unavailable is retried once after the connection restarts, and a
  failure of the retry returns the fallback value.
- A request that can change state on the server, such as `workspace/executeCommand`, goes through `SendOnceAsync`
  (the `RequestSendPolicy.Once` policy):
  - no attempt times out, so only the caller's token and the provider's disposal cancel it;
  - a wait never counts toward the restart threshold;
  - a connection boundary ends the request with its fallback instead of a second send, because the server may
    already have acted on the first.
- The document-request pipeline, the completion-item resolve, and the semantic-token and pull-diagnostics
  requests all route through it, sharing the same policy.
- A server-side rejection is deterministic and is not retried.
- A response payload that cannot be deserialized as the expected type is logged and reported as the fallback value
  instead of leaking the serializer fault.
- Timeout bookkeeping is per connection generation and resets when a request settles on its generation or a new
  generation finishes starting. A settlement from a superseded generation is ignored, so a late success cannot
  mask consecutive timeouts on the active one.

The `Provider/Pipeline` folder sends requests to the server under that policy, and `Core.Requests` decides
which of an editor's own requests may publish. The per-document coordinator of the folder supersedes through the
Core keyed gate, and the two share no other code.

### Response mapping

The response parsers, `DiagnosticsParser`, and `WorkspaceEditParser` turn server payloads into shared editor
models with one stance: a malformed piece is dropped or degraded, and never approximated into something the
server did not send.

- A range that only selects or highlights text, such as a hover or an outline range, converts through
  `ProtocolRangeConversion.TryGetOffsetRange`. It rejects an inverted or negative range on its raw coordinates,
  because clamping can collapse an inverted range into a valid-looking zero-length one, and then clamps a stale
  coordinate to the document.
- A completion edit whose range is inverted, negative, or starts or ends on a line outside the document is
  dropped, and the item stays usable through its insert text or label.
- A character past the end of its line is clamped. The shared rule differs here on purpose: an edit commits text,
  so a line clamped to the last line would insert it at the wrong place.
- A malformed additional edit is skipped on its own.
- A diagnostic without a range is skipped. Coordinates past the snapshot are clamped, and an inverted range
  collapses to a zero-length range at its start, which falls back to the word or content anchor there. The
  shared rule differs here on purpose: a stale or malformed diagnostic must still be shown.
- A workspace edit prefers `documentChanges` over `changes`, and falls back to `changes` when the list yields no
  usable edit.
- An unresolvable URI or a resource operation (create, rename, or delete) fails the whole edit closed, because a
  partial rename must not be applied. A text edit that cannot be mapped or has no `newText` is skipped, never
  turned into a deletion.

Provider is the one boundary that sees both a model type and a wire type of the same name, such as the
diagnostic severity enums. A file that names both aliases the wire namespace as `Lsp`
(`using Lsp = Nickelony.LanguageServer.Client.Protocol;`) and qualifies the wire side, as Roslyn's language
server (`LSP.`) and vscode-languageclient (`proto.`) do.

A pair of same-meaning enums maps through one explicit `switch` in a `...Conversion` type and never through a
cast, so a value that only one side defines cannot leak across, and a test lists every member of both enums.
Each range that goes to the server passes through `ProtocolRangeConversion` and each position through
`ProtocolPositionConversion`, and both clamp a negative coordinate to zero. A mapping from a request into a
wire structure follows the home rule of the [provider authoring guide](ProviderAuthoring.md#6-feature-slices).

### `DocumentTracker`

`DocumentTracker` tracks each document as content plus a version, counts the leases that keep a record tracked,
and fences the payloads it caches for each record (diagnostics, semantic tokens) against the version they were
parsed for. `LanguageServerFeatureProvider` creates one per provider, so a language package declares no tracker.

- `TrackedDocumentState` is the record of one document: its path, content, version, leases and payload caches.
- `TrackedDocumentSet` holds the records by normalized path, hands out the access stamps that order them, and evicts
  the idle records oldest first.
- `DocumentSynchronizationCommitter` commits the content and the leases of a synchronization to the records and
  returns the directive that mirrors it to the server.
- `DocumentVersionPolicy` is the version-acceptance rule.
- `VersionFencedCache` is the version-fenced cache behind it.

`DocumentTracker` owns the lock that guards its tracked state, shares it with the set and the committer, which assert it
in a debug build, and never blocks inside it on work that needs another thread to take that lock:

- `SynchronizeNormalized` commits the content and version under the lock, but it computes the incremental
  change range and its line mapping outside it.

A rename runs under the same lock. It replaces the path and URI of a record, and when the content differs it also
replaces the content and clears the payload caches, so no reader sees the new path beside the payloads of the old
text.

### Diagnostics version fence

A diagnostics payload reaches the cache through one path for push and pull delivery, and each stage after the
first rejects a stale payload. Semantic tokens pass the last two stages.

1. A push notification for a document that is not tracked is ignored. A pull report is parsed against the
   snapshot taken when the request was issued, and its version is that snapshot's version.
2. `DiagnosticsParser.TryParse` rejects a payload whose known version differs from the snapshot version it
   parses against. A payload without a version passes.
3. `DocumentTrackerDiagnosticsExtensions.TryStoreDiagnostics` runs under the tracker lock and rejects the payload when
   the tracked version moved on after the snapshot was taken, which closes the window between parse and store.
4. `VersionFencedCache` rejects a payload older than the cached version, and stores an unversioned
   payload only while no version is cached.

A rejected payload raises no `DiagnosticsUpdated`. `InvalidateServerSynchronization` resets the cached versions, so
the next payload is accepted whatever version it reports. An unchanged pull report advances the stored result
id only while the tracked version still equals the snapshot version.

### Request leases, deferred closes, and restart replay

A request takes a request lease while it synchronizes its document, which happens only when the connection is
ready and the capability gate, if any, passes. A `finally` block releases the lease through
`DocumentRequestReleaseRunner`, so a canceled request still releases it, in the document's scheduler slot and
with no cancellation token.

The release comes first, so the record becomes eligible for the close it deferred
and for the idle trim, and both run in that slot. The trim evicts idle records beyond
`LanguageServerFeatureProviderOptions.MaxTrackedIdleDocuments`, and `DocumentCloser` closes the server copy of
each open record it evicts, after the slot and while the client is ready.

`DocumentTracker.Close` reports `DocumentCloseOutcome.BusyWithRequests` when a close releases the
last open lease while a request lease remains. `DocumentCloser` then records the pending close and
sends nothing. The close completes at the first request release that leaves the record idle, and
`textDocument/didClose` is sent only while the client is ready.

An open of the document cancels the pending close. A rename carries it to the new path, and the release
follows the record through a bounded number of renames; past that bound, the close waits for the next request
release on that path.

`PendingCloseTracker` remembers the pending closes. It lives beside the synchronizer and the closer rather
than inside `DocumentTracker`, because the tracker would otherwise depend on scheduling and connection policy,
so the tracker reports only the `BusyWithRequests` outcome of `Close` and the release paths. A claim and the
re-mark that can follow it are two calls, so a caller runs them inside the document's scheduler slot.

`DocumentNotificationSender` turns a synchronization directive into `textDocument/didOpen`, `didChange` or
`didClose`. A change carries the computed range under incremental synchronization, and the whole content under
full synchronization or without a range. A session that negotiated no synchronization mode still gets the open
and the close, and its changes are skipped: the first skipped change of a connection generation logs a warning,
and the later ones log at debug level, so the session does not log one warning per keystroke.

A start that finds the client not ready calls `DocumentTracker.PrepareForRestart`, which marks every
tracked record closed. Only records with an open lease are replayed, in the tracker's enumeration order, which
the code does not sort. Each replay runs in its own scheduler slot, one record at a time, and skips a record
that is untracked, has lost its open lease, or is already open. A record held only by request leases is not
replayed; its next synchronization reopens it.

- A reopen that fails with an `IOException` or `ObjectDisposedException` stops the replay.
- The failed record is marked closed, but it keeps its leases, so a later start can reopen it.
- The failed record and every record after it stay pending, and the start counts as a failed startup attempt.
- A later start resumes the pending records while the client is ready, and otherwise captures the open-lease
  records again.

### Workspace watching

The watching engine (the watcher, the debouncer, the supervisor and the snapshot tracker) is the
`Nickelony.IDEKit.FileWatching` package, which the [file watching guide](FileWatchingGuide.md) describes. The
provider adds the part that talks to the server:

- `WorkspaceChangeDispatcher` creates one `FileWatchSupervisor` per root and the shared `WorkspaceChangeForwarder`
  that forwards their changes to the server, and routes each change to the root with the longest matching
  prefix. An empty watch-specification list disables workspace watching entirely.
- `FileWatchFailureConversion` turns a supervisor's `FileWatchFailure` into the `WorkspaceFileWatchFailure`
  that a host receives, with the provider's wording and failure code.
- `WorkspaceChangeSender` sends a forwarded batch to the server as a `workspace/didChangeWatchedFiles`
  notification, preceded by a settings push through the client when the batch touched a configuration path.
  `ProtocolFileChangeConversion` maps each `FileChangeKind` to the protocol's `FileChangeType`.
- `WorkspaceWatchingFactory` creates the default watcher and snapshot tracker of a root with the provider's
  path comparison, unless a replacement factory was given.

`WorkspaceChangeForwarder` decides what happens to a change set that cannot be delivered right now, for every root:

- A recoverable failure re-buffers the whole batch ahead of the changes buffered meanwhile, so a re-buffered
  delete cannot cancel against a newer create for the same path. Recoverable means an `IOException`, a disposed
  connection while the provider is alive, or a send canceled by something other than the caller's token.
- A startup that throws or reports no usable connection buffers the batch for a later replay, and a throw does not
  escape into the watcher that dispatched it.
- An unexpected failure is logged as a dropped batch, because replaying a partially observed batch could deliver
  it twice.
- A dispatch whose caller cancels re-buffers the batch while the provider is alive, and rethrows. The rule is
  the same whether the cancellation ends the connection start, the wait for the forwarding gate or the send.
- A replay canceled before it drained the buffer returns an empty list, because it took nothing from the buffer.
- A change set that arrives while forwarding is not allowed, which means no client exists or the provider is
  disposed, is dropped instead of buffered.
- Buffered changes are replayed when the connection starts or is found healthy, never by a new dispatch.
- A buffer that passes `WorkspaceChangeForwarder.DefaultBufferedPathLimit` distinct paths during a long outage
  empties into the dispatcher, which flags the root of each path.
- The next replay starts a `RescanAsync` of each flagged root's supervisor, which forwards what the root's tracked
  snapshot lacks, so the collapse loses no change inside a root and the buffer stays bounded.

The forwarder admits operations through a `StopGate` until its disposal begins, and it disposes its
synchronization gate when the last operation in flight leaves, so a late operation never meets a disposed gate. A
callback of an operation may dispose the forwarder: the release then follows the exit of that operation.

## Test seams

A test reaches an internal point of these packages in one of the three ways that
[CONVENTIONS.md](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md#tests) defines, and no type keeps
static mutable state for tests:

- A **collaborator** that the test builds and passes in replaces a dependency.
  - `StdioLanguageServerTransport` takes its child-process lifetime guard through
    `StdioLanguageServerTransportOptions.ChildProcessLifetimeGuardFactory`, and `WindowsJobObject.Instance` serves
    every transport that sets none and nothing disposes it, so a test creates its own job object and disposes it,
    which releases only that job.
  - `LanguageServerFeatureProvider` takes the workspace file watcher factory and the snapshot tracker factory
    through its internal constructor.
- An **observer** marks an interleaving point or a signal and arrives as an internal sealed `{Owner}TestHooks`
  object.
  - The provider exposes `TestHooks` with the startup lock wait, the local commit and the document signals.
  - `FileWatcher` and `FileSnapshotTracker` of the file watching package take theirs through an object initializer.
  - `LanguageServerClient` takes `LanguageServerClientTestHooks` as a constructor argument.
  - Production code calls a hook only through `?.Invoke()` and branches on nothing it returns.
- An **internal read-only state property** exposes a value that a test reads and production code never
  branches on.

A settable factory on a hooks type is not a kind of seam: a test that must replace a dependency passes a
collaborator instead.

## Shared source

`Nickelony.LanguageServer.Client` links `shared/Infrastructure/Paths/LocalPathRules.cs`, which
`Nickelony.IDEKit.Core` links too ([one home for a rule](ArchitectureGuide.md#one-home-for-a-rule)), and no
other language-server package links shared source. A project that sees a package's internals through
`InternalsVisibleTo`, such as its test project, must not link a copy of one of that package's files either.
The project would then see two copies of each linked internal type, and the compiler warns CS0436 at every use and
binds it to the project's own copy, a type distinct from the package's.
