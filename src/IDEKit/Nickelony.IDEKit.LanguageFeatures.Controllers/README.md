# Nickelony.IDEKit.LanguageFeatures.Controllers

The editor-side behavior of the language features, independent of any editor engine: the controllers that drive
hover, signature help, completion, code actions and diagnostics for one editor, and the engine-free machinery the
controllers drive, such as the completion session kernel and commit planner, the snippet expander, and the
definition resolvers. The completion window names engine types, so it ships in the binding packages as
`CompletionPresenter`. It depends on `Nickelony.IDEKit.Core`, `Nickelony.IDEKit.LanguageFeatures` for the contracts
it drives, `Nickelony.Matching` and `Nickelony.QuickPick` for the quick-pick modes, and
`Microsoft.Extensions.Logging.Abstractions` for the logger a host supplies, and targets `net8.0` with nullable
reference types.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.LanguageFeatures.Controllers --prerelease
```

```csharp
using Nickelony.IDEKit.Core.Text;              // StringTextSnapshot
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

// MyCompletionProvider is the host's ICompletionProvider, as in the LanguageFeatures package readme.
var kernel = new CompletionSessionKernel();
var provider = new MyCompletionProvider();
const string documentText = "pri";

CompletionSessionDirective directive = await kernel.GetDirectiveAsync(
    "C:/scripts/main.lua",
    provider,
    new StringTextSnapshot(documentText),
    documentText.Length,
    CompletionContext.Invoked,
    cancellationToken: CancellationToken.None);

if (directive.ReplacementRange is { } replacementRange && directive.Items is { Count: > 0 } items)
{
    // The host shows its window: the items filtered by the typed word, and the range the chosen item replaces.
    Console.WriteLine($"{items.Count} items replace {replacementRange}");
}
else if (directive.AllItemsFilteredOut)
{
    // The typed word filtered every candidate out; a host may dismiss a narrowed session
    // instead of keeping stale entries.
}
```

## Remarks

- **A controller whose request logic names no engine type lives here.** An editor binding supplies the engine
  through a per-engine factory (`CodeActionControllerFactory.Create` over a text area), and a host with another
  editor passes its own source or delegate to the controller.
- **The completion kernel keeps no state and no cache.** Every request invokes the provider again, so
  incompleteness, caching, and retriggering are host policy.
- **A host with another editor drives completion through `CompletionController`.** It passes a delegate that
  captures the document text and the caret, and an `ICompletionPresenter` that shows the decisions, and it
  forwards each typed character and the replacement of its document.
- **The controller owns the triggers, the debounce and the rule that the newest request wins.** An editor binding
  creates it with its `CompletionControllerFactory.Create`.
- **The editor modes of the quick pick live here.** `GoToLineQuickPickProvider` (`:`) and `OpenEditorsQuickPickProvider`
  (`edt `) move the caret and list the tabs of an editor, and `DocumentSymbolQuickPickProvider` (`@` and `@:`) and
  `WorkspaceSymbolQuickPickProvider` (`#`) drive `IDocumentSymbolProvider` and `IWorkspaceSymbolProvider`. All four
  implement the provider contract of `Nickelony.QuickPick`; the [quick pick
  guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/QuickPickGuide.md#quick-open-open-editors-and-go-to-line)
  owns their rules.
- **The library never rewrites snippet text on the wire.** `SnippetExpander.Expand` models the tabstop syntax,
  the editor bindings insert it through the editor's snippet, and a host with its own completion data owns
  navigation and undo.

The [language features
guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageFeaturesGuide.md) owns the completion
lifecycle and merge rules and the per-feature notes.

## License

MIT © 2026 Kewin Kupilas.
