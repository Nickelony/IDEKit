# Nickelony.KeyBindings.Wpf

WPF adapter for
[`Nickelony.KeyBindings`](https://github.com/Nickelony/LanguageServer/blob/main/src/KeyBindings/Nickelony.KeyBindings/README.md):
it maps WPF input to the toolkit-neutral key model and provides the WPF entry points. It depends on
`Nickelony.KeyBindings` and WPF and targets `net8.0-windows`.

## Getting started

```sh
dotnet add package Nickelony.KeyBindings.Wpf --prerelease
```

A WPF host chooses its display-text formatter explicitly and dispatches key events
through the adapter:

```csharp
using Nickelony.KeyBindings;
using Nickelony.KeyBindings.Wpf;

// The catalog and the overrides store are built as in the Nickelony.KeyBindings readme.
var options = new KeyBindingServiceOptions
{
    DisplayTextFormatter = KeyGestureDisplayTextFormatter.CurrentCulture
};

var service = new KeyBindingService<AppCommand>(catalog, overridesStore, options);
```

```csharp
using System.Windows.Input;
using Nickelony.KeyBindings;
using Nickelony.KeyBindings.Wpf;

// The handler of the key-down event of the element that has the focus.
void OnEditorKeyDown(IKeyBindingDispatcher dispatcher, KeyEventArgs e)
{
    // Both outcomes consume the stroke: Executed ran a command, Pending buffered a chord.
    // The second argument is the active context token of the host, or null when it has none.
    if (dispatcher.HandleKeyDown(e, context: null) != KeyStrokeDispatchOutcome.NotHandled)
        e.Handled = true;
}
```

## Remarks

- **Key conversion.** `ToKeyStroke` normalizes `Key.System`, reads modifiers from the event's keyboard
  device, and returns no stroke for IME or dead-character events.
- **AltGr.** `IsAltGr` reports an AltGr stroke, which Windows synthesizes as left Ctrl plus right Alt on a key
  that types a character, so a Ctrl+Alt chord on an F-key is not AltGr.
- **Dropped strokes.** An event another handler already marked handled is never dispatched, and auto-repeat
  and AltGr strokes are dropped unless `KeyDownHandlingOptions` opts them back in (pass
  `new KeyDownHandlingOptions { IgnoreAutoRepeat = false }` for commands meant to repeat while the key is
  held).
- **Display text.** `KeyGestureDisplayTextFormatter` renders the text WPF's converter produces: `Meta` as
  `Windows+`, letters and main-row digits as characters, WPF's key text for the rest (so `Slash` renders as
  `OemQuestion`), and a chord's strokes joined with a comma.
- **Unsupported gestures.** WPF rejects a letter or main-row digit without Ctrl, Alt, or Windows, such as
  `Shift+S`, as a gesture, so the formatter renders the modifier-and-key text instead of throwing.
- **Not the Windows profile.** A `DesktopKeyDisplayTextFormatter` over `KeyBindingPlatformProfile.Windows` is
  toolkit-free and renders different text: it labels `Meta` as `Win` and renders `Slash` as `/`.

The [key bindings guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/KeyBindingsGuide.md) owns
the dispatch and hosting contracts, the pending-chord cancel triggers, and the display text conventions.

## License

MIT © 2026 Kewin Kupilas.
