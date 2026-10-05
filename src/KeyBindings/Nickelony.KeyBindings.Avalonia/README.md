# Nickelony.KeyBindings.Avalonia

Avalonia adapter for
[`Nickelony.KeyBindings`](https://github.com/Nickelony/LanguageServer/blob/main/src/KeyBindings/Nickelony.KeyBindings/README.md):
it maps Avalonia input to the toolkit-neutral key model and provides the Avalonia entry point. It depends on
`Nickelony.KeyBindings` and `Avalonia`, targets `net8.0`, and uses only `Avalonia.Input`, unlike the
Windows-only `Nickelony.KeyBindings.Wpf` sibling.

## Getting started

```sh
dotnet add package Nickelony.KeyBindings.Avalonia --prerelease
```

An Avalonia host dispatches key events through the adapter:

```csharp
using Avalonia.Input;
using Nickelony.KeyBindings;
using Nickelony.KeyBindings.Avalonia;

// The handler of the key-down event of the element that has the focus. The dispatcher is built as in the
// Nickelony.KeyBindings readme, and the gate is created once over the element that routes the key events.
void OnEditorKeyDown(IKeyBindingDispatcher dispatcher, KeyRepeatGate repeatGate, KeyEventArgs e)
{
    // Both outcomes consume the stroke: Executed ran a command, Pending buffered a chord.
    // The second argument is the active context token of the host, or null when it has none;
    // the third ignores a repeated press of a held key.
    if (dispatcher.HandleKeyDown(e, context: null, repeatGate) != KeyStrokeDispatchOutcome.NotHandled)
        e.Handled = true;
}
```

## Remarks

- **Handled events are skipped.** An event another handler already marked handled is never dispatched.
- **Auto-repeat needs a gate.** Avalonia's `KeyEventArgs` exposes no auto-repeat flag, so the adapter tracks held keys
  in a `KeyRepeatGate`, which you create over the element that routes the key events (`new KeyRepeatGate(window)`) and
  dispose with it.
- **The gate admits a press once**, forgets a key on its release (handled releases included), and forgets
  every key when focus leaves its scope or its window is deactivated. A call without a gate dispatches every repeat.
- **No AltGr filter.** An AltGr filter would depend on how Avalonia reports the left Ctrl and right Alt pair that
  Windows sends, and a Ctrl+Alt heuristic would silently drop genuine Ctrl+Alt bindings. WPF's `KeyDownHandlingOptions`
  has no counterpart here.
- **No display formatter.** Display text is toolkit-free: use the neutral `KeyDisplayTextFormatter`, or a
  `DesktopKeyDisplayTextFormatter` over a `KeyBindingPlatformProfile` preset, through `KeyBindingServiceOptions.DisplayTextFormatter`.

The [key bindings guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/KeyBindingsGuide.md) owns
the dispatch and hosting contracts, the pending-chord cancel triggers, and the display text conventions.

## License

MIT © 2026 Kewin Kupilas.
