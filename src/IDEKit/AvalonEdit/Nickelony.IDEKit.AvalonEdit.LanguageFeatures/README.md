# Nickelony.IDEKit.AvalonEdit.LanguageFeatures

The language-features UI layer of the Nickelony.IDEKit package family. It brings
the hover, signature-help, completion-window, and code-action controllers plus the
definition-navigation, diagnostic-segment projection, and semantic-token colorizing
helpers to AvalonEdit editors, built on the dependency-free
`Nickelony.IDEKit.Core` and `Nickelony.IDEKit.IntelliSense` contracts and
host-state records plus the `Nickelony.IDEKit.AvalonEdit` document/editing
helpers.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvalonEdit.LanguageFeatures
```

The [wiring example](#wiring-example) below composes the controllers on an
AvalonEdit editor; the per-slice inventory and the behavioral reference live in
[REFERENCE.md](REFERENCE.md).

## Requirements

.NET 8 (`net8.0-windows`) and WPF; AvalonEdit 6.3.1.120.

## Dependencies

`Nickelony.IDEKit.Core`, `Nickelony.IDEKit.IntelliSense`, and `Nickelony.IDEKit.AvalonEdit`, plus `Microsoft.Extensions.Logging.Abstractions` for the controllers' optional loggers.

### Logging

Controllers accept an optional `Microsoft.Extensions.Logging.ILogger` and
default to `NullLogger` when none is supplied, so hosts control where request
failures are logged. Event ids are stable, unique within this package, and drawn
from the package's own `1000` block, so they never collide with the larger blocks
the sibling packages allocate (Markdown `2000`, TextMate `3000`, and
`Nickelony.KeyBindings` `4000`): completion request failures use
`1000`, the completion tooltip presenter uses `1001` (description resolve
failed) and `1002` (tooltip access unsupported), hover uses `1010`-`1012`
(request failed, host callback failed, offset mismatch), signature help uses
`1020` (request failed) and `1021` (host callback failed), and code actions use
`1030` (request failed) and `1031` (host callback failed).

## Thread affinity

The controllers are created and used on the UI thread that owns the editor: they
touch editor and popup state directly, and their debounce timers run on that
thread's dispatcher. Provider requests stay asynchronous; their continuations are
marshalled back to the owning thread, explicitly when the creating thread captured
no synchronization context. The normative thread-affinity contract is in
[REFERENCE.md](REFERENCE.md).

## Package layering

A helper stays in this package when it consumes an IntelliSense payload and would
otherwise force the base package to reference IntelliSense (dependency inversion).
`SemanticTokensColorizer` and `ISemanticTokenStyleResolver` are the clearest
instance of that rule: they paint
`Nickelony.IDEKit.IntelliSense.SemanticTokens.TextSemanticToken`, so they live here
instead of in `Nickelony.IDEKit.AvalonEdit`. Keep editor-neutral, payload-free
helpers in the base package and IntelliSense-payload consumers in this one.

## Host integration seams

The controllers are hook-driven: they take hooks for offset resolution,
request execution, and presentation, so a host wires them to its own editor
state and skins. Host hooks are grouped in dedicated hook types
(`TextCompletionControllerHooks`, `TextHoverControllerHooks`,
`TextSignatureHelpControllerHooks`, and `TextCodeActionControllerHooks`), so wiring stays declarative and new hooks
can be added without growing the constructors. `TextCompletionController` takes
the text area, its `CompletionWindowSkin`, the options record, and the hooks, and
creates the `CompletionWindowCoordinator` for that text area itself, exposed as
`controller.WindowCoordinator`: a host observes window closures through
`WindowCoordinator.WindowClosed` without keeping its own window-tracking state
(the closure and presentation guarantees are normative and live in
[REFERENCE.md](REFERENCE.md)). Hosts keep their own completion-item
and window styling (for example a `CompletionData` type and window-style skin)
and can supply item factories and display-text selectors. The scheduled request
callback is a hook (`TextCompletionControllerHooks.ScheduledRequestAsync`): the
controller is fully configured when it is constructed, and a host that never
schedules requests simply omits the callback.

Trigger and popup-precedence policy stays in the host: helpers such as
"Ctrl+Space input", "F12 or Ctrl+Click for go-to-definition", or "do not show
hover while another popup is open" are a few lines of host code and are
deliberately not part of this package.

The controllers bind to the editor differently by design: the completion
controller takes the text area (it owns its window coordinator), the hover
controller takes the element hover positions resolve against plus its hooks,
and the signature help controller is host-state driven through hooks only. Each
shape matches what the controller actually touches.

The completion window is shown non-activatable by default, so clicks in the list
never activate the window or steal focus from the editor. A host that wants the
window to activate normally sets `NonActivatingWindow = false`; the option
remarks describe both modes. The empty-list close behavior is normative and lives
in [REFERENCE.md](REFERENCE.md).

The controller also installs the commit-character input policy while it is
alive: when the selected item's completion data declares commit characters
(`ICommitCharacterCompletionData`, which the default bridge implements from the
shared item), a typed character from that set accepts the item and is still
typed, so one keystroke both commits and inserts the character. The policy runs
in the text-input preview stage, before text-composition services such as an
auto-closing service see the character, and it repeats the check in the text-entering
stage, which is how programmatic input arrives. Handlers that share the preview stage
still run in subscription order, so a host that must see the character first subscribes its input services before the controller; `AcceptOnCommitCharacters = false` turns the policy off.

The default bridge likewise feeds item facts into behavior:

- The item's additional text edits are applied together with the insertion as one undo unit
  (malformed, stale, or conflicting entries are skipped individually through the shared edit kernel).
- A deprecated item's label renders struck through without changing selection or committing.
- The item's commit characters drive the input policy above.
- The item's `FilterText` (falling back to the label) is the filter key; the label stays the
  displayed content.

## Wiring example

The following sketch wires the completion and hover controllers for an editor;
production hosts add lifetime management (`Dispose` on editor teardown) and
their own item and tooltip presentation. `RequestCompletionAsync`,
`RefreshCompletionState`, `BuildHoverRequestState`, `RequestHoverAsync`, and
`ShowHoverTooltip` stand for host-defined methods used by the sketch.

```csharp
var options = TextCompletionControllerOptions.Default with
{
    RequestDebounceDelay = TimeSpan.FromMilliseconds(50.0)
};

