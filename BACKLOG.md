# Backlog

Open work only, one entry per GitHub issue. The heading is the title, the bold line is the label set, the body is the issue.

Every open entry is additive under the [evolution policy](docs/ArchitectureGuide.md#evolution-after-publication), so it can
land in a minor release after publication. An entry whose work needs a breaking change lands before publication, or names
the additive form, such as an overload, an init-only option or a sibling interface.

## Repository, packaging and process

### REPO-1 - Confirm the Linux leg and widen it
**P2 · platform**

The `linux` job of `.github/workflows/build.yml` builds the provider projects and runs the Client, Lua and
`LocalPath` suites on `ubuntu-latest`. No runner has executed it, so only a push can show that it passes.
The FileWatching, Provider and Provider.Abstractions suites still name `C:\Workspace` paths, which the path
rules reject as relative off Windows, so the job leaves them out. Root those suites at a temporary path,
as the Client tests do with `TestPaths.Root`, and add them to the job. A macOS leg does not exist yet.

Done when the `linux` job passes on a push and also runs the FileWatching, Provider and Provider.Abstractions
suites.

### REPO-9 - Work the runs past the documentation aims down
**P3 · docs**

`DocumentationReportTests` reports the runs past the AGENTS.md aims without failing. The runs left to work down
are in these Markdown files: `docs/ClientGuide.md`, `docs/CoreGuide.md`, `docs/EditorUsageGuide.md`,
`docs/KeyBindingContextDesign.md`, `docs/LuaGuide.md`, `docs/MatchingGuide.md`, `docs/ProviderAuthoring.md`,
`docs/ProviderGuide.md`, `docs/RoslynGuide.md`, `docs/RoslynProviderDesign.md`, `README.md`, `samples/README.md`,
`tests/TestSupport/README.md`, `CONVENTIONS.md`, `REVIEWING.md` and `BACKLOG.md`; and in the `///` and `//`
comments of the source files, the `shared/docs` fragments and one project file, which the detailed report lists
per file.

Work them down in a dedicated pass, or when a doc is next touched: split a run where the split reads better, and
move a fact to the guide that owns it. Never cut a fact to meet an aim. A member doc that restates its owning
guide belongs to the same pass: move the duplicated detail to the guide and leave a one-line consequence plus a
link, because the `///` aim is a guide, not a rule.

Done when every run that the detailed guard report still lists past an aim is one that a split would make
harder to read.

### REPO-10 - Adopt the 120-column documentation width
**P4 · docs**

Stanza's own `.editorconfig` sets `stanza_doc_max_line_length = 120`. This tree leaves that key unset, so
documentation comments wrap at `stanza_max_line_length`. Adopting 120 rewraps most doc comments, so make the
change on its own, apart from any wording change.

Done when `.editorconfig` sets `stanza_doc_max_line_length`, and a `./format.ps1` run after the change alters
no documentation comment.

### REPO-11 - Check the nuget.org links after publication
**P3 · docs**

The nuget.org links in the package README badges and in docs/ClientGuide.md resolve only after the packages are
published, so they cannot be checked before then. The install lines of the package READMEs and of
docs/ConsumerIntegration.md carry `--prerelease` while the version has a suffix, because `dotnet add package`
finds no stable version of a prerelease-only package.

Done when the nuget.org badge links resolve after publication, and the install lines drop `--prerelease` at the
first stable version.

### REPO-14 - Repack the local feed and spot-check before publication
**P1 · process**

Publication waits for two things: the solution is clean, and the owner has visually spot-checked the packages.
Repack the feed from the settled tree, and delete the previous `.nupkg` and `.snupkg` files first. A
same-version package already in the feed or the global packages folder is reused, so a repack over an old
package can publish stale content. The spot-check comes after the repack.

Done when the feed holds one fresh package set built from the settled tree, and the owner has checked it.

### REPO-17 - Settle the members the block-body rules disagree on
**P4 · hygiene**

Some members oscillate between `ZA0040` (a wrapped expression body wants a block) and `ZA0041` (a one-line
block wants an expression body), because the block fits on one line where the expression body does not, so
neither code fix is stable. `ConnectionState` and `NavigationLocation` hold some of them, and the build reports
each.

Done when the build reports no `ZA0040` or `ZA0041`.

### REPO-23 - One link and define scheme for the small adapters
**P4 · refactor**

`shared/KeyBindings` is linked file by file while `shared/QuickPick` is linked by a wildcard, and the Avalonia adapters set
`AVALONIAKEYBINDINGS` and `AVALONIAQUICKPICK` by hand while `AVALONIAEDIT` follows from `EditorEngine` in
`Directory.Build.targets`. A contributor who adds a third adapter family copies whichever project they open first. Derive the
adapter define from one property, as `EditorEngine` does, and link each shared home by wildcard.

Done when no adapter project defines its toolkit symbol by hand and every shared home is linked by a wildcard.

### REPO-24 - Split `ProjectReferenceGuardTests` by concern
**P4 · test**

`ProjectReferenceGuardTests` holds the reference tiers, the readme diagram, the import map, the build targets and the project
naming, so a failure name does not say which rule broke. Split it into one guard class per concern.

Done when each guard class checks one concern and its name says which.

### REPO-26 - Tighten when a shared file takes a seam
**P4 · docs**

Step 3 of the sharing order in `docs/EditorSharingGuide.md` admits a seam for a single divergence that needs no private state,
which overlaps step 2, so `HoverSeam` and `PointerNavigationSeam` hold one member each. State two triggers instead: a subscription
whose event or delegate type differs per engine, or a divergence at three or more sites.

Done when the guide states the triggers and each seam in the bindings meets one of them or is folded into its caller.

## Nickelony.LanguageServer.Client

### CLIENT-1 - Host seams for show-document, telemetry and dynamic registration
**P3 · feature**

`LanguageServerClientHooks` surfaces `window/showMessage`, `window/showMessageRequest`, `$/progress` and
`workspace/applyEdit`. The client logs `telemetry/event` at debug level, does not handle `window/showDocument` (a
server sees an unknown method), and ignores `client/registerCapability` and `client/unregisterCapability`, so a server that
opens a document in the editor, a host that forwards telemetry and a server that registers a capability dynamically get
nothing from the client. Add `ShowDocument` and `TelemetryEvent` hooks to the existing ones, with the same defaults
(the logger for telemetry, a failure answer for show-document), as new init-only members of `LanguageServerClientHooks`
and `LanguageServerFeatureProviderHooks`, plus an option to honor dynamic registration.

Done when a host receives `window/showDocument` and `telemetry/event` through the hooks, and a registered capability reaches
`NegotiatedCapabilities`, covered by client tests.

### CLIENT-2 - Workspace folder mutation
**P2 · feature**

The folder set is fixed at construction and `workspace/didChangeWorkspaceFolders` is never sent. Add an
explicit folder-change API that updates the snapshot and notifies the server, keeping the fixed-set mode
documented. The provider must add and remove watch supervisors in the same change, because it creates watch
supervisors and snapshots per construction-time root.

Done when adding or removing a folder sends `workspace/didChangeWorkspaceFolders` and updates the watch
supervisors, covered by a client test.

### CLIENT-3 - Per-resource configuration
**P2 · feature**

`workspace/configuration` answers every item from the single global settings snapshot, and the item model does
not carry `scopeUri`. Add a resource-aware provider overload with the global snapshot as fallback and
`scopeUri` preserved.

Done when a configuration item with a `scopeUri` is answered from that resource's settings, covered by a
client test.

### CLIENT-4 - Protocol optional-member retention
**P2 · feature**

Not modeled yet: completion `labelDetails` and `insertTextMode`; a typed signature-help `activeParameter`; the
`codeAction/resolve` request (the client reads only the `resolveProvider` flag),
`codeActionProvider.codeActionKinds`, `CodeActionContext.only` and `triggerKind`; and a typed document-change
union. Each lands as a new member or a new type on top of the existing tolerant readers: the typed
`activeParameter` is a new init-only member beside the raw `JsonElement` `ActiveParameter` of
`SignatureHelpResponse` and `SignatureInformationPayload`, which keep their type, and the union is a set of
new types beside `WorkspaceDocumentChangePayload`, which keeps its members. Decide model-versus-document per
field when a consumer needs it (LuaLS and the Roslyn language server are the supported servers today).

Done when each field above is modeled as a new member or type, or is listed as unmodeled in the limitation
list of `docs/ClientGuide.md`.

### CLIENT-8 - Initialize payload extension seam
**P2 · feature**

The payload stays closed to `locale`, `clientInfo`, `trace` and extra members. Add an
`InitializeParametersProvider`-style hook plus optional `Locale`/`ClientInfo` options when a host needs them.
`InitializeParamsBuilder` builds the `InitializeParams` record, so the hook joins it and the record gains the members.

Done when a host can supply initialize parameters through a hook, covered by a client test.

### CLIENT-12 - Remaining host-flavored knobs
**P3 · feature · trigger-gated**

The watcher timing constants (250 ms, 2 s, 3 attempts and a 64 KiB buffer) are private to
`FileWatcher` in the FileWatching package. Workspace folder display names are derived from the path, and
the document version policy is internal and starts at 1. Promote each when a host needs it.

### CLIENT-14 - Per-client path identity policy
**P3 · feature · trigger-gated**

The provider compares document paths through `LanguageServerFeatureProviderOptions.PathComparison`, but the
client keys its diagnostics payloads and normalizes its `initialize` roots with the shared path rules, which
read their case default from the operating system once per process. Core's `LocalPath.NormalizeRoots` and
`LocalPathComparisonPolicy` already take an explicit policy, so the gap is a per-client policy that the client
honors as the provider does. It lands when a multi-client or case-sensitive-volume host appears.

### CLIENT-15 - Client test debt
**P4 · test · parked**

The client suite has no tests for null-parameter notification framing or store-level concurrency. Most client
tests install a session through a white-box helper and reach the collaborators through the internal composition of
the client (the connection state, the session supervisor, the server-message router and the start gate), which
bypasses the public start path.

Done when the client suite tests null-parameter notification framing and store-level concurrency, and the
tests that install a session by hand start through a scripted transport instead.

### CLIENT-16 - Diagnostics delivery measurement
**P3 · perf · measurement-gated**

Whole-set diagnostic materialization per drain cycle is unmeasured at large workspace sizes. Profile a 10k-diagnostic burst before choosing
between incremental delivery and batching.

Done when a profile of a 10k-diagnostic burst decides between incremental delivery and batching.

### CLIENT-18 - `textDocumentSync` options modeling
**P3 · feature**

`openClose`, `save`, `willSave` and `willSaveWaitUntil` are parsed away; the Provider sends `didOpen`/`didClose`
regardless, and nothing in the family sends `didSave`. Implement when a server actually depends on it; the gap is
listed in the limitation list of `docs/ClientGuide.md`.

Done when a server that requests save notifications receives `textDocument/didSave`, covered by a client test.

### CLIENT-25 - Semantic-token deltas
**P3 · feature**

Implement client-side delta-edit application for semantic tokens. The client never requests deltas today, so
the request path and the capability surface for them come with it.

Done when the client requests semantic-token deltas and applies the returned edits, covered by a test.

### CLIENT-27 - Typed per-feature request facade
**P3 · feature**

Add a typed per-feature request facade on `LanguageServerClient` for the subset `Provider` uses and for a
protocol-only host, as new members beside the untyped
`SendRequestAsync<TResult>(string method, object parameters, ...)`. The untyped path and the public
`LspMethodNames` constants stay as the escape hatch for a request the facade does not model, as
vscode-languageclient keeps `sendRequest(method, params)`.

Done when `Provider` sends its requests through the facade, covered by a client test, and the untyped
`SendRequestAsync` keeps its signature.

### CLIENT-28 - Split `WindowsJobObject`
**P3 · platform**

Move the internal `WindowsJobObject` out of the neutral `Client` package into a
`Nickelony.LanguageServer.Client.Windows` (or shared platform) package. Client keeps its default kill-on-close
guard on Windows, so the new package is an implementation detail that Client depends on, and a host sees the
same guard without referencing it.

Done when the Client package compiles without `WindowsJobObject`, the Windows-only type ships in a separate
package that Client references, and the Windows default guard is unchanged, covered by the client suite.

### CLIENT-32 - `System.Text.Json` source generation
**P3 · perf**

Adopt source generation for the built-in payload types and converters (trimming/AOT + startup).
`ProtocolSerialization` builds every serializer option set of the client, so a generated context joins the
options there.

Done when the built-in payload types serialize through a source-generated `JsonSerializerContext`, covered by
the client suite.

### CLIENT-36 - Non-Windows child-process lifetime guard
**P3 · platform**

The default `IChildProcessLifetimeGuard` is a kill-on-close job object on Windows and a documented no-op on
every other platform, so on Linux and macOS only the graceful shutdown handshake and the forced teardown stop
the server, and a host crash can strand it. Add an opt-in POSIX guard (a `prctl(PR_SET_PDEATHSIG)` launcher on
Linux, a process-group equivalent on macOS), or a new member that exposes which guarantee applies so a host
knows when to supply its own guard. Either lands on `StdioLanguageServerTransportOptions`, which holds the launch
settings of the stdio transport, and the default guard of each platform stays as it is.

Done when the opt-in POSIX guard ships with a test on each platform it covers, or the guarantee each platform
gives is exposed on the stdio transport's options, and the default guard is unchanged.

### CLIENT-37 - `TimeProvider` for the request timeout
**P4 · feature**

`RequestAttemptRunner` (Provider) reads `LanguageServerFeatureProviderOptions.RequestTimeout` and calls
`CancelAfter`. Inject `TimeProvider` the way `FileChangeDebouncer` does.

Done when a test drives a request timeout with a fake `TimeProvider` without real waiting.

### CLIENT-38 - Parse completion `itemDefaults` once per list
**P4 · perf**

`CompletionResponseJsonConverter` applies a list's `itemDefaults` once for each item
(`ApplyCompletionItemDefaults`, called from `DeserializeItems`), so a large completion list repeats the
defaults read for every entry. Read the defaults once per list.

Done when a large completion list is deserialized with one defaults read, covered by a test.

## Nickelony.LanguageServer.Provider

### PROVIDER-5 - Deferred-close move bound
**P3 · bug**

`FollowRenamedDocumentCloseAsync` follows at most four move hops, so a pathological storm of moves can leave a
deferred close pending until a later release/open/close/trim touches the newest path. Consider a record-bound
close intent if a host reports it.

Done when a test moves a document past the follow limit during one release, and its deferred close completes
on a later release, open, close or trim.

### PROVIDER-9 - Forwarder constructor bundling
**P3 · refactor · trigger-gated**

`WorkspaceChangeForwarder` takes five constructor parameters and the public `FileWatcher` constructor six. Named
arguments keep call sites readable, so bundle a constructor's parameters into a context record only when a seventh
parameter appears or a second consumer needs the same bundle. The forwarder is internal, so its constructor changes
freely; `FileWatcher` keeps its six-parameter constructor and gains an overload that takes the record, which carries the
seventh value.

Done when a constructor past that point takes one context record and its suites pass.

### PROVIDER-13 - Generation fence for watch-supervisor snapshot captures
**P3 · perf · trigger-gated**

The initial and recovery captures run on the thread pool, outside the watch-supervisor lock, but no generation
check gates their results. A fence is the next step if a stale-capture report or a measured recovery latency
justifies it.

### PROVIDER-14 - Nested-root ownership index
**P3 · perf · trigger-gated**

Forwarding and snapshot application resolve the owning scope by scanning every root per path. A prefix index would keep that flat per path
if host configurations grow many roots.

### PROVIDER-15 - Cross-family helper consolidation
**P3 · refactor · trigger-gated**

Event-raising policy exists in Provider (`ProviderEvents`) and Client (`SequentialSignalSubscriberSet`);
background-task observation exists in Provider (`ProviderContext.ObserveBackgroundTask`) and in Client (its own
`BackgroundTaskObserver`). Consolidation waits for a shared neutral home, and for a fourth consumer in either
family.

### PROVIDER-18 - Document-identity redesign
**P3 · feature · trigger-gated**

Document identity is the normalized absolute local path plus the platform case policy. Virtual/untitled/remote
documents and case-sensitive volumes on case-insensitive hosts are unrepresentable. Trigger: the first host
that needs one of those identities, or a case-policy incident. The work is family-level (Client + Provider +
language packages), and the new identities come as new members and types beside the local-path identity, so a
document identified by its local path keeps that identity and its behavior.

### PROVIDER-19 - Provider feature gaps
**P3 · feature**

Server diagnostics for never-opened documents are dropped. The missing `textDocument/didSave` is the client gap in
CLIENT-18. Revisit when a provider needs them (the Lua provider documents the current boundary). Delivering
diagnostics for a never-opened document changes what a host receives, so it is an opt-in option of
`LanguageServerFeatureProviderOptions` whose default keeps dropping them.

### PROVIDER-27 - Opt-in debounce of the per-change semantic-token refresh and diagnostics pull
**P2 · perf**

`SemanticTokensSource` and `DiagnosticsSource` each start one server round-trip
per synchronized keystroke, and the next keystroke cancels a request still in flight. On a 1,642-line Lua file
LuaLS answers a full semantic-token request in about 100 ms, so at a 100 ms typing gap up to 15 of 20 requests
are computed and thrown away, and at 30 ms almost all of them; Roslyn's diagnostics pull on an 825-line file
behaves the same. Add an opt-in trailing debounce of the two refresh triggers, not of `didChange`, as a
`LanguageServerFeatureProviderOptions` member whose default keeps today's immediate refresh. The measurement
suggests about 150 ms.

Done when the option exists, a test shows a burst under the option starting one request per feature, and the
default sends one per keystroke as today.

### PROVIDER-29 - Bound the diagnostics fan-out allocations
**P4 · perf**

`PerDocumentRequestCoordinator.RunForOpenDocumentsAsync`, which the semantic-token and diagnostics fan-outs
share, allocates a `Task[]` plus a `SemaphoreSlim` per call; `Parallel.ForEachAsync` would remove both.

Done when the fan-out uses `Parallel.ForEachAsync` and the provider suites pass.

### PROVIDER-35 - Store-wide idle-document counter for `TrimIdleDocuments`
**P4 · perf**

Lets the trim early-out in O(1) on the idle count instead of enumerating every tracked document on each request release when the total
exceeds the cap.

Done when `TrimIdleDocuments` reads an idle count instead of enumerating every document, covered by a test.

### PROVIDER-36 - Limit the snapshot SHA-256 fingerprint to recovery captures
**P4 · perf**

`FileSnapshotEntry.ContentFingerprint` holds the first eight bytes of a SHA-256 hash, and the snapshot delta
compares whole entries, so the fingerprint is part of every comparison
(`FileSnapshotWalker.ComputeFileContentFingerprint`). Move the fingerprint out of the per-change delta, which then
compares timestamp and length and falls back to them when no fingerprint is present. This is an accuracy and cost
trade-off: files over 16 KiB are hashed from an 8 KiB head sample and an 8 KiB tail sample (`FingerprintSampleLength`), so
a same-length rewrite that changes only the middle of such a file can go unseen within one file-system timestamp tick.

A timestamp-and-length comparison is sound only when the captured and applied entries stay comparable. An
entry the apply path stored without a fingerprint compares unequal to a later full capture of unchanged
content, which reports a spurious `didChangeWatchedFiles`. The delta must therefore keep the two kinds
comparable, either by reusing verified hashes or by giving each path a capture generation
alongside its fingerprint.

Done when the delta compares timestamp and length for an entry without a fingerprint, covered by a test.

### PROVIDER-37 - One Roslyn provider for a mixed C# and Visual Basic workspace
**P3 · feature**

The Roslyn language server serves C# and Visual Basic in one process, yet each provider takes a profile with one
`LanguageId`, so a workspace with both languages starts two servers. A per-document language id on the profile
would let one provider serve both `RoslynLanguage` values from one session. The additive form is an optional
init-only property on the profile that overrides `LanguageId` per document and defaults to the profile's single
language, so `required LanguageId` stays as it is.

Done when one provider opens a C# document and a Visual Basic document in one server process, covered by a test.

### PROVIDER-38 - A document-scoped custom-request route for language packages
**P3 · feature · trigger-gated**

The document-request pipeline of the provider (`IDocumentRequestPipeline` with its
`DocumentRequestDescriptor`) is internal, so a language package cannot send a server-specific document
request, such as a vendor extension of its language server, through the provider. It gets the feature members
the provider implements and nothing else. Expose a document-scoped custom-request route to language packages when
one needs a server-specific request, designed with the per-feature providers rather than beside them.

Done when a language package sends such a request through the route, covered by a test.

### PROVIDER-43 - Signal-based absence proofs and a concurrent-delivery fake
**P3 · test**

The framework suite in `Provider.Tests` proves an absence (no restart, no notification, no event) by waiting
`TestPolling.AbsenceWindow`, which cannot observe an absence without a bound. Convert those waits to
signal-based observation once the provider exposes a quiescence seam (a document-scheduler or fake-server
seam), which does not exist yet; the watcher, semantic-token and disposal absence checks need a
completion-signal design first. The fakes raise their events synchronously on the calling thread, so a test
cannot observe coalesced delivery at the provider layer; a concurrent-delivery fake would make it provable.

Done when the watcher, semantic-token and disposal absence tests observe a signal instead of
`TestPolling.AbsenceWindow`, and a concurrent-delivery fake proves coalesced delivery at the provider layer.

### PROVIDER-48 - The resolve flow and the priority ranking of the completion slice
**P3 · refactor · trigger-gated**

`CompletionProvider` is the only provider that sends a second request: `ResolveCompletionItemAsync` and its
tracked-snapshot check are most of the file and use `RequestRunner` and `ProviderContext`, while the other providers use
the pipeline only. `CompletionResponseParser` also ranks priorities (`ComputePriorityRanks`, `GetPrioritySortKey`).

Move the resolve flow to a collaborator when a second resolve-like request appears, and the ranking to its own type
when the parser next grows.

Done when the provider only answers the completion request and the parser only parses.

### PROVIDER-50 - One home for the connection readiness rule
**P3 · refactor**

`ProviderLifecycleCoordinator.IsConnectionReady` and the private `IsStartReady` both state that the provider's startup
flag is set and the client is ready. They read the two in opposite orders, and `IsConnectionReady` also checks disposal
and a missing client, so a change to the rule has to be made in both places (K3).

Write `IsConnectionReady` over the member that the start path reads, and settle the order of the two reads once.

Done when one member states the readiness rule, a test pins the order of the reads or shows that the order is free, and
the coordinator tests pass unchanged.

### PROVIDER-51 - A distinct outcome for a document change committed locally
**P3 · refactor**

`DocumentSynchronizer` reports `DocumentMirrorOutcome.Failed` for a change it committed to the tracked state while the
server was unavailable, so `OpenDocumentAsync` and the request path react to "not skipped" instead of naming the case.
Add a `CommittedLocally` member and let each caller state its reaction to it.

Done when a local commit reports its own outcome, each caller handles it by name, and a test pins the outcome of a change
made while the provider is unavailable.

### PROVIDER-52 - Direct suites for the attempt runner and the location normalizer
**P3 · test**

`RequestAttemptRunner` and `RequestedDocumentNormalizer` are reached only through `RequestRunnerTests` and the feature
provider tests, so their own rules have no test that names them: the rejection codes, a `JsonException`, an
`ObjectDisposedException` while the provider is disposing against one while it is live, the `Once` and `Repeatable`
policies, and each normalizer overload against a request path.

Done when each type has a suite in the mirrored test folder that pins those rules.

### PROVIDER-54 - One meaning for "slot" in the provider docs
**P4 · docs**

The provider comments and `docs/LanguageServerInternals.md` use "slot" in about 85 places for at least four things: a
document's operation lane, a node of that lane (which `OperationLaneTracker` calls a node), a registration of
`LatestUpdateTracker`, a `PayloadSlot`, and a channel position of `DrainWorker`. Read each sentence and give each concept
one word, keeping "slot" for at most one of them.

Done when "slot" names one concept across the provider package and its guide.

## Nickelony.LanguageServer.Lua

### LUA-1 - Workspace-edit resource operations
**P3 · feature**

`WorkspaceEdit` (Nickelony.IDEKit.LanguageFeatures) represents text edits only, so a rename edit that carries
file create, rename or delete operations is dropped whole. `docs/LuaGuide.md` lists the limitation, and code
actions drop only the affected action. If a host needs resource operations end to end, extend the edit model
with new members beside the text edits, and return such an edit only to a host that opts in, because a host
that applies only text edits would otherwise miss the file operations; without the option the edit is still
dropped.

Done when a rename whose server edit creates, renames or deletes a file returns that operation to a host that
opts in, and is still dropped for one that does not, covered by a Lua test.

### LUA-2 - Library-authored UI copy
**P3 · feature · trigger-gated**

The default startup-failure messages of the provider and the `Unknown Lua diagnostic.` fallback are
English strings authored by the library and surfaced through its events and diagnostics. Carry structured
failure data (or host-supplied copy) when a host needs to localize or restyle them.

## Nickelony.LanguageServer.Roslyn

### ROSLYN-1 - Interpret the client-side code-action commands
**P3 · feature**

The Roslyn language server offers actions whose command is `roslyn.client.nestedCodeAction` or
`roslyn.client.fixAllCodeAction`. The server does not execute those commands: their arguments carry the nested actions
and the fix-all flavors that the client is to interpret, and `executeCommandProvider` does not list them. The provider
omits such an action, so the "Suppress or Configure issues" menu and the fix-all actions do not appear in a host.
Interpret both in the Roslyn package: expand a nested action into a submenu of the actions it names (each resolved
through `codeAction/resolve` with the nested path), and offer a fix-all action by the scope the user picks. Nested
actions need a menu model with children, which `CodeAction` does not have, so add the children as an init-only property
and a presenter that shows them. A host built without the children cannot present such an action, so the provider
offers the nested and fix-all actions only when a new init-only option asks for them, and omits them by default.

Done when the Roslyn package offers the nested actions of an issue and a fix-all action with the option set and omits
them without it, and applying one changes the documents, covered by a Roslyn test against a scripted client.

## Nickelony.LanguageServer.Provider.Abstractions

### ABS-2 - Formatting options extension
**P3 · feature**

`FormattingOptions` carries the same options as the wire payload (`TabSize`, `InsertSpaces`). Adding LSP's
`trimTrailingWhitespace`/`insertFinalNewline`/`trimFinalNewlines` needs the Client payload and a server that
consumes them. They come as new init-only properties of `FormattingOptions` and `FormattingOptionsPayload`,
not as positional parameters, because a new positional parameter breaks a compiled caller.

### ABS-3 - Rename validation seam
**P3 · feature**

The contract returns `null` for an invalid replacement name and documents blank names as invalid. A
prepare-rename step (validate and fetch the placeholder before the host prompts) is worth adding only when a
host UX requires it, as an `IRenameProvider` member with a default implementation or on a sibling interface,
so an existing provider compiles unchanged.

### ABS-5 - Capability flag for completion
**P3 · feature**

The `LanguageServerFeatureProviderCapabilities` snapshot carries no completion flag (the client's
`SupportsCompletionResolve` covers only resolve), so a host cannot pre-disable completion UI, and an
unsupported call costs a round trip and returns the fallback. Add a completion flag to the snapshot if a host
needs it, as a new init-only property. The always-attempt policy itself is intentional and stays.

### ABS-6 - Measure the full-text-per-request hand-off
**P3 · perf · measurement-gated**

Every request carries the authoritative document text. Measure the copy cost for large documents before
considering any snapshot/version alternative. The request records keep `string DocumentText`, so a snapshot
alternative would be a new init-only property beside it.

Done when a measurement of the copy cost on a large document decides whether a snapshot alternative is needed.

## Nickelony.IDEKit.Core

### CORE-1 - `IncrementalLineStateCache` span-based transition delegate
**P3 · perf · blocked**

Every transitioned line materializes a `string` for the `Func<string, TState, TState>` delegate. A one-off
measurement, not reproducible in the repository (CORE-5 would make it so), found that a full scan allocates
~2.8 B per char (~1.4x the document's UTF-16 size) in per-line substrings: 11.4 MB and 5.5-6.7 ms over 100,000
lines. An offset-only walk allocates nothing. A constructor overload that takes a `ReadOnlySpan<char>`-based
transition delegate, beside the string one that stays, would remove the allocations. It is blocked on CORE-7:
it needs an `ITextSnapshot` span/text accessor, and the AvalonEdit `TextDocumentSnapshot` cannot answer one
without materializing the whole document.

Done when a span-delegate constructor overload exists beside the unchanged string one, and a test shows that a
full scan through it allocates no per-line string.

### CORE-2 - Stateful backward search for `FindReplaceOperations`
**P3 · perf**

`FindPreviousMatch` rescans the document prefix and materializes matches on every call. Hosts that navigate matches in reverse repeatedly
(Tomb's find/replace view) should cache one `FindAllMatches` enumeration or consume a stateful search helper. Revisit with the next
find/replace feature.

### CORE-3 - `RegexCache` eviction policy or removal
**P3 · perf · measurement-gated**

Insertion-ordered (FIFO) eviction is deterministic and lock-free on hits, but a burst of distinct patterns can
evict a working set that is then re-parsed. An LRU or segmented policy needs a measured pattern-churn report
first; the cache currently stays at or below 64 entries. If the cache becomes a maintenance burden, add a
caller-supplied regex factory seam and delete the cache.

### CORE-5 - Benchmark harness for the Core hot paths
**P3 · perf/test**

The allocation fixes are covered by behavioral tests and identity assertions, but there is no benchmark
evidence for typing-path budgets. A small BenchmarkDotNet project (whitespace conversion, resolver, edit
preparation, line diff) would make future hot-path claims measurable. It also takes over the complexity probes
in `tests/IDEKit/Nickelony.IDEKit.Core.Tests/TestSupport/TestMeasurements.cs`.

Done when the listed hot paths have benchmarks and no complexity probe remains in `TestMeasurements.cs`.

### CORE-6 - Comment-spacing policy
**P3 · feature · trigger-gated**

`LineCommentPlanner` fixes the "+1 space / remove one space" convention. Add an `insertSpace`-style option (VS Code parity) only when a
host needs non-default spacing; a model-only knob would be unproduced surface today.

### CORE-7 - Snapshot span access + resolver typing path
**P3 · perf · measurement-gated**

A span accessor on `ITextSnapshot` and a clamped caret threaded through the auto-closing resolver are the
hot-path items. `TextDocumentSnapshot` cannot answer a span without materializing the whole document text, so
the change needs a profile (and a per-implementation strategy) before promotion; it would otherwise regress
first-keystroke cost on large files. The accessor takes a default implementation over `GetText`, or goes on a
sibling interface a snapshot opts into, so an existing implementer compiles unchanged.

### CORE-10 - LSP and Roslyn conversion helpers
**P3 · feature**

A host that adopts Core next to an LSP or Roslyn model converts its positions, ranges and edits by hand. Add
the conversion helpers as new types, so the existing Core types keep their members.

Done when a host converts an LSP or Roslyn position, range and text edit to the Core types through the
helpers, covered by a test.

### CORE-12 - `IdentifierSpanMode.Nearest`
**P3 · feature**

`IdentifierSpanMode` has `Containing`, `AtOffset` and `EndingAtOffset` only. A host that needs the identifier
nearest an offset that touches none has no mode for it. The enum's documentation does not say it can grow, so
a `Nearest` member would break a host's exhaustive `switch`; the lookup is a new `IdentifierOperations` method
instead.

Done when the nearest-identifier method exists and a test covers an offset that touches no identifier.

### CORE-15 - Comment-scanner dialect gaps
**P3 · feature · trigger-gated**

Interpolated-string holes are not modeled and the fixed-three dialects (Java text blocks, Kotlin, Swift) are approximations; both are
documented. Revisit only if a host needs tokenizer-accurate scanning (the docs point such hosts at their own tokenizer).

### CORE-16 - Bookmark sidecar cross-process coordination
**P3 · feature · trigger-gated**

Two processes saving one sidecar file are not coordinated. A save that finds the destination held open without
sharing fails, and otherwise the last save wins. A lock file would coordinate editing across processes; defer
it until a host asks for it. A lock file changes what a save writes and when it fails, so it is an opt-in
`BookmarkSidecarStore` option whose default keeps today's saves.

### CORE-18 - Core `ReadOnlySpan<char>` identifier overload
**P3 · perf · trigger-gated**

Add the span overload when the host expansion loop grows or profiling justifies it.

### CORE-24 - Dedicated-thread `IUiDispatcher`
**P3 · feature**

A console host or a test host that drives a library controller or `ProjectDiagnosticsSource` writes its own `IUiDispatcher` over a
background thread: a queue, a thread-affinity check and a timer that posts to the queue. The engine adapters cover the editors only.
Add one public implementation to `Nickelony.IDEKit.Core.Threading` that owns a thread, runs the posted work and the scheduled callbacks on
it, and stops with `Dispose`. Additive: no contract changes.

Done when a test posts, schedules and cancels a schedule on it, a call from another thread fails `VerifyAccess`, and disposal stops the
thread and rejects later posts the way the contract allows.

### CORE-25 - Go to line: negative lines and the `,` and `#` separators
**P3 · feature**

The go to line of VS Code counts a negative line from the end of the document (`-1` is the last line) and accepts `12,5` and `12#5` beside
`12:5`. `LineColumnQuery.TryParse` reads digits and the colon only, so each of those texts lists the hint instead of a position, and
`WorkspaceFileQuickPickProvider` splits `file.cs:12` on the colon alone. `TryParse` documents a sign as no position and `Line` as at
least zero, so it stays as it is: add a parse entry beside it whose result carries the from-end flag as an init-only property, and let the
go to line and quick-open providers call it. Keep the clamps: a negative line past the start clamps to the first line.

Done when the new parse entry reads `-3` as the third line from the end, `12,5` and `12#5` as line 12, column 5, `TryParse` still answers
`false` for `-3`, a test pins the clamps of each form, and the go to line mode previews each form in a sample drive.

### CORE-26 - An "open to the side" accept
**P3 · feature**

The quick open of VS Code opens a result beside the current editor with Ctrl+Enter. `QuickPickAcceptRequest` carries the item and the typed
text, and the controller and the presenter know one way to accept, so a host cannot tell the two apart. Add an accept flag that the presenter
sets from Ctrl+Enter and the request carries to the provider, and let the files provider pass it to the open callback. Additive.

Done when a presenter test in each binding presses Ctrl+Enter and the provider sees the flag, the sample opens the file in its second group,
and a P12 drive does it in both sample heads.

## Nickelony.IDEKit.Workspace, Workspace.Views and Workspace.QuickPick

### WS-2 - Windows/macOS alias canonicalization
**P3 · feature**

8.3 short names, trailing dots or spaces, and NFC/NFD spellings still produce a second tracked document for one file. Documented as
spelling-based identity with host responsibility for canonicalization; an optional alias normalization profile would ride a future
path-policy seam.

### WS-4 - Async local file-system I/O
**P3 · perf · watch**

`LocalWorkspaceFileSystem` uses synchronous `System.IO` probes (`File.Exists`, `Directory.Exists`,
`File.Move`) inside task-returning members; on slow network shares this can block pool threads. Revisit when a
host targets remote shares. `File.Move` has no async counterpart, so the pass moves the blocking calls off the
caller's thread inside the same members, and the seam keeps its signatures and results.

### WS-5 - Real permission-denied integration test
**P3 · test**

Unit coverage simulates `UnauthorizedAccessException` on the read paths and descendant captures. A Windows ACL-based integration test
(deny-read directory) would validate the `AccessDenied` mapping against a real file system; environment-dependent, so it lands with a CI job
that can arrange denied access.

Done when a CI job arranges a denied directory and the `AccessDenied` mapping test passes there.

### WS-8 - Save-as validation order
**P3 · perf**

`SaveAsAsync` captures and hashes the source (and the destination for a second path) before
`WriteReplacementAsync` encodes the content, so unencodable content pays two whole-file hashes before
`InvalidEncoding`. Reorder only together with an encode-once refactor of the write path; the current order is
pinned by `SaveAs_UnencodableContent_ReportsInvalidEncoding`.

### WS-11 - Store-level document defaults
**P3 · feature**

An options value carrying the default open options and new-file format would spare editor hosts from threading encoding and line terminator policy
through every call; per-call values remain overrides.

### WS-17 - `IWorkspaceFileSystem.CreateDirectoryAsync` seam
**P3 · feature · trigger-gated**

The commit path requires the destination directory to exist (host responsibility, documented). A
create-directory member would let a headless non-local file system provision roots through the seam; add when
the first such consumer appears. The member takes a default implementation on `IWorkspaceFileSystem`, or goes
on a sibling interface a file system opts into, so an existing implementer compiles unchanged.

### WS-18 - Workspace enumeration API
**P3 · feature**

`IWorkspaceDocumentReader.GetSnapshotsUnderDirectory` enumerates documents only under one directory. Add a
member that returns every tracked document when a host flow needs it (close-all, multi-volume workspaces). The
member takes a default implementation on `IWorkspaceDocumentReader`, or goes on a sibling interface, so an
existing implementer compiles unchanged.

### WS-23 - View-issue reason vocabulary
**P3 · decision**

A `WorkspaceDocumentViewSynchronizationIssue` carries the failure a view reported, but a view blocked purely
by state (pending edits or a conflict) reports no failure, so a host cannot distinguish the two blocking
reasons without probing the view itself. A reason vocabulary would touch every reporting site, and the host
owns the views. It would be a new member of the issue beside `Failure`, which keeps its meaning.

### WS-24 - Per-view dispatch affinity
**P3 · feature**

The coordinator takes one `IUiDispatcher`, so a multi-window host whose views live on several UI threads must run one
coordinator per store and thread (the constructor remarks document that the dispatcher hosts every thread-affinity
decision and that one coordinator coordinates one store). An additive overload that asks a dispatcher per view can be
considered if such a host adopts the package.

### WS-26 - Remaining peer-copy allocations
**P4 · perf**

`ViewTracker.GetPeers` still copies for each phase of delete, directory delete and directory rename (blocking
check, guard capture, sweep, report). Capture once per operation if a hot host profile shows it.

### WS-27 - View enumeration API
**P3 · feature**

There is no way to ask the coordinator which views are attached to a document (`GetAttachedViews(documentId)`
/ `IsViewRegistered(view)`); only the internal `ViewTracker` answers it. Add it when a multi-window host
needs it, as `IWorkspaceViewCoordinator` members with default implementations or on a sibling interface, so an
existing implementer compiles unchanged.

### WS-29 - Async-capable view contract
**P3 · feature**

Add a `Task`-returning member set, or an `IWorkspaceDocumentViewAsync` sibling interface, so a host whose editor applies snapshots
asynchronously need not block inside a dispatched action. Additive.

### WS-30 - Cheaper file stamps
**P3 · perf**

Introduce an mtime+size fast path for `FileStamp` with the SHA-256 hash as the disambiguator. No
public-surface change; the hash is retained because it detects a rewrite that keeps the previous last-write
time. A hash-free mode with documented weaker conflict detection would also spare hosts a decorator, but it
needs an answer for `ReplacementStateUnknown` classification when the content hash is absent.

### WS-36 - `WorkspaceViewCoordinator` view-id reuse
**P4 · perf**

Reuse the registration state so a `ViewIdReader.Read` call does not re-compose the view id; requires threading
registration state through `ViewTracker.GetPeers`, `GetPeersByDocumentKey` and `GetRegisteredViews`, plus
`ViewSnapshotPair` and the failure collectors.

### WS-47 - Move the attached-document queue out of `ViewTracker`
**P3 · refactor**

`ViewTracker` tracks the views by instance, view id and document, and it also keeps the attached documents and queues their changes,
because a registration and the change it causes must be ordered under one lock. Move the attached-document entries and the change queue to
an internal collaborator that the tracker calls under the same lock, so each type does one job and the queue order stays the operation order.

Done when `ViewTracker` holds no attached-document state, and the attached-document tests pass unchanged.

### WS-48 - Release a tracked document
**P2 · feature**

`IWorkspaceDocumentStore` opens a document on demand and has no member that stops tracking it, so a document that
`WorkspaceEditApplier` opened for an edit, because no view showed it, stays tracked with its text in memory until the store
is disposed. A host that applies many edits grows the store with every document they touched. Add a release that stops
tracking a document no view is attached to, and that refuses a document with unsaved changes through its outcome. A host
can implement the interface, so the release is a sibling interface that `WorkspaceDocumentStore` implements, and the
applier releases only through a store that implements it.

Done when a document an apply opened can be released, a release of an attached or a dirty document is refused with its
own outcome, and each case has a test.

### WS-49 - Commit the unattached documents an apply changed
**P3 · feature**

An edit that `WorkspaceEditApplier` applies to a document no view shows leaves the change in the store and nowhere a user
sees it, so every host saves those documents itself, as the sample's `SampleDocuments.ApplyEditAsync` does. Add an
option of `WorkspaceEditApplierOptions` that commits each unattached document the apply changed, and a member of
`WorkspaceEditApplyResult` that lists the documents it committed, apart from the ones it changed.

Done when the option commits only the unattached documents the apply changed, the result names them, a failed commit is
reported without undoing the other replacements, and each case has a test.

### WS-50 - Find in files
**P3 · feature**

A Find in Files panel needs a workspace search: a literal or regular-expression query with case and whole-word
options, include and exclude globs, the unsaved text of open documents in place of the disk, results streamed per
file with the line, the character and a preview, a bound on the files and the matches, cancellation, and a replace
that applies through the view coordinator as one change per file (reviewable through AGENT-2 when that exists).
Agents bring their own search, so this serves the user's panel.

Done when a test searches a folder with an unsaved open document, an excluded folder and a binary file, streams the
results, cancels a long search, and replaces across two files, and a P12 drive searches the sample folder in both
sample heads.

### WS-51 - Honor `.gitignore` in the quick-open file list
**P3 · feature**

The file list leaves out the folders and files that `ExcludeGlobs` names and nothing else, so a folder that a repository ignores, such as
`node_modules`, lists unless the host repeats its `.gitignore` as globs. Read the `.gitignore` files of the roots, the nested ones included,
with their negation and directory-only patterns, behind an option that is off by default, and apply them in the walk and in the watcher
reports. Additive.

Done when a test with a root `.gitignore`, a nested one, a negation and a directory-only pattern keeps the ignored files out of the list
after the walk and after a watcher report, and the option off lists them.

### WS-52 - A host-supplied editor history for the recently opened files
**P3 · feature**

The `recently opened` group of the files mode holds the files the user accepted in the mode and the open documents. The quick open of VS Code
also lists the files the user opened another way. Add a hook to the options that returns the paths the host opened recently, from the
oldest to the newest, and merge them into the group in the order of use. Additive.

Done when a test with a hook that returns two paths lists them in the group after the accepted files in the order of use, drops a deleted
one, and lists nothing extra when the hook is `null`.

### WS-53 - A logger for the file watcher of the quick-open file list
**P3 · diagnosability**

The tracker creates its `FileWatcher` without a logger, so an overflow, a dropped event or a restart of a watcher leaves only the fault that
reaches `reportFault`. Take an `ILoggerFactory` as an init-only property of `WorkspaceFileQuickPickOptions`, hand it to each watcher, and
keep `reportFault` for the failures the provider contains itself.

Done when a test with a recording logger sees the watcher start and the failure of a watcher that cannot start, and a provider without a
logger behaves as before.

### WS-54 - The quick-open file list over `IWorkspaceFileSystem`
**P3 · feature**

The file list reads the local disk through `Directory.Enumerate` and a `FileWatcher`, while the document store reads through
`IWorkspaceFileSystem`. A host with a virtual or remote file system lists nothing. `IWorkspaceFileSystem` has no enumeration or watch
member and a host implements it, and `Nickelony.IDEKit.Workspace.QuickPick` may not reference the Workspace package. So define
enumeration and watching as interfaces in a package both can take, such as Core, which a host's `IWorkspaceFileSystem` implementation
also implements. Let the provider take one through an init-only property of `WorkspaceFileQuickPickOptions`, use it for the
enumeration, the existence checks and the watcher, and keep the local disk as the default.

Done when a test lists, follows and opens files of an in-memory `IWorkspaceFileSystem`, and the default behavior is unchanged.

### WS-55 - One watch supervisor for the quick-open file list
**P3 · refactor**

`WorkspaceRootWatchSupervisor` starts a watcher per root, replaces one that failed and reconciles by walking the root again,
which is the job of the public `FileWatchSupervisor`, with a different retry rule. Build the file list on
`FileWatchSupervisor` with a reconcile that walks the root, or state on the type why the list needs its own retry on the
next query.

Done when the file list uses `FileWatchSupervisor` or the type states the reason, and the quick-pick tests pass.

### WS-56 - An editor-backed document view in each binding
**P3 · feature**

Every host writes the same `TextEditor`-backed subclass of `WorkspaceDocumentTextViewBase`: `IsReadOnly`, `Text`,
`ReplaceContent`, and `TextChanged` forwarded to `HandleTextChanged`, as the samples' `SampleDocumentView` does. Ship
it once per binding, which needs a binding package that references `Workspace.Views`; the tier rules of the architecture
guide decide whether that is an optional adapter package or a folder of the binding.

Done when both bindings ship the view under the tier rules, the samples use it, and the shared editor test tree pins a
typed edit, a replacement and the read-only state.

### WS-59 - A publication test that cannot fail
**P3 · test**

`WorkspaceDocumentTextViewBaseTests.PositivePublishDelay_ATickWithNothingPendingOrAfterDisposal_PublishesNothing` stays green
when the `!_isDisposed` guard of `WorkspaceDocumentTextViewBase` is removed and when the fake dispatcher ignores
`RunsCanceledCallbacks`, because nothing is pending to publish in either case. Give the tick after disposal a pending change,
so the guard is the only thing that stops the publication.

Done when the test fails with the guard removed.

### WS-60 - Pin the symlink policy of writes and moves
**P3 · test**

`docs/WorkspaceGuide.md` says that writes and moves do not follow symlinks, and no test in `Nickelony.IDEKit.Workspace.Tests`
creates one. Add tests that write through and move a path that is a symlink, for a file and a directory link, and skip them
where the process may not create a link.

Done when a test fails if a write or a move follows the link.

## Nickelony.IDEKit.AvalonEdit family

### EDITOR-1 - Code-action document text copy
**P2 · perf**

`CodeActionSelection` carries a document snapshot, so a settled request whose builder reads no text copies
nothing. A builder that builds a `CodeActionRequest` reads the text with `GetText`, which copies the whole
document once per settled request because `CodeActionRequest.DocumentText` is a string. A one-off measurement
on a real `TextDocument`, not reproducible in the repository, puts the copy at ~2x the UTF-16 size (24.4 MB
and 7.0 ms over 100,000 lines). Negligible below ~1 MB. `CodeActionRequest` keeps `string DocumentText`; a
snapshot member is a new init-only property beside it, added once a measurement on a large document shows the
copy matters or a host reports large-file typing latency.

Done when a measurement decides the snapshot member, and a builder that sets it copies no document text,
covered by a test.

### EDITOR-5 - Language/tokenization context for auto-closing
**P4 · feature · trigger-gated**

The resolver's unmatched-closer scan and the auto-close-before gates read raw text, so language-specific exclusions (strings, comments) and
the language-configuration `notIn`/`onEnterRules` concepts are missing. Add a host-supplied context predicate together with the first host
that needs scope-aware auto-closing.

### EDITOR-7 - `UnsavedChangesTracker` document-sized diff reads
**P4 · perf · watch**

The line view updates incrementally, but each finished document update still runs `LineDiffer` over the whole line list once
(~2.8 ms average for 20k-line random-edit probes, and about 150 ms for a 4,000-line batch of indentation edits). Re-open if
large-document typing shows marker latency; a dirty-region diff is the eventual shape.

### EDITOR-8 - Eager bookmark-anchor release on document swap
**P4 · bug · trigger-gated**

Stale anchors keep the previous document alive until the next mutation; reads stay side-effect free by design and `IsBoundToCurrentDocument`
guards persistence. An explicit rebind or a swap notification lands with the first host that swaps documents without mutating bookmarks.

### EDITOR-9 - Comment feature set
**P3 · feature · trigger-gated**

Block-comment toggling (including the line-comment fallback), VS Code's minimum-column comment alignment with the all-blank force-add case,
and comment continuation on Enter are absent. They belong to the Core planner plus a sibling service; add with the first host that needs
them.

### EDITOR-10 - Enter-action indentation model
**P3 · feature · trigger-gated**

`IIndentationPolicy` returns only a replacement indentation string, so outdent/indent-outdent/append-text
enter rules are not representable. Add a richer on-enter result when a language profile needs on-enter actions
beyond indentation, as a new interface member with a default implementation over the indentation string, or on
a sibling interface a policy opts into, so the existing member and its implementers stay unchanged.

### EDITOR-12 - Auto-closing strategy shape
**P3 · feature**

A per-kind `autoSurround` strategy would align the Core auto-closing options with VS Code. It lands as a new
init-only option on `AutoClosingOptions` whose default keeps today's wrapping through
`AutoClosingPair.WrapSelection`, and the overtype default stays as it is. It waits for a host that needs
VS Code parity.

Done when the surround option selects per pair kind whether typing an opening token wraps a selection, and its
default keeps today's behavior, covered by a test.

### EDITOR-13 - Asynchronous bookmark persistence
**P4 · feature**

`IBookmarkStore` is synchronous-only, so sidecar I/O runs on the calling thread. Add `Task`-based members only
with a host that saves off the UI thread, with default implementations over the synchronous members or on a
sibling interface a store opts into, so an existing store compiles unchanged.

### EDITOR-16 - Indexed tracked-closing-text lookup
**P4 · perf · watch**

`AutoClosingInsertionTracker` (used by `AutoClosingService`) records one anchor per inserted closing
text. A probe tests the newest entry first, and otherwise retires every entry whose text is gone from its
anchor and scans the rest newest-first. Revisit with an offset index or aged pruning only if a document with
tens of thousands of tracked pairs ever shows keystroke latency.

### EDITOR-19 - Semantic-token model revision
**P3 · feature · trigger-gated**

`SemanticTokensColorizer` anchors a set to the document when it is applied, so a set that a source computed for
text older than the document paints at its offsets in the newer text until the next set. It also clips
multi-line tokens to their start line and needs `Rebuild()` after theme changes. A set that carries the document
version it was computed for would let the colorizer map it through the edits since, as VS Code's
`ModelSemanticColoring` does, and paint-time style resolution behind an opt-in option would make the rebuild
unnecessary while `Rebuild()` stays callable. The additive form of the version is a sibling interface of
`ISemanticTokensSource` that a source implements to name the version, plus a new event-args type or an optional
init-only property on `SemanticTokensUpdatedEventArgs`; the colorizer reads the sibling with a type test. Adopt when a source can name the version of its tokens, or when a
multi-line-token or theme-switch complaint arrives.

### EDITOR-129 - A completion scope tracker from a comment syntax
**P3 · feature**

`CompletionControllerHooks.ScopeAt` answers the `CompletionScopes` at the caret, and every host with typing triggers
writes the same tracker for it: the sample's `CodeScopeTracker` keeps the open comment or string at each line start in a
`DocumentLineStateCache` and asks `CommentOperations.GetCodeEnd` about the line before the caret. Ship that tracker in the
binding, built from a `CommentSyntax` over a `TextDocument`, so a host passes its `ScopeAt` as the hook.

Done when both bindings ship the tracker, the sample uses it in place of its own, and a test pins a comment, a string and
a block comment that opened on an earlier line.

### EDITOR-131 - A column-preserving caret mapping for indentation conversion
**P4 · feature**

`ConvertIndentationToSpaces` and `ConvertIndentationToTabs` leave the caret and the selection to the engine's anchors, so a caret
inside a replaced indentation lands behind the new indentation instead of on the same visual column. The extensions document that
mapping, so it stays the default: add an overload that takes the caret mapping as an option, and let the column mapping place a position
inside the indentation by its column, clamped to the new width, with a rule for a position inside a tab.

Done when a caret in the indentation of a converted line keeps its visual column through the new overload, the existing calls map as
before, and a test in both bindings covers each.

### EDITOR-132 - A chosen line terminator for a text without one
**P4 · feature**

The AvalonEdit and AvaloniaEdit engines insert the terminator of the caret's line for Enter and for a pasted line break, or
`Environment.NewLine` when the document has no line terminator at all, so `ConvertLineTerminators` cannot steer the first new line
of an empty or one-line text. A host that records another style for such a text, for example LF on Windows, sees the engine insert
the platform's terminator, and the view then publishes the style of the text. Add a binding call that makes the editor use a given
style for the line breaks it inserts into a text with no terminator, by replacing the inserted terminator inside the undo step of
the Enter or the paste, and state how long it lasts with the editor.

Done when Enter and a paste in an empty document insert the chosen terminator in both bindings as one undo step, covered by a test
in the shared editor test tree.

### EDITOR-133 - An overview ruler beside the vertical scrollbar
**P3 · feature**

A host sees diagnostics, unsaved changes, bookmarks and search hits only for the lines on screen. Add an overview ruler that
draws a mark for each marked line over the height of the whole document and a band for the viewport, and scrolls the editor on
a click or a drag through `EditingSeam.SetScrollOffset`. The ruler reads its marks from `ILineStatusSource`, so the change and
bookmark trackers feed it unchanged, and diagnostics and find results each need an adapter onto that contract. The line to
pixel mapping comes from the engine's `TextView` (the visual top of a document line over the document height), so folding and
word wrap need no rule of their own. Keep the mapping and the mark merge engine-free in a shared body, with a thin drawing half
per binding, as the line-status margins are built. Neither engine has a right-hand margin collection, so the item decides
between a control the host places beside the editor and a template that puts it next to the scrollbar, and states the choice.

Done when both bindings ship the ruler with a mark color per source, the samples show it, and a test in the shared editor
test tree pins the line to pixel mapping with a folded region and the scroll a click produces.

### EDITOR-134 - A code minimap on the overview ruler
**P4 · feature**

VS Code's minimap draws the document in miniature so a reader can spot its shape and jump to a region. Add it as an option of
the overview ruler of EDITOR-133: an engine-free model turns each line into colored runs, and each binding paints them into
a cached `WriteableBitmap` and repaints only the lines an edit or a retokenization touches. The TextMate colorizer colors only
the visible lines, so the colors come from the tokens of the session's internal TextMate model for every line, and a document
without a grammar paints its text in one color. State the cost bound on the option: the paint is incremental, and a document
past a stated line count paints uncolored blocks.

Done when both bindings paint the minimap with TextMate colors, an edit repaints only the lines it touched, and a test pins
the run model and the incremental repaint.

### EDITOR-135 - A find controller and a find bar
**P3 · feature**

`Core.FindReplace` matches a `SearchQuery` over a string, and everything above that is host code: the samples'
`SampleSearch` writes next and previous with wrap-around, the match highlights, the "n of m" counter and replace-current
by hand. Its replace-all swaps the whole text, which drops anchors and bookmarks and leaves a coarse undo step.

- **Controller.** Add an engine-free find controller that holds the query, the scope (the document or the selection), the
  current match and the counter, and does next and previous with wrap, replace-current, and replace-all as one change of
  one edit per match (C15). It reuses `RegexCache`, so typing does not recompile the pattern.
- **Bindings.** Each binding gets a renderer that highlights every match and marks the current one, and a skinnable find
  bar with one body under `shared/Editor/`. The item decides between that bar and a skin of the engines' own
  `SearchPanel`, which ignores the query model and the theme, and states the choice.
- **Matching.** Find matches exactly, as a replace must change what was matched, so it takes no fuzzy matcher.

Done when both bindings ship the bar and the highlights, `SampleSearch` is wiring only, and tests pin the wrap, the
selection scope and a replace-all that keeps an anchor and undoes in one step.

### EDITOR-130 - A semantic-tokens controller factory
**P3 · feature**

`DiagnosticsControllerFactory` installs the renderer, subscribes to the source for one document and retires on a document
swap, while a host that colors semantic tokens wires `SemanticTokensColorizer`, the line transformer and
`SemanticTokensSourceExtensions.Subscribe` by hand, as the sample's `SampleSemanticTokens` does. Add a factory that creates
that wiring for one text area and one document id, under the one-controller-per-document rule of the language-features
guide.

Done when both bindings ship the factory, the sample uses it, and a test pins the subscription, the disposal and the
document swap.

### EDITOR-24 - In-place code-action menu refresh
**P3 · feature**

A newer publish closes an open menu (newest-evaluation-wins). Closing is the safe default and stays the
default. Re-opening the menu in place with refreshed items, preserving the anchor and the user's position, is
a UX refinement behind an opt-in option on `CodeActionMenuOptions`, to consider only if a host reports the
close as disruptive.

Done when the opt-in option refreshes an open menu in place, and a menu without it still closes on a newer
publish, covered by a test.

### EDITOR-26 - Replaceable code-action menu
**P3 · feature**

`CodeActionPresenter` opens its actions in a `ContextMenu`. A host with another popup surface can add the
presenter's `Margin`, set `OpensOnLeftClick` to `false` and never call `TryOpenMenu`, but the margin then
reports no click: the host hit-tests the margin to learn which line was pressed and draws its own popup from
`Actions` (the editor language-features guide documents `Actions`). When a second consumer needs a different
popup, add a menu interface that the presenter takes through a new option, with today's `ContextMenu` as the
default, and a new margin event that reports the pressed indicator line.

### EDITOR-28 - Wide rendered content is clipped
**P3 · design**

The viewer disables horizontal scrolling by design (the plain-text fallback relies on it to honor the theme
width), so a table wider than the theme limit - or an unbreakable run WPF cannot wrap - is clipped with no way
to scroll to it. Evaluate a strategy (table column caps, cell-level wrapping, or a horizontal scrollbar)
before hosts render arbitrary wide content. Each changes what a host sees, so the chosen one is an opt-in
`MarkdownRenderOptions` member whose default keeps today's clipping.

### EDITOR-29 - Optional document cache
**P4 · perf**

Every entry point re-parses and re-builds the document. Hosts that re-render identical content could benefit from a small bounded cache
keyed by content identity plus theme and options identity. Measure first.

### EDITOR-30 - Interactive-content keyboard verification
**P4 · test**

The suite pins that `AllowContentInteraction` makes the viewer selectable/focusable and openable hyperlinks focusable; actual Enter
activation and tab order come from WPF's hyperlink behavior and should be verified visually when a document-style host adopts the option.

Done when Enter activation and tab order are verified on a document-style host.

### EDITOR-33 - Keyboard access for embedded code blocks
**P4 · feature**

Embedded blocks are never focusable, so keyboard copy is unavailable; the host presents its own editor through `CreateCodeBlockEditor`. Add
an opt-in only with a concrete host requirement.

### EDITOR-34 - Highlighting-name scan
**P4 · perf**

`FindDefinitionByName` linear-scans the registered definitions for definitions AvalonEdit's case-sensitive name lookup misses. Negligible
per code block; add a cache only if a host renders many unresolved-language blocks and measures the cost.

### EDITOR-35 - Line-height fallback probe
**P4 · test**

`GetEditorLineHeight` falls back when `TextView.DefaultLineHeight` is not usable: on WPF it measures a
`FormattedText`, and AvaloniaEdit uses a font-size ratio. The fallback is defensive and has no test because
the engine always reports a positive height. Verify or simplify when a measurement-related issue next
surfaces.

Done when a test covers the fallback, or the fallback is removed.

### EDITOR-37 - Per-block rendering resilience
**P3 · bug**

`MarkdownRenderer.CreateContent` and `CreateContentTree` catch a rendering failure for the whole document in
the shared render pipeline and replace it with plain text, so one unsupported or throwing node costs the
entire tooltip. When a host reports content that degrades, add per-block rendering with a per-block fallback
as an opt-in `MarkdownRenderOptions` member whose default keeps the whole-document fallback, or report a
structured failure through a new member; `CreateContent` and `CreateContentTree` keep their results.

Done when a host that opts in keeps the blocks around a throwing node, and a host that does not still gets the
plain-text fallback, covered by a test.

### EDITOR-38 - Inline-code selection fidelity
**P3 · bug**

Inline code renders as an `InlineUIContainer` (a chip), so it sits outside the document's text range: with
`AllowContentInteraction` enabled, select-all and copy omit inline-code text and the selection skips over the
chip. The alternative (a styled `Run`, as Markdig.Wpf uses) trades the chip for selectable text, which changes
what a host sees, so it is an opt-in `MarkdownRenderOptions` member whose default keeps the chip; revisit when
an interaction-enabled host reports the gap.

Done when the opt-in member renders inline code as selectable text that copy includes, and the default still
renders the chip, covered by a test.

### EDITOR-39 - Code-block pre-measure cost
**P4 · perf**

`MarkdownCodeBlockFactory.CreateCodeBlockElement` lays out the editor's text view at creation to pin the
clamped height, and a single extremely long wrapped line is fully formatted by that measure. Measure lazily,
keeping the clamped height a host sees, if a host feeds pathological single-line code; a cap on the measured
height changes that height, so it would be an opt-in option whose default keeps today's measure.

### EDITOR-41 - Page-scroll wheel mode coverage
**P4 · test**

`MarkdownScrollChaining.WheelStep.FromDelta` classification is unit-tested, but real page-mode scrolling depends on the machine's
`SystemParameters.WheelScrollLines = -1` setting and cannot be automated here. Re-verify manually before shipping a wheel-behavior change.

### EDITOR-44 - Snapshot through a document snapshot plus line ranges
**P3 · perf**

The line list keeps a full copy of every line text so the tokenizer thread never reads the UI-thread document; the established
AvaloniaEdit.TextMate integration stores per-line ranges and reads text through `TextDocument.CreateSnapshot()`, avoiding the extra full
copy at the cost of per-read allocations. Worth revisiting only if a host reports snapshot memory pressure on very large documents.

### EDITOR-47 - Viewport-priority tokenization
**P3 · perf**

Coloring follows the model's background pass in document order, so a region scrolled into view can stay base-styled until the pass reaches
it. Prioritizing the viewport needs a scheduler change in TextMateSharp (`ForceTokenization` must not race the tokenizer thread); revisit if
a host reports visible latency on large freshly opened documents.

### EDITOR-48 - One derived-typeface cache for the TextMate and semantic-token colorizers
**P3 · refactor**

`TextMateTypefaceCache` (the TextMate binding packages) and `DerivedTypefaceCache` (the LanguageFeatures binding packages) are two internal
copies of one cache of the typeface that a style's bold and italic settings derive from a base typeface, because a shipped package reads no
other package's internals. Make it one public type next to `TextRunStyleApplier` in `Nickelony.IDEKit.AvalonEdit.Rendering` and
`Nickelony.IDEKit.AvaloniaEdit.Rendering`, with a direct test, and delete both copies.

Done when one cache serves both colorizers and neither internal copy remains.

### EDITOR-49 - Paint-path closure allocations
**P3 · perf · measurement-gated**

`TextMateColorizer.ColorizeLine` allocates one `Action<VisualLineElement>` closure per token per
repaint (`ChangeLinePart(..., element => ApplyStyle(element, style))`). AvalonEdit's own transformers use the
same pattern, so replace it with a reusable applier per resolved style only if profiling shows paint cost on
pathological lines.

### EDITOR-55 - TextMate selector operators
**P4 · feature · trigger-gated**

Support the remaining selector operators (exclusion, wildcard, priority, grouping) when a real `.tmTheme`
needs them. A selector that uses one is reported and dropped today, so matching it changes how an existing
theme styles text; the support is an opt-in option of the resolver, taken through a new
`TextMateScopeResolver` constructor overload, and the existing constructor keeps dropping such selectors.

### EDITOR-56 - `ITextMateStyleResolver` seam
**P4 · feature · trigger-gated**

Add the seam when computed-style overlays or a second resolver implementation appears.

### EDITOR-57 - `IHighlightingDefinitionResolver` seam
**P3 · feature · trigger-gated**

Add the TextMate highlighting-registry seam when two isolated editor configurations need different registries in one process.

### EDITOR-58 - Configurable Markdown link affordances
**P3 · feature**

`MarkdownRenderOptions` should expose the underline, cursor, and focusability of a rendered link (only its
color is themable today), defaulting to today's values. `MarkdownFlowDocumentEmitter` hardcodes
`TextDecorations.Underline` and `Cursors.Hand`/`Cursors.Arrow`, and derives `Focusable` from
`AllowContentInteraction` alone.

Done when the link underline, cursor and focusability are `MarkdownRenderOptions` members, with today's values
as the defaults.

### EDITOR-59 - Enable Markdown task lists and footnotes
**P3 · feature**

The narrow Markdig pipeline renders task-list items and footnotes as literal text. Enabling the extensions
changes what a host sees, so each is an opt-in `MarkdownRenderOptions` member whose default keeps today's
literal text.

Done when a Markdown test shows task-list items and footnotes rendered with the options set, and as literal
text without them.

### EDITOR-60 - Lighter Markdown code-block presenter
**P4 · perf · watch**

`MarkdownCodeBlockFactory` instantiates a full AvalonEdit `TextEditor` per code block inside a non-virtualized
`BlockUIContainer`; consider lazy materialization, which keeps the editor a host receives, or a lighter
presenter, which changes it and is therefore an opt-in option whose default keeps the editor.

### EDITOR-62 - Diagnostics underline geometry options
**P3 · feature**

`DiagnosticsRenderer` fixes the squiggle shape and the severity-to-shape mapping; expose geometry/amplitude
(or a per-severity shape delegate) as new options whose defaults draw today's shapes, while keeping the pens
themable.

### EDITOR-64 - Single palette seam for sample visuals
**P3 · refactor**

`BookmarkMargin`, `ChangeMarkerMargin`, and `DiagnosticsRenderer` ship sample brushes as defaults; add one documented palette seam so a
theme host overrides once.

Done when one documented palette type overrides the sample brushes of each visual that uses them.

### EDITOR-67 - Expose Markdown block-layout geometry as theme members
**P3 · feature**

`MarkdownLayout` (the block measurements both emitters read) and `MarkdownCodeBlockFactory` hard-code the
corner radius, border, quote-bar thickness, list indent, and table/inline-code padding; surface the most
opinionated ones on `MarkdownRenderTheme`, which both bindings share, so a host can restyle the content
without forking the emitter.

### EDITOR-68 - Non-activating completion popup via a WPF `Popup`/`ToolTip` host
**P3 · refactor**

`CompletionWindowInterop` hooks `WM_MOUSEACTIVATE` and the controller manually routes list clicks; a
`Popup`-hosted (non-activating) list would remove both the interop and the click handler. It would also
replace the `CompletionWindow` that the public `CompletionPresenterHooks.ConfigureWindow` hands to a host, so
it is an opt-in presenter beside today's window, which stays the default.

Done when a host that opts into the `Popup` presenter gets a non-activating list without the interop, and a
host that does not still receives the `CompletionWindow` through `ConfigureWindow`.

### EDITOR-69 - TextMate typeface cache key robustness
**P4 · perf · watch**

`TextMateTypefaceCache` keys on the base typeface and the bold and italic flags. Key on the `(FontFamily,
Style, Weight, Stretch)` tuple if AvalonEdit ever returns fresh instances per paint (add a regression assert).

### EDITOR-72 - Completion window sizing by real measurement
**P2 · refactor**

The default values of `CompletionWindowOptions` (the horizontal chrome, the maximum height, and the icon and
detail widths) size the completion window. Measuring the real item containers (or size-to-content) would let
template, font and DPI changes adapt without a constant change, but it would replace the public
`CompletionWindowOptions` defaults, so it is an opt-in sizing mode beside today's. The current sizing
(`CompletionWindowSizing.MeasureRequiredWidth`) is a template-profile policy with replacement seams, and it
stays the default.

Done when a host that selects the measured mode gets a window sized from its item containers, and the default
sizing is unchanged, covered by a test.

### EDITOR-77 - `BookmarkTracker` O(n^2) restore + per-toggle allocation
**P3 · perf**

Replace the per-line linear `FindBookmarkAnchor` with a single pass/sorted set, and drop the per-toggle `HashSet<int>` allocation in
`PruneInvalidAnchors`.

### EDITOR-79 - `DocumentLineStateCache` incremental line map
**P3 · perf · watch**

Avoid re-splitting snapshot text by maintaining an incremental line map (as `UnsavedChangesTracker` does). The materialization cost (a
snapshot line read forces a full text copy once per recomputing burst) is documented on the type and pinned with a deletion-burst test, but
the incremental line map stays open here.

### EDITOR-81 - Remaining test-coverage residuals
**P4 · test hygiene**

Still unexercised: the scroll-bar press (needs a templated editor) and the off-thread affinity guards of the
members of `CompletionPresenter`.

### EDITOR-83 - Markdown link accessibility
**P3 · feature**

`MarkdownLinkElement` is a plain `TextBlock` with no automation peer, so screen readers see text, not a link;
the WPF sibling uses a `Hyperlink`, which exposes a link role. Add an automation role/peer, or document the
limitation.

Done when `MarkdownLinkElement` exposes a link automation role, covered by a test.

### EDITOR-89 - Unify the two TextMate cache eviction policies
**P4 · refactor**

`TextMateTypefaceCache` clears wholesale at its 64-entry bound, while `TextMateScopeResolver` trims to a
low-water mark under a 4096-entry bound. Unify the two, or document why they differ. The resolver's trim
evicts in dictionary enumeration order, so the evicted set is not deterministic; make eviction deterministic
only if a consumer needs it. The bounds become configurable only when a consumer needs configurable limits.

### EDITOR-97 - Code-block editors in the tab order
**P4 · feature**

The AvaloniaEdit viewer sets `IsTabStop` from `AllowContentInteraction`
(`src/IDEKit/AvaloniaEdit/Nickelony.IDEKit.AvaloniaEdit.Markdown/MarkdownRenderer.cs`), but the code-block editors
never take a tab stop. `MarkdownCodeBlockFactory` (`shared/Editor.Markdown/MarkdownCodeBlockFactory.Common.cs`) sets
`IsTabStop = false` on the editor and its text area. Decide whether a host needs code-block editors in the tab
order; if it does, add an option on `MarkdownRenderOptions`.

### EDITOR-103 - Avoid the per-probe `TextSegment` allocation in `AutoClosingService.IsRangeEditable`
**P3 · perf**

A `TextSegment` is allocated per probe; reuse one or probe without materializing a segment.

### EDITOR-110 - Re-enable the completion tooltip when AvaloniaEdit exposes it
**P3 · feature · trigger-gated**

The Avalonia binding builds no tooltip pipeline, because AvaloniaEdit's completion window publishes no tooltip member; the window shows an item's
`Description` in its own tooltip, so an Avalonia host sees a Markdown documentation as raw text. When the engine
exposes one, add to the Avalonia binding what the WPF binding declares for it, so that the Markdown renders there too: `CompletionTooltipPresenter`, `CompletionTooltipTheme`,
`CompletionTooltipResolver`, the `TooltipTheme` option, the `ConfigureTooltip`, `ResolveDescriptionAsync` and `BuildMarkdownContent` hooks, and
`CompletionPresenter.IsTooltipSupported`, and drop their rows from the parity allow-list. The presenter shows a Markdown documentation of the
default adapter as the element that `BuildMarkdownContent` builds, as the WPF presenter does, and it does so in the selection change that
follows the engine's own handler, so the raw Markdown is never drawn. Without a `ResolveDescriptionAsync` hook it also resolves the selected
item of the default adapter through the item's own resolve callback, and it builds the element once per documentation. The `CompletionPresenter` engine halves already
route every tooltip call through partial methods that the Avalonia half leaves empty.

AvalonEdit has the same constraint for its own tooltip, which is shared through the
`CompletionTooltipPresenter` remarks. When AvalonEdit exposes its tooltip publicly or upstream a public
details member, add a single-owner design in which the presenter owns the content, as an opt-in option:
`Description` keeps its value unless a host opts in, and only then is it `null` while a resolver is
configured.

Done when the Avalonia binding shows a completion tooltip through the members above, a Markdown documentation
renders as the built element in it, and the opt-in single-owner mode leaves `Description` unchanged for a
host that does not select it, covered in both bindings.

### EDITOR-114 - Cover the WPF press handler with a hit position
**P4 · test**

`LineStatusIconMarginBase.OnMouseLeftButtonDown` takes the click position from `e.GetPosition(this)`, and a
synthetic `MouseButtonEventArgs` reports the physical cursor instead of a chosen point, so no WPF test presses
a hosted margin over a line: the handler's `Handled` result is untested when a line resolves, both when the
click action handles the press and when it does not, while the click path itself is covered through
`TryHandleClickAt`. The AvaloniaEdit suites cover the handler because the event carries the position. Move
the real cursor over a topmost host window and send the press, without a test hook in product code.

Done when a WPF test presses the left button over a line of a hosted margin and asserts that the event is
handled, and that a press the click action declines stays unhandled.

### EDITOR-128 - A signature trigger that opened no popup
**P3 · bug · watch**

In a sample drive against LuaLS, the first `(` typed after a completion list had been open in a session opened no
signature popup in both heads, while every later `(` did. A cold session that typed `greet(` first opened the popup,
the sample's output logged no failure, and the server's trigger list was already set, because completion had
answered its own trigger characters. The remaining suspects are the server's first answer and a show that the
inactive-window rule refused while the completion window held the foreground.

Done when a drive reproduces it with the provider's log and the presenter's refusals recorded, and the cause is
fixed or documented.

### EDITOR-138 - Group the binding `Completion/` folder as Controllers does
**P4 · refactor**

The `Completion/` folder of each language-features binding holds about twenty files flat, while `LanguageFeatures.Controllers`
keeps its commit and snippet files in the `Commit/` and `Snippets/` subfolders of its `Completion/` folder. Mirror those
subfolders as internal-only folders of the bindings.

Done when the binding's commit and snippet files sit in the subfolders that Controllers names.

### EDITOR-139 - A hover popup the pointer can enter, and a pointer anchor
**P3 · feature**

The hover popup closes when the pointer leaves the text area and does not track the pointer over itself, and
`HoverPresenter` anchors it below the line of the hover range, not at the pointer. A host whose hovers hold links or
text to select must replace the presenter. Add init-only members to `HoverPresentationOptions`, one that keeps the popup
open while the pointer is over it and one that anchors it at the pointer, both defaulting to today's behavior. A drive of
both sample heads decides first whether moving the pointer onto the popup counts as leaving the text area.

Done when a drive of both sample heads moves the pointer from a hover target onto the popup and reads it, the popup
closes once the pointer leaves both, and a test pins each default.

## Nickelony.IDEKit.LanguageFeatures and LanguageFeatures.Controllers

### INTEL-1 - Snippet parser linearization
**P3 · perf**

`SnippetExpander` has a quadratic worst case: a run of unterminated `${` before a trailing `}` rescans the
suffix (`AppendExpanded`, `FindConstructEnd`, `AppendUnclosable`), and sparse same-range close braces need a
memoized "no `}` to the right" boundary. Fold the scans into one stack-based scan-and-emit pass, keeping
`Expand`'s signature and behavior and the documented LSP snippet subset.

Done when a benchmark or test with an adversarial unterminated-`${` input shows the expander's cost grows
linearly with the input length.

### INTEL-3 - Snippet-expander pluggability
**P3 · feature**

`SnippetExpander` implements the VS Code/LSP snippet dialect as a static, opt-in helper; a host with
another placeholder syntax does not call it. Add an `ISnippetExpander` seam, or a delegate, when a second
dialect or an adopter asks for one. The change is additive, and the built-in expander stays the default.

### INTEL-4 - `DocumentSymbolOutlineBuilder` factory simplification
**P3 · feature**

The bundle offers optional `RangeSelector`/`SelectionRangeSelector`/`DetailSelector`, and `Build` has a flat and a
grouped overload; the remaining gap is a `Func<TItem, DocumentSymbol>` factory overload for deeper shaping (nesting
beyond the one grouping level) and the delegate-reference equality of the bundle. Introduce it when a host needs that
shaping.

### INTEL-5 - Unset-state API for `InsertText`/`FilterText`
**P3 · feature**

The getters keep the label fallback and the merge tracks "never set" internally. Add `HasInsertText`/`HasFilterText` when a consumer needs
to distinguish unset from a label fallback.

Done when `HasInsertText` and `HasFilterText` exist and a test covers an unset value.

### INTEL-7 - Collection snapshot allocation policy
**P3 · perf · measurement-gated**

`CollectionSnapshots.Capture` keeps the defensive `Array.AsReadOnly([..])` wrapper (two allocations per
populated collection) while helpers return caller-owned lists. Revisit with a measurement on the
semantic-token hot path.

### INTEL-9 - `SemanticToken` modifier allocation
**P3 · perf · measurement-gated**

`CaptureModifiers` builds a `List<string>`, dedups it with `List.Contains`, and wraps it in a
`ReadOnlyCollection<string>` per modifiered token. Revisit with a semantic-token hot-path measurement; the
read-only contract must stay intact.

### INTEL-10 - Outline builder child snapshot ownership
**P3 · perf**

Grouped outlines materialize group children twice (the builder fills a `List` and `DocumentSymbol` copies it
into a read-only snapshot). An internal ownership-taking path would remove one array copy per group.

### INTEL-12 - `CompletionItemKind.FromIdentifier` allocation
**P3 · perf · measurement-gated**

An unknown identifier allocates a fresh instance per call; the equality-based design makes this harmless, but a parse-heavy provider could
intern non-well-known results if profiling justifies it.

### INTEL-13 - Snippet grammar consolidation
**P3 · refactor**

The expander is a deliberately bounded, tolerant subset, pinned by corpus tests. Reusing an established parser
(or extending the subset to mirrors and variable resolution) lands only when a host needs more than the
documented grammar. A construct outside the subset expands literally today, so a wider grammar is an opt-in
overload of `Expand` whose existing form keeps the documented subset.

### INTEL-19 - Snapshot-typed completion request overload
**P3 · perf**

`CompletionSessionKernel.GetDirectiveAsync` already takes an `ITextSnapshot`, but it builds a
`CompletionRequest` whose `DocumentText` is a string, so every call runs `snapshot.GetText(0, TextLength)`. A
string-backed snapshot returns its own string, while an editor-backed snapshot such as `TextDocumentSnapshot`
copies the document once per keystroke. Add a `CompletionRequest` constructor overload that accepts an
`ITextSnapshot`, as EDITOR-1 does for code actions, so those hosts avoid the document-sized copy. The record
keeps `string DocumentText` and its string constructor, and the snapshot is a new init-only property beside
them.

Done when a snapshot-typed `CompletionRequest` overload exists and the string overload is unchanged.

### INTEL-20 - `CompletionContext.FromIdentifier`
**P3 · feature**

Add `CompletionContext.FromIdentifier`, the parse counterpart of `CompletionItemKind.FromIdentifier`, as a new
static member.

Done when a test parses each known trigger name through `FromIdentifier` and gets the same trigger back.

### INTEL-21 - Optional fuzzy completion scoring
**P3 · feature**

`CompletionFilter.FilterByWord` keeps an item only when its `FilterText` contains the typed word as a
case-insensitive substring, so a server that ranks by fuzzy match, such as LuaLS, loses the items that hold the word
only as a subsequence. A host can pass its own `CompletionItemFilter` to the kernel, but the editor list may filter the
same items again against `ICompletionData.Text`, and no public option reaches that second filter. Keep the substring
filter as the default, add `CompletionFilter.FilterByFuzzyWord` over `Nickelony.Matching`, which the Controllers
package already references, and state the pairing in the language-features guide. Read the engines' list filter
first: if it drops the items the kernel kept, add an init-only window option that leaves the narrowing to the kernel.

Done when a test shows the fuzzy filter keeping a subsequence match that the default drops, a binding test shows the
window keeping that item, and the guide shows the pair.

### INTEL-23 - `CompletionItem` ordering helpers
**P3 · feature**

Add optional comparer helpers that derive ordering from `SortText`, so a host cannot let `Priority` drift from it. Additive.

Done when a comparer derives ordering from `SortText`, covered by a test.

### INTEL-27 - Versioned workspace edits
**P3 · feature**

`TextDocumentEdit` carries no document version, and the provider's `WorkspaceEditParser` drops the
`textDocument.version` of each `documentChanges` entry, so typing that reaches the store between the server request
and the apply goes undetected: `WorkspaceEditApplier` plans the server's line and character edits against the newer
text. Carry the version the server edited through `TextDocumentEdit`, map it to the store version the request
synchronized, and report a moved version as a plan issue. The additive form is an optional init-only `Version` on
`TextDocumentEdit` and an opt-in property on `WorkspaceEditApplierOptions` that turns the check on, so a host that
does not set it sees today's behavior; the plan issue reuses `WorkspaceEditPlanIssueKind.DocumentChanged`, which
avoids a new member of that closed enum.

Done when a rename whose document changed after the request is a plan issue in a test over the provider.

### INTEL-28 - Snippet placeholders through `AdjustInsertion`
**P3 · feature**

A host whose `CompletionItemAdapterHooks.AdjustInsertion` changes the text of a snippet gets a plain-text commit and
no tabstop navigation, because the snippet path runs only while the adjusted text equals the expansion's text:
the placeholder ranges describe the text the hook received (the Snippet commits section of the
language-features guide). A host that indents the continuation lines of a multi-line snippet there loses the
tabstops of every such snippet. Let a `CompletionInsertion` carry the placeholders remapped to the adjusted text,
so a host that rewrites the text keeps the navigation. Additive: an insertion without placeholders keeps the
current rule.

Done when an `AdjustInsertion` that indents a multi-line snippet and returns the remapped placeholders commits a
snippet whose tabstops Tab steps through, covered by a test in both bindings.

### INTEL-29 - `TryRetarget` over an explicit word span
**P3 · feature**

`CompletionSessionKernel.TryRetarget` finds the word at the caret now with the kernel's configured `CompletionWordLocator`, so a host that
asks through `GetDirectiveForWordSpanAsync` with spans of its own cannot say where the word is now. Add an overload that takes the current
`CompletionWordSpan`, validated like the explicit span of `GetDirectiveForWordSpanAsync`: it lies within the snapshot and contains the
caret. Additive: the existing overload stays.

Done when a test retargets a decision with an explicit span in place of the locator, and a test shows that a span which misses the caret throws.

### INTEL-30 - Wait for the last run of `ProjectDiagnosticsSource`
**P3 · feature**

`ProjectDiagnosticsSource.Dispose` stops the pending and the running analysis at once, but it does not wait for the call of the host
hook that is running: the call ends when the hook returns. A host that releases what the hook reads (a compiler, a file handle) right
after `Dispose` cannot tell when the last call is over. Add `DisposeAsync` or a task that completes when the last call returned, on top of
the task the source already keeps for the run in flight. Additive: `Dispose` keeps its behavior.

Done when a test disposes a source whose hook ignores its token and holds, shows that the new member stays pending, and shows that it
completes once the hook returns.

### INTEL-31 - Inline completions
**P3 · feature**

A suggestion shown as faded text after the caret, accepted with Tab and dismissed with Esc, is the main AI editing
feature, and nothing renders one. Add an engine-free `IInlineCompletionProvider` (a request with the document text,
the caret and a trigger kind; items with an insert text and a range), the LSP `textDocument/inlineCompletion`
request in the client and the provider, a controller that asks after a typing pause, cancels on the next keystroke
and drops an answer for text that has moved on, and a presenter per binding that draws the text without changing the
document, accepts it in one undo step (Tab, or Ctrl+Right for one word), and steps between alternatives. An open
completion window takes Tab first. The additive form of the provider side is a sibling interface that
`LanguageServerFeatureProvider` also implements, read with a type test, and not a new base of
`ILanguageServerFeatureProvider`.

Done when the controller is tested without UI (debounce, supersession, a stale answer dropped), the presenter is
tested in both bindings (draw, accept, accept a word, dismiss, undo), a fake server answers
`textDocument/inlineCompletion`, and a P12 drive shows a suggestion from a scripted provider in both sample heads.

### INTEL-32 - Symbol modes: the symbol at the caret and streamed workspace results
**P3 · feature**

The `@` mode of VS Code starts with the symbol that contains the caret active, and `DocumentSymbolQuickPickProvider` starts with the first
symbol active. The server answers `workspace/symbol` in its response alone, because the request carries no partial-result token, so a large
workspace shows nothing until the list is complete. Let a provider name the key of the item that starts active, through a default
interface member of `IQuickPickProvider` that names none, and let the controller honor it for the first answer of a session. Handle
`$/progress` of partial workspace symbols in the client, and let a provider report the symbols that arrived so far to the quick pick,
which lists them before the final answer, through a sibling interface of `IWorkspaceSymbolProvider` that a provider opts into.

Done when a test starts `@` on the symbol that contains the caret, a fake server streams partial results that list before the final answer,
and a P12 drive shows both in both sample heads.

### INTEL-33 - Tags and the deprecated flag of workspace symbols
**P3 · feature**

The client capability for `workspace/symbol` advertises no `tagSupport`, `WorkspaceSymbol` carries no tags, and the quick pick draws no
deprecated symbol struck through, as VS Code does. Advertise `tagSupport` with `SymbolTag.Deprecated`, read the tags and the `deprecated`
flag of `SymbolInformation` into a `bool IsDeprecated` of `WorkspaceSymbol`, and strike the label through in both quick-pick windows through
a flag of `QuickPickItem`. Additive.

Done when a test lists a deprecated symbol from a captured Roslyn answer for a method that carries `[Obsolete]`, the item carries the flag,
and the window of each binding draws the label struck through.

### INTEL-34 - An engine-free merge of the workspace symbols of several providers
**P3 · feature**

`WorkspaceSymbolQuickPickProvider` asks the providers in parallel, drops the duplicates, merges in provider order and contains a failing
provider, and a consumer without a quick pick, such as the agent tool of AGENT-3, would repeat that. Move the asking, the merge, the
duplicate rule and the containment into an engine-free type below the quick pick in `Nickelony.IDEKit.LanguageFeatures` that implements
`IWorkspaceSymbolProvider` over a list of providers, and let the quick-pick provider call it. Additive.

Done when the tests of the merge, the duplicate rule, the order and the containment run against that type without a quick pick, and the
quick-pick tests keep passing over it.

### INTEL-35 - Workspace symbols while a server loads its projects
**P3 · feature**

A workspace search starts a stopped server and waits for its initialization, but Roslyn answers `workspace/symbol`
with an empty list until it has loaded its projects, so the first search shows "No matching workspace symbols" for
several seconds and finds the symbol only after another keystroke. Show the quick pick's busy state while the server
reports work-done progress for its load, and ask again when that progress ends. Additive.

Done when a fake server that reports load progress and answers empty until it ends makes the `#` mode show busy, then
the symbols without another keystroke, and a drive against Roslyn shows the same.

### INTEL-36 - Plan snippet placeholders once
**P4 · refactor**

`CompletionSnippetBuilder` decides which placeholders are separate and which occurrence is each index's target, and these
engine-free decisions compile in shared binding source once per engine. Move them into a planner under
`Controllers.Completion.Snippets`, so a binding only emits the engine's snippet elements from the plan.

Done when the planner is tested once in `LanguageFeatures.Controllers.Tests` and the builder only emits elements.

### INTEL-37 - Check the word at the caret when a scheduled word request runs
**P2 · bug**

A letter typed in code arms a request in `CompletionController.HandleCharacterTyped`, and the debounce callback runs
`RequestAsync()` without asking `ScopeAt` again; the request reads the caret when it runs. A character that resolves to
`None`, such as a space or the first character of a comment opener, cancels nothing, so a letter followed by a space
within `RequestDebounceDelay` requests at the caret after the space, with an empty word, and opens an unfiltered list.
Make the request that `HandleCharacterTyped` schedules check, when it runs, that `ScopeAt(caret)` is still one of the
policy's `WordScopes` and that a non-empty word ends at the caret, and request nothing otherwise. Canceling on `None`
is no fix, because a digit inside an identifier resolves to `None` too. The public `ScheduleRequest` keeps requesting at
the caret, since a host that calls it asked for that. The change removes requests that only a typing race produces.

Done when a test sees no request after a letter and a space within the debounce, none after a letter and a character
after which `ScopeAt` answers a comment, and one request for the grown word after a letter and a digit.

### INTEL-38 - Close the completion window when the provider answers with no items
**P3 · feature**

When the provider returns no items, `CompletionSessionKernel` returns `CompletionSessionDirective.None`, and
`CompletionPresenter.ApplyDirective` leaves the window as it is, as the controller documents. A window that an earlier
answer opened keeps its old items when a later request at another position, such as after a trigger character, finds
nothing, and a commit then inserts an item that does not apply there. Add an init-only
`CompletionControllerOptions.ClosesOnEmptyAnswer`, default `false`, that dismisses the presenter for an accepted
decision that is `None`; a failure, a cancellation, a superseded answer and `NoMatches` still leave the window.

Done when a controller test with the option set dismisses the presenter for an empty answer and leaves it in the other
four cases, and the default behaves as before.

### INTEL-39 - Replace or drop the additional edits of a completion item
**P3 · feature**

`CompletionItem.WithoutTextEdit()` clears `TextEdit` and keeps `AdditionalTextEdits`, and the class is sealed with
init-only members and an internal copy constructor, so no public member drops or replaces the additional edits. A host
that rebases an open item onto newer text therefore still commits additional edits whose ranges were computed for the
older text, atomically with the insertion. Add `CompletionItem.WithAdditionalTextEdits(IReadOnlyList<CompletionTextEdit>)`,
which returns a copy with the given edits, an empty list dropping them, and name it in the language-features guide.

Done when a test replaces and drops the edits of an item and keeps every other field, and the guide names the call.

### INTEL-40 - Choose which trigger characters of a provider open completion
**P3 · feature**

`CompletionTriggerResolver` treats every one-character entry of `ICompletionTriggerCharacters.TriggerCharacters` as a
trigger, and `LanguageServerFeatureProvider` answers the server's negotiated list unchanged. A server that announces a
space, a parenthesis or a quote, as LuaLS does, makes the default policy request completion after each of them, and the
only remedy is a wrapper provider that forwards every other member. Add an init-only
`CompletionTriggerPolicy.AcceptsTriggerCharacter`, a `Func<char, bool>` that accepts every character by default.

Done when a test with a predicate that rejects the space and the parenthesis requests for a dot only, and the default
requests for every announced character.

## Nickelony.KeyBindings

### KEYS-3 - Runtime reload of externally edited overrides
**P3 · feature**

The rebuild is private, so a host that watches its settings document must reconstruct the service (losing `BindingsChanged` subscribers) or
replay `ResetAll`-plus-`Apply`. Add a `Reload()` that re-reads `IKeyBindingOverrideStore.Load()`, validates, publishes atomically and
raises the event.

Done when `Reload()` re-reads the store, publishes atomically and raises `BindingsChanged`, covered by a test.

### KEYS-4 - Additive binding helpers
**P3 · feature**

`Apply` replaces the whole binding set, so "add one shortcut" and "remove one shortcut" are read-modify-write in the host. Add
`AddBinding`/`RemoveBinding` helpers (or a diff-accepting overload) once a remap UI needs them.

### KEYS-5 - WPF keystroke capture helper
**P3 · feature**

VS Code-style "define keybinding" capture (ignore modifier-only/repeat/IME, Escape cancels, live draft chord) is host UI. Provide a tested
WPF helper or a documented recipe when the first remap surface is built.

### KEYS-40 - A chord-cancel helper with the idle timer the guide prescribes
**P3 · feature**

The key-bindings guide has a host wire `CancelPendingChord` to Escape, to focus loss and to an idle timer of one to two
seconds, and the sample wires Escape and focus loss but no timer. Every host writes the same three triggers. Add a helper
in each adapter that attaches the three to a control and a dispatcher chain, with the idle delay as an option, or align
the guide with what the sample does.

Done when the guide and the sample agree, and, with the helper, a test pins each of the three triggers on both adapters.

### KEYS-7 - `CommandCatalog.TryGetDescriptor(string serializedId)`
**P3 · feature**

`KeyBindingService` keeps a parallel set of cataloged identifiers, and the service and the conflict detector
scan override entries against it in three operations; a serialized-identifier lookup would remove the
parallel set. Add it when a host needs the lookup as well.

### KEYS-10 - KeyBindingOutcome.Conflict owner data
**P3 · feature**

A remap surface cannot name the command that owns a colliding chord from the outcome alone. This is fixable additively (a richer `Validate`
overload returning a record, or an `out` owner parameter), so it is no breaking-change risk and waits for the remap UI.

### KEYS-12 - Host-supplied typed-text alias set
**P3 · feature**

`KeyStroke.TryParse` / `KeyChord.TryParse` accept a fixed alias set (`win`, `cmd`, `command`, `super`, `windows`, `meta`); allow a host to
add or remove aliases without a package change once a shortcut editor needs it.

### KEYS-13 - Prefix-lookup trie for very large catalogs
**P4 · perf**

A prefix probe is a frozen-set hit, but `BindingClaimRules.HasPrefixRelationship` scans every bound chord per
proposed binding; a trie would make it O(key length). Measure before changing.

### KEYS-14 - Avalonia AltGr filtering
**P3 · feature · blocked**

The Avalonia adapter offers no AltGr filter. The filter needs a check of how Avalonia reports the left Ctrl
and right Alt pair that Windows sends; that check has not been made. A filter changes which strokes reach the
dispatcher, so it is an opt-in option of the adapter whose default keeps today's strokes.

Done when the check is made and, for a host that opts in, an AltGr press dispatches no Ctrl+Alt stroke,
covered by a test.

### KEYS-17 - Allocation-free dispatch
**P4 · perf**

`KeyBindingDispatcher.Dispatch` builds a `KeyChord` for every stroke. The `KeyChord` constructor validates the
strokes for repeats in O(n^2) and copies them, and the pending-chord repeat check is an O(n) `Contains`.
Measure before changing a path this small.

### KEYS-18 - A lighter `Validate` dry run
**P4 · perf**

A proposal builds a full candidate snapshot and rebuilds the whole map set to answer a question; a structural pre-check could short-circuit
the common "the chord is free" case.

### KEYS-21 - A non-generic convenience facade (or builder)
**P3 · feature**

Adopting the generic `KeyBindingService<TCommandId>` is verbose for a host with a single command model. Add a
non-generic facade (or builder). The change is additive.

### KEYS-22 - Localized display text
**P3 · feature**

The desktop formatter is explicitly US-layout and non-localized, so a host that needs localized text supplies
its own `IKeyDisplayTextFormatter` until the core offers localization. `KeyBindingPlatformProfile` already
relabels single keys through `KeyTextOverrides` and the modifiers through its label properties, so the open
item is localizing the built-in labels. Localized labels change the text a host shows, so they come behind an
opt-in option, and the default keeps the US-layout labels.

### KEYS-24 - Benchmark the dispatcher and the publish path before optimizing
**P4 · perf**

No benchmark project measures the per-stroke allocation of `Dispatch` or the cost of a publish. Add one, so
changes such as KEYS-17 are judged on measured cost.

Done when a benchmark project measures the per-stroke allocation of `Dispatch` and the publish cost.

### KEYS-25 - Dispatcher pending-context visibility for a custom chain
**P3 · feature**

A host that wants a routing policy other than `KeyBindingDispatcherChain<TCommandId>` cannot read
`KeyBindingDispatcher<TCommandId>.PendingContext` (it is `internal`); expose it (or a chain-friendly accessor) when a host needs a different
policy.

### KEYS-29 - A `when`-expression subset (or several bindings per `(context, chord)`)
**P3 · feature**

The single active context token cannot express two commands on one chord under different independent
conditions; `docs/KeyBindingContextDesign.md` records the rejected alternatives. Additive design work.

### KEYS-30 - Opt-in no-op suppression for `Apply`
**P3 · feature**

`Apply` writes an override entry and raises `BindingsChanged` even when the proposed set equals the current bindings, so a host feeding a
binding loop thrashes the settings document; add an option/overload that skips the store call when the candidate set is unchanged.

Done when `Apply` with the option set does not raise `BindingsChanged` for an unchanged binding set, covered
by a test.

### KEYS-31 - A `TryCreate`-style service factory
**P3 · feature**

A loaded document that cannot be applied throws `InvalidOperationException` from the constructor, and the key
bindings guide tells a host to catch it and rebuild from an empty document; a factory returning the failure
detail (or the outcome) would remove the catch-and-retry recipe.

Done when a factory returns the failure detail for a document that cannot be applied, covered by a test.

### KEYS-35 - `WithDisplayText` incremental recompute
**P3 · perf**

Carry the previous snapshot's texts forward and recompute only the changed command. Measurement-gated.

### KEYS-36 - Avoid boxing `ImmutableArray<KeyChord>` in `GetBindings`
**P3 · perf**

`GetBindings` returns the published `ImmutableArray<KeyChord>` as an `IReadOnlyList<KeyChord>`, which boxes
the struct on every call. Store the list view in the published map when the map is built, so each call returns
it without a box. Measure a hot caller first.

### KEYS-41 - Print a chord as its strokes
**P4 · feature**

`KeyStroke` is a record struct with a readable `ToString`, but `KeyChord` prints its type name in logs and the debugger. Render a
chord with the neutral display formatter, as `Control+K, S`.

Done when a chord's `ToString` returns its strokes, pinned by a test.

### KEYS-43 - Place `KeyBindingPlatformProfile` by its job
**P4 · refactor**

`KeyBindingPlatformProfile` sits in `Keys/`, but all of its properties except `PrimaryStroke` are display labels. Move it to
`Display/`, or split it into a primary-modifier record and a labels record. Only the move is additive, because the
split removes members from the profile and so lands before publication.

Done when the profile, or each half of it, sits in the folder of its job.

### KEYS-44 - Chords that repeat a stroke
**P4 · feature**

VS Code binds chords such as `Ctrl+K Ctrl+K`, but `KeyChord` and `KeyBindingDispatcher.Dispatch` reject a chord that repeats a
stroke. Decide whether a host needs it; then support it or state the rule and its reason on `KeyChord`.

Done when `KeyChord` either accepts a repeated stroke, covered by a dispatch test, or documents why it refuses one.

### KEYS-45 - A shadowed always-active command keeps a chord prefix pending
**P3 · bug**

A context-scoped override can shadow a remappable always-active default of the same multi-stroke chord. The always-active
bucket of `PublishedSnapshot.CommandsByChordPrefix` still lists the shadowed command, so in that context `IsChordPrefix`
reports the first stroke as a prefix when the shadowed command can execute, while the completed chord resolves to the
override, which may not. The first stroke is then buffered and the completing stroke reports `NotHandled`. Leave out of
the always-active bucket a command whose chord the probed context shadows, or state the limit on `IsChordPrefix`.

Done when a dispatch test with a shadowing override whose command cannot execute leaves the first stroke unconsumed.

### KEYS-46 - Pin the prefix probe across buckets and overrides
**P3 · test**

`IsChordPrefix_PredicateSeesOnlyTheCommandsOfTheContextAndTheAlwaysActiveOnes` runs on a service with no always-active
chord, so no test covers a context bucket that rejects every command followed by an always-active bucket that accepts
one. No `KeyBindingMapBuilderTests` case pins the prefix buckets after an override takes a chord over or unbinds it.

Done when both cases have a test.

### KEYS-47 - Tell the sample user when `keybindings.json` is rejected
**P3 · feature**

The samples start with the default bindings when the key-binding service rejects `keybindings.json`, and log it to an
`ILogger` that neither sample host passes, so the user sees nothing, and the first rebinding overwrites the hand-edited
file. Show the rejection in the window's status line, and leave the file alone until the user rebinds on purpose.

Done when a sample window test sees the status line after a rejected file.

## Nickelony.IDEKit.FileWatching

### WATCH-1 - Lock-order inversion when a multi-spec start fails
**P3 · bug**

`FileWatchSupervisor` starts its `FileWatcher` while it holds `_watcherSyncRoot`. When that start fails part way, the
watcher disposes the native watchers it already started and waits for its notification lock, which an error thread of
one of them can hold while its failure callback waits for `_watcherSyncRoot`, so the two threads deadlock. It is a race with no deterministic
test. Starting outside the lock needs a disposed flag on the supervisor and a rule for a failure that another thread
reports during the start, which `_isStartingWorkspaceFileWatcher` absorbs today; without that rule the failure is dropped
and watching stops silently.

Done when the supervisor starts its watcher outside `_watcherSyncRoot`, and a test with a fake watcher that reports a
failure from another thread during a failing start shows no deadlock and no lost failure.

## Nickelony.IDEKit.JsonSchema

### JSON-1 - A lone surrogate escape throws out of the builder
**P3 · bug**

`JsonSchemaVocabularyIndexBuilder.Build(TextReader)` turns only a `JsonException` into a failed result. Reading a property
name or a string value whose escape is a lone surrogate, such as `"\ud800"`, throws `InvalidOperationException` from
`JsonElement`, so the exception leaves the builder instead of returning a `ParseFailed` result. A `$ref` that several
places point at a non-schema location also adds its warning once per reference.

Done when a schema with a lone surrogate escape returns a failed result, and a test pins it.

## Nickelony.IDEKit.Processes

### PROC-1 - Descendant lifetime binding
**P3 · feature**

A detached descendant can outlive a run and hold the redirected pipes (the capture is bounded; the descendant
remains). Bind the child tree to the runner with the Windows `WindowsJobObject`/`PlatformChildProcessGuard`
seam (the in-repo precedent) plus a POSIX process-group story if a host needs a guaranteed reap. A reaped
child tree changes what a run does, so the binding is an opt-in option whose default keeps today's behavior.

Done when a grandchild process started by a run with the option set ends with that run, and a run without it
is unchanged, covered by a test on Windows.

### PROC-2 - Captured-output policy
**P3 · feature**

Captured output is unbounded unless `ProcessRunnerOptions.MaxCapturedCharactersPerStream` is set, and the
default leaves it unset. The capture has no streaming or callback seam, so a host cannot read output while the
process runs. Add a streaming or callback seam when a host runs tools with unpredictable output volume. A
streaming seam changes what a run does, so it is an opt-in option whose default keeps today's capture.

Done when a host that opts in can read output while the process runs, and a run without the option captures as
before, covered by a test.

### PROC-3 - Redirected-stream thread cost
**P4 · perf · measurement-gated**

Each redirected stream parks one thread-pool thread while a quiet child runs (the runtime's synchronous pipe
redirection). `docs/ProcessesGuide.md` documents it; revisit only if a host profile shows thread-pool
starvation.

### PROC-5 - Per-run grace-period overrides
**P3 · feature**

The grace periods are fixed at runner construction. A host that mixes a fast linter with a slow compiler needs
one runner per timing policy or an override; add an optional per-call override as new init-only members of
`ProcessRunRequest` when a host actually carries more than one timing policy. A new `Run`/`RunAsync` parameter
is a binary break of `IProcessRunner`, so the override goes on the request.

Done when a per-call grace-period override exists as request members and a test covers it.

## Agent support

An agent brings its own file, search and shell tools, so these entries add only what the IDE alone can supply. Each
entry builds on the entries with lower numbers.

### AGENT-1 - Editor context snapshot
**P3 · feature**

An agent needs what the user is looking at, and a host assembles it by hand from each editor and the view
coordinator. Add an engine-free snapshot of the IDE context: the active document's path, version and unsaved state,
its selections with their text and positions, the visible line range, the open documents with their unsaved state,
and the diagnostics of the active document. A call per binding captures it from a `TextEditor` on the dispatcher
thread, and a debounced change event fires when the active document, the selection or the set of open documents
changes. It is data only, with no transport and no agent types.

Done when a test captures the snapshot in both bindings (a selection across lines, two open documents with one
unsaved, diagnostics present) and the change event fires once per settled change.

### AGENT-2 - Review a proposed or external change
**P3 · feature**

An agent either writes files on disk or proposes a change for the user to accept, and both need one review: a diff
per file between the text the user last had and the new text, shown inline in the editor (removed lines as a
read-only block, added lines highlighted), with keep and undo per hunk and per file, and an undo of everything the
change touched. Add an engine-free change set (per file: the base text, the proposed text, and the hunks that
`LineDiffer` and `TextIncrementalEditCalculator` compute), a presenter per binding that renders it on an open editor,
and a path that applies or reverts a hunk through the view coordinator in one undo step. An external write to an
open document with no unsaved changes becomes a reviewable change against the text before the reload; a document
with unsaved changes keeps the existing conflict path.

Done when a test keeps one hunk and undoes another in a change set over two files, a revert of the whole set
restores both files byte for byte, an external write to an open saved document shows as a reviewable change, and a
P12 drive reviews a change in both sample heads.

### AGENT-3 - MCP bridge for agents
**P3 · feature**

Depends on AGENT-1 and AGENT-2. Add a package `Nickelony.IDEKit.Mcp` over the official C# MCP SDK that runs a local
MCP server exposing the IDE as tools: the context of AGENT-1, the diagnostics of a file or of the workspace, opening
a file at a range, the document and workspace symbols, and proposing a change for review through AGENT-2 (the call
returns when the user keeps or undoes it). The host chooses the tools it exposes and starts and stops the server. The
server listens on loopback only, requires a token per session, and refuses a path outside the workspace roots.
Discovery follows the agent's own convention through a host hook (for example a lock file the agent reads), so the
package names no agent.

Done when an MCP client test lists the tools, reads the context and the diagnostics, opens a file, and proposes a
change that a fake review accepts; a request with a wrong token or a path outside the roots is refused; and a live
drive connects an MCP-capable agent CLI to the sample.

### AGENT-4 - Streaming chat transcript
**P3 · feature**

A chat panel renders a Markdown answer that grows chunk by chunk, and the Markdown renderers build a whole document
from its text, so a streamed answer is rendered again for every chunk. Add an append path that renders only the
last open block again, and code-block actions through hooks (copy, insert at the caret, propose as a change through
AGENT-2), in both bindings. The panel, the agent connection and the conversation store stay host code.

Done when appending 2,000 chunks to a transcript renders only the open block again (measured), the code-block hooks
fire in both bindings, and the sample shows a scripted transcript.
