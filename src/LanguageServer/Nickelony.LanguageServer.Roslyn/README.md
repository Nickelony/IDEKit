# Nickelony.LanguageServer.Roslyn

C# and Visual Basic language-server provider for the [Nickelony Language
Server](https://github.com/Nickelony/LanguageServer) family, backed by Microsoft's **Roslyn language server**
(`roslyn-language-server`), the LSP server that powers the C# and Visual Basic experience in editors. One
profile factory, `RoslynLanguageServerProfileFactory`, configures the Provider package's
`LanguageServerFeatureProvider` for either language, which then serves the family's `ILanguageServerFeatureProvider` surface.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Roslyn.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Roslyn)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

There is no standalone C# or Visual Basic language server: the Roslyn language server serves both languages from one
process. So the handshake and the server launch arguments are shared, and a `RoslynLanguage` (`RoslynLanguage.CSharp`
or `RoslynLanguage.VisualBasic`) selects only the document language identifier, the display name, and the source and
project file patterns the provider watches. The payload caching and the pull-diagnostics loop live in
[`Nickelony.LanguageServer.Provider`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Provider/README.md).

## Getting started

```sh
dotnet add package Nickelony.LanguageServer.Roslyn --prerelease
```

```csharp
using Nickelony.LanguageServer.Provider;
using Nickelony.LanguageServer.Roslyn;

string workspaceRootDirectoryPath = "/home/user/workspace";        // any absolute local path
string serverExecutablePath = "/home/user/.dotnet/tools/roslyn-language-server";

var profile = RoslynLanguageServerProfileFactory.Create(
    RoslynLanguage.CSharp,                                   // or RoslynLanguage.VisualBasic
    new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 20 });

using var provider = new LanguageServerFeatureProvider(profile, [workspaceRootDirectoryPath], serverExecutablePath);
```

The provider launches the `roslyn-language-server` executable at `serverExecutablePath`; the [Roslyn guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/RoslynGuide.md#server-acquisition) owns how to install it.

## Remarks

- **Pass `null` for the executable path** to construct a provider whose language features stay unavailable: it
  reports a persistent startup failure instead of throwing, and every call returns its documented fallback
  value.
- **The provider asks the server to load the workspace's projects.** Each session start sends `solution/open`
  for a root that holds exactly one solution at its top level, and otherwise `project/open` with the language's
  project files under the roots; the [Roslyn guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/RoslynGuide.md#project-loading)
  owns the rules.
- **One profile serves one language.** A workspace that holds both languages uses two providers, one per profile, and each starts its own server process.
- **Diagnostics are pull-oriented.** The server answers `textDocument/diagnostic` requests and asks the client
  to re-query through `workspace/diagnostic/refresh`; the framework's pull loop feeds the same cache and
  `DiagnosticsUpdated` event the push path uses, so a host observes one model either way.
- **Loose source files with no project** are loaded in the server's miscellaneous-files mode, where cross-file features are reduced; add the file to a project for full fidelity.

The [Roslyn guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/RoslynGuide.md) owns the server installation, the options, the launch arguments, the project loading, the settings document, the watch set, and the limitations. The [consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md) owns the host-facing lifecycle, threading, and disposal contract. The [Roslyn design note](https://github.com/Nickelony/LanguageServer/blob/main/docs/RoslynProviderDesign.md) owns the rationale for one provider that serves both languages and for the pull-diagnostics design.

## License

MIT © 2026 Kewin Kupilas.
