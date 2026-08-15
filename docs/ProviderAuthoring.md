# Provider authoring guide

This guide is for implementing a language provider package on the Nickelony Language Server
family. It is the provider-side counterpart of the host-facing
[consumer integration guide](ConsumerIntegration.md). The language-neutral framework lives in the
`Nickelony.LanguageServer.Provider` package: a provider package derives from
`LanguageServerIntelliSenseProviderBase` and implements the language-specific
hooks. `Nickelony.LanguageServer.Lua` (the LuaLS-backed provider) is the verified reference
implementation of those hooks; every recipe step and trap below matches how that package
implements it. A second pattern sits between the base and a language package when several languages
share one server: `Nickelony.LanguageServer.Roslyn` is a shared core that implements every
language-neutral hook and leaves only the identity abstract, and
`Nickelony.LanguageServer.CSharp` and `Nickelony.LanguageServer.VisualBasic` derive from it - see
[Sharing a core across languages](#sharing-a-core-across-languages).

## Package shape

A provider package sits on three framework packages:

- `Nickelony.LanguageServer.Provider` - the provider framework: the provider base class
  (`LanguageServerIntelliSenseProviderBase`) with the generic semantic-token contract it implements
  (`ILanguageServerSemanticTokensProvider`, defined in `Abstractions`), the tunables
  (`LanguageServerProviderOptions`), request dispatch with timeout and restart policy (reached
  through the provider base's protected `SendRequestAsync` seam), the workspace watch primitive
  (`WorkspaceFileWatcher`), the tracked-document store framework (`TrackedDocumentStore`,
  `TrackedDocumentState`), the protocol-to-editor bridges
  (`ProtocolRangeConversion`, `SemanticTokenConversion`, the kind conversions, and the markup pair),
  the semantic-token decoder (`SemanticTokensDecoder`),
  and the internal file-change forwarding, snapshot tracking, per-document operation scheduling,
  lifecycle, document-synchronization, semantic-token, workspace-watching, and request machinery
  behind the language hooks.
- `Nickelony.LanguageServer.Abstractions` - the editor-facing contracts: the provider interface
  (`ILanguageServerIntelliSenseProvider`), the semantic-token contract
  (`ILanguageServerSemanticTokensProvider`) and its event payload
  (`SemanticTokensUpdatedEventArgs`), the lifecycle state enum
  (`LanguageServerProviderState`), the startup-failure and watcher-failure payloads, and the
  lifecycle rules that provider authors must honor.
- `Nickelony.LanguageServer.Client` - the LSP transport and the protocol primitives: process launch
  and JSON-RPC hosting, the initialize handshake, the capability snapshot, the path helpers
  (`LanguageServerPaths`), and the protocol payload folders.

The framework packages depend on the IDEKit packages for the editor models; `Client` deliberately has
**no** `Abstractions` edge (the Provider package bridges the two) and no IDEKit edge either, so
anything that touches a shared editor model lives in `Provider`. A provider package references the
framework and owns:

- the composition root: one public provider class deriving from
  `LanguageServerIntelliSenseProviderBase` and inheriting the base contract and the standard
  feature implementations (completion, hover, definition, references, rename, formatting,
  signature help, document symbols, and code actions); it overrides a feature only when the
  language needs different behavior,
- the hook implementations: display name, language identifier, configuration-file
  rule, settings payload, startup-failure reports, and the virtual extension points
  (`OnDocumentSynchronizedAsync`, `OnTrackedDocumentInvalidated`, `OnDocumentMoved`, `OnDisposing`), plus
  the constructor arguments the framework needs up front (the tracked-document store and the watch
  specifications). A provider that exposes diagnostics or semantic tokens also implements the matching
  optional hooks (step 4),
- the language-specific contract additions, if any (Lua exposes none: it implements the framework's
  `ILanguageServerIntelliSenseProvider` and `ILanguageServerSemanticTokensProvider` directly),
- the option type (Lua: `LuaLanguageServerOptions`) and language-specific event payloads; a provider
  that shares another package's core reuses that core's options instead of declaring a duplicate
  (C# and Visual Basic reuse `RoslynLanguageServerOptions`),
- the client construction with its handshake payload factories (client capabilities,
  initialization options, settings) and the workspace conventions (watched file patterns,
  configuration-file refresh rule) fed into the hooks,
- the mapping slices: one parser partial per feature; the standard feature request-and-parse lives in
  the framework base, so a provider adds a provider partial only for a language-specific override.

Everything else stays internal to the framework. The Lua package's public surface is exactly two
types: the provider (`LuaLanguageServerIntelliSenseProvider`) and the language option type
(`LuaLanguageServerOptions`); a provider that reuses a core's options exposes a single type
(`CSharpLanguageServerIntelliSenseProvider`, `VisualBasicLanguageServerIntelliSenseProvider`).
The semantic-token event payload (`SemanticTokensUpdatedEventArgs`) and the semantic-token contract
(`ILanguageServerSemanticTokensProvider`) are framework types in
`Nickelony.LanguageServer.Abstractions`.
The framework ships one concrete store, `ServerPayloadDocumentStore`, that caches the diagnostics and
semantic-token payloads a language server sends for a document, together with the access stamps the
synchronization pipeline maintains; `Lua` and `Roslyn` both use it. A provider whose caching needs differ
supplies its own store and state instead.
The tracked-document state
(`ServerPayloadDocumentState`) stays internal, because the provider base and the tracked-document store are
non-generic and never expose the state type: the store's hooks are typed on `TrackedDocumentState`
and the store casts to its own state. Keep a new provider package that small and raise
members to public only when a host needs them.

### Sharing a core across languages

When several languages share one server process, put the shared handshake, launch profile, settings,
and payload caching in a core package that derives from `LanguageServerIntelliSenseProviderBase` and
implements every language-neutral hook, leaving the identity abstract (`LanguageId`,
`ProviderDisplayName`). A language package then derives from the core, passes its own source and
project watch patterns to the core's protected constructor (the core composes them with the shared
build inputs, so those are declared once across the whole family), and overrides only the identity.
Use this shape when one server binary serves several languages - `Nickelony.LanguageServer.Roslyn`
serves C# and Visual Basic, and there is no C#-only or Visual Basic-only server - and the standalone shape above
when the server *is* the language.

Two consequences to plan for:

- The core keeps its test-injection constructor `internal` and grants the language packages
  `InternalsVisibleTo`, rather than exposing it as `protected internal`; a test-only seam should not
  widen the core's public API.
- The core's public surface is the abstract provider plus its options, so a language package that
  reuses those options adds no option type of its own.

## Recipe

### 1. Contract

Derive from `LanguageServerIntelliSenseProviderBase`, passing the language's tracked-document
store to the constructor (the base and the store are non-generic, so the language state type is
never exposed). The base implements `ILanguageServerIntelliSenseProvider` and
`ILanguageServerSemanticTokensProvider`
end to end: the lifecycle surface (`IsAvailable`, `State`), the capability flags
(`SupportsRename`, `SupportsReferences`, `SupportsFormatting`, `SupportsDocumentSymbols`,
`SupportsCodeActions`), the payload events
(`DiagnosticsUpdated`, `SemanticTokensUpdated`, `CapabilitiesChanged`, `StartupFailed`, `WorkspaceWatcherFailed`), the
document lifecycle members (`OpenDocument`, `UpdateDocument`, `CloseDocument`, `MoveDocument`),
the cached reads (`GetDiagnostics`, `GetSemanticTokens`), the semantic-token refresh controller, and
the request scaffolding. The base also carries the standard LSP implementation of the feature
members (completion, hover, definition, references, rename, formatting, signature help, document
symbols, and code actions), so the language class inherits them and overrides only the ones whose
behavior differs; it adds any language contract additions (Lua: none).
The diagnostics and semantic-token hooks are optional: implement them only when the provider exposes
that feature (the base defaults drop published diagnostics and return no semantic tokens). Each request method accepts the current
buffer text plus a `TextPosition` (or a request record for features that carry additional state)
and a `CancellationToken`; the sync surface returns owned snapshots.

No hook runs during base construction: the tracked-document store and the watch specifications are
constructor arguments, so a derived provider can build them from its own initialized state. Every
hook runs lazily on the calling thread after construction completed; `ProviderDisplayName` and
`LanguageId` are read on demand by log and synchronization paths and must not throw.

### 2. Client construction and handshake

Build one `LanguageServerClient` per provider and pass it to the base constructor, which takes
ownership of it (the client is disposed with the provider):

- Pass the normalized workspace roots as a list. The first entry is the primary root
  (`rootUri`); every entry is advertised through `workspace/workspaceFolders` and answered by
  the `workspace/workspaceFolders` callback.
- Pass the server executable path; `null` is a supported configuration that reports a
  persistent `StartupFailed` instead of throwing.
- Configure `LanguageServerClientOptions`: `SettingsProvider` (a `Func<object>` that the client
  caches for `workspace/configuration` answers and refreshes through
  `workspace/didChangeConfiguration`), plus `ClientCapabilitiesProvider` and
  `InitializationOptionsProvider` (both `Func<IReadOnlyList<string>, object?>` receiving the
  normalized roots in caller order). `ServerArguments`, `EnvironmentVariables`, and the
  `InitializeTimeout`/`ShutdownRequestTimeout`/`DisposeWaitTimeout` budgets cover the process
  launch.
- Build the capability and initialization payloads in dedicated factories so tests can pin
  them (Lua: `ClientCapabilitiesFactory`,
  `InitializationOptionsFactory`).

The payload factories may run on background transport threads; keep them thread-safe,
non-blocking, and cheap.

### 3. Lazy startup and recovery

The base never starts the server from the constructor; it starts on demand - first request,
first document open/update, first workspace-change forwarding - through one serialized startup path with a fast path
for an already-healthy client, a startup lock that makes concurrent callers share one recovery
flow, tracked-document reopen after a transport restart (an open that lands inside a startup or
replay window is deferred and applied once the startup settles, so its open reference is never
dropped), and consecutive-failure counting with
the hard failure threshold from `LanguageServerProviderOptions` (a false start result and an
unexpected startup exception both count; once the latch engages, the reported failed state holds
until the provider is recreated and is not downgraded by a later transport fault). The language
package supplies the failure messages (`CreateStartupFailureMessage` for the transient/persistent
split, `CreateMissingClientFailureMessage` for the unconfigured-client configuration) and tunes
the threshold through the options; it otherwise inherits the flow.

### 4. Document synchronization

The base maps the document lifecycle onto `textDocument/didOpen`, `textDocument/didChange`, and
`textDocument/didClose`, driven through the framework's internal `DocumentOperationScheduler` (per-document
serialization, latest-update coalescing, exclusive rename slots) and the language's tracked
store (the `documentStore` constructor argument). The language package customizes through hooks:

- `LanguageId` for the `didOpen` language identifier (Lua sends `"lua"`; C# sends `"csharp"`; Visual Basic sends `"vb"`).
- `OnDocumentSynchronizedAsync` for post-sync refresh work beyond semantic tokens and diagnostics (the base
  triggers the semantic-token refresh and, for a pull-capable server, the diagnostics pull itself); it is
  deliberately not invoked on request-driven synchronization, so each IntelliSense request does
  not issue an extra request. It runs while the document's scheduler slot is still held (and,
  during a restart replay, while the provider's startup serialization is held), so a hook must not
  call provider document or request APIs or await provider startup; detach long-running work
  instead. A change that a session without a
  negotiated synchronization mode cannot express skips both the send and this hook, because the
  server's copy still describes the previous content.
- `OnTrackedDocumentInvalidated` for language state that must follow close, move,
  idle trimming, and synchronization invalidation.
- `InvalidateTrackedDocumentSynchronization` to bridge the base's generic store synchronization
  invalidation onto the language store (both Lua and Roslyn forward to
  `ServerPayloadDocumentStore.InvalidateServerSynchronization`).
- `GetTrackedDiagnostics` and `HandleDiagnosticsPayload` to expose diagnostics; `HandleDiagnosticsPayload`
  parses and caches a payload for a tracked document (return `null` to drop an unparsable or
  stale payload). The framework serves both delivery models through the one hook: a push
  `textDocument/publishDiagnostics` notification and a pull `textDocument/diagnostic` report both arrive here, so a
  pull-only provider implements only this hook.
- `GetDiagnosticsSnapshot` (an internal seam reached through the framework's `InternalsVisibleTo`) to expose
  the cached diagnostic entries together with the content snapshot their offsets refer to; the base's
  code-action implementation uses it to rebuild the server's diagnostic context for the requested range, so a
  provider that caches diagnostics overrides it (Lua and Roslyn forward to
  `ServerPayloadDocumentStore.GetDiagnosticsSnapshot`).
- `GetTrackedSemanticTokens` and `TryStoreSemanticTokens` to expose semantic tokens through the base's
  version-fenced token cache; `TryStoreSemanticTokens` returns `false` for a stale token set.

The diagnostics and semantic-token bullets are optional: a provider that omits them exposes no
diagnostics or semantic tokens (the base returns empty results and raises no update).

The base chooses the change payload from the negotiated `TextDocumentSyncKind` (incremental when
accepted, full otherwise; a server that accepts neither fails loudly at that point under the
default client configuration, and under a client that does not require synchronization a change
that cannot be expressed is skipped with a warning while the next session re-establishes the full
content), keeps request-driven synchronization from triggering refreshes, and degrades to the
documented request fallback when a synchronization send fails.

### 5. Workspace watching

The base mirrors external workspace changes to the server with the Client primitives - one
watch scope per root (watcher, snapshot tracker, recovery state, failure latch, so a failure is
contained to its root and the failure message can name it), a shared internal file-change
forwarder buffering recoverable gaps and replaying them once the server is
ready again, ownership-based forwarding when roots nest (a path is normally forwarded only by the
scope whose root is its longest prefix, so overlapping watchers produce one notification; a root
whose own watcher is inactive stays covered by the delivering watcher), and snapshot
reconciliation after watcher recovery (changes that cannot be replayed unambiguously are dropped
rather than approximated).

The language package supplies:

- the watch specifications constructor argument - the file patterns to watch (Lua: `*.lua`
  recursively plus `.luarc.*` in the workspace root; C#: `*.cs` and `*.csproj`, and Visual Basic: `*.vb` and
  `*.vbproj`, all recursively, with the shared solution, project-asset, build-properties, and build-packages patterns
  composed in by the Roslyn core). Patterns follow the
  `WorkspaceWatchSpecification` contract (a file-name filter without directory separators) and the
  platform's file-name casing behavior. Passing an empty list disables workspace watching for the
  provider, so a host that bridges file events itself can opt out without faking a pattern.
- `IsConfigurationPath` - the configuration-file predicate that triggers a settings refresh
  before the watched-files notification is forwarded. A provider whose settings do not come from a
  workspace file returns `false` unconditionally (Roslyn, C#, and Visual Basic: their options drive the
  settings payload, while a watched build-file change is still forwarded so the server reloads the
  affected projects).
- `CreateSettingsPayload` - the payload for the resulting `workspace/didChangeConfiguration`
  notification.

The framework creates each root's watcher through the virtual `CreateWorkspaceFileWatcher` hook,
which returns an `IWorkspaceFileWatcher`; override it (or assign `WorkspaceFileWatcherFactory` on
`LanguageServerProviderOptions`) to substitute the watching engine, for example with a host file API,
without subclassing the provider. The default watches through the framework's `WorkspaceFileWatcher`,
and a throwing hook surfaces through `WorkspaceWatcherFailed` instead of escaping request APIs.
`WorkspaceChangeAccumulator` is internal: it is the watcher's debounce buffer and the change
forwarder's pending buffer, and no consumer needs it as surface.

### 6. Feature slices

Every feature already has its slice in the framework: the base implements the standard request-and-parse for
all nine features (completion, hover, definition, references, rename, formatting, signature help, document
symbols, and code actions), and the mapping layer (`ResponseParser`) converts each response. Override a base
feature only when the language's wire shape or behavior differs; otherwise there is nothing to implement.

Adding a new feature, or a language-specific override, keeps the same three-piece slice:

1. A protocol payload family under `Nickelony.LanguageServer.Client/Protocol/` when the wire
   shape is not already modeled. Converters are tolerant by policy: malformed elements are
   skipped with a warning, missing optional pieces degrade instead of failing the whole
   response, and open protocol numerics (kinds, severities) stay representable for the provider
   to map.
2. A parser partial that maps payloads to the shared IDEKit models. Convert protocol ranges
   through the document line map; never approximate a malformed edit (a range that cannot be
   mapped is dropped, not turned into a deletion).
3. A provider override for the language-specific behavior: call `base` for the standard path where it
   applies, confirm the server capability, issue the request through the inherited pipeline
   (`SendDocumentRequestAsync`/`SendDocumentPositionRequestAsync`), and map the response with the parser.
   The pipeline accepts a reference-type or a value-type `TResponse`: the outcome is classified from the
   request itself rather than from the response value, so a value-type response does not depend on a null
   check, and a reference-type payload that arrives as a JSON null returns the fallback instead of reaching
   the parser.

### 7. Capability flags

Read the negotiated server capabilities through the inherited `Client` accessor and surface
them as `Supports*` properties (`IsAvailable && Client.Supports*`). Gate each request at dispatch
time with the client flag - the pipeline takes a `supportsRequest` delegate - and return the
feature's documented empty/fallback value when the gate fails. All five flags
(`SupportsReferences`, `SupportsRename`, `SupportsFormatting`, `SupportsDocumentSymbols`, and
`SupportsCodeActions`) are public on the narrow feature contracts the provider inherits, so a host
can consult the availability of any gated feature before offering it. A request that is not gated
must still degrade through the fallback when the server rejects it.

### 8. Callbacks and disposal

The base owns the admission discipline and the disposal sequence: registration goes through the
admitted add path (`AddAdmittedCallback`), raises go through the subscriber enumerator that
checks admission per handler and isolates throwing handlers (logged; later subscribers still
run), fire-and-forget work goes through the background observer (`ObserveBackgroundTask`), and
disposal closes callback admission, cancels the disposal token, disposes the workspace
coordinator (watchers and forwarding buffer), cancels queued document work, and disposes the
client, idempotently. The language package:

- raises its own language events through `RaiseSubscribers`,
- overrides `OnDisposing` for language-owned teardown (Lua needs none today: the base owns the
  semantic-token and pull-diagnostics teardown - it removes the refresh-notification subscriptions and drains the
  in-flight token and diagnostics requests in its own disposal path),
- keeps the base's deliberate choice not to dispose the dispose token source (request paths may
  still link timeout tokens against it; see the trap below).

## Traps (learned on the reference implementation)

### Path normalization contract

- Normalize every host-supplied path at the public boundary with
  `LanguageServerPaths.TryNormalizeLocalPath`; an unusable path is a no-op or empty result,
  never a throw. Work with normalized paths internally (dictionary keys, routing, comparisons).
- Convert to protocol URIs with `LanguageServerPaths.CreateFileUri(normalizedPath)` and back
  with `TryGetLocalPath`; compare with `LanguageServerPaths.AreLocalPathsEqual` /
  `LanguageServerPaths.LocalPathComparer`, which follow the platform policy (Windows and macOS
  are case-insensitive).
- Document identity is the normalized path end to end. There is no document-to-root routing
  layer; roots only shape the initialize payload and the watch scopes.
- A case-only rename refers to the same document on case-insensitive file systems; the provider
  must treat it as a no-op instead of closing and reopening.

### Zero-based coordinates and UTF-16

- Protocol positions are zero-based line/character pairs, and characters are UTF-16 code units
  (LSP's fixed encoding; the client never negotiates another one).
- The provider model exchanges document offsets or `TextPosition` pairs; convert through
  `TextLineMap` / `ProtocolRangeConversion` and let the conversion clamp out-of-document
  coordinates instead of failing.
- Negative inputs are representable in payloads and clamped at the provider boundary
  (`SendDocumentPositionRequestAsync` clamps the `TextPosition` components to zero).
- Reversed ranges cannot be converted; drop them (and their containing edit/entry) rather than
  guessing, and keep the protocol numeric accessible so the mapping layer chooses the fallback.

### Snapshot ownership

- Every payload a provider publishes or caches is an owned immutable snapshot detached from
  internal state; consumers must never observe a later mutation through an earlier event.
- Tracked documents are content plus version; caches are version-fenced, so a payload parsed
  against an older snapshot cannot overwrite newer state. Diagnostics for documents the provider
  has never synchronized are dropped: without tracked content, server ranges cannot be mapped.

### Threading, callback admission, and disposal

- Callbacks may arrive on background threads. Handlers run serially for one invocation, and a
  throwing handler does not prevent later subscribers from being notified.
- Admission closes before disposal releases resources; registration after admission closes is
  ignored, and raising checks admission again per handler.
- The dispose token source is deliberately never disposed by the base:
  request paths may still link timeout tokens against it during disposal, and linking against
  the token of a disposed source throws `ObjectDisposedException` instead of surfacing the
  documented fallback value.

### Hook fault containment

- The framework contains the language hooks it calls around its own work (the post-synchronization
  refresh, tracked-document invalidation, the move hook, the failure-text hooks, the
  configuration-path probe, the settings payload, the diagnostics read): the fault is logged as
  contained and the operation still reports its documented result.
- The tracked-document store's required transforms (`CreateTrackedDocumentState`,
  `ReopenTrackedDocumentState`, `ReplaceTrackedDocumentContent`) are **not** contained. They produce or
  update the record the framework mirrors, so a fault there propagates unchanged: a contained fault
  could not report a truthful outcome, and containing it would also swallow the store's deliberate
  contract exceptions (an already-bound request reference, for example). The store keeps its key
  invariant (a dictionary entry key always equals its record's current path), so the record stays
  reachable and the caller can retry the next synchronization.
- Failure text: the startup-failure and missing-client messages are supplied by the language package,
  because they carry installation wording and product names. The watcher-failure message is host-neutral
  framework copy, so the framework authors it and a host localizes by mapping the message at its own
  presentation layer.

### Lazy startup and capability flags

- The startup state object owns the state transitions, the failure counters, and the transport
  generation fencing that makes stale transport events harmless; transitions are ignored once
  disposal starts, and the disposed state is terminal.
- Capability values are valid for one transport generation: read them through the client
  (never cache them across restarts), and raise `CapabilitiesChanged` whenever a restart or
  transport loss may have changed them. Hosts re-read `IsAvailable` and the `Supports*`
  properties on that event.
- `IsAvailable` is a projection: ready session, not disposing, client ready, startup succeeded.

### Generation invariants

Three owners fence state by transport generation, and a change to one must keep the others
consistent:

- the provider's startup state (`StartupState`) accepts a completed startup only
  for the generation that is still ready and rejects unavailability notifications for
  generations before it - this is the provider-state fence;
- the client's capability store publishes one immutable snapshot per generation and gates
  request results and server callbacks on the owning session still being current - this is the
  capability fence;
- the request dispatcher tracks consecutive timeouts per generation so a stale timeout cannot
  mark a replacement transport unhealthy - this is the timeout fence.

The rule they share: act on a generation only after observing that it is still the current one,
and never let an event for generation N affect generation N+1.

### Request dispatch and timeout policy

- Route feature requests through the shared dispatcher and the base's document-request
  pipeline: ensure the transport is started (capability values only exist after the
  initialize handshake), check the capability gate, acquire a temporary request reference,
  synchronize the document, send with the per-request timeout, parse, and release the
  reference in `finally` (so caller cancellation cannot leak the reference). The release is
  bound to the record itself (`DocumentRequestReference`), so a rename that rekeys the record
  cannot strand the reference. An unsupported request therefore produces no document traffic
  at all.
- Caller cancellation is the only `OperationCanceledException` that escapes to the caller
  (rethrow when the caller's token fired). Provider disposal, provider timeouts, internal
  transport failure, server rejections, and unsupported capabilities produce the documented
  fallback value instead.
- The framework defaults (inherited by Lua): 10 seconds per request, and 2 consecutive request
  timeouts mark the transport unhealthy so the next request restarts it; a successful start, and
  a server rejection, reset the tracking. Timeout-driven restarts are a deliberate fail-safe for
  an unresponsive server and deviate from mainstream language-server clients; set
  `RequestTimeoutRestartThreshold` to `0` (or raise `RequestTimeout`) for a server that
  legitimately exceeds the timeout while it indexes a workspace.

### Document reference semantics

- One `OpenDocument` call acquires one open reference; pair each with one
  `CloseDocument` call, so repeated opens require repeated closes. `UpdateDocument` acquires no
  reference.
- Request paths acquire a temporary request reference and release it after completion; the
  reference is released by identity (`DocumentRequestReference`), so it still finds the record
  when a rename rekeyed it. Idle documents are trimmed beyond the configured cap
  (`LanguageServerProviderOptions.MaxTrackedIdleDocuments`; Lua: 16) with a `didClose`,
  while documents held by an open reference are never trimmed. A close that was deferred because a request
  reference was still active completes when that reference is released.
- Forward `didClose` only when a server session is running: starting a server just to close a
  document is wasteful and can race disposal. Closes are delivered through the document's own
  scheduler slot with a tracked-state recheck, so a close that arrives during a restart replay
  still closes a document the replay reopened instead of leaving a phantom server-open copy.

### Test strategy

- Unit slices: a fake `ILanguageServerClient` that captures notifications and answers scripted
  requests (the Client's event and dispatch surface is small enough to fake completely), an
  `InternalsVisibleTo` test-access bridge for provider internals, and polling helpers for
  callback admission/disposal timing. Pin handshake factories with payload tests and parsers
  with raw-JSON tests per feature.
- Real-server door: an `Integration/` suite that exercises the actual server executable behind
  an environment-configured archive path and reports inconclusive when no server is configured.
  Use it for emission questions the fake cannot answer (LuaLS: hierarchical document symbols),
  keep the suite spec-first, and keep the fakes authoritative for behavior.

## Server compatibility constraints

The client makes five fixed choices that a candidate language server must fit (the full
language-#2 kickoff checklist is recorded in `future-backlog-2026-09-17.md`):

- The server process working directory defaults to the executable's folder; set
  `LanguageServerClientOptions.ServerWorkingDirectory` to pin another directory (for example the
  workspace root).
- Positions are zero-based and fixed to UTF-16 code units.
- Diagnostics are delivered by push and, when the server advertises it, by pull: the framework consumes
  `textDocument/publishDiagnostics` when the server pushes, and additionally runs a `textDocument/diagnostic`
  pull loop when the server advertises `diagnosticProvider` (`SupportsPullDiagnostics`). Both delivery models
  feed the same language hook and `DiagnosticsUpdated` event.
- Dynamic registration is unsupported by design: the client advertises
  `dynamicRegistration = false` and logs and ignores `client/registerCapability` /
  `client/unregisterCapability`.
- Multi-root is fixed at construction: all roots are advertised and watched, one watch scope
  per root, and `workspace/didChangeWorkspaceFolders` is not sent.
