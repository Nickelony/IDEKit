# <img width="32" height="32" alt="Nickelony IDEKit" src="https://github.com/user-attachments/assets/3b8c6718-3f76-4780-9872-168eee8fab74" /> Nickelony IDEKit - .NET editor and language-server libraries

A collection of lightweight .NET libraries for building editors and IDEs: editor primitives and bindings,
a key-binding command surface, a quick pick for a command palette or a quick open, and language-server providers.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

## Features

### IDEKit editor toolkit (`Nickelony.IDEKit.*`)

- **Dependency-free text primitives** - comment and string scanning, auto-closing, diffing, editing,
  find/replace, pathing, and diagnostics.
- **Language-feature contracts** - completion, hover, signature help, code actions, diagnostics,
  document and workspace symbols, and snippet payloads as plain editor types.
- **Document authority** - open, reload, rename, delete, and edit application over a file system,
  with reservations that serialize conflicting operations; an optional view-coordination layer
  keeps attached document views in sync.
- **Editor bindings** - the WPF AvalonEdit binding (editing, language-feature controllers, Markdown
  rendering, TextMate highlighting) and its cross-platform AvaloniaEdit mirror.
- **Batch process execution** - run an external process to completion and capture its output.

### KeyBindings command surface (`Nickelony.KeyBindings.*`)

- **Command catalog and dispatch** - a toolkit-neutral command surface with key bindings, key
  dispatch, and typed-text handling.
- **Rebinding and persisted overrides** - validated, persisted user overrides with a defined
  precedence order, plus toolkit-free Windows, macOS, and Linux display-text formatters and WPF and
  Avalonia input adapters.

### Quick pick (`Nickelony.QuickPick.*`) and fuzzy matching (`Nickelony.Matching`)

- **Command palette and quick open** - a toolkit-neutral controller that picks a mode by the prefix of the typed
  text, queries its provider, filters, ranks and previews the items, and accepts one; a host offers the modes it
  wants, from a command palette alone to the files, commands and symbols of an IDE.
- **WPF and Avalonia windows** - a popup with an input over a virtualized list, placed against any element of a
  host, with no editor control involved.
- **Fuzzy matching** - the subsequence scoring of the VS Code quick open and the word matching of its command
  palette, for any list that filters as the user types.

### Language-server family (`Nickelony.LanguageServer.*`)

- **Editor-first contracts** - completion, hover, definition, references, rename, formatting,
  signature help, document and workspace symbols, code actions, diagnostics, and semantic tokens,
  expressed as plain editor types with zero dependency on any LSP implementation.
- **Real LSP client** - process hosting, `initialize` handshake, capability negotiation,
  JSON-RPC over stdio via [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc).
- **Lifecycle and recovery** - restart-on-crash at the provider layer, connection generation fencing, graceful
  shutdown, and workspace re-sync after restart.
- **Document tracking** - full and incremental text-document synchronization, plus a workspace
  file watcher that forwards file changes to the server.
