# Nickelony.LanguageServer.Roslyn

Shared Roslyn language-server core for the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer)
family. It drives
Microsoft's **Roslyn language server** (`roslyn-language-server`), the LSP server that powers the C# and
Visual Basic experience in editors, and it is the base a language package derives from to expose one of those
languages through the family's `ILanguageServerIntelliSenseProvider` surface.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Roslyn.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Roslyn)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

There is no standalone Visual Basic language server: the Roslyn language server serves **C# and Visual Basic
from one process**, so the handshake and the feature plumbing live in this core and a language package declares
only its identity. `Nickelony.LanguageServer.CSharp` and `Nickelony.LanguageServer.VisualBasic` are the
concrete packages over it.

## What this package provides

- **The shared provider** (`RoslynLanguageServerIntelliSenseProvider`) - an abstract provider that supplies the
  Roslyn handshake, the launch profile, the tracked-document payload caching, and every language-neutral hook.
  A language package derives from it and overrides only `LanguageId` and `ProviderDisplayName`, passing its own
  source and project file patterns to the constructor.
- **The launch profile** - `--stdio --autoLoadProjects [<n>] --telemetryLevel off`. The value is appended only
  when `RoslynLanguageServerOptions.AutoLoadProjectsMaximum` is a positive count; the bare switch otherwise
  applies the server's own default cap, and a maximum of `0` omits the switch entirely. Automatic project
  loading is how the server discovers the solutions and projects below the advertised workspace folders;
  telemetry is pinned off because a library must not enable it on the host's behalf.
- **The handshake factories** - client capabilities (including the pull-diagnostics and semantic-token refresh
  capabilities the server acts on), initialization options, and the settings document the server reads through
  `workspace/configuration`.
- **`RoslynLanguageServerOptions`** - the session options: the automatic-project-loading maximum and whether
  completion offers unimported types and extension methods.

## Server acquisition

The library takes a `serverExecutablePath` and stays out of acquisition, exactly like the Lua provider does with
LuaLS. Install the server as a .NET tool (it needs the .NET 10 runtime) or point at a self-contained build:

```bash
dotnet tool install --global roslyn-language-server --prerelease
```

Pass `null` for the path to construct a provider whose IntelliSense stays unavailable; the provider reports a
persistent startup failure instead of throwing.

## Diagnostics

The Roslyn language server is **pull-oriented**: it delivers diagnostics through `textDocument/diagnostic` and
refreshes them through `workspace/diagnostic/refresh`, rather than through `textDocument/publishDiagnostics`.
The framework's pull-diagnostics loop consumes those reports and feeds the same diagnostics cache and
`DiagnosticsUpdated` event the push path uses, so a host observes one model either way.

## Design

The rationale for the shared core, the framework work it required (pull diagnostics, the shared feature layer
and response mapping), and the alternatives that were weighed is in
[`docs/RoslynProviderDesign.md`](https://github.com/Nickelony/LanguageServer/blob/main/docs/RoslynProviderDesign.md).

## Limitations

- This package is the shared core; a host constructs a concrete language provider
  (`Nickelony.LanguageServer.CSharp` or `Nickelony.LanguageServer.VisualBasic`), not the abstract core directly.
- Loose source files with no project are loaded in the server's miscellaneous-files mode, where cross-file
  features are reduced; add the file to a project for full fidelity.
- The settings document models only options whose `workspace/configuration` section name is fixed by the
  server; the remaining options keep the server's defaults.

## Requirements

- .NET 8 (package targets `net8.0`, cross-platform).
- The **Roslyn language server** (`roslyn-language-server`) at runtime; it needs the .NET 10 runtime. Pass
  `serverExecutablePath: null` when no installation is available: the provider still constructs, reports a
  persistent startup failure, and every call returns its documented fallback value until the provider is
  recreated with a valid path.

## Dependencies

- `Nickelony.LanguageServer.Abstractions`
- `Nickelony.LanguageServer.Client`
- `Nickelony.LanguageServer.Provider`
- `Nickelony.IDEKit.IntelliSense`
- `Nickelony.IDEKit.Core`
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3

`StreamJsonRpc` and the remaining client transport dependencies arrive transitively through
`Nickelony.LanguageServer.Client`.

## License

MIT © 2026 Kewin Kupilas.
