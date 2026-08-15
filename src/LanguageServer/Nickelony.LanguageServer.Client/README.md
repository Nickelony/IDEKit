# Nickelony.LanguageServer.Client

**A lightweight LSP client for .NET**, built on [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc). It spawns a language-server process and speaks the Language Server Protocol over stdio.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Client.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Client)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package is the **LSP client machinery** of the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family. It handles everything below the editor-facing contracts: hosting the server process, the `initialize` handshake, capability negotiation, JSON-RPC request/notification transport, and the document-synchronization payload machinery. The stateful provider machinery (tracked documents, the operation scheduler, and workspace watching) lives in `Nickelony.LanguageServer.Provider`.

## Getting started

```sh
dotnet add package Nickelony.LanguageServer.Client
```

```csharp
using Microsoft.Extensions.Logging;
using Nickelony.LanguageServer.Client;

var client = new LanguageServerClient(
    workspaceRootDirectoryPaths: ["/home/user/workspace"],   // e.g. @"C:\my\workspace" on Windows
    serverExecutablePath: "/opt/example-language-server/example-language-server",
    options: LanguageServerClientOptions.Default,
    logger: loggerFactory.CreateLogger<LanguageServerClient>());

client.DiagnosticsPublished += (_, eventArgs) =>
{
    // Diagnostics for a tracked document were published by the server;
    // eventArgs.Parameters carries the payload. May be raised on a background
    // thread - marshal to the subscribing thread.
};

bool ready = await client.StartAsync(cancellationToken);

if (ready)
{
    var result = await client.SendRequestAsync<MyResponse>(
        "textDocument/hover", hoverParams, cancellationToken);

    await client.SendNotificationAsync(
        "textDocument/didChange", changeParams, cancellationToken);
}

await client.DisposeAsync();
```

Options let you tune lifecycle timeouts and provide LSP payloads for your host. Start from
`LanguageServerClientOptions.Default` and override only what you need:

```csharp
var options = LanguageServerClientOptions.Default with
{
    SettingsProvider        = () => new { maxPreload = 10 },
    InitializeTimeout       = TimeSpan.FromSeconds(20),
    ShutdownRequestTimeout  = TimeSpan.FromSeconds(3),
    DisposeWaitTimeout      = TimeSpan.FromSeconds(5),
    ClientCapabilitiesProvider = _ => new { textDocument = new { hover = true } },
    InitializationOptionsProvider = _ => new { },
    ServerArguments = ["--log-level=warn"],
    ServerWorkingDirectory = "/home/user/workspace",   // default: the executable's directory
    EnvironmentVariables = new Dictionary<string, string> { ["EXAMPLE_SERVER_HOME"] = "/opt/example-server" },
};
```

`SettingsProvider` is optional: leave it at its default to push an empty settings payload.

A connection is opened through `LanguageServerClientOptions.Transport`, which defaults to
`StdioLanguageServerTransport.Default` (the executable above is launched as a child process and the
protocol runs over its standard streams). Supply your own `ILanguageServerTransport` to reach the server
over another channel, for example one your host already spawned, a local socket, or an in-memory pair:

```csharp
var options = LanguageServerClientOptions.Default with
{
    Transport = new MyExistingProcessTransport()   // returns an ILanguageServerConnection
};
```

A transport returns an `ILanguageServerConnection` (read/write/error streams plus the optional server
process); the client adopts it for the session, watches the process for an unexpected exit, runs the
graceful `shutdown`/`exit` sequence on teardown, and disposes the connection last. The transport owns
everything it created until it returns the connection, so a failed or canceled `ConnectAsync` must
release it itself.

Host-side utilities: `TryMarkTransportUnhealthy` (with `TransportUnavailable`) is the generation-safe way
to invalidate a transport you observed failing. The provider framework's internal ordering and quiescence
helpers for its own host-scheduled operations ship with `Nickelony.LanguageServer.Provider`.

