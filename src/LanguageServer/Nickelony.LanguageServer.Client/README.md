# Nickelony.LanguageServer.Client

**A lightweight LSP client for .NET**, built on [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc). It spawns a language-server process and speaks the Language Server Protocol over stdio.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Client.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Client)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

The client package of the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family. It handles everything below the editor-facing contracts: hosting the server process, the `initialize` handshake, capability negotiation, JSON-RPC requests and notifications, and the document-synchronization payloads. Its only dependencies are StreamJsonRpc and Microsoft.Extensions.Logging.Abstractions; it references no editor or IDE package, so a protocol-only host takes nothing beyond them. The stateful provider machinery (tracked documents, workspace watching, the semantic-token decoder) lives in [`Nickelony.LanguageServer.Provider`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Provider/README.md).

## Getting started

```sh
dotnet add package Nickelony.LanguageServer.Client --prerelease
```

```csharp
using System.Text.Json.Nodes;
using Nickelony.LanguageServer.Client;

var transport = new StdioLanguageServerTransport(
    "/opt/example-language-server/example-language-server",
    StdioLanguageServerTransportOptions.Default with { ServerArguments = ["--stdio"] });

// The options and an ILogger are further optional constructor arguments.
await using var client = new LanguageServerClient(["/home/user/workspace"], transport);

client.DiagnosticsPublished += (_, eventArgs) =>
{
    // eventArgs.Parameters is an owned snapshot; marshal to the UI thread.
};

if (await client.EnsureStartedAsync(CancellationToken.None))
{
    // The server has the document only after a textDocument/didOpen notification.
    JsonNode hover = await client.SendRequestAsync<JsonNode>(
        "textDocument/hover",
        new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = "file:///home/user/workspace/main.lua" },
            ["position"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
        },
        CancellationToken.None);
}
```

The stdio transport's options hold the launch settings of the server process, and the client options tune
the lifecycle timeouts and provide the LSP payloads the host owns. Start from the `Default` of either and
override only what is needed. Supply a custom `ILanguageServerTransport` instead to reach a server over another
channel, such as one the host already spawned.

## Remarks

- **The client is low-level.** For editor-facing language features, consume a provider package such as
  [`Nickelony.LanguageServer.Lua`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Lua/README.md),
  which implements the
  [`Nickelony.LanguageServer.Provider.Abstractions`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Provider.Abstractions/README.md)
  contracts on top of this client.
- **Connection failures are classified by one base type.** Every connection-usability failure derives from `LanguageServerConnectionException` (itself an `IOException`), so one `catch` covers "no ready session yet", "became unavailable", and "superseded"; a server error response surfaces as `LanguageServerRequestRejectedException` carrying the JSON-RPC `ErrorCode`. The client never restarts itself.
- **Sends are concurrent** and capability reads are lock-free snapshot reads; re-read `NegotiatedCapabilities` after `EnsureStartedAsync`. The client raises diagnostics, semantic-token refresh, pull-diagnostics refresh, and connection-unavailable events, each of which may arrive on a background thread.
- **Server messages that answer no request reach the host through a `LanguageServerClientHooks` constructor argument.** Set
  `ShowMessage`, `ShowMessageRequest`, `Progress` or `ApplyWorkspaceEdit` to receive `window/showMessage`,
  `window/showMessageRequest`, `$/progress` and `workspace/applyEdit`.
- **A set `Progress` or `ApplyWorkspaceEdit` hook also advertises its client capability.** An unset hook keeps the
  default: the logger, a dropped notification or an answer that nothing was applied.
- **A payload built from the host's own DTO needs the protocol casing.** The built-in `Protocol/` records
  declare their wire names, so serialize any other payload with
  `LanguageServerClient.CreateProtocolSerializerOptions()` before sending it.
- **Positions are UTF-16 code units**, workspace folders are fixed at construction time, and dynamic registration is deliberately unsupported.

The [client guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ClientGuide.md) owns the transport seam, capability negotiation, the protocol wire-shape conventions, and the full limitation list. The [consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md) owns the lifecycle, threading, restart, failure-classification, and disposal contract. The [sample hosts](https://github.com/Nickelony/LanguageServer/blob/main/samples/README.md) drive the Lua and Roslyn providers, which run over this client.

## License

MIT © 2026 Kewin Kupilas.
