# Provider authoring guide

This guide shows how to add a language to the Nickelony Language Server family. It is the provider-side
counterpart of the host-facing [consumer integration guide](ConsumerIntegration.md).

A language package ships a factory that returns a `LanguageServerProfile` from
`Nickelony.LanguageServer.Provider`: the identifiers, watch patterns, handshake payloads and rules of one language
server. The host hands the profile to the sealed `LanguageServerFeatureProvider`, which implements every
feature; the LuaLS-backed `Nickelony.LanguageServer.Lua` is the verified reference implementation.

When several languages share one server binary, one factory serves them all and a language selector carries what
differs: `Nickelony.LanguageServer.Roslyn` serves C# and Visual Basic this way - see [Sharing a
server across languages](#sharing-a-server-across-languages).

## Package shape

A language package sits on the framework packages:

- `Nickelony.LanguageServer.Provider` is the provider framework, with these parts:
  - The provider is the sealed `LanguageServerFeatureProvider`. It implements the aggregate interface
    (`ILanguageServerFeatureProvider`, in `Provider.Abstractions`), which inherits the language-feature
    contracts and sources and the document synchronization target (`IDocumentSynchronizationTarget`).
  - The authoring surface is what a language package builds: `LanguageServerProfile` with its
    `FileWatchSpecification` list and `LanguageServerProfileHooks`, the optional `DiagnosticMappingPolicy`,
    and the `LanguageServerClientCapabilitiesOptions` with its capability deltas.
  - The tunables are the provider options (`LanguageServerFeatureProviderOptions`).
  - The internal [response mapping](LanguageServerInternals.md#response-mapping), document tracking,
    semantic-token decoding, forwarding, snapshot tracking, scheduling, lifecycle, synchronization, watching,
    and request machinery sit behind the provider.
- `Nickelony.LanguageServer.Provider.Abstractions` holds the editor-facing contracts: the provider interface
  (`ILanguageServerFeatureProvider`), the capability snapshot (`LanguageServerFeatureProviderCapabilities` and
  its source `ILanguageServerCapabilitiesSource`), the startup-failure and watcher-failure payloads, and the
  lifecycle rules a provider honors. The feature contracts it inherits live in
  `Nickelony.IDEKit.LanguageFeatures`.
- `Nickelony.LanguageServer.Client` holds the LSP connection and protocol primitives: process launch and
  JSON-RPC hosting, the initialize handshake, the capability snapshot, the file-URI conversion
  (`DocumentUriConversion`), and the protocol payload folders.

### What a language package owns

`Provider.Abstractions` and `Provider` depend on the IDEKit packages for the editor models. `Client`
deliberately references neither IDEKit nor `Provider.Abstractions`, so the code that converts protocol
payloads into editor models lives in `Provider`, which bridges them. A language package references the
framework and owns these parts:

- The profile factory is one public static class that returns a `LanguageServerProfile`. It sets the language
  identifier, the display names, the watch specifications, the diagnostic mapping policy (when the language
  exposes diagnostics), the client capabilities, the launch settings (`Launch`) and the hooks.
- The hooks cover the configuration-file rule, the settings document, the initialization options and,
  optionally, the notifications sent on each session start (`BuildSessionStartNotifications`).
  `LanguageServerProfileHooks.Default` fits a language that has none of them.
- The profile sets the startup-failure messages only when its wording differs from the defaults, which the
  profile builds from the display names.
- The package declares the option type (Lua: `LuaLanguageServerOptions`; Roslyn: `RoslynLanguageServerOptions`)
  and feeds its values into the hooks, the launch settings and the workspace conventions (watched file
  patterns, configuration-file refresh rule).
- It adds no response mapping and no member to the provider: the framework supplies the standard feature
  request-and-parse.

### Public surface

- A language package keeps everything beyond its factory and option type internal. The Lua package's public
  surface is the factory (`LuaLanguageServerProfileFactory`) and the language option type
  (`LuaLanguageServerOptions`). The Roslyn package adds the language selector (`RoslynLanguage`) to its factory
  and option type.
- The framework tracks documents and caches the diagnostics and semantic-token payloads of the server itself,
  so a language package takes no tracker and declares no state type.
- Every other member of the provider is internal or private. The collaborators and the request pipeline serve
  the framework's own tests and the rest is implementation, so a language package never reaches them: an
  `InternalsVisibleTo` grant names only a test assembly ([architecture
  guide](ArchitectureGuide.md#internals)).
- Keep a new language package that small and raise members to public only when a host needs them.

### Sharing a server across languages

When several languages share one server binary, one factory serves them all. It takes a language selector (for
Roslyn, the `RoslynLanguage` enum) and maps it to an internal descriptor that holds the language's identity
(the language identifier and the display name) and its own source and project watch patterns. The factory
composes those patterns with the shared build inputs, declared once, and builds every language-neutral value
(the handshake, the server arguments, the settings, and the diagnostic mapping) for all of them. One provider
instance serves one language-server session with one language, so a workspace that mixes the languages runs one
provider, and one server process, for each.

Use this shape when one server binary serves several languages, as `Nickelony.LanguageServer.Roslyn` does
for C# and Visual Basic ([Roslyn guide](RoslynGuide.md#position)). Use the standalone shape above
when the server *is* the language.

Consequences to plan for:

- The selector is a closed set, because the server serves a fixed set of languages, so a host cannot declare a
  language the server would not analyze. The descriptor is internal, and a test that needs another identifier
  reaches the factory overload that takes it.
- The default failure messages name the server and the language apart, so a profile whose server name already
  holds the language sets its failure messages itself.

## Recipe

A language package is one factory: it returns a profile that holds the language's data and the few rules that
only code can express. The skeleton below is the whole of a profile that exposes diagnostics; the steps after it
explain each part.

```csharp
using System.Text.Json.Nodes;
using Nickelony.LanguageServer.Client;
using Nickelony.LanguageServer.Provider;

public static class MyLanguageServerProfileFactory
{
    public static LanguageServerProfile Create()
    {
        return new LanguageServerProfile
        {
            LanguageId = "mylang",
            LanguageDisplayName = "My language",
            ServerDisplayName = "My language server",

            WatchSpecifications =
            [
                new("*.mylang", IncludeSubdirectories: true),
                new(".mylangrc", IncludeSubdirectories: false),
            ],

            // Optional: a mapping policy turns diagnostics on.
            DiagnosticMappingPolicy = DiagnosticMappingPolicy.Default with { UnknownMessage = "Unknown My language diagnostic." },

            ClientCapabilities = LanguageServerClientCapabilitiesOptions.Default with { CodeActionKinds = ["quickfix"] },

            Launch = StdioLanguageServerTransportOptions.Default with { ServerArguments = ["--stdio"] },

            Hooks = new LanguageServerProfileHooks
            {
                IsConfigurationPath = static path =>
                    string.Equals(Path.GetFileName(path), ".mylangrc", StringComparison.OrdinalIgnoreCase),

                BuildSettings = static () => new JsonObject { ["MyLang"] = new JsonObject { ["Strict"] = true } },
                BuildInitializationOptions = static () => null,
            },
        };
    }
}
```

A host creates the provider from the profile, the workspace roots and the server path:

```csharp
using var provider = new LanguageServerFeatureProvider(
    MyLanguageServerProfileFactory.Create(),
    [workspaceRootDirectoryPath],
    serverExecutablePath,
    logger: logger);
```

### 1. Profile and contract

The provider implements `ILanguageServerFeatureProvider` end to end, which includes the diagnostics and
semantic-token sources:

- The lifecycle and cached reads cover `IsAvailable`, `GetDiagnostics`, `GetSemanticTokens`.
- The payload events cover `DiagnosticsUpdated`, `SemanticTokensUpdated`, `CapabilitiesChanged`,
  `StartupFailed`, `WorkspaceFileWatchFailed`.
- The document lifecycle of `IDocumentSynchronizationTarget` covers `OpenDocument`, `UpdateDocument`, `CloseDocument`,
  `RenameDocument`.
- The provider publishes the `Capabilities` snapshot (step 7).

It also exposes the standard LSP implementation of the feature members, each handed to an internal provider of
its slice. The class is sealed, so a language shapes the features only through the profile (Lua adds no contract
additions). A feature added after publication arrives as a sibling interface of the provider and not as a new base
of `ILanguageServerFeatureProvider`, as the [evolution rules](ArchitectureGuide.md#evolution-after-publication)
say.

Diagnostics are optional: a profile that supplies no `DiagnosticMappingPolicy` exposes none, and the provider
drops every diagnostics payload. Semantic tokens need nothing from the language; the provider decodes and caches
them.

Each request method accepts a language-feature request record and a `CancellationToken`. The record carries
the document identifier (for this provider, a local file path), the current document text, and, for a
position-scoped feature, a document offset, plus any feature-specific state. The synchronous reads return
owned snapshots.

No profile delegate runs during construction, so the provider never sees a half-initialized profile: the
identifiers, display names, watch specifications, messages and the mapping policy are data. The profile
validates its values as they are assigned and copies its lists, and the provider checks each watch specification
when it is constructed. Every hook runs lazily, on whichever thread reaches it. `LanguageId` is read on demand by
the synchronization path.

### 2. Client construction and handshake

The provider creates one `LanguageServerClient` per instance from the profile and owns it (the client is
disposed with the provider). `LanguageServerFeatureProvider.Create(profile, roots, client)` adopts a client that
the host built itself instead; the launch settings, client capabilities and payload builders of the profile
apply only to a client that the provider creates.

- The provider normalizes the workspace roots first, so an invalid list fails before a client exists. The
  first entry is the primary root (`rootUri`); every entry is advertised through `workspace/workspaceFolders`
  and answered by the same callback.
- A null, empty or whitespace server executable path yields no client, and the provider reports a persistent
  `StartupFailed` with the `MissingClientFailureMessage` of the profile. `StdioLanguageServerTransport` itself
  throws for such a path (`ArgumentNullException` for null, `ArgumentException` otherwise).
- The profile maps onto the client as follows. `Launch` configures the `StdioLanguageServerTransport` that the
  provider creates over the executable path: the arguments, the working directory, the environment variables
  and the child-process guard. `ClientCapabilities` feeds the client capabilities through
  `LanguageServerClientCapabilitiesBuilder`.
  `Hooks.BuildSettings` is the `SettingsProvider` (a `Func<JsonNode>` that returns the settings document: the
  client caches it for `workspace/configuration` answers, pushes it through `workspace/didChangeConfiguration`
  at startup, and pushes it again when the provider calls `PushSettingsAsync`).
  `Hooks.BuildInitializationOptions` is the initialization options, and `Hooks.BuildSessionStartNotifications`,
  when set, is the `SessionStartNotificationsProvider`. A host that needs the
  `InitializeRequestTimeout`/`ShutdownRequestTimeout`/`DisposeWaitTimeout` budgets builds the client itself.
- The `logger` goes to the client, so its connection and server-message diagnostics reach the host's logger.
- Declare the capability delta as a `LanguageServerClientCapabilitiesOptions.Default with { ... }` value, and
  build the initialization options and the settings document in dedicated builders, so tests can pin them
  (Lua: `LuaLanguageServerProfileFactory.ClientCapabilities`, `InitializationOptionsBuilder`, `SettingsBuilder`).
  `LanguageServerClientCapabilitiesBuilder.Build` builds the neutral capability baseline from that value.
- Spell the settings document's member names exactly as the server expects them: the client applies no naming
  policy to a `JsonNode`, so a section such as LuaLS's `Lua` keeps its casing.

The hooks may run on background connection threads: keep them thread-safe, non-blocking, and cheap.

### 3. Lazy startup and recovery

The provider never starts the server from the constructor; it starts on demand (first request, first document
open/update, first workspace-change forwarding) through one serialized startup path with these behaviors:

- A fast path applies to an already-healthy client.
- A startup lock makes concurrent callers share one recovery flow.
- An `OpenDocument` call that lands inside a startup or restart-replay window is retried while that flow
  settles, up to a bounded number of retries. When the connection keeps failing, the call sends nothing.
- The record keeps its open lease for a later restart replay only when the open was committed locally. The
  provider commits locally only while the client is down, because only a start that finds it down takes the
  replay snapshot. It skips that commit while a start holds the startup lock, while the client is ready, while
  it is `Starting` or `Failed`, or while an interrupted replay is pending, and the open is retried as the
  previous bullet describes. An update skipped this way is dropped, and the next update carries the content.
- Consecutive-failure counting uses the hard failure threshold from `LanguageServerFeatureProviderOptions`,
  where a false start result and an unexpected startup exception both count; once the latch engages, the
  reported failed state holds until the provider is recreated and is not downgraded by a later connection
  fault.

The profile supplies the failure messages: `TransientStartupFailureMessage`, `PersistentStartupFailureMessage`
and `MissingClientFailureMessage`. They default to sentences built from `ServerDisplayName` and
`LanguageDisplayName`, and a blank value suppresses that report. A language package sets one only when its
wording differs, and the Lua and Roslyn packages set none. The host, not the package, tunes the failure threshold
through `LanguageServerFeatureProviderOptions.HardStartupFailureThreshold`.

### 4. Document synchronization

The provider maps the document lifecycle onto `textDocument/didOpen`, `textDocument/didChange`, and
`textDocument/didClose`, driven through the framework's internal `DocumentOperationScheduler`
(per-document serialization, latest-update coalescing, and exclusive operations on one or two document
paths, as a rename uses) and the framework's document tracker.

A profile supplies these values for it:

- `LanguageId` is the `didOpen` language identifier (Lua sends `"lua"`, C# `"csharp"`, Visual Basic `"vb"`).
- `DiagnosticMappingPolicy` holds the mapping rules that turn diagnostics on. The provider parses and caches
  both a push `textDocument/publishDiagnostics` notification and a pull `textDocument/diagnostic` report with
  this one policy, so a pull-only language supplies only the policy. A profile that supplies none drops every
  diagnostics payload and exposes no diagnostics.

After an open or change synchronization, the provider refreshes the semantic tokens and, for a pull-capable
server, pulls diagnostics. A request that carries newer text refreshes after the change it sends, because the
host's update with the same text then has nothing to send, and the request's temporary open of a document the
host did not open refreshes nothing. A change that the negotiated synchronization mode cannot express skips
the send and the refresh, because the server's copy still describes the previous content.

The provider's code-action request sends the cached diagnostics as the context for the requested range, so a
profile with a policy gets the server's quick fixes. A profile without one sends an empty context, and a
server that derives its quick fixes from that context offers none.

The provider caches semantic tokens against the tracked document version, and it rejects a stale token set.

The provider chooses the change payload from the negotiated `TextDocumentSyncKind` (incremental when
accepted, full otherwise), keeps request-driven synchronization from triggering refreshes, and
degrades to the documented request fallback when a synchronization send fails.

A server that accepts neither mode fails loudly under the default client configuration; under a
client that does not require synchronization, a change that cannot be expressed is skipped with a
warning while the next session re-establishes the full content.

### 5. Workspace watching

The provider mirrors external workspace changes to the server with the [file-watching engine](FileWatchingGuide.md):

- The provider keeps one watch supervisor per root (watcher, snapshot tracker, recovery state, failure latch), so
  a failure is contained to its root and the failure message can name it.
- The provider uses a shared internal workspace-change forwarder that buffers recoverable gaps and replays them
  once the server is ready again.
- Forwarding is ownership-based when roots nest: a path is normally forwarded only by the scope whose root is
  its longest prefix, so overlapping watchers produce one notification, and a root whose own watcher is
  inactive stays covered by the delivering watcher.
- Snapshot reconciliation after watcher recovery drops changes that cannot be replayed unambiguously rather
  than approximating them.

The profile supplies:

- `WatchSpecifications` supplies the file patterns to watch (Lua: `*.lua` recursively
  plus `.luarc.*` in the workspace root; C#: `*.cs` and `*.csproj`; Visual Basic: `*.vb` and `*.vbproj`, all
  recursively, with the shared solution, project-asset, build-properties, build-targets, build-packages, and
  global-json patterns composed in by the Roslyn factory).
  - Each pattern follows the `FileWatchSpecification` contract, a file-name filter without
    directory separators, and the platform's file-name casing behavior.
  - An empty list disables watching, so a language package whose server watches files itself can opt out
    without faking a pattern.
- `Hooks.IsConfigurationPath` is the configuration-file predicate that triggers a settings push before the
  watched-files notification is forwarded: the provider calls `ILanguageServerClient.PushSettingsAsync`, and the
  client sends the document its `SettingsProvider` returns, the same one that answers `workspace/configuration`.
  A language whose settings do not come from a workspace file returns `false` unconditionally (Roslyn, for C# and
  Visual Basic: its options drive the settings, while a watched build-file change is still forwarded so the
  server reloads the affected projects).

The framework creates each root's watcher itself, with one `FileSystemWatcher` per watch specification; the
[provider guide](ProviderGuide.md#internal-machinery) describes the machinery. A watcher that fails to start
is classified as a startup failure and reported once per root through `WorkspaceFileWatchFailed`, until a
watcher starts again, instead of escaping request APIs.

### 6. Feature slices

Every feature already has its slice in the framework: an internal provider such as
`HoverProvider` sends the standard request, and a static parser beside it converts the response:
a `{Feature}ResponseParser`, or the shared `WorkspaceEditParser`, `DiagnosticsParser` or
`SemanticTokensDecoder` for the features that return those shapes. The provider is sealed and the request
pipeline is internal, so a language package gets the features as they are and cannot send a request of its own
through the provider.

A feature the framework lacks is added to the framework as a slice of the same shape:

1. A protocol payload family under `Nickelony.LanguageServer.Client/Protocol/` when the wire
   shape is not already modeled. Converters are tolerant by policy: malformed elements are
   skipped with a warning, missing optional pieces degrade instead of failing the whole
   response, and open protocol numerics (kinds, severities) stay representable for the provider
   to map.
2. A static `{Feature}ResponseParser` that maps payloads to the shared IDEKit models, unless a shared parser
   already maps the response shape. Convert protocol ranges through the document line map; never approximate a
   malformed edit (a range that cannot be mapped is dropped, not turned into a deletion).
3. An internal `{Feature}Provider` that implements the feature's language-feature contract:
   build a `DocumentRequestDescriptor` (the capability gate and the request), send it through
   `IDocumentRequestPipeline`, and map the response with the parser. The provider exposes it as a public
   member that guards its arguments and passes the call on. A request-to-wire mapping of one
   statement stays inline in the provider; a longer one is a static `{Feature}RequestConversion` beside the
   response parser, so the provider reads as a descriptor, a send and a parse.

The pipeline accepts a reference-type or value-type `TResponse`. It classifies the outcome from the
request rather than the response value, so a value-type response needs no null check. A reference-type
payload that arrives as JSON null returns the fallback instead of reaching the parser.

### 7. Capability flags

The provider reads the negotiated server capabilities once per decision, from the client's
`NegotiatedCapabilities` snapshot, so the flags of one decision never mix two connection generations. The
provider publishes the flags a host decides on as one `Capabilities` snapshot
(`LanguageServerFeatureProviderCapabilities`), which reads as `None` while the provider is not available and
which `CapabilitiesChanged` announces. The pipeline gates each request at dispatch time with the client flag
(the request descriptor's `SupportsRequest` gate) and returns the feature's documented empty/fallback value
when the gate fails; a `null` gate leaves the request ungated.

The snapshot carries `SupportsReferences` and `SupportsRename`, whose requests are gated, so a host can consult
availability before it offers the feature. It also carries `SupportsHover`, `SupportsDefinition` and
`SupportsSignatureHelp`, whose requests always go out, so a host can skip one the server cannot answer. A request
that is not gated must still degrade through the fallback when the server rejects it. Formatting, document symbols,
workspace symbols, code actions and completion resolve are gated the same way but carry no flag, because no host
decides on them.

### 8. Callbacks and disposal

The provider owns the admission discipline and the disposal sequence. Registration ignores a handler added after
admission closes. Raises run the handlers serially, check admission again before each one, and isolate a
throwing handler (logged; later subscribers still run). Fire-and-forget work runs under a background observer,
so a fault is logged instead of surfacing as an unobserved task exception.

Disposal cancels the disposal token first, which closes callback admission and stops every collaborator from
admitting work. It then drops the event subscribers, unsubscribes from the client, disposes the workspace
coordinator (watchers and forwarding buffer), cancels queued document work, and disposes the client,
idempotently. The language package adds nothing to it: Lua and Roslyn need no teardown of their own, because
the provider removes the refresh-notification subscriptions and cancels the in-flight token and diagnostics
requests in its own disposal path.

## Traps

### Path normalization contract

- Normalize every host-supplied path at the public boundary with `LocalPath.TryNormalize`; an unusable path,
  which includes a relative one, is a no-op or empty result, never a throw. Work with normalized paths
  internally (dictionary keys, routing, comparisons).
- Convert to protocol URIs with `DocumentUriConversion.CreateFileUri(normalizedPath)` and back with
  `DocumentUriConversion.TryGetLocalPath`. The framework compares document paths through
  `LanguageServerFeatureProviderOptions.PathComparison`, which defaults to
  `LocalPathComparisonPolicy.ForCurrentPlatform` (Windows and macOS are case-insensitive). A language package
  compares the file-name parts it matches itself, such as extensions, with the platform policy (`Comparer` for
  dictionary keys, `Comparison` for string operations).
- Document identity is the normalized path end to end. There is no document-to-root routing
  layer; roots only shape the initialize payload and the watch supervisors.
- A case-only rename refers to the same document under a case-insensitive path comparison; the provider
  must treat it as a no-op instead of closing and reopening.

### Zero-based coordinates and UTF-16

The provider requests carry document offsets, and the request pipeline maps them to positions with the document's
`TextLineMap`; the [language features guide](LanguageFeaturesGuide.md#text-and-position-conventions) defines
those conventions. LSP fixes protocol characters to UTF-16 code units, and the client never negotiates another
encoding.

- `TextLineMap` clamps out-of-document coordinates instead of failing, whereas the framework's range mapping
  rejects a negative or reversed protocol range.
- A negative offset, or one past the end of the document, is clamped to the document when the request pipeline
  maps it to a position.
- Reversed ranges cannot be converted; drop them (and their containing edit/entry) rather than
  guessing, and keep the protocol numeric accessible so the mapping layer chooses the fallback.

### Snapshot ownership

- Every payload a provider publishes or caches is an owned immutable snapshot detached from
  internal state; consumers must never observe a later mutation through an earlier event.
- Tracked documents are content plus version; caches are version-fenced, so a payload parsed
  against an older snapshot cannot overwrite newer state. Diagnostics for never-synchronized
  documents are dropped: without tracked content, server ranges cannot be mapped.

### Threading and disposal

- Callbacks may arrive on background threads, so a handler never assumes the UI thread.
- The dispose token source is deliberately never disposed by the provider: request paths may still link
  timeout tokens against it during disposal, and linking a disposed source's token throws
  `ObjectDisposedException` instead of surfacing the documented fallback value.

### Hook fault containment

- The framework contains the profile delegates it calls around its own work (the configuration-path probe) and
  the word rule of the diagnostic mapping policy: the fault is logged as contained and the operation still
  reports its documented result. The client contains a fault of its settings provider the same way: the push is
  skipped and logged, so the watched-files notification still goes out.
- Failure text: the profile supplies the startup-failure and missing-client messages, which default to sentences
  built from the display names, and a language package sets them when its wording carries installation
  instructions or product names. The watcher-failure message is host-neutral framework copy, so the framework
  authors it and a host localizes by mapping the message at its own presentation layer.

### Lazy startup and capability flags

- The startup state object owns the state transitions, the failure counters, and the connection
  generation fencing that makes stale connection events harmless; transitions are ignored once
  disposal starts, and the disposed state is terminal.
- Capability values are valid for one connection generation: read them through the client
  (never cache them across restarts), and raise `CapabilitiesChanged` whenever a restart or
  connection loss may have changed them. Hosts re-read `IsAvailable` and `Capabilities` on that event.
- `IsAvailable` is a projection: ready session, not disposing, client ready, startup succeeded.

### Generation invariants

The startup state, the client's connection state, and the request runner each fence their state by
connection generation, as the [LanguageServer internals guide](LanguageServerInternals.md) describes. A
change to one keeps the shared rule: act on a generation only after observing that it is still the current
one, and never let an event for generation N affect generation N+1.

### Request dispatch and timeout policy

- Feature requests go through the shared request runner and the document-request pipeline, which runs
  these steps in order:
  1. ensure the connection is started, because capability values exist only after the initialize handshake;
  2. check the capability gate;
  3. synchronize the document, which also acquires the temporary request lease;
  4. send with the per-request timeout;
  5. parse, and release the lease in `finally` when this request acquired one, so caller cancellation cannot
     leak it.
- The release is bound to the record itself (`DocumentRequestLease`), so a rename that relocates the record
  cannot strand the lease.
- An unsupported request therefore produces no document traffic at all.
- Caller cancellation is the only `OperationCanceledException` that escapes to the caller
  (rethrow when the caller's token fired). Provider disposal, provider timeouts, internal
  connection failure, server rejections, and unsupported capabilities produce the documented
  fallback value instead.
- Each request runs under `RequestTimeout`, and `RequestTimeoutRestartThreshold` consecutive timeouts mark
  the connection unhealthy so the next request restarts it; a successful start, and a server rejection,
  reset the tracking.
- The [options](ProviderGuide.md#options) state the defaults, and the setting for a server that legitimately
  exceeds the timeout while it indexes a workspace.
- Timeout-driven restarts are a deliberate fail-safe for an unresponsive server, and they deviate from
  mainstream language-server clients.

### Document lease semantics

- One `OpenDocument` call acquires one open lease; pair each with one
  `CloseDocument` call, so repeated opens require repeated closes. `UpdateDocument` acquires no
  lease.
- Request paths acquire a temporary request lease and release it after completion.
- Idle documents beyond the cap (`LanguageServerFeatureProviderOptions.MaxTrackedIdleDocuments`, in the
  [options](ProviderGuide.md#options)) are trimmed with a `didClose` each time a request releases its lease, but
  documents held by an open lease are never trimmed.
- A close deferred because a request lease was still active completes when that lease is released.
- Forward `didClose` only when a server session is running: starting a server merely to close a document is
  wasteful and can race disposal.
- Closes run in the document's own scheduler slot and recheck the tracked state, so a close that arrives
  during a restart replay still closes a document the replay reopened instead of leaving a phantom
  server-open copy.
- The calls on one document reach its scheduler slot in the order the host made them, also while an earlier call
  waits for the connection to start. A close made after an open therefore closes the document that open opened,
  and a close or an open ends the updates of its document that still wait for the connection, so an update made
  before a close cannot recreate the record the close removed. A call on another document never waits for it.

### Test strategy

- Unit slices fake `ILanguageServerClient` for captured notifications and scripted request answers, grant a
  provider package's internals to its own test project with `InternalsVisibleTo`, and pin handshake factories
  with payload tests and parsers with raw-JSON tests per feature.
- The Lua tests have an opt-in `Integration/` suite that drives the real server executable and reports
  inconclusive when none is configured; reserve it for emission questions the fake cannot answer.

## Server compatibility constraints

The client makes fixed choices a candidate language server must fit:

- The server process working directory defaults to the executable's folder; set `ServerWorkingDirectory` on
  the profile's `Launch` to pin another directory (for example the workspace root).
- Positions are zero-based UTF-16 code units; see
  [Zero-based coordinates and UTF-16](#zero-based-coordinates-and-utf-16).
- Diagnostics arrive by push and, when the server advertises it, by pull: the framework consumes
  `textDocument/publishDiagnostics` when the server pushes, and additionally runs a `textDocument/diagnostic`
  pull loop when the server advertises `diagnosticProvider`
  (`NegotiatedCapabilities.SupportsPullDiagnostics`). Both delivery models feed the same diagnostic mapping
  and `DiagnosticsUpdated` event.
- Dynamic registration and the fixed multi-root set are covered in the
  [client limitations](ClientGuide.md#limitations).