> **Tip:** `LanguageServerClient` is a low-level building block. For editor-facing IntelliSense, use the
> [`Nickelony.LanguageServer.Lua`](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua)
> provider, which implements the editor contracts from
> [`Nickelony.LanguageServer.Abstractions`](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
> on top of this client.

## Requirements

.NET 8 (the package targets `net8.0`). The default stdio transport additionally needs
the language-server executable at runtime; a host that supplies its own
`ILanguageServerTransport` does not.

## Dependencies

- `StreamJsonRpc` 2.25.29
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3

Package boundary: these are the package's only dependencies. It references no editor or IDE package, so a
protocol-only host takes nothing beyond StreamJsonRpc and logging. The protocol-to-editor bridges and the
provider machinery that needed shared editor models live in `Nickelony.LanguageServer.Provider`.

## Features

- **Process hosting** - starts the language-server executable and performs the full LSP
  initialization handshake (`LanguageServerClient.StartAsync`).
- **Multi-root workspaces** - the constructor takes a workspace root list; every entry is
  advertised through `workspace/workspaceFolders`, and the first entry is the primary `rootUri`.
  An empty list is valid and models a folderless session: `rootUri` and `workspaceFolders` are sent
  as `null`. The folder set is fixed at construction time (see Limitations).
- **Typed JSON-RPC** - `SendRequestAsync<TResult>` / `SendNotificationAsync` over stdio via
  StreamJsonRpc, with a `JsonSerializer` tuned for LSP conventions. A server error response is
  surfaced as `LanguageServerRequestRejectedException` (carrying the JSON-RPC `ErrorCode`), so a
  rejection is distinguishable from a transport failure and the transport stays ready. Every
  transport-usability failure derives from `LanguageServerTransportException` (itself an `IOException`),
  so one catch clause covers "no ready session yet", "became unavailable", and "superseded".
- **Resilient sessions** - transport generation tracking with crash-resilient session invalidation:
  when the transport of a ready session crashes or becomes unhealthy, the client marks the transport
  unavailable (raising `TransportUnavailable`) and the host recreates the session on the next
  startup attempt, followed by workspace re-sync. Failures before a session becomes ready surface as
  `false` from `StartAsync` with the cause logged and exposed through `LastStartupException` instead.
- **Capability negotiation** - read negotiated capabilities such as
  `SupportsCompletionResolve`, `SupportsReferences`, `SupportsDocumentSymbols`, `SupportsCodeActions`,
  `SupportsCodeActionResolve`, `SupportsRename`, `SupportsFormatting`, `SupportsHover`, `SupportsDefinition`,
  `SupportsSignatureHelp`, `SupportsSemanticTokensFull`, `SupportsPullDiagnostics`, and
  `TextDocumentSyncKind`.
- **Document synchronization payloads** - the wire shapes for full and incremental text-document
  synchronization (`textDocument/didOpen`, `didChange`, `didClose`, and rename requests) live here. The
  stateful store that tracks them (`TrackedDocumentStore`, request references, and close outcomes) ships
  with `Nickelony.LanguageServer.Provider`, which is where a host that mirrors documents to a provider is
  already working.
- **Signature-label parsing** - `SignatureLabelParser` turns a signature-help label into parameter spans.
- **Server events** - `DiagnosticsPublished`, `SemanticTokensRefreshRequested`, and
  `DiagnosticRefreshRequested` are raised for server notifications and requests, and `TransportUnavailable`
  is raised when the active transport is lost; the client also pushes `workspace/didChangeConfiguration` with
  a cached settings payload.
- **Semantic tokens** - the client owns only the wire shapes (`Protocol/SemanticTokens/`) and requests
  full tokens only; it does not service semantic-token delta responses. The decoded editor-coordinate
  model (`SemanticToken`) ships with `Nickelony.LanguageServer.Abstractions`; its decoder
  (`SemanticTokensDecoder`) and the bridge to the shared editor payload (`SemanticTokenConversion`)
  ship with `Nickelony.LanguageServer.Provider`.
- **Protocol helpers** - the `Protocol/DocumentSymbols/` family
  carries the document-symbol request and the tolerant response payloads that cover the
  hierarchical `DocumentSymbol` and flat `SymbolInformation` shapes with one type; the
  `Protocol/CodeActions/` family carries the code-action request (range plus diagnostics context)
  and the tolerant literal-action responses that keep every titled entry, including command-only
  entries, bare `Command` literals (identifier and arguments are preserved), and
  `codeAction/resolve`-style entries whose payload arrives through `data`.
- **Logging** - `Microsoft.Extensions.Logging.Abstractions` throughout; pass your `ILogger`
  and get structured, level-appropriate logs. The library keeps the logger you supply verbatim, so the
  category is yours to choose: pass `loggerFactory.CreateLogger<LanguageServerClient>()` to see the
  client's messages under that category.

## Project structure

The package uses a **single flat namespace** (`Nickelony.LanguageServer.Client`) for its entire
public and internal surface: folders express slice ownership only, and a file keeps its namespace
when it moves between slices. The one exception is the internal `Nickelony.LanguageServer.Protocol`
namespace, which owns the linked `LspMethodNames` table (`shared/Protocol/LspMethodNames.cs`); no
other type leaves the flat namespace. The slice map:

| Folder | Contents |
|---|---|
| `Pathing/` | `LanguageServerPaths` (path and file-URI identity helpers) |
| `Protocol/` | All LSP wire DTOs and their JSON converters, one subfolder per feature (`Common/`, `Requests/`, `Notifications/`, `Callbacks/`, `Capabilities/`, `WorkspaceEdits/`, `CodeActions/`, `Completion/`, `Diagnostics/`, `DocumentSymbols/`, `Hover/`, `Navigation/`, `References/`, `SignatureHelp/`, `SemanticTokens/`) |
| `Transport/` | Client runtime: the pluggable `ILanguageServerTransport`/`ILanguageServerConnection` connection seam with the default `StdioLanguageServerTransport` (and the `ChildProcessLifetime` platform guard, whose Windows implementation is `WindowsJobObject`), transport session/host, RPC forwarding, capability store, options, the `LanguageServerTransportException` family, and subscription plumbing |

Placement rules:

- **Wire shapes live in `Protocol/`.** Every DTO that mirrors an LSP message and every
  `JsonConverter` for one lives under `Protocol/<Feature>/`; feature logic stays in its own slice.
- **The protocol types are owned in-repo, not sourced from a package.** The `Protocol/` DTOs and
  converters are maintained by hand against the LSP specification. Both established protocol
  packages were rejected: `Microsoft.VisualStudio.LanguageServer.Protocol` is a Visual Studio
  component and would pull an editor-flavoured dependency into a package whose contract is that it
  has none; `OmniSharp.Extensions.LanguageServer.Protocol` is server-first and brings a heavy
  dependency graph (not editor-flavoured, but unwanted in a thin client package). The maintenance
  cost of owning the subset this client uses is accepted here.
- **The package has no editor dependency.** No type here, public or internal, references a shared
  editor model. The protocol-to-editor bridges (`ProtocolRangeConversion`, `SemanticTokenConversion`,
  `WorkspaceEditConversion`, `FormattingOptionsConversion`, the two kind conversions, and the
  `ProtocolMarkupContent`/`MarkupContentReader` pair) and the
  provider machinery that consumed those models (`Documents/`, `Workspace/`) live in
  `Nickelony.LanguageServer.Provider`. A new bridge between an LSP payload and an editor payload
  belongs to that package, not here.
- **Path identity defaults to the platform.** `LanguageServerPaths` derives its comparison from the
  operating system; a host whose volume semantics differ passes its own `StringComparison`/
  `StringComparer` to the identity helpers (`AreLocalPathsEqual`, `NormalizeWorkspaceRoots`), which
  accept an explicit policy.
- **The semantic-token wire shapes are wire-only.** The raw `int[]` data stream and the tolerant
  response payload live in `Protocol/SemanticTokens/`; the decoded editor-coordinate model lives in
  `Nickelony.LanguageServer.Abstractions`.
- **Payload collections are read-only lists.** A DTO exposes a collection of payload objects as
  `IReadOnlyList<T>`, never `T[]`; the tolerant collection converters read either JSON shape, so the
  declared shape follows this rule instead of the serializer's preference. An array stays where the
  member is not a payload collection: a dense primitive buffer (the semantic-token `int[]` data
  streams), a `params` parameter, and a helper that returns a freshly allocated array so its caller
  owns a concrete buffer (for example `LanguageServerPaths.NormalizeWorkspaceRoots`).

## Events and threading

The client raises four events: `DiagnosticsPublished` carries a `publishDiagnostics`
payload (`eventArgs.Parameters`), `SemanticTokensRefreshRequested` reports a
`workspace/semanticTokens/refresh` request the client acknowledges so the host can
re-push tokens, `DiagnosticRefreshRequested` reports a `workspace/diagnostic/refresh`
request the client acknowledges so the host can re-pull diagnostics, and
`TransportUnavailable` reports a transport the client observed failing. Every event may be
raised on a background thread, so marshal to the owning dispatcher before touching
thread-affine state, and serialize any shared state a handler reads or writes.

## Error handling

- Construction is fail-fast: `LanguageServerClientOptions` validates its own values, so a
  misconfigured client throws at the constructor instead of at the first request.
- `StartAsync` reports whether the session became ready; an ordinary startup failure
  leaves `IsReady` `false` and surfaces the cause through `LastStartupException` rather
  than throwing.
- A request that cannot be delivered returns its documented fallback value (caller
  cancellation still propagates as `OperationCanceledException`); a transport you
  observed failing is invalidated through `TryMarkTransportUnhealthy`, and the next
  `StartAsync` is the restart.
- Disposal is idempotent and drains in-flight work to its configured wait timeout; the
  client never restarts itself, so restart policy, backoff, and workspace re-sync stay
  with the host.

## Limitations

- The default `StdioLanguageServerTransport` launches a server process; connecting to a server the host did not
  spawn (or over a channel that is not a process) requires a custom `ILanguageServerTransport`. A connection
  without a process exposes no exit signal, so teardown reports no process exit and relies on the connection's
  own disposal.
- Workspace folders are fixed at construction time. Entries are validated (null, empty, whitespace-only, and
  duplicate entries are rejected, the last by normalized-path identity) and advertised through
  `workspace/workspaceFolders` with the first entry as the primary `rootUri`; the client never sends
  `workspace/didChangeWorkspaceFolders`.
- Positions are UTF-16 code units: the client advertises `general.positionEncodings: ["utf-16"]` and fails
  transport startup when a server selects another encoding. The initialize payload forces
  `textDocument.semanticTokens.multilineTokenSupport` and `overlappingTokenSupport` to `false` when the host
  advertises them, because the decoder clamps a token to its line end and the decoded model cannot represent
  overlapping tokens.
- `RequireTextDocumentSynchronization` defaults to `true`: the client rejects servers that advertise neither
  full nor incremental `textDocumentSync`. Set it to `false` to accept such servers.
- Dynamic registration is deliberately unsupported: the server-callback target logs and ignores
  `client/registerCapability` and `client/unregisterCapability`, and the initialize payload forces
  `dynamicRegistration = false` on every declared `workspace`/`textDocument` capability plus
  `workspace.applyEdit = false`.
- Child-process lifetime binding uses a Windows job object; on other platforms the client relies on the
  graceful `shutdown`/`exit` teardown. Teardown waits a short grace period for the process to exit on its own
  after `exit` before forcing termination, so an orderly exit is not raced by the kill.
- Event delivery stops when disposal completes: a payload queued for a subscriber but not yet started is
  dropped, and a handler that already started runs to completion. Diagnostics coalesce per document URI (a file
  URI by its normalized local path, any other URI by its exact text) and every handler receives its own
  one-level-deep copy of the diagnostics sequence, while the nested related-information and tag values stay
  shared and read-only.
- `workspace/configuration` request items are answered from the single global settings snapshot; a
  server-requested per-resource `scopeUri` is not honored.
- The `textDocumentSync` options `openClose`, `save`, `willSave`, and `willSaveWaitUntil` are not modeled: a
  server that advertises `openClose: false` still receives `didOpen`/`didClose`, and `save: true` never
  triggers a `didSave`.
- Malformed protocol payloads follow a per-feature tolerance policy:

  | Payload | Tolerance |
  |---|---|
  | Malformed list entries | skipped with a warning |
  | Edit-bearing collections (`documentChanges`, `changes`, `additionalTextEdits`) | a non-array root is rejected instead of degrading to an empty edit list |
  | `publishDiagnostics` that cannot be read as well-formed | dropped at the server-callback target so it can never clear stored diagnostics (`PublishDiagnosticsParams.IsDegraded` marks a payload a host deserializes directly) |
  | Hover response with a malformed optional `range` | contents kept, range dropped |
  | Definition target without a usable absolute URI | skipped; the definition reader is deliberately stricter than its sibling readers because a target is dereferenced to open a document |

- Pull diagnostics is modeled at the wire level only: the client ships the `textDocument/diagnostic` request payload
  and the full/unchanged report shapes (`Protocol/Diagnostics/`) and raises `DiagnosticRefreshRequested`, while the
  request-and-apply loop that refreshes a host's diagnostics cache lives in
  `Nickelony.LanguageServer.Provider`. Push (`textDocument/publishDiagnostics`) remains the only delivery model the
  `Lua` provider consumes; a server that advertises `diagnosticProvider` is served by the provider framework's pull
  loop.

- The generic send surface stays generic: the client exposes `SendRequestAsync<TResult>`/`SendNotificationAsync`
  plus the built-in `Protocol/` records, not a per-feature typed facade (that lives in
  `Nickelony.LanguageServer.Provider`/`Nickelony.LanguageServer.Lua`). Payloads a host builds itself must be
  serialized with `LanguageServerClient.CreateProtocolSerializerOptions()` because the client normalizes only
  the payloads it builds (capabilities, initialization options, settings).
- There is no restart or orderly stop method: a restart is `StartAsync` again after the transport was marked
  unhealthy (the documented restart contract), and stopping is disposal.
- Completion snippets stay raw: the client forwards `insertTextFormat` and the snippet text untouched, so
  tabstop expansion stays the consuming host's commit-time concern.
- The settings provider is optional; without one the client pushes an empty
  `workspace/didChangeConfiguration` payload and answers `workspace/configuration` requests from an empty
  snapshot.

## License

MIT © 2026 Kewin Kupilas.
