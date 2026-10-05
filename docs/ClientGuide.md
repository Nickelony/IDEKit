# LanguageServer client guide

`Nickelony.LanguageServer.Client` hosts a language server process and runs LSP JSON-RPC over it, as the protocol client
of the Nickelony Language Server family. This guide is its host-facing reference: it owns the transport seam,
capability negotiation, the protocol wire-shape conventions, and the client's limitations.

The lifecycle, threading, restart, failure-classification, and disposal contract is in the [consumer
integration guide](ConsumerIntegration.md). The machinery behind those contracts is in the [LanguageServer
internals guide](LanguageServerInternals.md). A host that wants editor-facing language features consumes a
provider package such as
[`Nickelony.LanguageServer.Lua`](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua) instead.

## Position

The package handles everything below the editor-facing contracts: hosting the server process, the `initialize`
handshake, capability negotiation, JSON-RPC requests and notifications, and the document-synchronization
payload machinery. It references no editor or IDE package, so a protocol-only host takes nothing beyond
StreamJsonRpc and the logging abstractions.

Because it references no other package, the client compiles the local-path rules that Core exposes as
`LocalPath` from one shared source file, and `DocumentUriConversion` converts between those paths and file
URIs.

The stateful provider machinery - tracked documents, the operation scheduler, workspace watching, response
mapping, and semantic-token decoding - lives in `Nickelony.LanguageServer.Provider`.

## The transport seam

A transport opens the channel between the client and one server: it launches or reaches the server and returns
the connection whose streams the protocol runs over.

