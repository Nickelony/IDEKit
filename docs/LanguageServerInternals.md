# LanguageServer internals

This guide holds the design rationale for the framework-internal machinery of the `Nickelony.LanguageServer.*`
packages. The types it describes are `internal`: they are composition details of `Nickelony.LanguageServer.Client`
and `Nickelony.LanguageServer.Provider`, not host API. The rationale lives here so the code keeps a short summary
and a link instead of an essay, and a host author reads the model in one place.

Host-facing contracts are documented in the package `README.md` files, `docs/ConsumerIntegration.md`, and
`docs/ProviderAuthoring.md`. This guide is only about what happens behind those contracts.

## Client transport machinery

### `TransportHost`

Owns one active transport session at a time: it creates the session (`StdioLanguageServerTransport` by default),
the JSON-RPC instance, and the callback target; it coordinates startup, restart, and teardown through the capability
store; and it reports an unexpected process exit or JSON-RPC disconnect. A session that fails to activate is disposed
in the background, so a slow teardown cannot block the next startup. Session teardown sends a graceful
`shutdown`/`exit` sequence bounded by the configured shutdown request timeout, waits a short grace period for a
voluntary exit, then forces termination when the server did not exit.

### `CapabilityStore`

Publishes the immutable capability snapshot the public surface reads. The snapshot carries a transport generation;
every capability read is fenced on the generation so a stale session cannot republish readiness. `IsReady`,
`TransportGeneration`, and the `Supports*` flags all read a single snapshot reference, so a reader never observes a
half-updated capability set. Marking a generation unhealthy republishes the default snapshot for that generation and
records an invalidation marker, which makes a second mark a no-op and stops a late startup completion from
republishing the generation as ready.

### `StartupState`

Serializes the startup decision: lazy start, in-flight start, a failed start with a recorded failure, and the ready
state. The startup-succeeded flag is invalidated before the state transition runs, so the availability conjunction
that reads it closes the transient window between "the transport is gone" and "the provider state is unavailable".

### `DiagnosticsRouter`

Routes diagnostics payloads and refresh requests (semantic tokens and pull diagnostics) from transport callbacks to
the client's subscribers through serialized background pumps. Diagnostics are stored as the latest payload per
document identity: the normalized local path for a file URI, and the raw URI text for any other document. A bounded
single-slot channel acts as a wake signal, so bursty notifications collapse to one wake-up instead of an unbounded
backlog. Both pumps are observed background loops; an unexpected termination marks the transport unhealthy rather
than recreating locally, because only a fresh session can guarantee that no queued delivery was lost.

### `ProtocolForwarder`

Owns request/notification dispatch to the active session, the host settings snapshot, and the request-timeout
policy. The settings factory runs synchronously on the JSON-RPC read loop under the snapshot lock, so the host
factory must be cheap, non-blocking, and must not re-enter the client. Abandoned tasks from timed-out waits are
routed through `ObserveAbandonedTask`, so a late fault is logged instead of surfacing as an unobserved exception.

### `WindowsJobObject` / `IChildProcessLifetimeGuard`

The Windows child-process guard places the server process in a job object with kill-on-close, so a host crash does
not strand the server. The guard is injectable (`LanguageServerClientOptions.ChildProcessLifetimeGuardFactory`);
non-Windows platforms are a documented no-op.

## Provider machinery

### `DocumentOperationScheduler`

Serializes per-document operations, coalesces pending latest-only updates, and runs exclusive operations that
temporarily take ownership of one or two document paths. It owns one ordered operation chain per normalized
document path plus one global chain. A per-document operation joins its path's chain, a global operation joins the
global chain, an exclusive operation (a rename) joins the global chain and its affected paths' chains, and a
latest-only update appends a coalescable node that is skipped when it was superseded before it started. Enqueuing
captures the tails of the chains the node joins, waits for them, and installs the node as their new tail, so later
work on a chain never overtakes it.

Queued delegates must not call back into the scheduler while they execute: every enqueue and wait method rejects
flow-local re-entrancy with an `InvalidOperationException` instead of risking a deadlock. Provider-owned work that
must be detached from the operation that started it runs through `RunDetachedFromActiveContext`, so it does not
inherit that operation's re-entrancy scope (this is how the semantic-token refresh and diagnostics pull run without
inheriting their document slot).

### `RequestDispatcher`

Applies the timeout and restart policy to provider requests: a request that exceeds the timeout is retried up to a
threshold before the transport is marked unhealthy, and a request that fails over to the transport-start path
tolerates a restart. The provider base's protected `SendRequestAsync` seam routes through it; the semantic-token and
pull-diagnostics requests reach it directly, sharing the same policy. A server-side rejection is deterministic and is
not retried, and a response payload that cannot be deserialized as the expected type is logged and reported as the
fallback value instead of leaking the serializer fault. Timeout bookkeeping is per transport generation and resets
when a request settles on its generation or a new generation finishes starting.

### `TrackedDocumentStore` / `ServerPayloadDocumentStore`

Track documents as content plus version, and fence cached payloads (diagnostics, semantic tokens) against the
version they were parsed for. `TrackedDocumentStore` is the general reference-tracking store; the payload store keeps
the standard diagnostics and semantic-token caches so a provider with ordinary needs declares no store of its own.
`DocumentVersionPolicy` and `VersionFencedPayloadCache` are the version-acceptance rules and the version-fenced
cache behind them. Every protected member of `TrackedDocumentStore` runs under the store's lock and must not block on
work that needs another thread to take it, and its rename hook runs after the renamed entry became visible at its new
path, so a throwing override cannot leave a record unreachable.

### Workspace watching

`WorkspaceFileWatcher` watches one root for the configured patterns and dispatches coalesced change batches.
`WorkspaceWatchScope` owns one watcher per configured root and contains a watcher failure to its own root.
`WorkspaceChangeCoordinator` composes the roots, the debounce buffer, and the change forwarder, and reconciles the
tracked snapshot after an outage. `WorkspaceChangeAccumulator` is the debounce buffer and the forwarder's
accumulator; it coalesces repeated updates per path while preserving delete/create replacement semantics. A failed
watcher dispatch is retried with bounded exponential backoff; when the attempt limit is reached the pending changes
are dropped, the watchers stop, and the owner is notified once so it can create a replacement. The dispatch callback
has no bounded timeout and must observe the lifetime token. An empty watch-specification list disables workspace
watching entirely.

### `WorkspaceSnapshotTracker`

Tracks the last known workspace file set so a recovery can report a delta instead of a full rescan. A capture claims
an ordering ticket before the walk, and an apply that claimed a later slot wins, so a recovery capture cannot
resurrect a stale entry over a concurrent apply's commit. A capture against a briefly-missing root keeps the previous
snapshot instead of installing an empty one, which would make the next delta report every file as created.

## Shared source

`shared/Protocol/LspMethodNames.cs` and `shared/Protocol/JsonElementReadHelpers.cs` are compiled into every package
that needs them through a `<Compile Include>` link. The `Nickelony.LanguageServer.Protocol` namespace is shared by
those linked copies so the constants and helpers resolve the same way in each assembly. A package that consumes the
provider framework's internals through `InternalsVisibleTo` must not link the same shared file, because two visible
copies of one internal type make every use ambiguous (CS0436).
