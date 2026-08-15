# Nickelony.LanguageServer.VisualBasic

Visual Basic language-server provider for the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer)
family, backed by
Microsoft's **Roslyn language server** (`roslyn-language-server`).

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.VisualBasic.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.VisualBasic)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

There is no standalone Visual Basic language server: the Roslyn language server serves **C# and Visual Basic from
one process**, so this package is a thin language layer over the shared
[`Nickelony.LanguageServer.Roslyn`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Roslyn/README.md) core. It declares the
`vb` document language identifier, the Visual Basic source and project file patterns the server watches, and the
display name; the handshake, the launch profile, the tracked-document payload caching, and the pull-diagnostics
loop all live in the core.

## Usage

Construct the provider with the workspace roots and the path to the Roslyn language server executable; the
optional `RoslynLanguageServerOptions` carries the session tunables (the automatic-project-loading maximum and
whether completion offers unimported types and extension methods).

```csharp
using var provider = new VisualBasicLanguageServerIntelliSenseProvider(
    [workspaceRootDirectoryPath],
    serverExecutablePath,
    new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 20 });
```

Pass `null` for the executable path to construct a provider whose IntelliSense stays unavailable; the provider
reports a persistent startup failure instead of throwing.

## Server acquisition

The library takes a `serverExecutablePath` and stays out of acquisition, exactly like the Lua provider does with
LuaLS. Install the server as a .NET tool (it needs the .NET 10 runtime) or point at a self-contained build:

```bash
dotnet tool install --global roslyn-language-server --prerelease
```

## What this package adds

- The `vb` language identifier, so the Roslyn language server analyzes opened documents as Visual Basic.
- The watched Visual Basic source (`*.vb`) and project (`*.vbproj`) patterns, composed with the shared
  solution, project-asset, build-properties, and build-packages patterns the core supplies.
- The `"Visual Basic"` display name used in log and diagnostic text.

## Limitations

- The shared behavior, the settings the server reads, and the project model are documented by
  `Nickelony.LanguageServer.Roslyn`.
- Loose source files with no project are loaded in the server's miscellaneous-files mode, where cross-file
  features are reduced; add the file to a project for full fidelity.

## Requirements

- .NET 8 (package targets `net8.0`, cross-platform).
- The **Roslyn language server** (`roslyn-language-server`) at runtime; it needs the .NET 10 runtime. Pass
  `serverExecutablePath: null` when no installation is available: the provider still constructs, reports a
  persistent startup failure, and every call returns its documented fallback value until the provider is
  recreated with a valid path.

## Dependencies

- `Nickelony.LanguageServer.Roslyn`
- `Nickelony.LanguageServer.Provider`
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3

`Nickelony.LanguageServer.Abstractions`, `Nickelony.LanguageServer.Client`, and the remaining client transport
dependencies arrive transitively through `Nickelony.LanguageServer.Provider`.

## License

MIT © 2026 Kewin Kupilas.