The client takes its transport as a constructor argument and asks it for a new connection on every session
start. `StdioLanguageServerTransport` launches an executable as a child process and runs the protocol over its
standard streams. Its `StdioLanguageServerTransportOptions` hold the launch settings: the arguments, the working
directory (the executable's directory unless set), the environment variables, and the child-process lifetime
guard. Supply a custom `ILanguageServerTransport` to reach a server over another channel, for example one the
host already spawned, a local socket, or an in-memory pair; such a client names no executable at all.

A transport receives a `LanguageServerConnectRequest`, which carries the client's logger, and returns an
`ILanguageServerConnection` (the read and write streams plus the optional server process). What to connect to
belongs to the transport. The client adopts the connection for the session, watches the process for an
unexpected exit, reads its standard error, runs the graceful `shutdown`/`exit` sequence on teardown, and
disposes the connection after it. The transport owns everything it created until it returns the connection, so
a failed or canceled `ConnectAsync` must release it itself.

`TryMarkConnectionUnhealthy`, paired with the `ConnectionUnavailable` event, is the generation-safe way for a host
to invalidate a connection it observed failing.

These terms name the parts of that picture:

- A **transport** opens a connection: `ILanguageServerTransport` and `StdioLanguageServerTransport` are transports.
- A **connection** is the live link of one session: the streams and the optional server process that one
  `ConnectAsync` call returned. Its **connection generation** numbers it from `1` upward, and a host passes the
  generation it observed to `TryMarkConnectionUnhealthy`, so a late call cannot invalidate a newer session.

```csharp
var stdio = new StdioLanguageServerTransport(
    serverExecutablePath,
    StdioLanguageServerTransportOptions.Default with { ServerArguments = ["--stdio"] });

await using var client = new LanguageServerClient([workspaceRootDirectoryPath], stdio);

// A custom transport: ConnectAsync returns the ILanguageServerConnection of a server the host already runs.
await using var attached = new LanguageServerClient([workspaceRootDirectoryPath], new MyExistingProcessTransport());
```

The client's startup-failure log names the transport through its `ToString`, which for the stdio transport
includes the executable path.

## Capability negotiation

After `initialize` completes, the client publishes one immutable capability snapshot,
[`NegotiatedServerCapabilities`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Client/NegotiatedServerCapabilities.cs),
through `ILanguageServerClient.NegotiatedCapabilities`. It carries:

- `TextDocumentSyncKind`, `SemanticTokenTypes`, and `SemanticTokenModifiers`.
- A `Supports*` flag per negotiated request, documented on the snapshot type with the request it covers.

The snapshot reads `NegotiatedServerCapabilities.None` until initialization completes and after the active connection is
detached or becomes unavailable. `IsReady` and `ConnectionGeneration` stay separate members of the client, because
they describe the session rather than the server.

The rules for reading the snapshot are in the
[consumer integration guide](ConsumerIntegration.md#using-the-client-directly).

The `initialize` payload is normalized to the capabilities the client services:

- `dynamicRegistration` is forced to `false` on every declared `workspace`/`textDocument` capability.
- `workspace.applyEdit` and `window.workDoneProgress` follow the [host hooks](#host-hooks): each is `true` when its
  hook is set, and forced to `false` when the hook is unset and the host advertises it.
- `general.positionEncodings` is pinned to UTF-16.
- `multilineTokenSupport` and `overlappingTokenSupport` are forced to `false` when advertised.

## Protocol wire shapes

Every LSP DTO and its `JsonConverter` lives under `Protocol/`, one subfolder per feature, in the
`Nickelony.LanguageServer.Client.Protocol` namespace. A host that drives a provider imports
`Nickelony.LanguageServer.Client` only; protocol-level code, such as a custom request sent through
`SendRequestAsync`, imports the wire namespace as well. The wire model is the contract between the client and
the provider framework, public because the two are separate packages; it stays in the client package, because
no host uses the protocol without the client. The conventions:

- A wire type takes the specification's name as its stem and one suffix: `<Feature>Params` for the parameters
  of a request or notification, `<Feature>Response` for what a request returns, `...Capability` for a server
  capability record, and `...Payload` for any other structure.
- Every enum, the identifier structures (`TextDocumentIdentifier`) and the specification's root names
  (`ServerCapabilities`, `WorkspaceFolder`) keep the bare specification name. In the suffixed names,
  `Reference` is the find-references request (`ReferenceParams`), not a .NET reference.
- A name the specification gives too generically to read alone takes its slice: `CapabilityRegistrationParams`
  for `RegistrationParams` and `WorkspaceConfigurationParams` for `ConfigurationParams`. `WindowMessageParams`
  serves `window/showMessage` and `window/logMessage`, which share one structure, and a capability record takes
  the name of the `ServerCapabilities` member that advertises it (`CompletionProviderCapability`).
- The references and formatting results are not a `<Feature>Response`: they are bare arrays of
  `LocationPayload` and `TextEditPayload`. `DocumentDiagnosticReportResponse` also names the embedded entries
  of its `RelatedDocuments`, which map a document URI to the same report type, so a single name serves the
  top-level result and its embedded entries. `SignatureHelpResponse` is likewise the embedded
  `activeSignatureHelp` of `SignatureHelpContextPayload`, the `context` of `SignatureHelpParams`.
- A wire type's shape follows what the type must guarantee:
  - A `readonly record struct` is the default for requests, params, positions, ranges, identifiers, and flat
    payloads. `default` is a harmless empty value there, so a member the specification requires stays nullable
    and a missing one does not fail a whole response.
  - A `sealed record` is a type that a struct could not keep honest. `default` would skip a validated or non-null
    member (`DefinitionTargetPayload`, `SymbolLocationPayload`, `MarkupContentPayload`), a non-null collection
    the constructor copies (`WorkspaceDocumentChangePayload`), or state beyond the members
    (`PublishDiagnosticsParams.IsDegraded`). A payload with many members, most of them optional, is a
    `sealed record` as well (`CompletionItemPayload`, `DiagnosticPayload`, `CodeActionPayload`,
    `DocumentSymbolPayload`), and so is a response root whose `null` result the client reports as a failure
    instead of reading it as an empty value (`InitializeResponse`).
  - A `readonly record struct` whose every member is nullable by design may copy its collections in its
    constructor, because `default` (every member `null`) is the valid empty value: `WorkspaceEditResponse`.
  - A `sealed class` is a response root that a converter builds around a list or a few optional members
    (`CompletionResponse`, `DefinitionResponse`, `HoverResponse`).
  - A union is an `abstract record` with one `sealed record` per case (`CompletionTextEditPayload`).
- A required member that a type validates is validated in its initializer and in its constructor, so a
  `with` expression cannot bypass the check.
- The DTOs and converters are maintained by hand against the LSP specification, because no protocol package
  supplies this subset without an unwanted dependency.
- No type here references a shared editor model; a new bridge between an LSP payload and an editor payload
  belongs to `Nickelony.LanguageServer.Provider`.
- The semantic-token wire shapes are wire-only: the raw `int[]` stream and the tolerant response payload live in
  `Protocol/SemanticTokens/`, and the decoded offset-based `SemanticToken` model lives in
  `Nickelony.IDEKit.LanguageFeatures`.
- `MarkupContentPayload` types every markup member. A documentation member reads the protocol's
  `string | MarkupContent`, where a bare string is plain text. `HoverResponse.Contents` is a list of them that reads
  `MarkupContent`, `MarkedString` and `MarkedString[]`, where a bare string is Markdown and a `{ language, value }`
  code block becomes a fenced Markdown block. A markup payload always writes back as `MarkupContent`.
- A signature-help parameter's `label` is a `SignatureHelpParameterLabelPayload`: the label text, or the offsets of
  the label inside the signature label.
- Public payload collections are `IReadOnlyList<T>`, and a few serializer DTOs keep an array member instead, such
  as `SemanticTokensResponse.Data`. `WorkspaceConfigurationParams.Items` stays an array because its converter is
  declared for `WorkspaceConfigurationItemPayload[]`, so retyping it as `IReadOnlyList<T>` would bypass the converter
  that keeps each `workspace/configuration` entry at its request position.

### Wire names that differ from the specification

A wire type keeps the specification's name as its stem. These types take another stem, because the specification
name is too generic to read alone, one record serves several specification structures, or the specification
names no structure for the shape:

| Wire type | Specification | Why the stem differs |
| --- | --- | --- |
| `CapabilityRegistrationParams`, `CapabilityUnregistrationParams`, `CapabilityRegistrationPayload`, `CapabilityUnregistrationPayload` | `RegistrationParams`, `UnregistrationParams`, `Registration`, `Unregistration` | the slice prefix makes the name readable alone |
| `WorkspaceConfigurationParams`, `WorkspaceConfigurationItemPayload` | `ConfigurationParams`, `ConfigurationItem` | the slice prefix |
| `InitializeResponse` | `InitializeResult` | `Response` is the suffix of what a request returns |
| `EmptyParams` | `InitializedParams` | the `initialized` notification and the `shutdown` request carry an empty object, and the specification names a structure for the first only |
| `SupportedCapability` | the `boolean | XOptions` members of `ServerCapabilities` | one record reads every provider that is advertised as a boolean or as an options object |
| `CodeActionDisabledPayload` | the `disabled` member of `CodeAction` | the specification names no structure for it |
| `WindowMessageParams` | `ShowMessageParams`, `LogMessageParams` | one record serves `window/showMessage` and `window/logMessage` |
| `CompletionRangeTextEditPayload` | `TextEdit` | the completion item's classic edit; `TextEditPayload` is the plain edit |
| `CompletionInsertReplaceTextEditPayload` | `InsertReplaceEdit` | the slice prefix |
| `DefinitionTargetPayload` | `Location`, `LocationLink` | one record reads both shapes |
| `SymbolLocationPayload` | the `location` of `SymbolInformation` | the specification names no structure for it |
| `WorkspaceSymbolLocationPayload` | the `location` of `WorkspaceSymbol`, a `Location` or a bare `{ uri }` | the specification names no structure for it |
| `WorkspaceSymbolPayload`, `WorkspaceSymbolResponse` | `SymbolInformation` and `WorkspaceSymbol`, and the array of either | one record reads both entry shapes, and the response root reads both arrays |
| `WorkspaceDocumentChangePayload` | `TextDocumentEdit`, `CreateFile`, `RenameFile`, `DeleteFile` | one union record for the entries of `documentChanges` |
| `WorkspaceResourceOperationOptionsPayload` | `CreateFileOptions`, `RenameFileOptions`, `DeleteFileOptions` | one record for the options of the three operations |
| `WorkspaceEditChangeAnnotationPayload` | `ChangeAnnotation` | the slice prefix |
| `SignatureHelpParameterLabelPayload` | the `label` of `ParameterInformation` | the specification names no structure for the string or offsets union |
| `WorkspaceEditResponse` | `WorkspaceEdit` | `Response`, because rename returns it and a code action embeds it |
| `ApplyWorkspaceEditResponse` | `ApplyWorkspaceEditResult` | `Response` is the suffix of what a request returns |
| `WorkDoneProgressPayload` | the `WorkDoneProgressBegin`, `WorkDoneProgressReport` and `WorkDoneProgressEnd` values of `$/progress` | one record serves the three reports, told apart by `Kind` |

## Document synchronization

The wire shapes for `textDocument/didOpen`, `didChange`, and `didClose` live in `Protocol/Synchronization/`. The
stateful tracker of the documents, with its request leases and close outcomes, is internal to
`Nickelony.LanguageServer.Provider`.

## Server events

The client raises `DiagnosticsPublished`, `SemanticTokensRefreshRequested`, `DiagnosticRefreshRequested`, and
`ConnectionUnavailable`. It pushes `workspace/didChangeConfiguration` once during initialization with the
document that `SettingsProvider` returns, and `PushSettingsAsync` pushes the current document again after the
host's settings change. `SettingsProvider` returns a `JsonNode`, so the server receives every member name as
the document spells it.

Diagnostics coalesce per document URI: a file URI by its normalized local path, and any other URI by its exact
text. Every handler receives its own one-level-deep copy of the diagnostics sequence, while the diagnostic
entries and their nested values stay shared and read-only.

The threading, marshaling, and disposal rules for event delivery are in the
[consumer integration guide](ConsumerIntegration.md#using-the-client-directly).

## Host hooks

A `LanguageServerClientHooks` object, passed to the `LanguageServerClient` constructor before the options, holds the
optional delegates for the server messages that the client does not surface by itself:

```csharp
await using var client = new LanguageServerClient(
    [workspaceRootDirectoryPath],
    stdio,
    new LanguageServerClientHooks { Progress = report => Console.WriteLine(report.Value.Title) });
```

| Hook | Server message | Without the hook |
| --- | --- | --- |
| `ShowMessage` | `window/showMessage` | the logger receives the message at the level of its type |
| `ShowMessageRequest` | `window/showMessageRequest` | the logger receives the message, and the client answers `null` |
| `Progress` | `$/progress` with a work-done report | the client drops the notification |
| `ApplyWorkspaceEdit` | `workspace/applyEdit` | the client answers that it did not apply the edit |

- A set `Progress` hook advertises `window.workDoneProgress`, and a set `ApplyWorkspaceEdit` hook advertises
  `workspace.applyEdit`; without the hook the client advertises neither, whatever the host's capabilities payload
  says, so a server sends progress and edits only to a client that handles them.
- `ShowMessage` and `Progress` run one at a time on a thread-pool thread, in the order the server sent the
  messages, so they return promptly: a later server message waits for them, while the answers to the host's own
  requests do not. A host that touches its UI posts to it.
- `ShowMessageRequest` and `ApplyWorkspaceEdit` return a task, so no server message waits for them, and they may
  take as long as the answer needs. Their token is canceled when the server cancels the request or the client is
  disposed.
- A `ShowMessageRequest` answer that names none of the offered actions counts as no choice.
- A server's edit can name any file the user can write, whatever the workspace roots are, so a host that confines
  the server to its workspace refuses an edit outside it in the `ApplyWorkspaceEdit` hook.
- A hook that throws is logged, and the message is answered as if the hook were unset; the server hears only a
  fixed reason when `ApplyWorkspaceEdit` fails, and not the exception text. A message from a connection generation
  that does not accept server messages is dropped.
- A `$/progress` notification whose value is not a `begin`, `report` or `end` report, such as a partial result,
  does not reach the hook.
- The provider framework offers the same hooks as shared models in `LanguageServerFeatureProviderHooks`, so a
  host that drives a provider never sees the wire types.

## Malformed-payload tolerance

A malformed protocol payload follows a per-feature tolerance policy:

| Payload | Tolerance |
|---|---|
| Malformed list entries | skipped with a warning, except `workspace/configuration` request items, which keep their position as a placeholder answered with `null` |
| Pull-diagnostics full-report `items` | one malformed diagnostic is skipped with a warning, matching push diagnostics; a non-array `items` root is rejected instead of degrading to an empty report |
| Edit-bearing collections (`documentChanges`, `changes`, `additionalTextEdits`) | a non-array root is rejected instead of degrading to an empty edit list |
| `publishDiagnostics` that cannot be read as well-formed | dropped at the RPC target so it can never clear stored diagnostics (`PublishDiagnosticsParams.IsDegraded` marks a payload a host deserializes directly) |
| Hover response with a malformed optional `range` | contents kept, range dropped |
| Hover `contents` element that is not markup | skipped with a warning; the other elements are kept |
| Documentation member that is neither a string nor a markup object | read as absent with a warning; the item or signature is kept |
| Parameter `label` that is neither a string nor a pair of integers | read as the empty label, so the parameter keeps its position |
| Definition target without a usable absolute URI | skipped; the definition reader is deliberately stricter than its sibling readers because a target is dereferenced to open a document |

## Limitations

- **Workspace folders are fixed at construction.** Each entry is validated: null, empty, whitespace-only, and
  relative entries are rejected, and so are duplicates, by normalized-path identity. Every entry is advertised
  through `workspace/workspaceFolders`, with the first as the primary `rootUri`, and the client never sends
  `workspace/didChangeWorkspaceFolders`.
- **An empty list models a folderless session.** The list is valid, and `rootUri` and `workspaceFolders` are sent
  as `null`.
- **Positions are UTF-16 code units.** The client advertises `general.positionEncodings: ["utf-16"]` and fails
  the startup when a server selects another encoding.
- **Text-document synchronization is required by default.** `RequireTextDocumentSynchronization` rejects servers
  that advertise neither full nor incremental `textDocumentSync`; set it to `false` to accept such servers.
- **Dynamic registration is unsupported.** The RPC target logs and ignores `client/registerCapability`
  and `client/unregisterCapability`, and the `initialize` payload sets `dynamicRegistration` to `false` wherever a
  capability object already has it (see [Capability negotiation](#capability-negotiation)).
- **`window/showDocument` and `telemetry/event` have no hook.** The client logs the telemetry event at debug level and
  does not handle `window/showDocument`, which a server then sees as an unknown method.
- **The `textDocumentSync` options `openClose`, `save`, `willSave`, and `willSaveWaitUntil` are not modeled.** The
  client sends no document notification itself: `Nickelony.LanguageServer.Provider` sends `didOpen`/`didClose`
  even when the server advertises `openClose: false`, and `save: true` never triggers a `didSave`.
- **Child-process lifetime binding uses a Windows job object.** On other platforms the client relies on the
  graceful `shutdown`/`exit` teardown, which waits a short grace period for the process to exit on its own after
  `exit` before forcing termination.
- **`workspace/configuration` request items are answered from the single global settings snapshot.** A
  server-requested per-resource `scopeUri` is not honored, and without a settings provider the client answers from
  an empty snapshot and pushes an empty `workspace/didChangeConfiguration` payload.
- **Pull diagnostics is modeled at the wire level only.** The client ships the `textDocument/diagnostic` request
  payload, the full and unchanged report shapes, and raises `DiagnosticRefreshRequested`. The request-and-apply
  loop that refreshes a host's diagnostics cache lives in `Nickelony.LanguageServer.Provider`, which runs it for a
  server that advertises `diagnosticProvider`.
- **The `Lua` provider consumes push diagnostics only.** Its client capabilities advertise no pull support, and
  `textDocument/publishDiagnostics` is its only delivery model.
- **The send surface stays generic.** The client exposes `SendRequestAsync<TResult>`/`SendNotificationAsync` plus
  the built-in `Protocol/` records, not a per-feature typed facade (that lives in
  `Nickelony.LanguageServer.Provider`/`Nickelony.LanguageServer.Lua`). `LspMethodNames` holds the method strings
  those two members take, and only the methods a host may send are public: the client sends the lifecycle methods
  and handles every server-to-client method itself. A payload a host builds itself must be
  serialized with `LanguageServerClient.CreateProtocolSerializerOptions()`, because the client normalizes only the
  payloads it builds (capabilities, initialization options, settings).
- **There is no restart or orderly stop method.** A restart is `EnsureStartedAsync` again after the connection was marked
  unhealthy (the documented restart contract), and stopping is disposal.
- **Completion snippets stay raw.** The client forwards `insertTextFormat` and the snippet text untouched, so
  tabstop expansion stays the consuming host's commit-time concern.
- **`ConnectionUnavailable` reports only the lost generation.** A runtime connection loss exposes no cause. A
  startup failure is exposed through `LastStartupException`.
- **`EnsureStartedAsync` requires a cancellation token.** No overload without one exists.
