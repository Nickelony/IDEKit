# Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures

The AvaloniaEdit binding of the IDEKit language features: it binds the language-feature controllers and services to an
AvaloniaEdit editor, one feature per namespace, as the [feature slices](https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#feature-slices) describe. It builds on the dependency-free `Nickelony.IDEKit.Core`
contracts, `Nickelony.IDEKit.LanguageFeatures`, `Nickelony.IDEKit.LanguageFeatures.Controllers` and the `Nickelony.IDEKit.AvaloniaEdit` document and editing
helpers. It targets `net8.0` and also depends on `Avalonia.AvaloniaEdit`.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures --prerelease
```

A `CompletionPresenter` shows the answers in the completion window, and a controller from
`CompletionControllerFactory` asks the provider for them. Every hook is optional. Typed letters then open the
window on their own, and so do the trigger characters of a provider that implements `ICompletionTriggerCharacters`.
Without a `ScopeAt` hook every offset counts as code, so a letter in a comment opens the window too. The example
sets one presenter hook and also asks at once:

```csharp
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

var editor = new TextEditor { Text = "pri", CaretOffset = 3 };

// For example, a LanguageServerFeatureProvider, which also names the server's trigger characters.
ICompletionProvider provider = CreateCompletionProvider();

// AvaloniaEdit hosts the completion list in a popup that carries no font of its own, so the
// list gets the editor's font instead of the window.
using var presenter = new CompletionPresenter(
    editor.TextArea,
    CompletionPresentationOptions.Default,
    new CompletionPresenterHooks { ConfigureWindow = window => window.CompletionList.FontSize = editor.FontSize });

using CompletionController completion = CompletionControllerFactory.Create(
    editor.TextArea,
    documentId: "C:/scripts/main.lua",
    provider,
    presenter);

// Call on the UI thread. The chosen item replaces the word before the caret, "pri".
bool opened = await completion.RequestAsync();
```

## Remarks

- The controllers are hook-driven and created and used on the editor's UI thread; host hooks are grouped
  in dedicated hook types.
- Completion and the presenter overload of `SignatureHelpControllerFactory` open on typed characters, and key
  gestures and popup precedence stay in the host.
- A host that starts every completion request itself sets `CompletionTriggerPolicy.Manual` on
  `CompletionControllerOptions.Triggers`, and one that turns typing triggers off at run time answers
  `CompletionScopes.None` from `ScopeAt`.
- Controllers accept an optional `Microsoft.Extensions.Logging.ILogger` and default to `NullLogger`; a
  provider failure inside a controller-owned pipeline is logged and reported as a not-applied result
  instead of thrown.
- A presenter that draws a window, a menu or a popup takes its look as one `presentation` argument, the
  `...PresentationOptions` record of its feature, which holds the themes and the settings of the presenter; a host
  restyles with `CompletionPresentationOptions.Default with { WindowTheme = ... }`.
- This binding builds no completion tooltip pipeline, because AvaloniaEdit publishes no completion tooltip
  member: it declares no `TooltipTheme` option, no `ConfigureTooltip`, `ResolveDescriptionAsync` or
  `BuildMarkdownContent` hook, and no `IsTooltipSupported` query. AvaloniaEdit shows an item's `Description` in its own tooltip, so a
  Markdown documentation shows there as raw text.
- `CompletionWindowOptions.PreventActivation` does not change activation here, because the completion window is a
  popup inside the editor's own window. The option still decides whether the presenter installs the explicit click
  selection: `true`, the default, installs it, and `false` installs nothing.
- Semantic-token colorizing lives here, not in the base package, because it paints a language-feature
  payload and would otherwise force the base package to reference `LanguageFeatures`.
- The [language-features guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md)
  owns the [typing triggers](https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#typing-triggers),
  the request-coordination semantics, the completion window sizing and tooltip rules, the refresh behavior,
  code actions, and thread affinity.
- The controllers whose request logic names no engine type come from `Nickelony.IDEKit.LanguageFeatures.Controllers`;
  this package adds their factories and the presenters that show the results.

## License

MIT © 2026 Kewin Kupilas.
