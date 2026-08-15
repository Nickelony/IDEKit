# Nickelony.LanguageServer.Abstractions

**Lightweight editor IntelliSense lifecycle contracts** for the [Nickelony Language Server](https://github.com/Nickelony/LanguageServer) family.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.LanguageServer.Abstractions.svg)](https://www.nuget.org/packages/Nickelony.LanguageServer.Abstractions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

This package contains the **editor-facing lifecycle contract** of the Nickelony Language Server
family - the stable seam between a text editor and any language provider. It holds the provider
lifecycle (document open/update/close/move, request cancellation, startup/capability events,
session state), the semantic-token contract, and the language-server request and result records
(diagnostics, document symbols, navigation results, edits, code actions, and failure payloads). All of
its types live in the single namespace `Nickelony.LanguageServer.Abstractions`; the `CodeActions/`,
`Completion/`, `Diagnostics/`, `DocumentSymbols/`, `Editing/`, `Hover/`, `Lifecycle/`, `Navigation/`,
`SemanticTokens/`, and `Signatures/` folder structure is purely organizational. The shared, protocol-free IntelliSense payload values
(`TextDiagnostic`, `TextDocumentSymbol`, `TextHoverInfo`, `TextDefinitionLocation`,
`TextSignatureHelp`, `TextCompletionItem`) live in the external-dependency-free
`Nickelony.IDEKit.IntelliSense` package under the `Nickelony.IDEKit.IntelliSense` feature
namespaces (`Nickelony.IDEKit.IntelliSense.Diagnostics`, `...DocumentSymbols`, `...Hover`,
`...Navigation`, `...Signatures`, `...Completion`); this package references that package for those
values and for `TextSignatureHelpContext`.

> **Dependency direction (settled).** `Abstractions` deliberately depends on the sibling
> `Nickelony.IDEKit.Core` and `Nickelony.IDEKit.IntelliSense` packages: together they are the
> family's shared, protocol-free editor model, and the contract tier reuses their
> `Nickelony.IDEKit.Core.Text` primitives and `Nickelony.IDEKit.IntelliSense.*` payload values
> instead of defining a second, parallel model. `Abstractions` is therefore **not** independently
> consumable without the IDEKit model packages; that coupling is intentional and the neutral
> payloads are not duplicated here. A consumer that only wants the shared values can take the
> IDEKit packages without the language-server tier.

> **Naming rule (`Text*` vs `LanguageServer*`).** The `Text*` prefix names the protocol-free editor
> model, whether reused from the IDEKit packages (`TextPosition`, `TextDiagnostic`) or defined here
> (`TextEdit`, `TextDocumentEdit`, `TextFormattingOptions`, `TextWorkspaceEdit`). The `LanguageServer*`
> prefix names the request records a caller passes to the provider surface
> (`LanguageServerCompletionRequest`, `LanguageServerFormattingRequest`). A type that is an editor-model
> input to a request keeps the `Text*` prefix, which is why `LanguageServerFormattingRequest` carries a
> `TextFormattingOptions` rather than a `LanguageServerFormattingOptions`.

## Getting started

```sh
dotnet add package Nickelony.LanguageServer.Abstractions
```

Reference the contract instead of a concrete provider:

```csharp
using Nickelony.IDEKit.Core.Text;
using Nickelony.LanguageServer.Abstractions;
using Nickelony.IDEKit.IntelliSense.Completion;

// An editor consumes the contract, whatever provider backs it:
ILanguageServerIntelliSenseProvider provider = GetProvider(); // any provider, for example one backed by a language server

provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    // eventArgs carries the file path and an owned diagnostics snapshot.
    // A consumer with thread-affine state marshals the callback to its owning thread.
};

provider.OpenDocument(filePath, sourceText);

IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(
    new LanguageServerCompletionRequest(filePath, sourceText, new TextPosition(line, column)));
```

Or implement the contract to provide IntelliSense for your own language - all the
`Text*` types are plain immutable records designed to be trivially constructible.

## Dependencies

`Nickelony.IDEKit.Core` (for the shared text primitives such as `TextPositionRange`) and
`Nickelony.IDEKit.IntelliSense` (for the shared `Nickelony.IDEKit.IntelliSense.*` payload values).
This is a deliberate, settled direction: IDEKit is the family's shared model, as the
dependency-direction note above records.

## What's inside

The contract surface, grouped by IntelliSense feature. Every type in this package is in the
single `Nickelony.LanguageServer.Abstractions` namespace; rows that name a
`Nickelony.IDEKit.IntelliSense.*` namespace are shared payloads referenced from the
`Nickelony.IDEKit.IntelliSense` package:

| Area | Types / members |
|---|---|
| Provider | `ILanguageServerIntelliSenseProvider` |
| Session state | `LanguageServerProviderState` |
| Capability flags | `SupportsRename`, `SupportsReferences`, `SupportsFormatting`, `SupportsDocumentSymbols`, `SupportsCodeActions` |
| Document lifecycle | `OpenDocument`, `UpdateDocument`, `CloseDocument`, `MoveDocument` |
| Completion | `LanguageServerCompletionRequest`; `TextCompletionItem`, `TextCompletionItemKind` (in `Nickelony.IDEKit.IntelliSense.Completion`) |
| Diagnostics | `TextDiagnostic` (in `Nickelony.IDEKit.IntelliSense.Diagnostics`), `TextDiagnosticSeverity` (in `Nickelony.IDEKit.Core.Diagnostics`) |
| Document symbols | `ILanguageServerDocumentSymbolProvider`, `LanguageServerDocumentSymbolRequest`; `TextDocumentSymbol`, `TextDocumentSymbolKind` (in `Nickelony.IDEKit.IntelliSense.DocumentSymbols`) |
| Hover | `LanguageServerHoverRequest`; `TextHoverInfo` (in `Nickelony.IDEKit.IntelliSense.Hover`), `TextMarkupKind` (in the root `Nickelony.IDEKit.IntelliSense` namespace) |
| Navigation | `ILanguageServerReferencesProvider`, `TextReferenceLocation`, `LanguageServerReferenceRequest`, `LanguageServerDefinitionRequest`; `TextDefinitionLocation` (in `Nickelony.IDEKit.IntelliSense.Navigation`) |
| Semantic tokens | `ILanguageServerSemanticTokensProvider`, `SemanticToken` |
| Editing | `ILanguageServerRenameProvider`, `ILanguageServerFormattingProvider`, `TextEdit`, `TextWorkspaceEdit`, `TextDocumentEdit`, `LanguageServerFormattingRequest`, `TextFormattingOptions`, `LanguageServerRenameRequest` |
| Code actions | `ILanguageServerCodeActionProvider`, `TextCodeAction`, `LanguageServerCodeActionRequest` |
| Signatures | `LanguageServerSignatureHelpRequest`; `TextSignatureHelp`, `TextSignatureInformation`, `TextSignatureParameterInfo`, `TextSignatureHelpContext`, `TextSignatureHelpTriggerKind` (in `Nickelony.IDEKit.IntelliSense.Signatures`) |
| Failures | `LanguageServerStartupFailure`, `WorkspaceWatcherFailure` |
| Event payloads | `DiagnosticsUpdatedEventArgs`, `SemanticTokensUpdatedEventArgs`, `StartupFailedEventArgs`, `WorkspaceWatcherFailedEventArgs` |

Because this package is the contract seam, any project in the family can reference it: an editor that
only consumes IntelliSense, or a provider that implements it, never needs to drag in
`StreamJsonRpc` or any LSP implementation. It does depend on the IDEKit editor-model packages
(`Nickelony.IDEKit.Core` and `Nickelony.IDEKit.IntelliSense`), so it is not an editor-independent
contract; that dependence is the settled family model recorded in the dependency-direction note above.

The capability-gated features are exposed through narrow contracts of their own -
`ILanguageServerReferencesProvider`, `ILanguageServerRenameProvider`, `ILanguageServerFormattingProvider`,
`ILanguageServerDocumentSymbolProvider`, and `ILanguageServerCodeActionProvider` - that the provider
contract inherits, so each `Supports*` flag sits next to the request it gates. Features that are
always attempted (completion, hover, definition, signature help, and the passive diagnostics read)
are declared directly on the provider contract. Names follow one policy: **every provider contract
carries the `ILanguageServer` family prefix**, and **every provider request record carries the
`LanguageServer` prefix too** (`LanguageServerCompletionRequest`, `LanguageServerSignatureHelpRequest`,
`LanguageServerCodeActionRequest`, `LanguageServerFormattingRequest`, `LanguageServerRenameRequest`,
`LanguageServerReferenceRequest`, `LanguageServerHoverRequest`, `LanguageServerDefinitionRequest`, and
`LanguageServerDocumentSymbolRequest`). That prefix keeps the provider request records distinct from the
synchronous sibling request records owned by the sibling `Nickelony.IDEKit.IntelliSense` package
(`TextCompletionRequest`, `TextSignatureHelpRequest`, `TextCodeActionRequest`). Result payloads keep
the shared `Text*` noun (`TextEdit`, `TextWorkspaceEdit`, `TextReferenceLocation`, and so on).

Document identity is a **local file path**: every lifecycle and request member takes the path of a
document on the local file system. Paths must be absolute - a relative path is anchored to the
process working directory and can silently address a different file. The provider normalizes paths
and compares them with the platform's path identity rules (case-insensitive on Windows and macOS by
default, ordinal elsewhere). A path that cannot be normalized to an absolute local path makes the
affected member a no-op or returns its documented fallback value, and diagnostics published for
non-file URIs are dropped. Buffers without a usable local path cannot be represented by this
contract, so a host maps them to the path the document would use in the editor. Supporting untitled
or remote documents would require a generalized identity model and is not offered by this contract
version.

