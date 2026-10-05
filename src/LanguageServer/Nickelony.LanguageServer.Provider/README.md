# Nickelony.LanguageServer.Provider

**The language-neutral provider framework** of the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family. It supplies the lifecycle, document synchronization, workspace watching, and request machinery a language package builds on, so a new language ships a profile instead of re-implementing the orchestration.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Provider.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Provider)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

The package sits on
[`Nickelony.LanguageServer.Provider.Abstractions`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Provider.Abstractions/README.md)
(the editor-facing contracts) and
[`Nickelony.LanguageServer.Client`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Client/README.md)
(the LSP client and shared primitives). It also references `Nickelony.IDEKit.LanguageFeatures`, the model
the language packages implement against, and `Nickelony.IDEKit.Core`, for the text primitives the document
machinery uses; StreamJsonRpc arrives transitively through the client. The client carries no editor
dependency, so the document tracker, the forwarding of workspace changes, and the mapping of server responses into
editor models live here. The file watcher itself is in `Nickelony.IDEKit.FileWatching`.

## Getting started

```sh
dotnet add package Nickelony.LanguageServer.Provider --prerelease
```

A language package ships a factory that returns a `LanguageServerProfile`, and the host hands the profile, the
workspace roots and the server executable path to the sealed `LanguageServerFeatureProvider`. The provider
creates the language-server client from the profile and disposes it with the provider:

```csharp
using Nickelony.LanguageServer.Provider;

public static class MyLanguageServerProfileFactory
{
    public static LanguageServerProfile Create() =>
        new()
        {
            LanguageId = "mylang",                                                      // the didOpen language identifier
            LanguageDisplayName = "My language",                                        // the name in log and diagnostic text
            ServerDisplayName = "My language server",
            WatchSpecifications = [new("*.mylang", IncludeSubdirectories: true)],       // an empty list disables watching
            DiagnosticMappingPolicy = DiagnosticMappingPolicy.Default,                 // null exposes no diagnostics
        };
}
```

A host then drives the provider through the `Nickelony.LanguageServer.Provider.Abstractions` contract. It
overrides a tunable with a `with` expression on the default options:

```csharp
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Provider;

string scriptRootDirectoryPath = "/home/user/scripts";
string serverExecutablePath = "/opt/my-language-server/my-language-server";
string filePath = Path.Combine(scriptRootDirectoryPath, "main.mylang");
string content = "print(";

LanguageServerFeatureProviderOptions options = LanguageServerFeatureProviderOptions.Default with { RequestTimeout = TimeSpan.FromSeconds(30) };

using var provider = new LanguageServerFeatureProvider(
    MyLanguageServerProfileFactory.Create(),
    [scriptRootDirectoryPath],                  // absolute local paths
    serverExecutablePath,                       // null or blank leaves the language server unavailable
    hooks: null,                                // the delegates for server messages; see below
    options: options,                           // null selects LanguageServerFeatureProviderOptions.Default
    logger: null);                              // null selects a no-op logger

provider.OpenDocument(filePath, content);
provider.UpdateDocument(filePath, content);

IReadOnlyList<CompletionItem> completions = await provider.GetCompletionItemsAsync(
    new CompletionRequest(filePath, content, caretOffset: content.Length));

provider.CloseDocument(filePath);
```

The reference implementation is [`Nickelony.LanguageServer.Lua`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Lua/README.md); the [provider authoring guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ProviderAuthoring.md) walks the profile and traps end to end.

## Remarks

- **Startup is lazy and every failure has a fallback.** A capability-gated request starts the server on demand; a provider constructed with no server path still constructs, reports a persistent startup failure, and returns each request's documented fallback value.
- **A hard startup failure is final.** After `HardStartupFailureThreshold` consecutive startup failures the provider stays in its failed state until it is recreated.
- **The profile's `Launch` holds the process settings.** The provider launches the executable through a
  `StdioLanguageServerTransport` with them, so a host overrides one setting with
  `profile with { Launch = profile.Launch with { EnvironmentVariables = variables } }` and keeps the package's
  arguments.
- **Events are raised on the thread that reports the change.** A pull-diagnostics report raises the same event
  from the pull task, so handlers must tolerate either path.
- **Push-diagnostics delivery is serialized per client.** The diagnostic mapping and every
  `DiagnosticsUpdated` handler finish before the next payload is dispatched, so handlers must be cheap and
  must not block.
- **A server's own messages reach the host through `LanguageServerFeatureProviderHooks`.** The hooks receive a
  message to show, a question with actions, work-done progress and an edit that the server asks the host to apply,
  as shared models; an unset hook keeps the default of its message. See the
  [consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md#server-messages).
- **The framework tunables are in `LanguageServerFeatureProviderOptions`.** A server that indexes a workspace
  while it starts can exceed the request timeout on a slow-but-healthy run. Consecutive request timeouts
  restart the connection, which discards the server's warm-up work. Raise `RequestTimeout`, or set
  `RequestTimeoutRestartThreshold` to zero to disable timeout-driven restarts.

The [provider framework guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ProviderGuide.md) owns the options, the internal machinery, the limitations, and the naming and slice conventions. The [LanguageServer internals guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md) owns the machinery behind the provider, and the [consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md) owns the host-facing lifecycle, threading, and disposal contract.

## License

MIT © 2026 Kewin Kupilas.
