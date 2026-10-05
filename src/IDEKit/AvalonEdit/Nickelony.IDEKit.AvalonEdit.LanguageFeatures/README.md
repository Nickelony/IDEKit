# Nickelony.IDEKit.AvalonEdit.LanguageFeatures

The AvalonEdit binding of the IDEKit language features: it binds the language-feature controllers and services to an
AvalonEdit editor, one feature per namespace, as the [feature slices](https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#feature-slices) describe. It builds on the dependency-free `Nickelony.IDEKit.Core`
contracts, `Nickelony.IDEKit.LanguageFeatures`, `Nickelony.IDEKit.LanguageFeatures.Controllers` and the `Nickelony.IDEKit.AvalonEdit` document and editing
helpers. It targets `net8.0-windows` (WPF).

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvalonEdit.LanguageFeatures --prerelease
```

A `CompletionPresenter` shows the answers in the completion window, and a controller from
`CompletionControllerFactory` asks the provider for them. Every hook is optional. Typed letters then open the
window on their own, and so do the trigger characters of a provider that implements `ICompletionTriggerCharacters`.
Without a `ScopeAt` hook every offset counts as code, so a letter in a comment opens the window too. The example
sets one presenter hook and also asks at once:

```csharp
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

var editor = new TextEditor { Text = "pri", CaretOffset = 3 };

// For example, a LanguageServerFeatureProvider, which also names the server's trigger characters.
ICompletionProvider provider = CreateCompletionProvider();

using var presenter = new CompletionPresenter(
    editor.TextArea,
    CompletionPresentationOptions.Default,
    new CompletionPresenterHooks { ConfigureWindow = window => window.FontSize = editor.FontSize });

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
- `CompletionPresenter.IsTooltipSupported` reports whether the running editor exposes a styleable
  tooltip; this binding resolves the completion window's private tooltip field by reflection.
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
