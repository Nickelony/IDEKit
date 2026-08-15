# Nickelony.LanguageServer.Provider

**The language-neutral provider framework** of the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family. It supplies the lifecycle, document synchronization, workspace watching, and request machinery that a language-server provider package builds on, so a new provider implements its language hooks instead of re-implementing the orchestration.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Provider.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Provider)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package is the **provider framework** of the family. It sits on the two language-server packages -
[`Nickelony.LanguageServer.Abstractions`](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
(the editor-facing contracts) and
[`Nickelony.LanguageServer.Client`](https://www.nuget.org/packages/Nickelony.LanguageServer.Client)
(the LSP transport and shared primitives) - and directly references the
`Nickelony.IDEKit.IntelliSense` model the language packages implement against. A language package derives from
`LanguageServerIntelliSenseProviderBase`, passes its tracked-document store, watch specifications,
and language-server client to the constructor, and implements the language-specific hooks - the configuration rule,
the settings payload, the failure reports, and, when it exposes the feature, the optional diagnostics and
semantic-token hooks. The base class already implements every feature member (its standard LSP request-and-parse
path), so a language package overrides one only when its wire shape or behavior differs. Hosts consume the resulting concrete provider
through `ILanguageServerIntelliSenseProvider`
(and `ILanguageServerSemanticTokensProvider` for semantic tokens).

Besides the base class, the package owns the machinery that only a provider needs: the tracked-document store
and operation scheduler, the workspace file watcher with its snapshot tracker, and the protocol-to-editor
bridges. The client package carries no editor dependency, so those types live here. The framework ships one
concrete store, `ServerPayloadDocumentStore` (internal, reached through `InternalsVisibleTo`), that caches the
diagnostics and semantic-token payloads a server sends for a document, so a provider with standard caching
needs no store of its own.

## Getting started

A provider derives from the base class and passes the language's store and watch specifications to the
constructor. The provider owns the client and disposes it with the provider:

```csharp
using var provider = new MyLanguageIntelliSenseProvider(
    [scriptRootDirectoryPath],                 // workspace roots (absolute local paths)
    new MyDocumentStore(),                     // the language's tracked-document store
    MyLanguageWatchSpecifications.All,         // watched file patterns; an empty list disables watching
    CreateLanguageServerClient(clientOptions)); // null when the language server is unavailable

provider.OpenDocument(filePath, content);
provider.UpdateDocument(filePath, content);

IReadOnlyList<TextCompletionItem> completions = await provider.GetCompletionItemsAsync(
    new LanguageServerCompletionRequest(filePath, content, position), cancellationToken);

provider.CloseDocument(filePath);
```

The lifecycle events (`DiagnosticsUpdated`, `SemanticTokensUpdated`, `CapabilitiesChanged`, `StartupFailed`,
`WorkspaceWatcherFailed`) are raised on the thread that reports the change, so a consumer with thread-affine state
forwards them to its owning thread itself. Push diagnostics delivery is serialized per client: the payload hook and
every `DiagnosticsUpdated` handler run before the next payload is dispatched, so handlers must be cheap and must not
block. A pull-diagnostics report raises the same event from the pull task instead, so handlers must tolerate being
invoked from either path.

The four composition inputs are the workspace root paths, the language's tracked-document
store, the watch specifications, and the language-server client. A provider that has no
server binary yet passes a `null` client and still constructs, reports a persistent startup
failure, and returns each request's documented fallback value. The client to pass is built
from `Nickelony.LanguageServer.Client` and the contracts it drives live in
`Nickelony.LanguageServer.Abstractions`.

## Dependencies

- `Nickelony.LanguageServer.Abstractions`
- `Nickelony.LanguageServer.Client`
- `Nickelony.IDEKit.IntelliSense`
- `Nickelony.IDEKit.Core`
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3

`StreamJsonRpc` and the remaining client transport dependencies arrive transitively through
`Nickelony.LanguageServer.Client`. `Nickelony.IDEKit.Core` is referenced directly because the moved
machinery needs its text primitives: the tracked-document store computes incremental edits through the
text-edit kernel, and the semantic-token bridge converts through the line map.

## Features

- **Provider base class** - `LanguageServerIntelliSenseProviderBase` implements
  `ILanguageServerIntelliSenseProvider` and `ILanguageServerSemanticTokensProvider` end to end:
  availability and state, the capability flags, the lifecycle events, the document lifecycle
  (`OpenDocument`/`UpdateDocument`/`CloseDocument`/`MoveDocument`), the cached diagnostics and
  semantic-token reads, the semantic-token refresh controller, the standard LSP implementations of the
  feature members (completion, hover, definition, references, rename, formatting, signature help,
  document symbols, and code actions), and shared synchronous/asynchronous disposal.
- **Lazy startup and recovery** - one serialized startup path with fast-path re-entry, restart
  shielding, resumable tracked-document reopen after a restart, transport-generation fencing, the
  hard-failure latch after consecutive startup failures, and an open that lands inside a startup or
  replay window being deferred instead of dropped (the retry joins the in-flight startup and
  applies the open once it settles).
- **Document synchronization** - full-content `didOpen`/`didClose` with full-or-incremental
  `didChange`, driven through the per-document scheduler and the version-fenced tracked-document
  store, with idle-document trimming that closes each evicted document through its own scheduler
  slot, move rekeying, per-document invalidation, and - when a session negotiated no synchronization mode -
  a change that is skipped with a warning instead of triggering a post-synchronization refresh. An open and a
  close are still sent without a negotiated mode so the server holds the document for requests that do not
  depend on synchronization; only the change is skipped.
- **Semantic tokens** - the base implements `ILanguageServerSemanticTokensProvider`: it refreshes a
  tracked document's full token set after every successful open/change synchronization and when the
  server requests a refresh, supersedes an in-flight refresh per document, bounds the refresh fan-out,
  version-fences the store through the language hooks, and raises `SemanticTokensUpdated` with
  `GetSemanticTokens` as the pull-side read of the same cache. A failed refresh keeps the previously
  cached tokens, and a rename re-keys or clears them.
- **Pull diagnostics** - for a server that advertises the pull capability (`SupportsPullDiagnostics`), the base
  requests `textDocument/diagnostic` after every successful open/change synchronization and when the server asks for
  a refresh, threads each report's `resultId` back as the next request's `previousResultId`, supersedes an in-flight
  pull per document, and bounds the pull fan-out. A full report flows through the same language hook
  (`HandleDiagnosticsPayload`) and `DiagnosticsUpdated` event the push notification uses, so a provider implements
  one diagnostics path for both delivery models; an unchanged report keeps the cached diagnostics, and a failed pull
  keeps the last known report. Related-document reports are applied to their tracked documents.
- **Workspace watching** - one watch scope per workspace root (watcher, snapshot tracker,
  recovery state, failure latch), ownership-based forwarding (each path is forwarded exactly once
  when roots nest), buffered replay of recoverable gaps, and configuration-file refresh through
  the language's settings payload.
- **Request pipeline** - per-request timeout with transport-restart hysteresis, capability
  gating before document synchronization, request dispatch after synchronization,
  caller-cancellation pass-through, and the documented fallback value for every failure mode -
  timeouts, transport loss, disposal, and server rejections (which never restart the transport).
- **Request and file-change helpers** - the request pipeline's per-request timeout,
  transport-generation fencing, a single retry after a transport boundary, and timeout-driven
  restart hysteresis are reached through the protected `SendRequestAsync` seam on the provider
  base; the base also owns the internal buffered replay of recoverable workspace-file-change
  delivery gaps behind an ensure-started gate, which drops changed batches that were not delivered
  yet on disposal.
- **Tunable policy** - `LanguageServerProviderOptions` carries the request timeout, the
  timeout-restart threshold (zero disables timeout-driven restarts), the hard startup-failure
  threshold, and the tracked-idle-document cap, with the family defaults baked in. A server that
  indexes a workspace while it starts can exceed the request timeout while it works, which would
  trip the restart hysteresis on a slow-but-healthy server and discard its warm-up work; raise
  `RequestTimeout`, or set `RequestTimeoutRestartThreshold` to zero to disable timeout-driven
  restarts entirely.
- **Document machinery for language packages that compose their own orchestration** - the tracked
  document store (`TrackedDocumentStore`), its snapshots and change ranges, and the document references
  and close outcomes are public, so a language package that does not use the base class can build
  document synchronization on them. The per-document operation scheduler and the workspace snapshot
  tracker are internal framework machinery, not a host surface.
- **Workspace watching utilities** - `WorkspaceFileWatcher` (one `FileSystemWatcher` per watch
  specification with change debouncing, batched forwarding, bounded retry backoff, and an optional
  failure callback) and the change records (`WorkspaceFileChange`, `FileChangeBatch`,
  `WorkspaceWatchSpecification`).
- **Protocol-to-editor bridges** - `ProtocolRangeConversion`, `SemanticTokenConversion`,
  `WorkspaceEditConversion` (workspace edits into the shared edit model), `FormattingOptionsConversion`
  (the abstraction's formatting options onto the wire payload), `TextCompletionItemKindConversion`,
  `TextDocumentSymbolKindConversion`, and the `ProtocolMarkupContent`/`MarkupContentReader` markup pair
  convert between the client package's protocol payloads and the shared editor models. They live here rather
  than in the client package so a protocol-only host takes no editor dependency.
- **Hooks, not forks** - the language package supplies its provider display name and language identifier,
  configuration-path rule, settings payload, and failure reports (the standard feature implementations and their
  response parsing live in this base), and can refine behavior
  through the virtual hooks (`OnDocumentSynchronizedAsync`,
  `OnTrackedDocumentInvalidated`, `OnDocumentMoved`, `OnDisposing`) or substitute the watching engine
  by assigning `LanguageServerProviderOptions.WorkspaceFileWatcherFactory` (or overriding
  `CreateWorkspaceFileWatcher`) to return a custom `IWorkspaceFileWatcher`.
  The diagnostics hooks (`HandleDiagnosticsPayload`/`GetTrackedDiagnostics`) and the semantic-token
  hooks (`GetTrackedSemanticTokens`/`TryStoreSemanticTokens`) are optional: the base defaults drop published
  diagnostics and store no tokens, so a provider implements them only when it exposes the feature. The
  same diagnostics hook serves both delivery models - a push notification and a pull report - so a pull-only
  provider implements only that hook.
  Post-synchronization hooks run inside the document's scheduler slot (and under the startup
  serialization during a restart replay); they must not call provider document or request APIs
  and should detach long-running work.
  [`Nickelony.LanguageServer.Lua`](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua)
  is the reference implementation; [`docs/ProviderAuthoring.md`](https://github.com/Nickelony/LanguageServer/blob/main/docs/ProviderAuthoring.md)
  describes the authoring model end to end.

## Limitations

- `WorkspaceFileWatcher` uses one `FileSystemWatcher` per watch specification with a 64 KiB buffer;
  watch filters follow the runtime's simple wildcard grammar on every platform (`*.*` and an empty
  filter are normalized to `*`), and empty or whitespace-only filters, directory aliases (`.` and
  `..`), rooted patterns, and patterns containing directory separators are rejected.
- `WorkspaceFileWatcher` retries a failed dispatch with bounded exponential backoff, then stops
  watching, drops the pending changes, and notifies the owner once; disposal is non-blocking and
  drops undelivered changes, so the recovery path is a replacement watcher whose missed changes are
  reconciled through the watcher's internal workspace snapshot.

## License

MIT © 2026 Kewin Kupilas.
