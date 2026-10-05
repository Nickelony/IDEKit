# Nickelony.LanguageServer.Provider.Abstractions

**The provider contract that hosts and language packages share** in the [Nickelony Language
Server](https://github.com/Nickelony/LanguageServer) family.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Provider.Abstractions.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Provider.Abstractions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

The editor-facing lifecycle contract of the family - the seam a text editor binds to and every language
provider implements. The aggregate provider interface inherits the language-feature contracts and sources, and
the editor-model payloads they carry are owned by `Nickelony.IDEKit.Core` and
`Nickelony.IDEKit.LanguageFeatures`, so this package reuses that model instead of defining a parallel one; a
consumer that wants only the shared values takes the IDEKit packages alone.

## Getting started

```sh
dotnet add package Nickelony.LanguageServer.Provider.Abstractions --prerelease
```

A host consumes the contract, whatever provider backs it:

```csharp
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Provider.Abstractions;

// GetProvider returns the provider that the host built, for example a Lua or a Roslyn provider.
ILanguageServerFeatureProvider provider = GetProvider();

provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    // eventArgs.Diagnostics is an owned snapshot; marshal to the UI thread.
};

string filePath = "/home/user/workspace/main.lua";
string sourceText = "local greeting = 'hi'\nprint(gre";

provider.OpenDocument(filePath, sourceText);

IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
    new CompletionRequest(filePath, sourceText, caretOffset: sourceText.Length));

provider.CloseDocument(filePath);
```

A language provider implements the same contract; see the
[provider authoring guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ProviderAuthoring.md).

## Remarks

- **Document identifiers are local file paths.** Pass fully qualified paths. Paths are compared with the
  provider's path comparison, which defaults to the platform's identity rules (case-insensitive on Windows and
  macOS, ordinal elsewhere).
- **An identifier that cannot be normalized to a local file is ignored.** A relative path is such an identifier, because it
  would resolve against the process working directory. The member becomes a no-op or returns its documented fallback,
  and diagnostics published for non-file URIs are dropped.
- **The supplied text is authoritative.** A request synchronizes the tracked document to the text on the
  request, so it works without a prior `OpenDocument`.
- **The document members belong to `IDocumentSynchronizationTarget`.** The provider inherits `OpenDocument`,
  `UpdateDocument`, `RenameDocument` and `CloseDocument` from that contract of `Nickelony.IDEKit.LanguageFeatures`,
  so an editor host that keeps its documents in a `WorkspaceViewCoordinator` hands the provider to a
  `WorkspaceDocumentSynchronizer` instead of calling them.
- **Payloads are owned snapshots.** The edit collections, the event-args lists, and each token's modifier
  list are copied on construction, so a provider may reuse or mutate its source.
- **Requests carry zero-based UTF-16 offsets.** The provider maps them to protocol positions, and a request
  rejects an offset outside its document text. The location results (`DefinitionLocation`,
  `ReferenceLocation`, `TextEdit`) carry zero-based line and character positions.
- **Editor model vs `LanguageServer*`.** A type without the prefix (`CompletionItem`, `HoverInformation`) is
  the protocol-free editor model owned by the IDEKit packages, and `LanguageServer*` names the lifecycle and
  capability types (`ILanguageServerFeatureProvider`, `LanguageServerFeatureProviderCapabilities`). A failure
  payload is named for what failed (`StartupFailure`, `WorkspaceFileWatchFailure`).
- **Every document is named by a `DocumentId`.** Requests, location results and events use the same word. This
  family fills it with the local file path.
- **The session starts lazily** on the first request or lifecycle operation, and the `Capabilities` flags are
  `false` before the first successful start; read the snapshot again after `CapabilitiesChanged`, and decide on
  `IsAvailable` whether a request goes out. The
  [consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md)
  owns the lifecycle, threading, and disposal contract.

## License

MIT © 2026 Kewin Kupilas.
