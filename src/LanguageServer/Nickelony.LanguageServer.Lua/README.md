# Nickelony.LanguageServer.Lua

**A lightweight Lua language-server provider** for the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family, backed by [LuaLS](https://github.com/LuaLS/lua-language-server) (`lua-language-server` executable).

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Lua.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Lua)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

Turns [LuaLS](https://github.com/LuaLS/lua-language-server) into a language-feature provider for editor-class
applications. It implements the language-service contracts from
[`Nickelony.LanguageServer.Provider.Abstractions`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Provider.Abstractions/README.md)
on top of the
[`Nickelony.LanguageServer.Client`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Client/README.md)
LSP client, through the language-neutral
[`Nickelony.LanguageServer.Provider`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Provider/README.md)
framework. Its public surface is the profile factory and the option type; StreamJsonRpc arrives
transitively through the client.

## Getting started

```sh
dotnet add package Nickelony.LanguageServer.Lua --prerelease
```

```csharp
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Lua;
using Nickelony.LanguageServer.Provider;
using Nickelony.LanguageServer.Provider.Abstractions;

var workspaceRoot = "/home/user/workspace";   // any absolute local path
var serverExecutablePath = "/opt/lua-language-server/bin/lua-language-server";

// Settings are optional: call `Create()` to keep the defaults, or override only what is needed.
var profile = LuaLanguageServerProfileFactory.Create(LuaLanguageServerOptions.Default with { RuntimeVersion = "LuaJIT" });

// An ILogger is an optional constructor argument.
var provider = new LanguageServerFeatureProvider(
    profile,
    workspaceRootDirectoryPaths: [workspaceRoot],
    serverExecutablePath: serverExecutablePath);

provider.DiagnosticsUpdated += (_, eventArgs) => { /* eventArgs.DocumentId, eventArgs.Diagnostics */ };
provider.SemanticTokensUpdated += (_, eventArgs) => { /* eventArgs.DocumentId, eventArgs.SemanticTokens */ };

string filePath = Path.Combine(workspaceRoot, "main.lua");
string sourceText = "local text = 'hi'\ntext:";
provider.OpenDocument(filePath, sourceText);

var completions = await provider.GetCompletionItemsAsync(
    new CompletionRequest(filePath, sourceText, sourceText.Length, CompletionContext.ForCharacter(':')));

provider.UpdateDocument(filePath, sourceText + "upper()");
provider.CloseDocument(filePath);
provider.Dispose();
```

Every `*Async` language-feature method carries the current document text (directly or in its request record),
so a host can call it from a live buffer without first waiting for the server to receive each edit. Callbacks
may fire on background threads; marshal to the owning thread before touching thread-affine state.

## Remarks

- **The LuaLS executable is required at runtime.** Pass its path to the provider, or pass `serverExecutablePath: null` when no installation is available: the provider still constructs, reports a persistent startup failure, and every call returns its documented fallback until it is recreated with a valid path.
- **Process settings go on the profile's `Launch`.** An environment variable, an extra LuaLS argument such as
  `--logpath` or the working directory is set with
  `profile with { Launch = profile.Launch with { ServerArguments = ["--logpath=logs"] } }`.
- **Documents must be on disk.** Every document API takes a local file-system path. A path that cannot be
  normalized to a local file makes the call a no-op, and a query returns its empty or `null` fallback (a `null`
  argument throws).
- **Untitled and purely virtual documents are not supported.** LuaLS operates on files on disk.
- **Two LuaLS settings are fixed, not host-settable.** Third-party checks stay disabled because a library cannot answer interactive prompts, and completion call snippets stay enabled so snippet insert texts are consumed end to end.
- **The provider owns its client and watcher.** Capability-gated requests start the server lazily; construct
  with `LanguageServerFeatureProvider.Create(profile, ...)` to drive a client the host already owns instead, which
  then carries its own settings.

The [Lua guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/LuaGuide.md) owns the options, the configuration-file watching, the per-feature mapping notes, and the limitations. The [consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md) owns the lifecycle, threading, document-lease, cancellation, and disposal contract.

## License

MIT © 2026 Kewin Kupilas.
