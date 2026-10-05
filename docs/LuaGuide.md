# Lua guide

This guide is the host-facing reference for `Nickelony.LanguageServer.Lua`, the LuaLS-backed provider of the
Nickelony Language Server family. It owns the LuaLS executable requirement, the provider options, the
configuration-file watching, the per-feature mapping notes, and the limitations.

The lifecycle, threading, document-lease, and disposal contract is provider-neutral and lives in the
[consumer integration guide](ConsumerIntegration.md); the provider implementation model is in the
[provider authoring guide](ProviderAuthoring.md). A running host is
[`SampleLanguageServers`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/LanguageServers/SampleLanguageServers.cs).

## Position

`Nickelony.LanguageServer.Lua` turns [LuaLS](https://github.com/LuaLS/lua-language-server)
(`lua-language-server`) into a language-feature provider for editor-class applications. It implements the
language-service contracts from `Nickelony.LanguageServer.Provider.Abstractions` on top of the
`Nickelony.LanguageServer.Client` LSP client, through the language-neutral `Nickelony.LanguageServer.Provider`
framework.

Its public surface is the profile factory (`LuaLanguageServerProfileFactory`) and the option type
(`LuaLanguageServerOptions`). The host passes the profile to `LanguageServerFeatureProvider`.

## The LuaLS executable

The provider launches the `lua-language-server` executable the host passes to its constructor. Install the
binary from the [LuaLS releases page](https://github.com/LuaLS/lua-language-server/releases) and pass its path.

A `null` or whitespace-only `serverExecutablePath` is valid when no installation is available: the provider
still constructs, its first call reports a persistent startup failure, and every call returns its documented
fallback value until the provider is recreated with a valid path.

`LanguageServerFeatureProvider.Create` drives a client the host already owns instead of launching a server; ownership of the
supplied `ILanguageServerClient` transfers to the provider. The options below apply only to a client the provider
launches itself, so a host-supplied client carries the settings it was built with.

## Options

`LuaLanguageServerOptions` overrides the LuaLS settings payload for a workspace, and each member documents its
default and the setting it writes. A host starts from `LuaLanguageServerOptions.Default` and overrides only the members
it needs, or omits `options` entirely to keep the defaults.

Two LuaLS settings are fixed: `workspace.checkThirdParty` is always `Disable` (a library cannot answer LuaLS's prompts),
and `completion.callSnippet` is always `Replace` (the provider consumes snippet insert texts end to end).

## Library folders and `.luarc.json`

Additional library folders can be declared through `AdditionalLibraryDirectories` (workspace-relative paths are
resolved by LuaLS against the workspace folder) or through the workspace's own `.luarc.json` as
`workspace.library`. The provider watches the configuration file and sends `workspace/didChangeConfiguration` when it
changes, so a folder that appears later is picked up automatically.

Only the `.json` and `.jsonc` extensions are accepted, so a sidecar such as `.luarc.json.bak` or an editor backup
like `.luarc.json~` does not trigger a reload.

## Feature notes

- **Completion** carries a protocol-faithful kind mapping, the protocol `sortText`/`preselect` fields,
  `Priority` ordering hints that agree with the protocol `sortText` order, and `completionItem/resolve` support.
  Snippet insert texts (`insertTextFormat: 2`) pass through with their raw placeholder syntax and the shared
  `CompletionInsertTextFormat` marker, and the editor bindings insert them at commit, selecting the first placeholder so
  Tab steps to the next ([snippet commits](EditorLanguageFeaturesGuide.md#snippet-commits)). The server's list is
  projected unchanged: duplicate entries stay visible in response order, each carrying its own resolve round trip.
- **Diagnostics** are per document, with their server messages (trimmed; a blank message becomes
  `Unknown Lua diagnostic.`) and the protocol `source`/`code` attribution preserved, delivered through
  `DiagnosticsUpdated` and cached for on-demand reads. A diagnostic without a severity is presented as an error
  and an unknown severity as a warning, because the protocol defines no severity default.
- **Hover** carries rich markdown content.
- **Navigation** covers go-to-definition and find-references.
- **Document symbols** map the hierarchical outline tree (names, kinds, full and selection ranges, nested
  children) onto the shared offset-based `DocumentSymbol` model. The client advertises
  `hierarchicalDocumentSymbolSupport`, so LuaLS normally answers with the hierarchical form; the flat
  `SymbolInformation` fallback is still handled. Symbols are best-effort: a server that does not advertise the
  capability yields an empty outline without a request.
- **Workspace symbols** come from the flat `workspace/symbol` answer, which carries a range for each symbol, so
  LuaLS advertises no `workspaceSymbol/resolve`. LuaLS lists a global function twice, answers an empty query with every
  symbol of the workspace, and answers `null` to a query of several words, so the quick pick sends it the first word
  only.
- **Code actions** map quick fixes and refactorings for a document range into `CodeAction` values with their
  workspace edits or their commands. The request context carries the diagnostics of the request, or the cached
  diagnostics reported for the range, so LuaLS's diagnostic quick fixes stay reachable. LuaLS lists its commands
  in `executeCommandProvider`, so a command action stays, and the host runs it through `ICodeActionCommandRunner`
  after it applied the action's edit. Most commands answer by asking the client to apply an edit
  (`lua.removeSpace`, `lua.solve`, `lua.autoRequire`, `lua.jsonToLua`), which reaches the host through
  `LanguageServerFeatureProviderHooks.ApplyWorkspaceEdit`; `lua.setConfig` ("Mark as defined global", "Disable
  diagnostics in the workspace") writes `.luarc.json` itself, which the configuration-file watcher then reports.
- **Rename** returns the server's edits as a `WorkspaceEdit`, and a blank new name returns `null` without a
  request.
- **Formatting** sends the request's formatting options and returns the server's text edits as a
  `WorkspaceEdit`, or `null` when the server returns none.
- **Signature help** supplies parameter info for function calls.
- **Semantic tokens** are shared `SemanticToken` values decoded from the server's full token stream and
  announced through `SemanticTokensUpdated`, with `GetSemanticTokens` as the pull-side read of the same cache. A
  refresh runs after every successful open or edit synchronization and whenever LuaLS requests one; a failed
  refresh keeps the last cached tokens. LuaLS does not serve delta requests, so the provider always requests full
  payloads.
- **Workspace coordination** tracks open documents, watches every configured workspace root for external changes,
  and re-opens tracked documents when the server restarts.

## Limitations

- **Diagnostics are push-only.** They arrive through `textDocument/publishDiagnostics`; LuaLS never serves a pull
  request, so the framework's pull-diagnostics loop is unused here. Diagnostics for a document that was never
  synchronized are dropped because there is no content to map server ranges against, and a payload that omits the
  protocol version is mapped against the content tracked at publish time.
- **Definition resolves the first target a server returns.** A response that offers several targets surfaces only
  its first usable entry, because the shared definition model carries one location.
- **Code-action requests that name no diagnostics rebuild the protocol diagnostics context from the last published
  snapshot.** When the document changed after that publish, the context stays empty until the next publish
  instead of sending server coordinates that describe different text. A request that sets
  `CodeActionRequest.Diagnostics` sends those diagnostics, whatever the cache holds.
- **Positions are fixed to UTF-16 code units**; the client does not negotiate another position encoding.
- **Dynamic registration is unsupported by design**: the client advertises `dynamicRegistration = false` and
  ignores `client/registerCapability` and `client/unregisterCapability` requests.
- **Semantic tokens cached for a document reflect the version they were decoded for** and can be briefly stale
  while a newer refresh is in flight; `SemanticTokensUpdated` announces each new set.
- **Each workspace root gets its own watcher**; a watcher failure is contained to that root and reported through
  `WorkspaceFileWatchFailed`.
- **Configuration watching covers `.luarc.json`/`.luarc.jsonc` files in each workspace root only.** A custom
  configuration file supplied through LuaLS's `--configpath` flag is not watched.
- **The provider constructor takes the profile, the workspace roots, the executable path, the host hooks, the
  provider options and a logger; `LuaLanguageServerOptions` goes to the profile factory.** A process setting, such as an
  environment variable, an extra argument or the working directory, goes on the profile's `Launch`
  (`profile with { Launch = profile.Launch with { ... } }`). The client timeouts need a host-built
  `LanguageServerClient` passed to `Create`. The client the provider creates reports its connection and
  server-message diagnostics to the provider's logger.
- **Workspace folders are fixed at construction**; `workspace/didChangeWorkspaceFolders` is not sent.
- **A rename edit that contains resource operations** (file create, rename, or delete) is dropped whole instead of
  being applied partially, because the shared workspace-edit model represents text edits only.
- **Documents must be on disk.** Every document API takes a local file-system path; untitled or purely virtual
  documents are not supported, because LuaLS operates on on-disk files. The path-handling rule for member calls
  is in the [consumer integration guide](ConsumerIntegration.md).
