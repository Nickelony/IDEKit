# Provider framework guide

This guide is the reference for the public surface and conventions of `Nickelony.LanguageServer.Provider`, the
language-neutral framework of the Nickelony Language Server family. It owns the option type, the
[workspace](ArchitectureGuide.md#workspace)-watching rules, the framework's limitations, and its naming and
slice conventions.

The recipe for implementing a provider is in the [provider authoring guide](ProviderAuthoring.md); the
machinery behind the provider is in the [LanguageServer internals guide](LanguageServerInternals.md); the
host-facing lifecycle, threading, and disposal contract is in the
[consumer integration guide](ConsumerIntegration.md).

## Position

The framework supplies the lifecycle, document synchronization, workspace watching, semantic-token, and
pull-diagnostics machinery a language package builds on, so a language package ships only its profile
instead of re-implementing the orchestration.

It sits on `Nickelony.LanguageServer.Provider.Abstractions` (the editor-facing contracts) and
`Nickelony.LanguageServer.Client` (the LSP client and shared primitives), and references the
`Nickelony.IDEKit.LanguageFeatures` model the language packages implement against.

`Nickelony.IDEKit.Core` is referenced directly for the text primitives the document machinery needs: the
document tracker computes incremental edits through `TextIncrementalEditCalculator` and converts their ranges
with `TextLineMap.GetPositionRange`, and the semantic-token decoder resolves line and character coordinates
through the line map. The client package carries no editor dependency, so the code that converts protocol
payloads into editor models lives in this package, in the response parsers beside the internal feature
providers. Its rules are in the [internals guide](LanguageServerInternals.md#response-mapping).

## Options

`LanguageServerFeatureProviderOptions` carries the framework tunables, and each member documents its default
and its supported range. A host starts from `LanguageServerFeatureProviderOptions.Default` and overrides with
a `with` expression; each property validates its value when it is assigned and throws
`ArgumentOutOfRangeException` for one outside its supported range.

`PathComparison` is the one comparison under which the provider decides that two paths are one document: the
tracked documents, the per-document scheduling and cancellation slots, the pending closes, and the workspace
change buffers all take it from the options. A host with a case-sensitive store sets
`LocalPathComparisonPolicy.CaseSensitive`, because the platform default is an operating-system assumption, not
a probe of the volume. The client the provider drives keys its diagnostics payloads and checks its workspace
roots with the platform default, whatever the option says.

## Host hooks

`LanguageServerFeatureProviderHooks` carries the delegates for the messages a server sends besides the answers to
requests: a message to show, a question with actions, work-done progress and an edit that the server asks the host
to apply. The provider constructor takes them before the options, and they apply to the client the provider
creates. The provider hands the host the shared models (`WindowMessage`, `WorkDoneProgress`, `WorkspaceEdit`)
instead of the wire payloads, and a hook that the host leaves unset keeps the client's default and does not
advertise the capability. The
[consumer integration guide](ConsumerIntegration.md#server-messages) shows the host side.

## Document synchronization

`LanguageServerFeatureProvider` is the `IDocumentSynchronizationTarget` of its server, so its document members
mirror a document to the server, and a host pairs every open with one close because an open holds a lease. The
[lease semantics](ProviderAuthoring.md#document-lease-semantics) of the authoring guide own the rules.

An editor host that keeps its documents in a `WorkspaceViewCoordinator` does not call these members itself: it
hands the provider to a `WorkspaceDocumentSynchronizer`, which opens each document once however many views show
it, as the [workspace views guide](WorkspaceViewsGuide.md#mirroring-documents-to-a-language-server) describes.

## Internal machinery

The document tracker with its snapshots, leases, and records, the per-document operation scheduler, and the
change forwarding to the server are internal framework machinery, not surface: a language package builds on the
provider, and only `FileWatchSpecification` crosses the package boundary, as the profile member that names the
files to watch.

The watching engine is the `Nickelony.IDEKit.FileWatching` package; the [file watching guide](FileWatchingGuide.md)
owns its debounce, retry and recovery rules. The provider supplies the default engine, forwards the changes it
dispatches, and reports an unrecoverable watcher failure through `WorkspaceFileWatchFailed`, whose payload
`Provider.Abstractions` carries because the provider contract raises the event. The framework's tests
substitute the engine, and the tracked-snapshot engine with it, through two factory parameters of the
provider's internal constructor.

## Limitations

- `FileWatcher` uses one `FileSystemWatcher` per watch specification with a fixed-size buffer.
- A filter supports the runtime's simple wildcards only: `*` matches zero or more characters and `?`
  matches exactly one. The watcher's rename-endpoint check matches in managed code and first normalizes an
  empty filter and `*.*` to `*`, whereas the snapshot tracker passes a wildcard filter to
  `Directory.EnumerateFiles` unchanged.
- A watch specification rejects empty or whitespace-only filters, directory aliases (`.` and `..`), rooted
  patterns, and patterns containing directory separators.
- A watcher can lose changes: a dispatch that keeps failing stops it, and disposal drops undelivered changes.
  Recovery is a replacement watcher whose missed changes are reconciled through the workspace snapshot, and the
  retry policy is in the [internals guide](LanguageServerInternals.md#workspace-watching).
- A recoverable forwarding failure replays the whole batch, so a settings push that reached the server before
  the failure is sent again. The repeat is safe because `workspace/didChangeConfiguration` and
  `workspace/didChangeWatchedFiles` are idempotent, and per-notification progress would need a new forwarding
  callback surface.
- An `IOException` thrown by `ILanguageServerClient.EnsureStartedAsync` marks the connection unavailable
  without a generation check. `LanguageServerClient` reports a startup failure by returning `false` and never
  throws one, so only a custom client reaches that path.
- Hover reads its response through the tolerant `HoverResponseJsonConverter`, but rename reads
  `WorkspaceEditResponse` without a root converter, so a workspace-edit shape that the strict payload type
  rejects yields the fallback `null`. A tolerant root converter waits for a real server that sends such a
  shape.

## Naming and slice placement

The file-watching package names the change pipeline with the `FileChange*` noun (`FileChangeBatch`,
`FileChangeDebouncer`, `FileChangeCoalescer`), its native watcher `FileWatcher*` and its supervision and failure `FileWatch*`;
the protocol side of this package uses `WorkspaceChange*` (`WorkspaceChangeForwarder`, `WorkspaceChangeDispatcher`);
document ownership uses `*Lease*`; and "reference" is reserved for LSP
code navigation. Wire DTOs follow the `*Response`/`*Payload` rule.

The package uses one flat namespace (`Nickelony.LanguageServer.Provider`); folders express slice ownership
only. A feature folder (`Completion/`, `Hover/`, `Navigation/` and so on) holds the internal provider of that
feature, its response parser, and its converters, and `Markup/` and `Ranges/` hold only the rules several
features share. The provider's cross-cutting partials sit at the project root, `Session/` holds the context and the services that
every collaborator shares, and the role folders that the slice guard lists keep role names. `ParsedDiagnostics`
and the `DiagnosticEntry` it holds live in `Diagnostics/`; the document tracker caches them through a payload
slot that the diagnostics slice declares. A file keeps one top-level type, except where
[rule C9](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md#contracts-and-shapes) lets a type
share its file with the enum or record that describes one of its members, as `WindowMessage` does with
`WindowMessageKind`. The [internals guide](LanguageServerInternals.md#provider-folder-order) lists the direction in
which the folders depend on each other.
