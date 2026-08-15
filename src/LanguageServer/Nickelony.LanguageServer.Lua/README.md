# Nickelony.LanguageServer.Lua

**A lightweight Lua language-server provider** for the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family, backed by [LuaLS](https://github.com/LuaLS/lua-language-server) (`lua-language-server` executable).

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Lua.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package turns [LuaLS](https://github.com/LuaLS/lua-language-server) into an IntelliSense
provider for editor-class applications. It implements the language-service contracts
from [`Nickelony.LanguageServer.Abstractions`](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
on top of the [`Nickelony.LanguageServer.Client`](https://www.nuget.org/packages/Nickelony.LanguageServer.Client)
LSP client, through the language-neutral
[`Nickelony.LanguageServer.Provider`](https://www.nuget.org/packages/Nickelony.LanguageServer.Provider)
framework. This package supplies the LuaLS-specific hooks and mapping. Construct the provider,
synchronize documents, and consume the callbacks.

## Getting started

```sh
dotnet add package Nickelony.LanguageServer.Lua
```

```csharp
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Signatures;
using Nickelony.LanguageServer.Abstractions;
using Nickelony.LanguageServer.Lua;

// Use any absolute local path; `C:\my\workspace` on Windows is as valid as
// `/home/user/workspace` on Linux/macOS.
var workspaceRoot = "/home/user/workspace";
var serverExecutablePath = "/opt/lua-language-server/bin/lua-language-server";

// Provider settings are optional; start from LuaLanguageServerOptions.Default and override the LuaLS
// defaults (runtime version, library folders, disabled diagnostics, semantic highlighting) you need,
// or omit the `options` argument to keep them all.
var options = LuaLanguageServerOptions.Default with
{
    RuntimeVersion = "LuaJIT",
    AdditionalLibraryDirectories = [Path.Combine(workspaceRoot, "vendor")],
    DisabledDiagnostics = ["undefined-global"]
};

var provider = new LuaLanguageServerIntelliSenseProvider(
    workspaceRootDirectoryPaths: [workspaceRoot],
    serverExecutablePath: serverExecutablePath,
    options: options,
    logger: loggerFactory.CreateLogger<LuaLanguageServerIntelliSenseProvider>());

// Callbacks may fire on background threads - marshal to the owning synchronization
// context (if any) before touching thread-affine state.
provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    // e.g. update the diagnostic markers for `eventArgs.FilePath`.
};

provider.SemanticTokensUpdated += (_, eventArgs) =>
{
    // e.g. render the tokens for `eventArgs.FilePath`.
};

// Track a document as the user edits it.
string filePath = Path.Combine(workspaceRoot, "main.lua");
provider.OpenDocument(filePath, sourceText);

// Then drive IntelliSense from your command handlers:
var completions = await provider.GetCompletionItemsAsync(
    new LanguageServerCompletionRequest(filePath, sourceText, new TextPosition(line, column), triggerCharacter: "."));
var hover      = await provider.GetHoverAsync(new LanguageServerHoverRequest(filePath, sourceText, new TextPosition(line, column)));
var definition = await provider.GetDefinitionAsync(new LanguageServerDefinitionRequest(filePath, sourceText, new TextPosition(line, column)));
// The optional trigger context forwards how the request was triggered (and the shown payload on
// retriggers); omit it for a position-only request.
var signatures = await provider.GetSignatureHelpAsync(
    new LanguageServerSignatureHelpRequest(filePath, sourceText, new TextPosition(line, column),
        new TextSignatureHelpContext(TextSignatureHelpTriggerKind.TriggerCharacter, "(")));
var references = await provider.GetReferencesAsync(new LanguageServerReferenceRequest(filePath, sourceText, new TextPosition(line, column)));
var edits      = await provider.RenameSymbolAsync(new LanguageServerRenameRequest(filePath, sourceText, new TextPosition(line, column), "newName"));
var formatted  = await provider.FormatDocumentAsync(new LanguageServerFormattingRequest(
    filePath,
    sourceText,
    new TextFormattingOptions(tabSize: 4, insertSpaces: true)));

provider.UpdateDocument(filePath, updatedSourceText);
provider.CloseDocument(filePath);
provider.Dispose();
```

For the complete construction, disposal, threading, document-reference, and cancellation
contract, see the repository's [consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md).

> **Note:** every `*Async` IntelliSense method carries the current document text (directly or in its
> request record), so you can drive them from a live document buffer without waiting for server
> round-trips of edits.

> **Document contract:** every document API takes a local file-system path (a relative path is
> anchored to the current process working directory). A path that cannot be normalized to a local
> file is ignored - the call is a no-op that returns its empty or `null` fallback, while a
> `null` path argument throws `ArgumentNullException` - and untitled or purely virtual documents
> are not supported because the language server operates on on-disk files. For the capability-gating
> and lazy-startup rule, see the [consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md).

## Requirements

- .NET 8 (package targets `net8.0`, cross-platform).
- The **LuaLS executable** at runtime - download the `lua-language-server` binary from the
  [LuaLS releases page](https://github.com/LuaLS/lua-language-server/releases) and pass its path
  to the provider. Pass `serverExecutablePath: null` when no installation is available: the provider
  still constructs, reports a persistent startup failure, and every call returns its documented
  fallback value until the provider is recreated with a valid path.

## Dependencies

- `Nickelony.LanguageServer.Abstractions`
- `Nickelony.LanguageServer.Client`
- `Nickelony.LanguageServer.Provider`
- `Nickelony.IDEKit.IntelliSense`
- `Nickelony.IDEKit.Core`
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3

`StreamJsonRpc` and the remaining client transport dependencies arrive transitively through
`Nickelony.LanguageServer.Client`.

## Features

- **Completion** - contextual item lists with a protocol-faithful kind mapping, the protocol
  `sortText`/`preselect` fields, `Priority` ordering hints that agree with the protocol `sortText`
  order, and `completionItem/resolve` support. Snippet insert texts (`insertTextFormat: 2`) pass
  through with their raw placeholder syntax and the shared `TextCompletionInsertTextFormat` marker,
  so a host expands `$1`/`${1:default}`/`${1|a,b|}` tabstops at commit time. The server's list is
  projected unchanged: duplicate entries stay visible in response order, each carrying its own
  resolve round trip.
- **Diagnostics** - per-document diagnostics with their server messages (trimmed; a blank message
  becomes `Unknown Lua diagnostic.`) and the protocol `source`/`code` attribution preserved,
  delivered through `DiagnosticsUpdated` and cached for on-demand reads. A diagnostic without a
  severity is presented as an error and an unknown severity as a warning, because the protocol
  defines no severity default.
- **Hover** - rich hover content with markdown support.
- **Navigation** - go-to-definition and find-references.
- **Document symbols** - the hierarchical outline tree for a document (names, kinds, full and
  selection ranges, nested children) mapped onto the shared offset-based `TextDocumentSymbol`
  model. The client advertises `hierarchicalDocumentSymbolSupport`, so LuaLS normally answers with
  the hierarchical form; the flat `SymbolInformation` fallback is still handled.
  Document symbols are best-effort: a server that does not advertise the capability yields an
  empty outline without a request.
- **Code actions** - quick fixes and refactorings for a document range mapped into
  `TextCodeAction` values with their workspace edits; the request context carries the cached
  diagnostics reported for the range, so LuaLS's diagnostic quick fixes stay reachable. Command-only
  actions are omitted because the client does not support `workspace/executeCommand`, and the
  advertised `codeActionLiteralSupport` keeps LuaLS on the literal action shape.
- **Rename & formatting** - symbol rename with workspace edits, and document formatting
  respecting LuaLS settings.
- **Signature help** - parameter info for function calls.
- **Semantic tokens** - shared `SemanticToken` values decoded from the server's full token stream and
  announced through `SemanticTokensUpdated`, with `GetSemanticTokens` as the pull-side read of the same
  cache. A refresh runs after every successful open or edit synchronization and whenever LuaLS requests
  one; a failed refresh keeps the previously cached tokens. LuaLS does not serve delta requests, so the
  provider always requests full payloads. The event, the cached read, and the refresh controller come
  from the framework's generic `ILanguageServerSemanticTokensProvider` contract, so they are available
  to any provider rather than being Lua-specific.
- **Configurable settings** - LuaLS runtime version, library folders, disabled diagnostics, and
  semantic highlighting overrides through `LuaLanguageServerOptions`. The defaults mirror the LuaLS
  defaults; two deliberate deviations keep the provider self-contained: third-party checks are
  disabled because a library cannot answer interactive prompts, and completion call snippets are
  enabled with `Replace` because the provider consumes snippet insert texts end to end. Library
  folders that live in the workspace can be declared through the workspace's `.luarc.json`
  (`workspace.library`); the provider watches the configuration file and asks LuaLS to reload it when
  it changes, so a folder that appears later is picked up automatically.
- **Workspace coordination** - tracking of open documents, watching of every configured workspace root
  for external changes, and re-opening of tracked documents when the server restarts. A watcher
  failure is contained to its own root.

## Limitations

- Diagnostics are consumed from server pushes (`textDocument/publishDiagnostics`); LuaLS is push-only, so the
  framework's pull-diagnostics loop is unused here. Diagnostics for a document that was never synchronized are
  dropped because there is no content
  to map server ranges against, and a payload that omits the protocol version is mapped against the content
  tracked at publish time.
- Definition resolves the first target a server returns; a response that offers several targets surfaces only its
  first usable entry, because the shared definition model carries one location.
- Code-action requests rebuild the protocol diagnostics context from the last published snapshot. When the
  document changed after that publish, the context stays empty until the next publish instead of sending server
  coordinates that describe different text.
- Positions are fixed to UTF-16 code units; the client does not negotiate another position encoding.
- Dynamic registration is unsupported by design: the client advertises `dynamicRegistration = false` and ignores
  `client/registerCapability` notifications.
- Semantic tokens cached for a document reflect the version they were decoded for and can be briefly stale while
  a newer refresh is in flight; `SemanticTokensUpdated` announces each new set.
- Each workspace root gets its own watcher; a watcher failure is contained to that root and reported through
  `WorkspaceWatcherFailed`.
- Configuration watching covers `.luarc.json`/`.luarc.jsonc` files in each workspace **root only**; a custom
  configuration file supplied through LuaLS's `--configpath` flag is not watched, and the provider exposes no
  server-argument surface to pass the flag.
- Workspace folders are fixed at construction; `workspace/didChangeWorkspaceFolders` is not sent.
- A rename edit that contains resource operations (file create, rename, or delete) is dropped whole instead of
  being applied partially: the shared workspace-edit model represents text edits only.

## License

MIT © 2026 Kewin Kupilas.
