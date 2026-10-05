# Nickelony.IDEKit.LanguageFeatures

Editor- and protocol-agnostic language-feature contracts and payload records for editor hosts and language
tooling. It depends on `Nickelony.IDEKit.Core` for snapshots, ranges, and text primitives, and targets `net8.0`
with nullable reference types. The behavior that drives these contracts in an editor lives in
`Nickelony.IDEKit.LanguageFeatures.Controllers`, and the language-server provider lifecycle lives in
`Nickelony.LanguageServer.Provider.Abstractions`.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.LanguageFeatures --prerelease
```

```csharp
using Nickelony.IDEKit.LanguageFeatures.Completion;

// A provider answers one request record and holds no document state.
public sealed class KeywordCompletionProvider : ICompletionProvider
{
    public Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
        CompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CompletionItem> items =
        [
            new CompletionItem("local") { Kind = CompletionItemKind.Keyword },
            new CompletionItem("function") { Kind = CompletionItemKind.Keyword },
        ];

        return Task.FromResult(items);
    }
}
```

## Remarks

- **Providers are asynchronous and stateless.** Each `I{Feature}Provider` answers one request record, observes
  a cancellation token, is safe to call from any thread, and holds no document state. A feature whose results
  reach the host without a request is a source (`IDiagnosticsSource`, `ISemanticTokensSource`).
- **Every document request names its document.** A request record other than `WorkspaceSymbolRequest` carries a
  required `DocumentId` beside the document text.
- **Requests and payloads address text by zero-based UTF-16 offsets.** `DefinitionLocation`,
  `ReferenceLocation`, `WorkspaceSymbolLocation`, and `TextEdit` are the exceptions, with zero-based line and
  character positions; Core's `TextLineMap` converts between the families.
- **Vocabularies are closed or open by design.** Completion kinds mirror the protocol's `CompletionItemKind`
  one-to-one, except members such as `Namespace` that have no protocol value. A host defines additional
  categories with `CompletionItemKind.CreateCustom`.
- **The library never rewrites snippet text on the wire.** A snippet item keeps its raw tabstop syntax.

The [language features
guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageFeaturesGuide.md) owns the text and
vocabulary conventions, the completion lifecycle and merge rules, the payload shapes, and the per-feature
notes.

## License

MIT © 2026 Kewin Kupilas.