The two navigation results name their document differently on purpose. `TextReferenceLocation`
uses the required local-path `FilePath` above, because this family drops a reference whose URI does
not resolve to a local path. The shared `TextDefinitionLocation` instead uses the nullable opaque
`DocumentId`, because a definition can point at a document the provider has not materialized and
because it must also distinguish the whole-definition range from the name range; providers in this
family fill that `DocumentId` with the local file path. A host that converts both results maps
`TextReferenceLocation.FilePath` and `TextDefinitionLocation.DocumentId` to the same document.

Request members carry the authoritative document text for the call: the provider synchronizes its
tracked document to the supplied content before issuing the request, which is what lets a request
work without a prior `OpenDocument`. A feature method takes a request record when its request
carries state beyond the document and position: references (the declaration flag), rename (the new
name), formatting (the options), code actions (the range), completion (the trigger character), and
signature help (the trigger context) use the request records listed above. Hover and definition take a
request record carrying the document and a `TextPosition`, and document symbols take a
`LanguageServerDocumentSymbolRequest` carrying the document pair.
`TextDocumentEdit` and `TextWorkspaceEdit` copy the collections they are given, and
`DiagnosticsUpdatedEventArgs` and `SemanticTokensUpdatedEventArgs` copy the sequence they are given, so a
provider may hand over a snapshot and reuse or mutate its source. All payload records compare by value,
including the element-wise comparison of the workspace-edit collections and a `SemanticToken`'s modifier
sequence.