// The scheduled-request hook runs the standard pipeline through the controller field, so the
// field is assigned after construction from the callback's point of view.
TextCompletionController? completion = null;

completion = new TextCompletionController(
    editor.TextArea,
    CompletionWindowSkin.Default,
    options,
    new TextCompletionControllerHooks
    {
        ConfigureWindow = window => window.FontSize = editor.FontSize,
        GetDisplayInfo = item => (item.Text, null),
        ResolveDescriptionAsync = (item, cancellationToken) => Task.FromResult<object?>(item.Description),
        TooltipSkin = CompletionTooltipSkin.Default with
        {
            Background = Brushes.Black,
            BorderBrush = Brushes.Gray
        },

        // The debounced request produces the decision; its items go through the default
        // TextCompletionItemCompletionData adapter, so no item mapper is needed here.
        ScheduledRequestAsync = () => completion!.RequestAsync(RequestCompletionAsync)
    });

// Observe every closure of a shown window, including host-forced closes.
completion.WindowCoordinator.WindowClosed += (_, _) => RefreshCompletionState();

var hover = new TextHoverController(
    editor,
    new TextHoverControllerHooks
    {
        GetOffsetFromPoint = point => editor.GetPositionFromPoint(point) is { } position
            ? editor.Document.GetOffset(position.Location)
            : null,
        BuildEvaluationState = BuildHoverRequestState,
        RequestHoverAsync = RequestHoverAsync,
        ShowTooltip = ShowHoverTooltip
    });
```

## Reference

The normative reference lives in [REFERENCE.md](REFERENCE.md): the feature-slice inventory, the request-coordination and completion-decision semantics, completion window sizing and tooltips, tooltip availability, and thread affinity.

## License

MIT © 2026 Kewin Kupilas.