- **Plug-and-play language packages** - point the Lua package at a `lua-language-server` executable, or the
  Roslyn package (C# and Visual Basic) at `roslyn-language-server`, and get
  language features, diagnostics, and semantic coloring in the editor.

## Getting started

### Consume a ready provider

The fastest way to get going is to reference the **Lua** package and host a
`LanguageServerFeatureProvider` over `LuaLanguageServerProfileFactory.Create()` in the editor. The **Roslyn**
package follows the same shape with `RoslynLanguageServerProfileFactory`, which serves C# or Visual Basic through
`RoslynLanguage.CSharp` or `RoslynLanguage.VisualBasic`:

```sh
dotnet add package Nickelony.LanguageServer.Lua --prerelease
```

```csharp
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Lua;
using Nickelony.LanguageServer.Provider;
using Nickelony.LanguageServer.Provider.Abstractions;

var provider = new LanguageServerFeatureProvider(
    LuaLanguageServerProfileFactory.Create(),
    workspaceRootDirectoryPaths: ["/home/user/workspace"],   // for example @"C:\my\workspace" on Windows
    serverExecutablePath: "/opt/lua-language-server/bin/lua-language-server");

provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    // Diagnostics for eventArgs.DocumentId may arrive on a background thread - marshal to the
    // owning thread and show them in the editor's error list / squiggles.
};

const string sourceText = "local text = 'hi'\ntext:";
provider.OpenDocument("/home/user/workspace/main.lua", sourceText);

var completions = await provider.GetCompletionItemsAsync(
    new CompletionRequest("/home/user/workspace/main.lua", sourceText, caretOffset: sourceText.Length));
```

### Compose a provider

A new language walks the composition path. Reference **Provider**, which brings **Provider.Abstractions** and
**Client** transitively, and ship a factory that returns a `LanguageServerProfile`. The profile carries what the
sealed `LanguageServerFeatureProvider` needs: the language identifier and display names, the watch
specifications, the client capabilities and the few hooks that only code can express:

```csharp
using Nickelony.LanguageServer.Provider;

public static class MyLanguageServerProfileFactory
{
    public static LanguageServerProfile Create() =>
        new()
        {
            LanguageId = "mylang",
            LanguageDisplayName = "My language",
            ServerDisplayName = "My language server",
            WatchSpecifications = [new("*.mylang", IncludeSubdirectories: true)],

            // The message defaults, the empty settings document and the default hooks fit a simple language.
            // A diagnostic mapping policy turns diagnostics on.
            DiagnosticMappingPolicy = DiagnosticMappingPolicy.Default,
        };
}
```

A host passes the profile, the workspace roots and the server path to the provider:

```csharp
using Nickelony.LanguageServer.Provider;

var provider = new LanguageServerFeatureProvider(
    MyLanguageServerProfileFactory.Create(),
    workspaceRootDirectoryPaths: ["/home/user/workspace"],
    serverExecutablePath: "/opt/my-language-server/my-language-server");
```

The [Provider README](src/LanguageServer/Nickelony.LanguageServer.Provider/README.md) and the
[provider authoring guide](docs/ProviderAuthoring.md) describe the profile end to end; the Lua
package is the reference implementation.

See the [Lua package README](src/LanguageServer/Nickelony.LanguageServer.Lua/README.md) for the full example, or the
[Provider](src/LanguageServer/Nickelony.LanguageServer.Provider/README.md),
[Client](src/LanguageServer/Nickelony.LanguageServer.Client/README.md), and
[Provider.Abstractions](src/LanguageServer/Nickelony.LanguageServer.Provider.Abstractions/README.md)
READMEs for the lower layers.
The [consumer integration guide](docs/ConsumerIntegration.md) documents construction,
disposal, event marshaling, document leases, and cancellation behavior.

## Architecture

At run time the editor calls a language package, which drives the language server through the provider
framework and the LSP client. The [dependency diagram](#dependency-diagram) lists each package's references.

```
┌───────────────────────────────┐
│         The editor            │
└───────────────┬───────────────┘
                │ uses
┌───────────────▼───────────────┐
│   Nickelony.LanguageServer    │  language packages
│   .Lua / .Roslyn              │  (LanguageServerProfile factories)
└───────┬───────────────┬───────┘
        │               │
        │       ┌───────▼──────────────┐
        │       │ Provider             │  provider framework: lifecycle, document sync,
        │       │ (watcher + requests) │  workspace watching, request machinery
        │       └───────────────┬──────┘
        │                       │
┌───────▼───────────────┐ ┌─────▼───────────────┐
│ Provider.Abstractions │ │ Client              │  LSP over stdio (StreamJsonRpc)
│ (contracts)           │ │ (process + RPC)     │
└───────────────────────┘ └──────┬──────────────┘
                                 │ stdio (JSON-RPC)
                         ┌───────▼──────────────┐
                         │  Language server     │  for example LuaLS or roslyn-language-server
                         └──────────────────────┘
```

## Requirements

- .NET 8. The language-server packages target `net8.0`; the WPF-targeted packages
  (`Nickelony.IDEKit.AvalonEdit*`, `Nickelony.KeyBindings.Wpf` and `Nickelony.QuickPick.Wpf`) target
  `net8.0-windows`, while the cross-platform AvaloniaEdit mirror
  (`Nickelony.IDEKit.AvaloniaEdit*`), `Nickelony.KeyBindings.Avalonia` and `Nickelony.QuickPick.Avalonia` target `net8.0`.
- The **Lua** package additionally needs the `lua-language-server` executable at runtime
  (download from the [LuaLS releases](https://github.com/LuaLS/lua-language-server/releases)).
- The **Roslyn** package needs the `roslyn-language-server`
  executable at runtime. Get it as a .NET tool, `dotnet tool install --global roslyn-language-server --prerelease`,
  which needs a newer .NET runtime than the packages target ([server runtime](docs/RoslynGuide.md#server-runtime)
  names it and says how a missing one shows). The packages themselves target .NET 8.

## Package groups

A host takes the groups that cover its feature and none of the others. The groups are a catalog of what ships;
the [architecture guide](docs/ArchitectureGuide.md#tiers) defines tiers as dependency levels and states the
direction of their references. Each package name links to its folder and README, and the [dependency
diagram](#dependency-diagram) lists each package's references.

| Group | Packages | Purpose |
|---|---|---|
| **Foundations** (UI-framework-free) | [`Nickelony.IDEKit.Core`](src/IDEKit/Nickelony.IDEKit.Core/), [`Nickelony.IDEKit.LanguageFeatures`](src/IDEKit/Nickelony.IDEKit.LanguageFeatures/), [`Nickelony.IDEKit.LanguageFeatures.Controllers`](src/IDEKit/Nickelony.IDEKit.LanguageFeatures.Controllers/), [`Nickelony.IDEKit.Markdown`](src/IDEKit/Nickelony.IDEKit.Markdown/), [`Nickelony.IDEKit.TextMate`](src/IDEKit/Nickelony.IDEKit.TextMate/), [`Nickelony.IDEKit.FileWatching`](src/IDEKit/Nickelony.IDEKit.FileWatching/), [`Nickelony.IDEKit.Workspace`](src/IDEKit/Nickelony.IDEKit.Workspace/), [`Nickelony.IDEKit.Workspace.QuickPick`](src/IDEKit/Nickelony.IDEKit.Workspace.QuickPick/), [`Nickelony.IDEKit.Workspace.Views`](src/IDEKit/Nickelony.IDEKit.Workspace.Views/) | Text primitives, editor contracts, file watching, document authority, the file list of a quick open, and optional document-view coordination. Reusable from any UI toolkit. |
| **Editor bindings** | [`Nickelony.IDEKit.AvalonEdit`](src/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit/), [`Nickelony.IDEKit.AvalonEdit.LanguageFeatures`](src/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit.LanguageFeatures/), [`Nickelony.IDEKit.AvaloniaEdit`](src/IDEKit/AvaloniaEdit/Nickelony.IDEKit.AvaloniaEdit/), [`Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures`](src/IDEKit/AvaloniaEdit/Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures/) | Editor UI bindings: editing, documents, status, and the language-feature controllers, for WPF (AvalonEdit) and cross-platform Avalonia (AvaloniaEdit). |
| **Optional integrations** | [`Nickelony.IDEKit.AvalonEdit.Markdown`](src/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit.Markdown/), [`Nickelony.IDEKit.AvalonEdit.TextMate`](src/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit.TextMate/), [`Nickelony.IDEKit.AvaloniaEdit.Markdown`](src/IDEKit/AvaloniaEdit/Nickelony.IDEKit.AvaloniaEdit.Markdown/), [`Nickelony.IDEKit.AvaloniaEdit.TextMate`](src/IDEKit/AvaloniaEdit/Nickelony.IDEKit.AvaloniaEdit.TextMate/), [`Nickelony.IDEKit.Processes`](src/IDEKit/Nickelony.IDEKit.Processes/) | Leaf packages for a specific feature; each is optional. `Nickelony.IDEKit.Processes` has no editor dependency. |
| **Command surface** (separate family, toolkit-neutral) | [`Nickelony.KeyBindings`](src/KeyBindings/Nickelony.KeyBindings/), [`Nickelony.KeyBindings.Wpf`](src/KeyBindings/Nickelony.KeyBindings.Wpf/), [`Nickelony.KeyBindings.Avalonia`](src/KeyBindings/Nickelony.KeyBindings.Avalonia/) | Command catalog, key bindings, persisted overrides, validation, rebinding, and key dispatch for any application with a command surface. |
| **Quick pick** (separate family, toolkit-neutral) | [`Nickelony.QuickPick`](src/QuickPick/Nickelony.QuickPick/), [`Nickelony.QuickPick.Wpf`](src/QuickPick/Nickelony.QuickPick.Wpf/), [`Nickelony.QuickPick.Avalonia`](src/QuickPick/Nickelony.QuickPick.Avalonia/), [`Nickelony.Matching`](src/Matching/Nickelony.Matching/) | A command palette or a quick open for any application: the controller, its WPF and Avalonia windows, and the fuzzy matching it ranks with. |
| **Experimental extras** | [`Nickelony.IDEKit.JsonSchema`](src/IDEKit/Nickelony.IDEKit.JsonSchema/) | JSON Schema **vocabulary index**, not a validator or context-aware completion engine. |
| **Language-server packages** | [`Nickelony.LanguageServer.Provider.Abstractions`](src/LanguageServer/Nickelony.LanguageServer.Provider.Abstractions/), [`Nickelony.LanguageServer.Client`](src/LanguageServer/Nickelony.LanguageServer.Client/), [`Nickelony.LanguageServer.Provider`](src/LanguageServer/Nickelony.LanguageServer.Provider/), [`Nickelony.LanguageServer.Lua`](src/LanguageServer/Nickelony.LanguageServer.Lua/), [`Nickelony.LanguageServer.Roslyn`](src/LanguageServer/Nickelony.LanguageServer.Roslyn/) | The provider contract, an LSP client, a provider framework, a Lua provider, and a Roslyn provider for C# and Visual Basic. |

### Package notes and non-goals

- **Foundations are UI-framework-free** - Core, LanguageFeatures, LanguageFeatures.Controllers, Markdown,
  TextMate, FileWatching, Workspace, Workspace.QuickPick, and Workspace.Views target `net8.0` and reference no WPF or AvalonEdit types. The
  AvalonEdit and AvaloniaEdit bindings reuse the Core and LanguageFeatures slices; the
  [editor-binding guide](docs/EditorBindingGuide.md) names those slices and the pieces each binding writes.
- **The quick pick is a family of its own.** `Nickelony.QuickPick*` and `Nickelony.Matching` reference nothing outside
  their family, so a host that wants a command palette in a view with no editor takes `Nickelony.KeyBindings*` and
  `Nickelony.QuickPick*` and nothing from IDEKit. The editor modes (files, open editors, go to line, symbols) live in
  the IDEKit packages that own what they list.
- **Document views are optional workspace architecture.** The workspace family splits document
  authority (`Nickelony.IDEKit.Workspace`, usable headless) from view coordination
  (`Nickelony.IDEKit.Workspace.Views`). A host that only needs document authority never takes
  the view contracts.
- **Base `Nickelony.IDEKit.AvalonEdit` does not depend on the workspace packages**
  (see the diagram below). The workspace packages ship the document-view coordination and the engine-free
  editor view base `WorkspaceDocumentTextViewBase`, so an application that needs that integration derives its
  editor view from the base in its own code.
- **`Nickelony.IDEKit.AvaloniaEdit` mirrors the AvalonEdit binding cross-platform.** Each binding ships the
  same feature slices over its own engine (AvalonEdit over WPF, `Avalonia.AvaloniaEdit` over Avalonia), and
  the [editor sharing guide](docs/EditorSharingGuide.md) records how the bindings share source and
  documentation.
- **The TextMate integrations share the neutral `Nickelony.IDEKit.TextMate` theme model and rule engine and
  the `shared/Editor.TextMate/` sources**, so they carry one ranking implementation (see the
  [optional-integration mirrors](docs/EditorSharingGuide.md#6-the-optional-integration-mirrors) section of the
  guide).
- **The Markdown integrations share the neutral `Nickelony.IDEKit.Markdown` model, theme, and options and
  the `shared/Editor.Markdown/` sources**, so they carry one mapping and one shared test suite. The Markdown
  mirror builds a native Avalonia control tree because Avalonia has no `FlowDocument`.
- **`Nickelony.IDEKit.Processes` is batch-scoped by design**: one request runs
  one process to completion, and its redirected output is captured while the process
  runs. The output-drain grace period bounds the wait for the pipes to close after the exit.
  There is no standard-input piping or streaming support; hosts that need interactive processes
  drive `System.Diagnostics.Process` directly, and the LSP client lives in
  `Nickelony.LanguageServer.Client`.
- **`Nickelony.IDEKit.JsonSchema` is experimental** and stays so until a second
  real consumer or a stable contract justifies promotion. It is not part of the
  core editor contract surface and may change without the usual preview-version
  compatibility guarantees. See its [README](src/IDEKit/Nickelony.IDEKit.JsonSchema/README.md) for the exact
  vocabulary-index contract.

### Dependency diagram

Each line lists the direct references in a package's project file, except the build-time
`Microsoft.CodeAnalysis.PublicApiAnalyzers` that every library references. In-repo packages drop the `Nickelony.`
prefix, and the language-server group also drops `LanguageServer.`; any other name is a NuGet package, and
`Logging` stands for `Microsoft.Extensions.Logging.Abstractions`.

```
IDEKit foundations and leaves:
  IDEKit.Core                          no references (UI-framework-free leaf)
  IDEKit.LanguageFeatures              -> IDEKit.Core
  IDEKit.LanguageFeatures.Controllers  -> IDEKit.Core, IDEKit.LanguageFeatures, Matching, QuickPick, Logging
  IDEKit.Markdown                      -> IDEKit.Core, Markdig, Logging
  IDEKit.TextMate                      -> IDEKit.Core, Logging
  IDEKit.FileWatching                  -> IDEKit.Core, Logging
  IDEKit.Workspace                     -> IDEKit.Core
  IDEKit.Workspace.QuickPick           -> IDEKit.Core, IDEKit.FileWatching, QuickPick
  IDEKit.Workspace.Views               -> IDEKit.Core, IDEKit.LanguageFeatures, IDEKit.Workspace
  IDEKit.Processes                     no references
  IDEKit.JsonSchema                    no references (experimental)

IDEKit editor packages, per engine (<Engine> is AvalonEdit or AvaloniaEdit):
  IDEKit.<Engine>                      -> IDEKit.Core, engine (no Workspace)
  IDEKit.<Engine>.LanguageFeatures     -> IDEKit.<Engine>, IDEKit.Core, IDEKit.LanguageFeatures, IDEKit.LanguageFeatures.Controllers, engine, Logging
  IDEKit.<Engine>.Markdown             -> IDEKit.<Engine>, IDEKit.Core, IDEKit.Markdown, engine, Logging
  IDEKit.<Engine>.TextMate             -> IDEKit.<Engine>, IDEKit.Core, IDEKit.TextMate, TextMateSharp, engine, Logging
  engine: AvalonEdit for WPF, or Avalonia and Avalonia.AvaloniaEdit for Avalonia

KeyBindings packages:
  KeyBindings                          -> Logging (toolkit-neutral)
  KeyBindings.Wpf                      -> KeyBindings (WPF)
  KeyBindings.Avalonia                 -> KeyBindings, Avalonia

QuickPick packages:
  Matching                             no references
  QuickPick                            -> Matching (toolkit-neutral)
  QuickPick.Wpf                        -> QuickPick, Matching (WPF)
  QuickPick.Avalonia                   -> QuickPick, Matching, Avalonia

Language-server packages:
  Provider.Abstractions                -> IDEKit.LanguageFeatures
  Client                               -> StreamJsonRpc, Logging (no editor dependency)
  Provider                             -> Provider.Abstractions, Client, IDEKit.Core, IDEKit.FileWatching, IDEKit.LanguageFeatures, Logging
  Lua                                  -> Provider, IDEKit.Core, IDEKit.FileWatching
  Roslyn                               -> Provider, Client, IDEKit.Core, IDEKit.FileWatching
```

## Building and testing

```sh
dotnet build Nickelony.IDEKit.slnx
dotnet test  Nickelony.IDEKit.slnx
```

The test suite covers all packages, plus opt-in integration tests that drive a real LuaLS or Roslyn language server
process. Neither server is bundled, so those tests report inconclusive until `NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE`
names a LuaLS archive and `NICKELONY_ROSLYN_LANGUAGE_SERVER` names `Microsoft.CodeAnalysis.LanguageServer.exe`; the
[LuaLS fixture README](tests/LanguageServer/Nickelony.LanguageServer.Lua.Tests/Integration/README.md) states
what the archive must contain. Run `dotnet test` for the current counts.

Tests in the `InteractiveWindow` category show real WPF content in a window, so they need an interactive window
station (a desktop session). The [test-support README](tests/TestSupport/README.md) documents each category and
what a headless run reports.

## Samples

Two runnable editors, a WPF host and an Avalonia host, wire the editor, language-feature, key-binding, quick-pick,
workspace and process packages together. Start one with `dotnet run --project samples/Nickelony.Samples.Wpf` or the
Avalonia head, and read the [sample README](samples/README.md) for what each file shows and how to point the editor at
a language server.

## Repository layout

```
src/        the packages, one folder per family
samples/    the runnable sample hosts, WPF and Avalonia
shared/     product source compiled into more than one project; shared/Samples/ holds the sample source
tests/      the test projects and their TestSupport/ helpers; tests/TestSupport/ states the environment-category policy
docs/       the guides, published as a DocFX site
icons/      the package and site icons
```

## License

[MIT](LICENSE) © 2026 Kewin Kupilas.
