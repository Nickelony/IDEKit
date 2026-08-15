# <img width="32" height="32" alt="Nickelony IDEKit" src="https://github.com/user-attachments/assets/3b8c6718-3f76-4780-9872-168eee8fab74" /> Nickelony IDEKit - .NET editor and language-server libraries

A collection of lightweight .NET libraries for building editors and IDEs: UI-framework-free editor primitives and IntelliSense contracts (`Nickelony.IDEKit.*`) with AvalonEdit and AvaloniaEdit adapters, a host-neutral command and keyboard-shortcut family (`Nickelony.KeyBindings.*`), and a language-server tier (`Nickelony.LanguageServer.*`) - a StreamJsonRpc LSP client plus ready-to-use Lua, C#, and Visual Basic providers (the last two over a shared Roslyn core).

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

## What is this?

This repository is a small, focused collection of .NET libraries that make it easy to build
editors and IDEs. It hosts three sibling families, consumable independently: the
`Nickelony.IDEKit.*` editor toolkit (text primitives, IntelliSense contracts, AvalonEdit
adapters, process execution), the host-neutral `Nickelony.KeyBindings.*` command and
keyboard-shortcut family, and the `Nickelony.LanguageServer.*` family that implements the
editor contracts on top of a real language server. The editor toolkit ships its UI bridges twice - a WPF
(AvalonEdit) adapter and a cross-platform Avalonia (AvaloniaEdit) mirror of the same feature slices. The
language-server tier is layered - contracts, transport,
provider framework, and the concrete language packages
(the full package list, tier groupings, and dependency edges are collected in
[Package tiers](#package-tiers)):

| Package | Purpose |
|---|---|
| [`Nickelony.LanguageServer.Abstractions`](src/LanguageServer/Nickelony.LanguageServer.Abstractions/) | Editor IntelliSense contracts with no external package dependencies - the stable seam between an editor and any language provider. |
| [`Nickelony.LanguageServer.Client`](src/LanguageServer/Nickelony.LanguageServer.Client/) | A lightweight LSP client built on StreamJsonRpc: spawns a language-server process and speaks LSP over stdio. |
| [`Nickelony.LanguageServer.Provider`](src/LanguageServer/Nickelony.LanguageServer.Provider/) | The provider framework: lifecycle, document synchronization, workspace watching, and request machinery for language-server providers. |
| [`Nickelony.LanguageServer.Lua`](src/LanguageServer/Nickelony.LanguageServer.Lua/) | A ready-to-use Lua provider that implements the editor contracts on top of the provider framework and drives the real LuaLS executable. |
| [`Nickelony.LanguageServer.Roslyn`](src/LanguageServer/Nickelony.LanguageServer.Roslyn/) | The shared Roslyn language-server core - handshake, launch profile, and pull diagnostics for Microsoft's Roslyn language server - that a language package derives from. |
| [`Nickelony.LanguageServer.CSharp`](src/LanguageServer/Nickelony.LanguageServer.CSharp/) | A ready-to-use C# provider that implements the editor contracts on top of the Roslyn core. |
| [`Nickelony.LanguageServer.VisualBasic`](src/LanguageServer/Nickelony.LanguageServer.VisualBasic/) | A ready-to-use Visual Basic provider that implements the editor contracts on top of the Roslyn core. |

## Features

Three sibling families, each consumable on its own (see [Package tiers](#package-tiers)).

### IDEKit editor toolkit (`Nickelony.IDEKit.*`)

- **Dependency-free text primitives** - comment and string scanning, auto-closing, diffing, editing,
  find/replace, pathing, and diagnostics with no editor or UI dependency.
- **IntelliSense contracts** - completion, hover, signature help, code actions, diagnostics,
  document symbols, and snippet payloads as plain editor types.
- **Document authority** - open, reload, rename, delete, and edit application over a file system,
  with reservations that serialize conflicting operations; an optional view-coordination layer
  keeps attached document views in sync.
- **Editor adapters** - WPF AvalonEdit bridges (editing, IntelliSense controllers, Markdown
  rendering, TextMate highlighting) and their cross-platform AvaloniaEdit mirror.
- **Batch process execution** - run an external process to completion and capture its output.

### KeyBindings command surface (`Nickelony.KeyBindings.*`)

- **Command catalog and dispatch** - a host-neutral command surface with key bindings, key
  dispatch, and typed-text handling.
- **Rebinding and persisted overrides** - validated, persisted user overrides with a defined
  precedence order, plus toolkit-free Windows, macOS, and Linux display-text formatters and an
  Avalonia input adapter.

### Language-server tier (`Nickelony.LanguageServer.*`)

The bullets below are a short overview; the per-package READMEs are the authoritative feature and
dependency reference for the tier.

- **Editor-first contracts** - completion, hover, definition, references, rename, formatting,
  signature help, document symbols, code actions, diagnostics, and semantic tokens,
  expressed as plain editor types with zero dependency on any LSP implementation.
- **Real LSP transport** - process hosting, `initialize` handshake, capability negotiation,
  JSON-RPC over stdio via [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc).
- **Robust lifecycle** - restart-on-crash at the provider layer, transport versioning, graceful
  shutdown, and workspace re-sync after restart.
- **Document tracking** - full and incremental text-document synchronization, plus a workspace
  file watcher that forwards file changes to the server.
- **Plug-and-play providers** - point the Lua provider at a `lua-language-server` executable, or the
  C# and Visual Basic providers (over the shared Roslyn core) at `roslyn-language-server`, and get
  IntelliSense, diagnostics, and semantic coloring in your editor.

## Getting started

### Consume a ready provider

The fastest way to get going is to reference the **Lua** package and host
`LuaLanguageServerIntelliSenseProvider` in your editor. The **CSharp** and **VisualBasic** packages (over the
shared **Roslyn** core) follow the same shape with `CSharpLanguageServerIntelliSenseProvider` and
`VisualBasicLanguageServerIntelliSenseProvider`:

```xml
<PackageReference Include="Nickelony.LanguageServer.Lua" Version="1.0.0-preview.43" />
```

```csharp
using Nickelony.IDEKit.Core.Text;
using Nickelony.LanguageServer.Abstractions;
using Nickelony.LanguageServer.Lua;

var provider = new LuaLanguageServerIntelliSenseProvider(
    workspaceRootDirectoryPaths: ["/home/user/workspace"],   // e.g. @"C:\my\workspace" on Windows
    serverExecutablePath: "/opt/lua-language-server/lua-language-server");

provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    // Diagnostics for eventArgs.FilePath arrive on a background thread - marshal to the
    // owning thread and show them in the editor's error list / squiggles.
};

provider.OpenDocument("/home/user/workspace/main.lua", sourceText);

var completions = await provider.GetCompletionItemsAsync(
    new LanguageServerCompletionRequest("/home/user/workspace/main.lua", sourceText, new TextPosition(line, column)));
```

### Compose your own provider

A new language walks the composition path: reference **Provider** (which brings
**Abstractions** and **Client** transitively), derive from
`LanguageServerIntelliSenseProviderBase`, and supply the four pieces the
base needs - the workspace root paths, the language's tracked-document store, the watch
specifications, and a `LanguageServerClient` (or `null` when the server binary is
unavailable). The framework ships a shared store with the standard diagnostics and
semantic-token caches, so a provider with ordinary caching needs only its own store when
its needs differ:

```csharp
using Nickelony.LanguageServer.Provider;

public sealed class MyLanguageIntelliSenseProvider : LanguageServerIntelliSenseProviderBase
{
    public MyLanguageIntelliSenseProvider(
        IReadOnlyList<string> workspaceRootDirectoryPaths,
        string? serverExecutablePath,
        ILoggerFactory loggerFactory)
        : base(
            workspaceRootDirectoryPaths,
            new MyDocumentStore(),
            MyWatchSpecifications.All,
            CreateClient(serverExecutablePath, loggerFactory))
    {
    }

    // Implement the language hooks: the configuration rule, the settings payload, the failure
    // reports, and, when the provider exposes them, the diagnostics and semantic-token hooks. The
    // base class already implements the feature members; override one only to change its behavior.
}
```

The [Provider README](src/LanguageServer/Nickelony.LanguageServer.Provider/README.md) and the
[provider authoring guide](docs/ProviderAuthoring.md) describe the hooks end to end; the Lua
package is the reference implementation.

See the [Lua package README](src/LanguageServer/Nickelony.LanguageServer.Lua/README.md) for the full example, or the
[Provider](src/LanguageServer/Nickelony.LanguageServer.Provider/README.md),
[Client](src/LanguageServer/Nickelony.LanguageServer.Client/README.md), and
[Abstractions](src/LanguageServer/Nickelony.LanguageServer.Abstractions/README.md) READMEs for the lower layers.
The [consumer integration guide](docs/ConsumerIntegration.md) documents construction,
disposal, event marshaling, document references, and cancellation behavior.

## Architecture

The diagram and the bullets that follow are a narrative view of the language-server tier; the
[package tiers](#package-tiers) table and the [dependency diagram](#dependency-diagram) are the
authoritative package and dependency references.

```
┌───────────────────────────────┐
│         Your editor           │
└───────────────┬───────────────┘
                │ uses
┌───────────────▼───────────────┐
│   Nickelony.LanguageServer    │  concrete language packages
│   .Lua / .Roslyn              │  (ILanguageServerIntelliSenseProvider)
│   .CSharp / .VisualBasic      │
└───────┬───────────────┬───────┘
        │               │
        │       ┌───────▼──────────────┐
        │       │ Provider             │  provider framework: lifecycle, document sync,
        │       │ (watcher + requests) │  workspace watching, request machinery
        │       └───────┬──────────────┘
        │               │
┌───────▼───────┐ ┌─────▼───────────────┐
│ Abstractions  │ │ Client              │  LSP over stdio (StreamJsonRpc)
│ (contracts)   │ │ (process + RPC)     │
└───────────────┘ └──────┬──────────────┘
                         │ stdio (JSON-RPC)
                 ┌───────▼──────────────┐
                 │  Language server     │  e.g. LuaLS or roslyn-language-server
                 └──────────────────────┘
```

- **`Abstractions`** depends only on the in-repo `Nickelony.IDEKit.Core` and
  `Nickelony.IDEKit.IntelliSense` contracts and adds no external package references - it is the
  stable seam any editor or provider can reference safely.
- **`Client`** depends on `StreamJsonRpc` and `Microsoft.Extensions.Logging.Abstractions` and takes no
  editor dependency.
- **`Provider`** depends on `Client` + `Abstractions` + `Nickelony.IDEKit.Core` +
  `Nickelony.IDEKit.IntelliSense` and supplies the provider framework: lifecycle, document
  synchronization, workspace watching, and request machinery.
- **`Lua`** depends on `Provider` + `Abstractions` + `Client` (plus the IDEKit models) and shells
  out to an external server executable.
- **`Roslyn`** shares that shape for Microsoft's Roslyn language server (C# and Visual Basic), and
  **`CSharp`** and **`VisualBasic`** are the thin language packages over it: each adds its language
  identifier (`csharp` / `vb`) and its file patterns and reuses `RoslynLanguageServerOptions`.

## Requirements

- .NET 8. The language-server packages target `net8.0`; the five WPF-based packages
  (`Nickelony.IDEKit.AvalonEdit*` and `Nickelony.KeyBindings.Wpf`) target
  `net8.0-windows`, while the cross-platform AvaloniaEdit mirror
  (`Nickelony.IDEKit.AvaloniaEdit*`) and `Nickelony.KeyBindings.Avalonia` target `net8.0`.
- The **Lua** package additionally needs the `lua-language-server` executable at runtime
  (download from the [LuaLS releases](https://github.com/LuaLS/lua-language-server/releases)).
- The **CSharp** and **VisualBasic** packages (through the **Roslyn** core) need the `roslyn-language-server`
  executable at runtime - `dotnet tool install --global roslyn-language-server --prerelease`, which
  requires the .NET 10 runtime, or a self-contained build.

## Package tiers

The repository hosts three sibling families - the `Nickelony.IDEKit.*` editor toolkit,
the host-neutral `Nickelony.KeyBindings.*` command surface, and the
`Nickelony.LanguageServer.*` family described above - which can be consumed
independently of each other. The tiers make the dependency direction explicit:
pick the smallest tier that covers your feature, and do not assume any higher
tier is required.

| Tier | Packages | Purpose | Dependencies |
|---|---|---|---|
| **Foundations** (UI-framework-free) | `Nickelony.IDEKit.Core`, `Nickelony.IDEKit.IntelliSense`, `Nickelony.IDEKit.Workspace`, `Nickelony.IDEKit.Workspace.Views` | Text primitives, editor contracts, document authority, and optional document-view coordination. Reusable from any UI toolkit. | Core: none. IntelliSense: Core. Workspace: Core. Workspace.Views: Workspace + Core. |
| **Editor adapters** | `Nickelony.IDEKit.AvalonEdit`, `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`, `Nickelony.IDEKit.AvaloniaEdit`, `Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures` | Editor UI bridges: editing, documents, status, and the IntelliSense controllers, for WPF (AvalonEdit) and cross-platform Avalonia (AvaloniaEdit). | AvalonEdit family: `net8.0-windows`, WPF. AvaloniaEdit family: `net8.0`, Avalonia (`Avalonia.AvaloniaEdit`). Base packages: Core. Language-features packages: base + `Nickelony.IDEKit.IntelliSense`. |
| **Optional integrations** | `Nickelony.IDEKit.AvalonEdit.Markdown`, `Nickelony.IDEKit.AvalonEdit.TextMate`, `Nickelony.IDEKit.AvaloniaEdit.Markdown`, `Nickelony.IDEKit.AvaloniaEdit.TextMate`, `Nickelony.IDEKit.Processes` | Leaf packages for a specific feature; adopt only what you need. | Markdown: editor adapter + Core + Markdig (WPF/AvalonEdit and cross-platform Avalonia/AvaloniaEdit). TextMate: editor adapter + TextMateSharp (both toolkits). Processes: none. |
| **Command surface** (separate family, host-neutral) | `Nickelony.KeyBindings`, `Nickelony.KeyBindings.Wpf`, `Nickelony.KeyBindings.Avalonia` | Command catalog, key bindings, persisted overrides, validation, rebinding, and key dispatch for any application with a command surface. | KeyBindings: `Microsoft.Extensions.Logging.Abstractions` only. KeyBindings.Wpf: KeyBindings, WPF (`net8.0-windows`). KeyBindings.Avalonia: KeyBindings, Avalonia (`net8.0`). |
| **Experimental extras** | `Nickelony.IDEKit.JsonSchema` | JSON Schema **vocabulary index**, not a validator or context-aware completion engine. | `Newtonsoft.Json.Schema` only (isolated). |
| **Language-server packages** | `Nickelony.LanguageServer.Abstractions`, `Nickelony.LanguageServer.Client`, `Nickelony.LanguageServer.Provider`, `Nickelony.LanguageServer.Lua`, `Nickelony.LanguageServer.Roslyn`, `Nickelony.LanguageServer.CSharp`, `Nickelony.LanguageServer.VisualBasic` | Editor IntelliSense contracts, an LSP client, a provider framework, a Lua provider, and a shared Roslyn core with C# and Visual Basic providers. | Abstractions: IDEKit.Core + IDEKit.IntelliSense. Client: StreamJsonRpc + Microsoft.Extensions.Logging.Abstractions (no editor dependency). Provider: Abstractions + Client + IDEKit.Core + IDEKit.IntelliSense. Lua: Abstractions + Client + Provider + IDEKit.Core + IDEKit.IntelliSense. Roslyn: Abstractions + Client + Provider + IDEKit.Core + IDEKit.IntelliSense. CSharp: Provider + Roslyn (Abstractions and Client are transitive). VisualBasic: Provider + Roslyn (Abstractions and Client are transitive). |

### Tier notes and non-goals

- **Foundations are UI-framework-free** - Core, IntelliSense, Workspace, and Workspace.Views target
  `net8.0` and reference no WPF or AvalonEdit types. They are the migration seam
  for a future toolkit switch (for example AvaloniaEdit). The editor-generic surface a second
  binding reuses lives here: the text and edit primitives (`Core.Text`, `Core.Editing`), the
  auto-closing resolver and the line-comment planner (`Core.AutoClosing`, `Core.Comments`),
  request coordination (`Core.Requests`), the navigation identity (`Core.Navigation`), the
  line-status contracts (`Core.LineStatus`), the diagnostic vocabulary (`Core.Diagnostics`), and
  the IntelliSense contracts, payloads, kernels, and host-state records. The
  [editor-binding guide](docs/EditorBindingGuide.md) inventories what a new binding reuses and
  what it writes.
- **Document views are optional workspace architecture.** The workspace family splits document
  authority (`Nickelony.IDEKit.Workspace`, usable headless) from view coordination
  (`Nickelony.IDEKit.Workspace.Views`). A host that only needs document authority never takes
  the view contracts, and a host that only needs the editor packages does not need the workspace
  family at all.
- **Base `Nickelony.IDEKit.AvalonEdit` does not depend on the workspace packages**
  (see the diagram below). The workspace packages ship the document-view coordination; the
  AvalonEdit-to-Workspace bridge is host code, so applications that need that integration combine
  the packages in their own host code.
- **`Nickelony.IDEKit.AvaloniaEdit` mirrors the AvalonEdit adapter cross-platform.** The two
  editor-adapter families ship the same feature slices on their own engine (AvalonEdit over WPF,
  `Avalonia.AvaloniaEdit` over Avalonia) and the same neutral packages; most mirrored files are a single
  shared source under `shared/Editor/` (or `shared/Editor.LanguageFeatures/`) linked into both bindings
  behind an `#if AVALONIAEDIT` header, so the two packages do not carry two copies of the same file. The
  [editor-binding guide](docs/EditorBindingGuide.md) records what a binding reuses, what it writes, and how
  source and documentation are shared. The Markdown and TextMate integrations are mirrored the same way
  (see section 5.6 of the guide): their engine-neutral halves live under `shared/Editor.Markdown/` and
  `shared/Editor.TextMate/`, and the Markdown mirror builds a native Avalonia control tree because Avalonia
  has no `FlowDocument`.
- **`Nickelony.IDEKit.AvalonEdit.Markdown`, `Nickelony.IDEKit.AvaloniaEdit.Markdown`,
  `Nickelony.IDEKit.AvalonEdit.TextMate`, and `Nickelony.IDEKit.AvaloniaEdit.TextMate`
  intentionally bring Markdig and TextMateSharp**; those focused dependencies are the point of
  the packages and stay confined to the family that needs them. The Markdown pair shares an internal
  toolkit-free document model and its Markdig walk, so the two packages carry one mapping and one shared
  test suite for it.
- **`Nickelony.IDEKit.Processes` is batch-scoped by design**: one request runs
  one process to completion and redirected output is captured while the process
  runs (bounded by the output-drain grace period). There is no standard-input
  piping or streaming support; hosts that need interactive processes drive
  `System.Diagnostics.Process` directly, and the LSP transport lives in
  `Nickelony.LanguageServer.Client`.
- **`Nickelony.IDEKit.JsonSchema` is experimental** and stays so until a second
  real consumer or a stable contract justifies promotion. It is not part of the
  core editor contract surface and may change outside the normal preview
  cadence. See its [README](src/IDEKit/Nickelony.IDEKit.JsonSchema/README.md) for the exact
  vocabulary-index contract.

### Dependency diagram

```
Nickelony.IDEKit.AvalonEdit.LanguageFeatures     (-> AvalonEdit, IntelliSense)
Nickelony.IDEKit.AvalonEdit.Markdown         (-> AvalonEdit, Core, Markdig)
Nickelony.IDEKit.AvalonEdit.TextMate         (-> AvalonEdit, TextMateSharp)
        │
        ▼
Nickelony.IDEKit.AvalonEdit                  (-> Core, AvalonEdit)   no Workspace

Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures   (-> AvaloniaEdit, IntelliSense)
Nickelony.IDEKit.AvaloniaEdit.Markdown       (-> AvaloniaEdit, Core, Markdig)
Nickelony.IDEKit.AvaloniaEdit.TextMate       (-> AvaloniaEdit, TextMateSharp)
        │
        ▼
Nickelony.IDEKit.AvaloniaEdit                    (-> Core, AvaloniaEdit)   no Workspace
        │
        ├──────────────────────────────────────┐
        ▼                                      ▼
Nickelony.IDEKit.IntelliSense                Nickelony.IDEKit.Workspace
        │  (-> Core)                            │  (-> Core)
        │                                      ▼
        │                            Nickelony.IDEKit.Workspace.Views   (-> Core, Workspace)
        └───────────────┬──────────────────────┘
                        ▼
              Nickelony.IDEKit.Core            (UI-framework-free, dependency-free leaf)

Optional / isolated leaves:
  Nickelony.IDEKit.Processes            dependency-free
  Nickelony.IDEKit.JsonSchema           -> Newtonsoft.Json.Schema only (experimental)

Key-bindings packages:
  Nickelony.KeyBindings                  host-neutral, standalone
  Nickelony.KeyBindings.Wpf              -> KeyBindings (WPF, net8.0-windows)
  Nickelony.KeyBindings.Avalonia         -> KeyBindings (Avalonia, cross-platform)

Language-server packages:
  Nickelony.LanguageServer.Abstractions  -> IDEKit.Core, IDEKit.IntelliSense
  Nickelony.LanguageServer.Client        -> StreamJsonRpc, Microsoft.Extensions.Logging.Abstractions (no editor dependency)
  Nickelony.LanguageServer.Provider      -> Abstractions, Client, IDEKit.Core, IDEKit.IntelliSense
  Nickelony.LanguageServer.Lua           -> Abstractions, Client, Provider, IDEKit.Core, IDEKit.IntelliSense
  Nickelony.LanguageServer.Roslyn        -> Abstractions, Client, Provider, IDEKit.Core, IDEKit.IntelliSense
  Nickelony.LanguageServer.CSharp        -> Roslyn, Abstractions, Client, Provider
  Nickelony.LanguageServer.VisualBasic   -> Roslyn, Abstractions, Client, Provider
```

## Building and testing

```sh
dotnet build Nickelony.LanguageServer.slnx
dotnet test  Nickelony.LanguageServer.slnx
```

The test suite covers all packages, plus four opt-in integration tests that drive a real LuaLS
process. LuaLS is not bundled: download a LuaLS release archive yourself and point
`NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE` at it to run them; without it they skip. Run `dotnet test`
for the current counts.

Some tests render real WPF content and show it in a window through the shared
`WPFTestHost.ShowInHostWindow`, which needs an interactive window station: run the suite from a desktop
session. On a headless session those tests report inconclusive and drop their coverage instead of
failing - the tier and its policy are documented in `tests/TestSupport/README.md`. The AvaloniaEdit
mirror suites instead run headless through the shared `AvaloniaTestHost` (Avalonia.Headless); their
render tier reports inconclusive because the headless drawing backend produces no pixels.

A third-party package bump is one change together with its contract suite: the Markdown parse and
render cases gate `Markdig`, and the tokenizer read-order, listener, invalidation, and disposal
contracts gate `TextMateSharp`.

## Repository layout

Paths are grouped by family; the [package tiers](#package-tiers) table is the authoritative package
list and dependency reference.

```
src/
  IDEKit/
    Nickelony.IDEKit.Core/                      UI-framework-free foundations: text primitives + editor contracts
    Nickelony.IDEKit.IntelliSense/              UI-framework-free IntelliSense contracts, payloads, and host-state records
    Nickelony.IDEKit.Workspace/                 UI-framework-free document authority + editing core (headless-friendly)
    Nickelony.IDEKit.Workspace.Views/           UI-framework-free document-view coordination (view contract + manager)
    Nickelony.IDEKit.Processes/                 Batch external-process execution
    Nickelony.IDEKit.JsonSchema/                Experimental JSON Schema vocabulary index
    AvalonEdit/
      Nickelony.IDEKit.AvalonEdit/              AvalonEdit editor adapter (no Workspace dependency)
      Nickelony.IDEKit.AvalonEdit.LanguageFeatures/   AvalonEdit IntelliSense controllers
      Nickelony.IDEKit.AvalonEdit.Markdown/     Markdown rendering (Markdig)
      Nickelony.IDEKit.AvalonEdit.TextMate/     TextMate highlighting (TextMateSharp)
    AvaloniaEdit/
      Nickelony.IDEKit.AvaloniaEdit/            AvaloniaEdit editor adapter (cross-platform, no Workspace dependency)
      Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures/   AvaloniaEdit IntelliSense controllers
      Nickelony.IDEKit.AvaloniaEdit.Markdown/     Markdown rendering (Markdig, native Avalonia content tree)
      Nickelony.IDEKit.AvaloniaEdit.TextMate/     TextMate highlighting (TextMateSharp)
  KeyBindings/
    Nickelony.KeyBindings/                      Host-neutral command + keyboard shortcut system
    Nickelony.KeyBindings.Wpf/                  WPF adapter (key mapping, event conversion, dispatch, display text)
    Nickelony.KeyBindings.Avalonia/             Avalonia adapter (key mapping, event conversion, dispatch)
  LanguageServer/
    Nickelony.LanguageServer.Abstractions/      editor contracts
    Nickelony.LanguageServer.Client/            LSP client + protocol machinery (no editor dependency)
    Nickelony.LanguageServer.Provider/          provider framework (lifecycle, document sync, workspace watching, protocol bridges)
    Nickelony.LanguageServer.Lua/               Lua provider backed by LuaLS
    Nickelony.LanguageServer.Roslyn/            shared Roslyn language-server core (C# and Visual Basic)
    Nickelony.LanguageServer.CSharp/            C# provider backed by the Roslyn language server
    Nickelony.LanguageServer.VisualBasic/       Visual Basic provider backed by the Roslyn language server
shared/
  Editor/                                       source shared by the two base bindings (`#if AVALONIAEDIT`)
  Editor.LanguageFeatures/                      source shared by the two language-feature bindings
  Editor.Markdown/                              source shared by the two Markdown packages (neutral model + walk)
  Editor.TextMate/                              source shared by the two TextMate packages
tests/
  IDEKit/
    Nickelony.IDEKit.*.Tests/                   per-package test projects
    AvalonEdit/
      Nickelony.IDEKit.AvalonEdit*.Tests/       editor-adapter test projects
    AvaloniaEdit/
      Nickelony.IDEKit.AvaloniaEdit*.Tests/     AvaloniaEdit editor-adapter test projects
  KeyBindings/
    Nickelony.KeyBindings.*.Tests/              per-package test projects
  LanguageServer/
    Nickelony.LanguageServer.Abstractions.Tests/
    Nickelony.LanguageServer.Client.Tests/
    Nickelony.LanguageServer.Client.Tests.FakeServer/
    Nickelony.LanguageServer.Provider.Tests/
    Nickelony.LanguageServer.Lua.Tests/
    Nickelony.LanguageServer.Roslyn.Tests/
    Nickelony.LanguageServer.CSharp.Tests/
    Nickelony.LanguageServer.VisualBasic.Tests/
  TestSupport/                                  shared test helpers and environment-tier policy
```

## License

[MIT](LICENSE) © 2026 Kewin Kupilas.
