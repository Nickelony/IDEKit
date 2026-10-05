# Language-features guide

A host consumes the language-features binding of either a WPF
(`Nickelony.IDEKit.AvalonEdit.LanguageFeatures`) or Avalonia
(`Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures`) Nickelony IDEKit editor.

Both bind the language-feature controllers and services to their engine's editor, one feature per namespace, as
[feature slices](#feature-slices) describes. Divergences are noted where real. The tier model is in the [editor binding guide](EditorBindingGuide.md), and the
shared-source rules are in the [editor sharing guide](EditorSharingGuide.md). A running version of the wiring below is
[`SampleLanguageSession`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Language/SampleLanguageSession.cs), which composes one collaborator per feature from the folder of that
feature.

## Feature slices

The package is organized into vertical feature slices, one namespace per feature folder (for example
`Completion`). Semantic tokens live in `SemanticTokens`, as they do in `LanguageFeatures`. The file tree and
the generated API reference are the inventory.

A helper stays in this package when it consumes a language-feature payload and would otherwise force the
base package to reference `LanguageFeatures` (dependency inversion). Editor-neutral, payload-free helpers
live in the base package; payload consumers live here.

`SemanticTokensColorizer` and `ISemanticTokenStyleResolver` are the clearest case - they paint
`Nickelony.IDEKit.LanguageFeatures.SemanticTokens.SemanticToken`.

Presentation has two senses here, and the names keep them apart. A `...PresentationState` is what a feature
shows now: the completion and signature help controllers expose it through a `CurrentPresentation` property, and
the hover controller deliberately does not, because the host owns the hover surface and learns every display
decision through its single display callback. A `...PresentationOptions` record is how a presenter shows the
feature: its themes, its window geometry and its own settings, which a host passes as the `presentation` argument
of the presenter.

Completion's state union is a visible list, a visible completion tooltip, or a scheduled request; a host
observes an in-flight request through the task that `RequestAsync` returns. Signature help's is a visible
popup, an in-flight request, or a scheduled request. Because the unions differ, neither controller exposes
these flags as separate pass-through properties; a host reads `CurrentPresentation`.

## Host wiring

The controllers and presenters are hook-driven: they take hooks for the host's decisions, so a host wires them to
its own editor state and themes. Host hooks are grouped in dedicated hook types - `HoverControllerHooks` and its
siblings for each controller, and `CompletionPresenterHooks` or `CodeActionMenuHooks` for a presenter - so wiring
stays declarative and new hooks can be added without growing the constructors.

The controllers and their factories take their arguments in the order of
[CONVENTIONS K2](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md#composition); a neutral
controller takes the engine fact (a pointer source, a caret delegate) in place of the text area and its required
dispatcher before the options. The hover, signature help and code-action controllers take the
provider as a constructor argument, so a host does not rebuild the request in a callback; its hook builds the
request record and may veto it. Completion takes its provider and the document identifier the same way, and
its `CompletionSessionKernel` turns the provider's items into a decision.

Completion is neutral the same way, with the window half in a presenter. `CompletionPresenter` takes the text area, a
`CompletionPresentationOptions` bag (its window and tooltip themes, its `CompletionWindowOptions`, its
commit-character setting and its tooltip delay) and a `CompletionPresenterHooks` bag, and creates the
`CompletionWindowPresenter` for that text area itself, exposed as `presenter.WindowPresenter`. It keeps the window,
its sizing, its tooltip and the commit-character policy. A host observes window closures through
`WindowPresenter.WindowClosed` without keeping its own window-tracking state. A host keeps its own completion-item
and window styling (an `ICompletionData` implementation and a `CompletionWindowTheme`) and can supply item factories
and display-text selectors.

`CompletionControllerFactory.Create` takes the text area, the document identifier, the provider, that presenter, a
`CompletionControllerHooks` bag (the scope at the caret and the host context version), a
`CompletionControllerOptions` record (the request delay and the trigger rule) and an optional kernel. It supplies the
engine facts (the caret and the typed characters) and the dispatcher.

The controller it returns owns the triggers, the debounce, the rule that the newest request wins, the move of a late
answer onto the word at the caret and the end of the session when the document is replaced. The two bags share no
member, so a host passes each to the type that reads it.

A host with another editor constructs `CompletionController` over a delegate that captures the document text and
the caret as a `CompletionEditorContext` and an `ICompletionPresenter` that shows the decisions, and forwards
each typed character to `HandleCharacterTyped` and the replacement of its document to `HandleDocumentReplaced`.

Gesture and popup-precedence policy stays in the host: "Ctrl+Space input", "F12 for go-to-definition", or
"do not show hover while another popup is open" are a few lines of host code and are deliberately not part of
the package. The Ctrl+click gesture is binding code, because it claims the press before the editor's selection
handler sees it ([definition navigation](#definition-navigation)).

The hover and signature-help presenters own the closes every popup shares, so a host decides precedence before
a show: a `BuildDecision` hook declines a hover while another popup is open, and the host calls `Dismiss` on the
signature-help controller when its own popup takes over.

`CompletionWindowOptions.PreventActivation` keeps the completion window from taking the editor's keyboard
focus: the presenter makes the window non-activating and installs explicit click selection, so a click in
the list selects an item without taking focus. A host that wants the window to activate normally sets
`PreventActivation = false` in the presentation's `WindowOptions`.

On the Avalonia binding the window is a popup hosted in the editor's own window, so the activation part
of the policy is a no-op there. The click-selection handler still installs when the option is `true`, and
`false` installs neither part.

The presenter installs the commit-character input policy while it is alive. When the selected item's
completion data declares commit characters (`ICompletionCommitCharacters`, which the default adapter
implements from the shared item), a typed character from that set accepts the item and is still typed.
One keystroke therefore both commits and inserts the character.

The policy runs on the text area's tunneling text-input stage (`PreviewTextInput` on WPF, the tunnel pass of
`TextInput` on Avalonia), which precedes `TextEntering`. A `TextEntering` service such as auto-closing
therefore sees the committed item whatever the subscription order. Programmatic input through
`PerformTextInput` bypasses that stage and does not commit. `AcceptOnCommitCharacters = false` in the
presentation turns the policy off.

The default adapter feeds item facts into behavior: the item's additional text edits are applied with
the insertion as one undo unit, and the shared edit kernel skips each entry individually. An entry is
skipped when its replacement text is null, when its range lies outside the document, or when it
conflicts with an earlier entry that was kept. The conflict rules are:

- A boundary touch is accepted: an insertion at a replacement's start or end is kept.
- An insertion strictly inside a replacement conflicts with it.
- Two replacements that overlap conflict.
- A replacement and an insertion at the same start offset do not conflict; the replacement is applied
  first, so the insertion's text lands before the replacement's text.
- The primary insertion is listed first, so a later entry cannot skip it. An entry with an empty range
  and empty text changes nothing and is not reported.

A deprecated item's label renders struck through without changing selection or committing.

The item's commit characters drive the input policy, and the item's `FilterText` (falling back to the
label) is the filter key while the label stays the displayed content.

A host that draws item icons, declines a stale commit or rewrites the insertion keeps the default adapter and
its commit path, additional text edits included, by passing `CompletionItemAdapterHooks` through
`CompletionPresenterHooks.ItemAdapter`. In this example, `icons`, `documentVersion` and `Indent` are host code:

```csharp
var hooks = new CompletionPresenterHooks
{
    ItemAdapter = new CompletionItemAdapterHooks
    {
        ImageFor = item => icons.For(item.Kind),
        CanCommit = item => item.RequestDocumentVersion == documentVersion,
        AdjustInsertion = (snapshot, offset, insertion) => Indent(snapshot, offset, insertion)
    }
};
```

- A `CanCommit` answer of `false` inserts nothing and applies no additional edit, and the window closes as it
  does after any commit.
- `AdjustInsertion` receives the document before the commit, the start offset of the range the insertion
  replaces, and the resolved insertion with a snippet already expanded.
- The result of `AdjustInsertion` is applied with the additional edits in one undo unit, and a snippet stays
  navigable only while the result keeps the text it received ([snippet commits](#snippet-commits)).
- The hooks reach only the default adapter, so a `CompletionItemFactory` that wraps its items in
  `CompletionItemAdapter` passes them to the adapter's constructor.

The hover and signature help controllers are neutral: `HoverControllerFactory.Create` and
`SignatureHelpControllerFactory.Create` take the text area they serve, the provider of the feature, and the
required hooks, and supply the engine facts (the pointer source and the caret) and the dispatcher. The hooks
carry only the host's decisions, and a binding's `HoverPresenter` and `SignatureHelpPresenter` show the answers
in a popup whose content the host builds, such as rendered Markdown:

```csharp
using var hoverPopup = new HoverPresenter(editor.TextArea, (hover, diagnostic) => RenderHover(hover, diagnostic));

using HoverController hover = HoverControllerFactory.Create(
    editor.TextArea,
    hoverProvider,
    new HoverControllerHooks
    {
        BuildRequest = offset => new HoverRequest(documentId, editor.Text, offset),
        ShowHover = hoverPopup.Show
    });

using var signaturePopup = new SignatureHelpPresenter(editor.TextArea, information => RenderSignature(information));

using var signatures = SignatureHelpControllerFactory.Create(
    editor.TextArea,
    signatureHelpProvider,
    signaturePopup,
    buildRequest: (offset, context) => new SignatureHelpRequest(documentId, editor.Text, offset, context));
```

The popups take their look as the `presentation` argument, a `HoverPresentationOptions` or a
`SignatureHelpPresentationOptions` that holds the popup theme, so a host restyles a popup with
`new HoverPresentationOptions { Theme = HoverPopupTheme.Default with { MaxWidth = 480 } }`.
`CompletionPresenter` and `CodeActionPresenter` take a `presentation` argument with their own bag, and the theme property of a presenter restyles it later. The options that time or steer requests stay in the
options records of `Nickelony.IDEKit.LanguageFeatures.Controllers`.

The presenters own the details every host gets wrong:

- The hover popup anchors at the start of the hover's range, else at the start of the diagnostic's range, else at
  the offset the controller targets, and the signature popup anchors at the offset of its request. The anchor
  follows an edit before it and the view's scrolling.
- A popup closes on Esc, when the text area loses keyboard focus, when its window deactivates and when the text
  area replaces its document, and a show while the window is inactive shows nothing.
- Any change of the text, typed, deleted, pasted or applied from another view, closes the hover popup and leaves
  the signature popup open.
- A WPF text area that no WPF `Window` owns, such as one in an `ElementHost`, follows the activation of the Win32
  top-level window that hosts it. An Avalonia text area inside a popup follows the window that hosts the popup.
- The popup takes the text area as its logical parent, so a code block of the content renders on Avalonia.
- The presenter overload of `SignatureHelpControllerFactory.Create` follows the caret: while the popup is open, a
  caret move schedules a `ContentChange` retrigger, whose `null` answer dismisses the popup, and a move before the
  offset that opened the popup dismisses it at once. `SignatureHelpPresenter.Hide` dismisses that controller too.
- Without a `ContextVersionProvider`, a controller from `HoverControllerFactory` counts the document's changes, so
  typed text discards the hover in flight.
- A `null` provider makes a controller that shows the diagnostic of `BuildDecision` under the pointer and never asks
  for hover content, so a host that only marks diagnostics writes no provider and no `BuildRequest` hook.

A host with a popup of its own passes its own `ShowHover`, and `SignatureHelpControllerHooks` to the other
`SignatureHelpControllerFactory.Create` overload; `ShowSignatureHelp` receives the answer and the offset of its
request.

Neither neutral controller decides when to ask. The pointer source of `HoverControllerFactory` asks the hover
controller once the pointer rests for `HoverPointerOptions.RestDelay`, and asks again at once when the pointer
leaves the text area, which hides the popup. A host with its own hover-intent policy sets the delay to `null`
and calls `hover.RequestAsync()` itself.

The controller asks its `IHoverPointerSource` for the document offset under the pointer when a request starts and
again when a result is about to be shown. The source maps the tracked position on every read, so a result for a
pointer that left or moved to another offset is discarded. A request while the source reports no offset - the
pointer left, has not moved since the source was created, or rests in the blank space past the end of a row -
requests nothing and hides the popup.

A host with a pen, touch, or synthetic pointer implements `IHoverPointerSource` over its own input and passes it
to the `HoverController` constructor with an `IUiDispatcher`.

Signature help opens on [typing triggers](#typing-triggers) through the presenter overload; a host on the hooks
overload calls `signatures.RequestAsync(offset)` or `signatures.ScheduleRequest()` from its own typing events.

`BuildRequest` returns `null` to veto a request, which concludes the evaluation like a provider that has nothing
to show. `ShowHover` receives `null` for both content arguments when the host should hide its popup, so one
callback shows and hides. Without a `BuildDecision` hook, the controller requests hover at the offset under the
pointer and shows the content; a host supplies the hook to show the diagnostic there as well, or to decline a
hover.

## Snippet commits

The default `CompletionItemAdapter` inserts a snippet item through the editor's snippet when its expansion has a
tabstop besides the final one, so a call such as LuaLS's `string.rep(s, n, sep?)` is filled in by tabbing through its
parameters. The commit selects the first tabstop in text order, and the editor's snippet input handler owns these
keys until the snippet ends:

| Key | Effect |
| --- | --- |
| Tab | Selects the first tabstop that starts after the caret, and wraps to the first tabstop when none does |
| Shift+Tab | Selects the last tabstop that ends before the caret, and wraps to the last tabstop when none does |
| Enter | Ends snippet mode with the caret where `$0` stood, or at the end of the inserted text without one |
| Esc | Ends snippet mode with the caret where it is |

The snippet also ends when the caret leaves the inserted text, when the text area replaces its document, when its text
is deleted, and when another snippet is committed inside it. The inner snippet replaces the outer one, so Tab then
visits only the inner tabstops. Once the snippet has ended, the editor handles Tab and Enter as it does anywhere else.

- **Typing replaces the selected tabstop**, and a tabstop repeated under one index mirrors it: each repeat shows the
  first occurrence that has text, so the inserted text can exceed the expansion's, and typing in the tabstop updates
  the repeats.
- **A choice placeholder inserts its first choice**, and the editor offers no list of the others.
- **The commit is one undo step.** The commit's one document update holds the snippet and the item's additional edits,
  so one undo removes all of them and ends snippet mode. An additional edit applies before the snippet is inserted, so
  an insertion at the snippet's start stays outside the first tabstop.
- **The editor's snippet inserts the text itself.** Each continuation line takes the indentation of the line that the
  snippet lands on, a tab becomes the editor's indentation string, and each line break becomes the document's line
  terminator, or the platform's when the document has no line break yet.
- **The commit is plain text** in four cases:
  - The expansion has only a final tabstop or none.
  - Tabstops nest, because the editor's snippet holds none inside another, or touch (`${1:name}${2:args}`),
    because Tab cannot step to a tabstop that starts where the previous one ends.
  - A typed commit character commits the item, because the character lands after the commit and would overwrite a
    selected tabstop, so the caret stays at the final tabstop for it.
  - `AdjustInsertion` returns changed text, because the tabstop ranges describe the text it received: a hook that
    returns the text unchanged gets the editor's indentation, a hook that indents the text itself gets a plain
    insertion, and the hook's `CaretOffset` applies to a plain insertion only.
- **A key binding that a host handles above the text area wins in snippet mode too.** The snippet's handler runs on
  the text area's tunneling stage, so a window's `PreviewKeyDown` on WPF or a tunneling `KeyDown` handler on
  Avalonia, as the sample window installs, takes Tab, Enter and Esc first.
  - A host that binds one of them skips it while `TextArea.StackedInputHandlers` is not empty, which holds in
    snippet mode and while a completion window is open.
  - A handler that runs later, such as a bubbling `KeyDown`, never sees these keys in snippet mode, because the
    snippet's handler marks them handled.
- **A host with its own completion data** inserts snippets itself, and `CompletionInsertionResolver.ResolveSnippet`
  returns the expansion it navigates.

## Typing triggers

Completion and signature help open on typed characters by default:

- `CompletionControllerOptions.Triggers` holds a `CompletionTriggerPolicy`. Under `Default`, a letter or an
  underscore typed in code schedules a request after `RequestDebounceDelay`, and a trigger character requests at
  once in every scope, with the character in its `CompletionContext`.
- The trigger characters come from a provider that implements `ICompletionTriggerCharacters`.
  `LanguageServerFeatureProvider` implements it from the server's negotiated capabilities, so the list is empty
  until the session starts. A server can announce characters that should not request, such as a space, a
  parenthesis or a quote, and the default policy requests at once on each of them, so such a host wraps the provider
  as the next paragraph shows.
- `CompletionControllerHooks.ScopeAt` answers the `CompletionScopes` value at the caret. Telling a comment from
  code is language work, so it stays a host hook, and without it every offset counts as code. An answer of
  `None` opens nothing, trigger characters included, so the hook also turns typing triggers off at run time.
- Every `RequestAsync`, a trigger character's included, cancels the word request an earlier keystroke
  scheduled, and the next keystroke's request supersedes it.
- The presenter overload of `SignatureHelpControllerFactory.Create` schedules a `TriggerCharacter` request on a
  character of an `ISignatureHelpTriggerCharacters` provider: a trigger character opens or refreshes the popup,
  and a retrigger character refreshes an open one.

Both evaluate a typed character after its input applied, whichever input handler applied it. The completion
window's commit on a commit character runs first, so a character that both commits and triggers inserts the
item, then opens a new window. An auto-closing insert or an overtype skip triggers as typing does; a wrap of a
selection does not, because the caret ends after the wrapped text.

Input of more than one character, such as a composition result, triggers nothing. Programmatic input such as `PerformTextInput` reaches no preview stage,
so when a `TextEntering` handler subscribed before the controller applies it, as an auto-closing service does,
it triggers nothing either.

A host with its own typing rules sets `CompletionTriggerPolicy.Manual`, then calls `ScheduleRequest` or
`RequestAsync(context)` itself; one with a run-time completion setting answers `None` from `ScopeAt` while the
setting is off. To change only the characters, it wraps its provider in one that answers its own
`TriggerCharacters`. A host whose completion items come from its own catalog rather than a provider implements
`ICompletionProvider` over that catalog, or computes a `CompletionSessionDirective` itself and applies it with
`CompletionPresenter.ApplyDirective`, which applies it as the newest state, so the host discards a stale one itself.
A `RequestLifetime` of `Nickelony.IDEKit.Core.Requests` does that: `RunAsync` admits a computation as the newest one,
and the host applies its directive only while `IsCurrent` accepts the generation `AdvanceGeneration` returned.

The controller's document identifier is fixed, so a document replacement retires it, as
[one controller per document](#one-controller-per-document) says.

## One controller per document

A controller serves the one document whose identifier it carries, so a host recreates it on a document replacement or a
move. Each request carries the editor's current text under that identifier, so a controller that kept working
after a replacement would hand the provider one document's text under another document's identifier.

- `CompletionController` closes its window, cancels its scheduled and in-flight requests, and requests nothing
  more once the text area replaces its document.
- A controller from `DiagnosticsControllerFactory` clears its underlines, cancels its pulls, and pulls and shows
  nothing more, pushes included. A host with its own editor calls `DiagnosticsController.HandleDocumentReplaced`.
- The click navigation that `AttachPointerNavigation` attaches claims no click after a replacement, so a click there is
  a plain click.
- The hover, signature-help and code-action controllers take the identifier from the host's `BuildRequest`
  hook, so a host whose hook captures one recreates them with the others.

## Request coordination semantics

The XML documentation of `HoverController`, `CompletionController`, and
`SignatureHelpController` is the normative contract for request coordination; this section is an
orientation, not the contract.

- `HoverController` - `CancelInFlightRequest` cancels the in-flight request, `InvalidateRequests`
  rejects outstanding results without canceling it, and the optional `ContextVersionProvider` value is
  captured before a request.
- `CompletionController` - the latest request wins: `RequestAsync` cancels the in-flight request and
  rejects its result.
  - `CancelInFlightRequest` cancels the provider token without invalidating the request.
  - `InvalidateRequests` rejects results without canceling.
  - The optional `ContextVersionProvider` value is captured when `RequestAsync` starts, and a decision that
    arrives after the value changed is discarded.
  - `CancelScheduledRequest` cancels only the debounced scheduled request.
  - A superseded tooltip description resolve of `CompletionPresenter` is canceled through the token passed to
    `ResolveDescriptionAsync`.
- `SignatureHelpController` - the latest request wins: starting a request cancels the in-flight one,
  and a superseded response is never applied.
  - `CancelScheduledRequest` clears the scheduled request.
  - `CancelInFlightRequest` cancels the provider token.
  - `InvalidateRequests` rejects results without canceling.
  - `Dismiss` does each of those, then invokes the host dismiss callback.
  - `RequestAsync` reports `true` when it published a presentation, and `false` when the request was
    superseded, canceled, dismissed, or failed.
  - `SignatureHelpControllerOptions` configures navigation and timing.

Each controller owns one internal request lifetime over the core `LatestRequestGate`. The
gate cancels the superseded request's token and rejects its result, so a provider that ignores
cancellation cannot publish a canceled decision. A request that starts after disposal calls no provider,
and disposal cancels the request in flight without superseding it.

Provider failures follow one convention across the controllers. A failure of a provider call or a host
hook inside a controller-owned pipeline is contained, logged through the controller's logger, and the
operation reports its not-applied result. Timer, dispatcher, and event-handler callers therefore never
observe provider exceptions. On a contained failure, completion's `RequestAsync` reports `false`, hover
falls back to its diagnostic or hides its popup, and signature help's `RequestAsync` reports
`false` while the popup keeps its previous presentation.

After disposal, the controllers return safe defaults instead of throwing, because the editor can still raise
their callbacks during teardown; only argument validation throws.

Each debounce default lives on an options record: `RequestDebounceDelay` of `CompletionControllerOptions`,
`SignatureHelpControllerOptions` and `CodeActionControllerOptions`, and `CompletionPresentationOptions.TooltipResolveDelay`
for the tooltip, which the presenter debounces. The hover delay is
`HoverPointerOptions.RestDelay` of `Nickelony.IDEKit.LanguageFeatures.Controllers.Hover`, because the binding's
pointer source decides when the pointer rests, and the hover controller has no options record.

## Completion window sizing and tooltips

The completion window sizes to its content, and the `CompletionWindowOptions` of the presentation set the
sizing:

- The measured content width plus `HorizontalChrome` is clamped by `MaxWidth`, and
  `MaxHeight` caps the content-driven height.
- `MinContentWidth` is the minimum content width measured before the chrome is added, so the
  window floor is `MinContentWidth` + `HorizontalChrome`; the presenter constructor rejects a
  presentation whose `MaxWidth` cannot hold that floor.
- The default measurement sizes the display text and adds an optional icon column (`ItemIconWidth`,
  only for items that supply an image) and an optional detail gap (`ItemDetailSpacing`, only when
  detail text is present).
- Items are measured until the window's maximum content width is reached (identical display strings
  only once per pass), so the widest items size the window.
- A host with a different template supplies `MeasureItemWidth` or overrides the numeric options.

All numeric options are validated. The window chrome comes from `CompletionWindowTheme`, and
`ConfigureWindow` runs after the presenter sized the window - on every open and on every in-place
refresh - so a hook can pin any window or completion-list value, including the width.

Where the running engine exposes a tooltip (see below), the tooltip resolve debouncer is created at
construction and each selection change arms it, so `ResolveDescriptionAsync` descriptions work even
when nothing schedules a request. String descriptions render in a wrapping
`TextBlock`, matching the engine's stock tooltip, and null descriptions keep the tooltip hidden.

Without a `ResolveDescriptionAsync` hook, the tooltip resolves the selected item of the default
`CompletionItemAdapter` itself, so the documentation that a language server sends with `completionItem/resolve`
reaches the tooltip with no host code:

- The resolve runs once the debounce settles, so it covers the selected item only, and it calls the item's own
  resolve callback. An item without a callback, and an item of a host's own completion data, resolve nothing.
- Each item resolves at most once per list: reselecting it shows the kept result and sends no second request. A
  failed or canceled resolve is not kept, so the next selection asks again.
- The resolved item's Documentation replaces what the tooltip shows, by its kind. A resolved item without
  documentation leaves the shown description in place.
- A failed resolve leaves the shown description in place and is logged as the tooltip's other failures are; a resolve
  that a newer selection canceled is not.
- A set hook wins, and the item's callback then never runs.

A Markdown documentation renders through the host's Markdown renderer, as a hover does, because the binding
references no renderer:

```csharp
// markdownTheme is host code; MarkdownRenderer is the binding's Markdown renderer.
var hooks = new CompletionPresenterHooks
{
    BuildMarkdownContent = markdown => MarkdownRenderer.CreateContent(markdown, markdownTheme)
};
```

- The default adapter's `Description` stays the Markdown text, and the tooltip reads the documentation with
  its kind beside it. The returned element replaces the editor's text in the same selection change, so the raw
  Markdown is never drawn.
- A documentation of the plain-text kind, an item that is not a `CompletionItemAdapter`, and a tooltip without the
  hook show their text in the wrapping `TextBlock`. So does a Markdown documentation whose builder returns `null` or throws,
  and the failure is logged.
- A resolved documentation follows the same rule by its kind, whether the item resolved itself or a
  `ResolveDescriptionAsync` result is a `MarkupContent`, so a host resolver returns the resolved item's `Documentation`.
- The element is built once per documentation: the selection change builds it, and the debounced pass and the
  resolved result reuse it while the documentation is the same, until the window closes.
- `CurrentPresentation.TooltipContent` reports what the tooltip was given: the `MarkupContent` of a Markdown
  documentation while the hook is set, a `MarkupContent` that a host resolver returned, and the item's text
  otherwise.

The tooltip chrome (placement, offset, border, padding, and optional brushes) comes from
`CompletionPresentationOptions.TooltipTheme`. `CompletionTooltipTheme.Default` is the starting point.
Its placement, offset, border, and padding apply unconditionally, and only the nullable brushes
(`Background` and `BorderBrush`) leave the theme's tooltip colors in place.

`ConfigureTooltip` runs after the theme is applied, so a host can make a final change to the tooltip. It runs
only when a window opens, not when an open window refreshes in place, and it does not run when the editor's
tooltip cannot be resolved (see [Tooltip availability](#tooltip-availability)).

After an open, the best-matching item is selected. A preselected item (LSP's `preselect`) that survived
filtering wins and is scrolled into view, because the engine's selected-item setter does not scroll. The
flag is read through `ICompletionPreselection`, which the default adapter implements.

`WindowClosed` on the presenter's `WindowPresenter` is raised exactly once per shown window, whether
it closes through `Close`, `Dispose`, or being replaced by a window for a new range.

## Completion requests and decisions

Create the presenter and then the controller on the editor thread, and let the `using` declarations dispose them
in reverse order at the end of the scope. A decision the host computes outside a request is applied with
`CompletionPresenter.ApplyDirective`.

```csharp
// Also import the binding's Completion namespace.
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

using var completion = new CompletionPresenter(editor.TextArea, CompletionPresentationOptions.Default);

using CompletionController controller = CompletionControllerFactory.Create(
    editor.TextArea,
    documentId: "C:/scripts/main.lua",
    provider,
    completion);

// RequestAsync asks the provider at the caret, applies the decision, and reports whether a window opened.
bool applied = await controller.RequestAsync();
```

`RequestAsync` starts the request as the latest one with a snapshot of the document and the caret, and the
kernel computes the `CompletionSessionDirective` from the provider's items and the word before the caret. A
superseded or canceled result is discarded, and so is a result that arrives after the `ContextVersionProvider`
value changed. A result that arrives after typing follows the
[late-answer rule](#answers-that-arrive-after-typing).

It applies the decision on the editor thread through the dispatcher, even when the continuation already runs
there.

Cancellation and provider or callback failure are contained and report `false` rather than throwing, and a
failure is also logged.

A host that runs its own request steps, such as extra provider round trips or several sources, puts them in
its `ICompletionProvider`, which receives the request's token, or passes a kernel with its own
`CompletionWordLocator` and `CompletionItemFilter`. A failed request and an answer without items leave an open
window as it is.

`ApplyDirective` maps the decision's items through the `CompletionItemFactory` hook when it is set, and
through the built-in `CompletionItemAdapter` otherwise, so plain label/description/insert-text items
need no host mapper. A host that already holds shared `CompletionItem` instances wraps them with
`CompletionSessionDirective.Open(items, replacementRange)` and calls `ApplyDirective`.

`ApplyDirective` throws `InvalidOperationException` when the factory returns `null` for an item, so a
host maps only shapes it can present.

The decision's item set is the shared session kernel's answer for the typed word. The engine's list
re-filters and ranks the same collection for display. The default adapter exposes the item's `FilterText`
as the engine's filter key, so both matchers read the same text.

The split is deliberate - the kernel owns session decisions and the engine owns list presentation -
which is why both filters must agree on the same key.

## Answers that arrive after typing

A provider can answer after the user typed on: a trigger character requests at once, and the letters typed
right after it arrive before the server answers. `RequestAsync` therefore compares the answer's word with the word
at the caret when the answer arrives, using the kernel's `CompletionWordLocator`, as VS Code does:

- **The answer stays valid** while the word at the caret starts where the answer's word started, reaches at least
  as far as it did, and still begins with its text.
- **It then opens on the word as it stands now**: the window spans the text typed since the request, so the
  engine's list filters by it and selects the best match.
- **For example,** an answer for `string.` with `re` typed after it opens the items that match `re`, and with the
  caret moved back between `r` and `e` it opens those that match `r`.
- **Any other change discards the answer.** A space or a line terminator after the word, a move to another word,
  a move back into the word the answer was computed for, and an edit before the word fail the test.
  `RequestAsync` reports `false` and an open window stays as it is.
- **A commit replaces the typed text too.** When an item's range ends before the caret, the commit replaces from the
  range start up to the caret and never past the live completion segment's end, so `string.` plus `re` and the
  item `rep` commit `string.rep`.
- **The insert range of an `InsertReplaceEdit` keeps the text after the caret** when the segment spans the whole
  word. A range that reaches the caret or passes it is used as is.

`ApplyDirective` applies the decision it is given and checks none of this, so a host that applies its own
decisions discards a stale one itself.

## Refreshing an open window

`OpenOrRefresh(items, replacementRange)` takes the range the chosen item replaces; the shared
`CompletionSessionDirective` type carries the same range on every decision that has items, so
`ApplyDirective` never receives a partially populated decision.

When a window is open and the requested range starts where the open window starts, the window
is refreshed in place. Its end offset and item list are updated, and the list is re-filtered for the new
query, even when the query did not change, including the empty query. The initial selection is
re-established, with no close/reopen flash and no second `WindowClosed` report.

Offsets beyond the document end are clamped.

A refreshed window whose filtered list is empty closes itself when the window option `DismissWhenEmpty` is enabled; the
call still reports `true`, because the refresh was applied.

A different start offset starts a new session: the old window closes (reporting the closure) and a new
one opens for the new range.

## Tooltip availability

`CompletionPresenter.IsTooltipSupported` is a static query that reports whether the running editor
exposes a styleable tooltip, and the tooltip-aware paths (`ResolveDescriptionAsync` tooltips and tooltip
styling) run only when it does.

The WPF binding resolves the completion window's private tooltip field by reflection and decorates the
existing tooltip; when the field is absent it reports `false` and skips those paths (logged once). The
Avalonia binding publishes no such member, so it declares no tooltip theme, hooks or query and builds no
tooltip pipeline, while everything else keeps working. Its engine shows an item's `Description` in its own
tooltip, so a Markdown documentation shows there as raw text.

The WPF binding owns no second tooltip, because a replacement would render the same content twice and
lose the engine's own show/hide bookkeeping.

## Diagnostics

`DiagnosticsControllerFactory.Create` underlines the diagnostics of one document. It installs a
`DiagnosticsRenderer` in the text view, asks an `IDiagnosticsProvider` once the edits settle, and shows what an
`IDiagnosticsSource` pushes, such as the diagnostics of a language server. Disposing the controller removes the
renderer.

```csharp
using DiagnosticsController diagnostics = DiagnosticsControllerFactory.Create(
    editor.TextArea,
    documentId,
    provider: null,
    source: languageServerProvider,
    configureRenderer: renderer => renderer.ErrorPen = errorPen);

// The diagnostic under the pointer, found where its text is now.
Diagnostic? diagnostic = diagnostics.FindAt(offset);
```

- The underlines follow the edits made after a set arrived: a line typed above an error moves its underline at
  once, and a deletion over the faulty word removes it before the producer answers.
- The newest delivery wins. A pushed set replaces the shown one, and so does the answer of a pull, unless an edit
  or a push came after the pull started.
- A pushed set is shown at the offsets it carries, in the text as it is now: the controller does not map it through
  the edits made since its producer read the text.
- A source therefore delivers only a set for the text that the host last reported, as the
  [`IDiagnosticsSource` contract](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.LanguageFeatures/Diagnostics/IDiagnosticsSource.cs)
  says. `ProjectDiagnosticsSource` drops the result of an analysis that a later change superseded, and the
  language-server provider drops a payload that names another document version than the one it tracks.
- A set reaches the controller one dispatcher turn after its source raised it, so an edit handled in between shows
  the set shifted until the next set replaces it.
- Every edit restarts `DiagnosticsControllerOptions.SettleDelay` and cancels the pull in flight, so typing pulls
  once, after the last keystroke.
- `Current` holds the ranges the producer reported, while `FindAt` and `TryGetShownRange` answer where each
  diagnostic is shown now. `FindAt` serves the diagnostic of a `HoverDecision`, so the hover anchors at the moved
  text, and a problems list reads each row's range through `TryGetShownRange` when `Changed` is raised or the text
  changes.
- A controller needs a provider, a source, or both.

## Code actions

`CodeActionController` is host-driven and engine-free: the host's `BuildRequest` hook builds a
`CodeActionRequest` from the editor context (returning `null` vetoes the context), the provider supplies
the actions, and the host's `ExecuteActionAsync` hook applies the invoked action. An action's edit can name
several documents, so a host applies it as it applies a rename, for example through `WorkspaceEditApplier`,
which plans every document's edits against the text its views published and changes nothing when the plan fails.

`CodeActionControllerFactory.Create` supplies the engine side: it reads the caret, the selection, and the document
of the text area, and runs the controller on the text area's dispatcher.

`CodeActionPresenter` shows the controller's actions in the text area. Its `Margin` renders the indicator,
and `TryOpenMenu` opens the themed menu at the indicator line. Add the margin to the editor's left
margins. Editor context changes schedule requests on their own. `RequestAsync` covers provider state that
changed without a caret move, for example new diagnostics, and it supersedes any scheduled or outstanding
request. The host key gesture (Ctrl+.) calls `TryOpenMenu`.

The published snapshot is readable through `Actions`. A host with its own indicator opens the same menu
at its own click point with `TryOpenMenu(line, anchor)`. The menu surface stays library-owned. Its chrome
is customizable through `CodeActionMenuHooks.ConfigureMenu`, and each created item through
`CodeActionMenuHooks.ConfigureMenuItem`.

The margin's left-press gesture is the library default: a host that owns the gesture sets
`CodeActionMargin.OpensOnLeftClick` to `false` and opens the menu itself.

An action that a provider offered without an edit, with `Data` alone, is resolved when the user chooses it: the
controller asks the provider's `ICodeActionResolver` for the edit, once and only for that action, and the execute
hook receives the resolved action. After the hook returns, the provider's `ICodeActionCommandRunner` runs the
action's command, so the command sees the edit applied; a hook that throws ends the action before the command.

The language-server provider implements both contracts, so a Roslyn quick fix and a LuaLS command action work without
host code beyond the execute hook, and a server command that answers with `workspace/applyEdit` reaches the host
through `LanguageServerFeatureProviderHooks.ApplyWorkspaceEdit`.

Actions are snapshots, and the controller guards them: it refuses an action when the editor's text differs from the
text of the selection the published actions were requested for, or when the action is not one of `Actions`, and the
execute hook never receives a stale action. An action that needs the provider is checked again after the resolution
returns, because the user can type while the server computes the edit, and a second call for an action that is
still running does nothing.

`ExecuteActionAsync` returns a `CodeActionExecutionOutcome`, so a host with its own
menu or key command can tell the user why nothing changed; the controller logs the refusals as well.

The host's edit pipeline stays the last guard for text that changes after the hook starts. The menu presents the
snapshot that was published when it opened, and the presenter closes it whenever the controller replaces
or clears that snapshot, so its items always belong to the current set.

```csharp
using var codeActions = CodeActionControllerFactory.Create(
    editor.TextArea,
    codeActionProvider,
    new CodeActionControllerHooks(
        BuildRequest: BuildCodeActionRequest,
        ExecuteActionAsync: ApplyCodeActionAsync));

using var presenter = new CodeActionPresenter(
    editor.TextArea,
    codeActions,
    CodeActionPresentationOptions.Default);

// The margin renders the light bulb and opens the menu on a left press on its line; set
// OpensOnLeftClick = false to make the host own the gesture and call TryOpenMenu directly.
editor.TextArea.LeftMargins.Add(presenter.Margin);

// Request again whenever provider state changed outside the editor (for example diagnostics).
await codeActions.RequestAsync();
```

On editor teardown, dispose the presenter and then the controller, and remove the margin from the text
area. The presenter does not own the controller, and it leaves the margin in place when it is disposed.

## Definition navigation

`TextAreaNavigationExtensions` navigates a text area to a definition. A host with an `IDefinitionProvider`, a
language-server provider included, calls `GoToDefinitionAsync` from its F12 command and attaches the Ctrl+click
gesture once:

```csharp
// F12: the definition of the symbol at the caret.
await editor.TextArea.GoToDefinitionAsync(documentId, provider, editor.CaretOffset, OpenOtherDocumentAsync);

// Ctrl+click, or Command+click on macOS in the Avalonia binding, until the subscription is disposed.
using IDisposable pointerNavigation = editor.TextArea.AttachPointerNavigation(documentId, provider, OpenOtherDocumentAsync);
```

- The gesture claims a single left press that holds exactly its modifiers over the text of a line, so the press
  neither moves the caret nor starts a selection.
- A press without the modifiers, a double press, and a press on a margin or past the end of a line stay plain
  clicks.
- A click that resolves no definition places the caret at the click, where a plain click puts it.
- A later navigating click cancels the one in flight, and only the later one navigates.
- A definition in another document goes to the cross-file callback, which opens that document and moves its
  caret.
- `GoToDefinitionByHoverAsync` serves a provider whose hover names the symbol, and `GoToDefinitionBySymbolAsync`
  serves a name catalog that resolves without a position. Neither navigates against a language server, because
  its hover names no symbol and it resolves by position only.

## Thread affinity

The controllers are created and used on the thread that owns the state they touch: for the
editor-bound controllers, the editor thread. A neutral controller reads the editor through its source and
shows results through host hooks on that thread, the completion controller drives its window there, and
the debounce timers run on that thread's dispatcher.

Provider requests stay asynchronous. Their continuations marshal back to the owning thread, and every publish
goes through the dispatcher even when that continuation already runs there. A provider that completes on a
thread-pool thread therefore never touches dispatcher-bound state from there.

The marshaled publish is a blocking dispatch. A caller that starts a request on the owning thread must
await the returned task, because blocking the owning thread prevents the publish from running.

Every controller runs on one dispatcher, and it fails fast when it is created on another thread. Each neutral
controller takes its `IUiDispatcher` as a constructor argument, so a test can drive it, and its debounce timer,
with a dispatcher of its own. Their factories pass the adapter of the text area's dispatcher (the base
package's `UiDispatcher`) and verify the thread before they subscribe to the text area.

A host that implements `ICodeActionSelectionSource` raises its two events on that dispatcher's thread, because the
controller rejects a notification from another thread. The publication step runs on that dispatcher
and checks once more that its request is still current, so a result that a newer request,
`InvalidateRequests`, `CancelInFlightRequest`, disposal, a code-action context change, or for signature help
`Dismiss`, made stale never shows, whichever thread the provider continuation ran on.

## Logging

Controllers accept an optional `Microsoft.Extensions.Logging.ILogger` and default to `NullLogger` when none is
supplied, so hosts control where request failures are logged. Event ids follow the [log event
ids](ArchitectureGuide.md#log-event-ids) registry.