## Capability flags and lazy startup

The language-server session starts lazily on the first request or lifecycle operation, and capability flags are
`false` before the first successful start. The full lazy-startup and capability-gating rule is in the
[consumer integration guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md);
the feature table above lists the flags.

## Coordinate conventions

The contract uses zero-based line and character positions (the unit system of the language-server
protocol) for its range payloads:

- **Request positions are zero-based** line and character positions carried by
  `Nickelony.IDEKit.Core.Text.TextPosition` (`LanguageServerReferenceRequest.Position`,
  `LanguageServerRenameRequest.Position`, the position carried by `LanguageServerCompletionRequest` and
  `LanguageServerSignatureHelpRequest`, the position parameter of hover and definition, and both
  ends of `LanguageServerCodeActionRequest.Range`); negative values are changed to zero. The
  optional completion trigger character is a string, and a blank value is treated as absent.
- **Results and range payloads use zero-based** line and character positions carried
  by `Nickelony.IDEKit.Core.Text.TextPositionRange` (`TextEdit.Range`,
  `TextReferenceLocation.Range`); the line and character unit model is documented on
  `Nickelony.IDEKit.Core.Text.TextPosition`. Range values are stored as supplied.
- The shared IntelliSense payloads use zero-based UTF-16 document offsets where their request
  records carry offsets (for example `TextHoverRequest.HoveredOffset`).

Hosts convert between their own caret coordinates and these bases at the boundary.

## License

MIT © 2026 Kewin Kupilas.
