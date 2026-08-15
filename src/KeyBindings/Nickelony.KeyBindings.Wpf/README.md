# Nickelony.KeyBindings.Wpf

WPF adapter for [`Nickelony.KeyBindings`](../Nickelony.KeyBindings/README.md).
It maps WPF input to the host-neutral key model and provides the WPF entry points.

## Getting started

```sh
dotnet add package Nickelony.KeyBindings.Wpf
```

A WPF host chooses its display-text formatter explicitly and dispatches key events
through the adapter:

```csharp
var options = new KeyBindingServiceOptions
{
	DisplayTextFormatter = KeyGestureDisplayTextFormatter.Default
};
```

```csharp
using Nickelony.KeyBindings.Wpf;

private void OnEditorKeyDown(object sender, KeyEventArgs e)
{
	// Both results consume the keystroke: Executed ran a command, Pending buffered a chord.
	// The second argument is the host's active context token, or null when it has none.
	if (_shortcutDispatcher.HandleKeyDown(e, _editor.ActiveMode.Token) != KeyChordDispatchResult.NotHandled)
		e.Handled = true;
}
```

## Dependencies

`Nickelony.KeyBindings` (the host-neutral command and key model the adapter
maps onto WPF) and WPF itself (the package targets `net8.0-windows`).

## What's inside

- `KeyCodeMapper` - converts between `System.Windows.Input.Key` and `KeyCode`, and
  between `ModifierKeys` and `KeyModifierSet`.
- `KeyEventArgsExtensions.ToKeyCombo` - creates a `KeyCombo` from a `KeyEventArgs`,
  normalizing `Key.System` and reading modifiers from the event's keyboard device.
- `KeyEventArgsExtensions.IsAltGr` - reports whether an event was produced by AltGr, which
  Windows synthesizes as left Ctrl plus right Alt.
- `KeyBindingDispatcherExtensions.HandleKeyDown` - dispatches a `KeyEventArgs`
  through `KeyBindingDispatcher<TCommandId>` and returns `KeyChordDispatchResult`:
  `Executed` when a command ran, `Pending` when the stroke started or continued a
  chord, and `NotHandled` otherwise. The caller marks the event `Handled` for both
  `Executed` and `Pending`, because a buffered chord consumed the keystroke. The active
  context token is a required argument, so a host whose control has view modes passes the
  token of its current mode and a host with no contexts passes `null`.
- `KeyGestureDisplayTextFormatter` - derives from `KeyDisplayTextFormatter` and renders
  culture-aware WPF gesture text (`Meta` renders as `Windows+`). Letters and main-row
  digits render as characters; other keys render as their `Key` names (for example
  `Ctrl+OemQuestion`), combos WPF rejects as gestures fall back to WPF's converter
  text (`Shift+S`, `5`), and a chord's gestures are joined with a comma (`Ctrl+K,
  Ctrl+S`).

The Windows desktop conventions (`Ctrl+`, `Win+`, `Enter`, `Esc`, and US-layout glyphs such as `=`
for `Equals`, with a `Num ` prefix for the numpad keys) come from the `Windows` preset of
`DesktopKeyDisplayTextFormatter` in the host-neutral `Nickelony.KeyBindings` package; a bare
`DesktopKeyDisplayConventions` instead renders the neutral labels (`Control`, `Meta`, `Numpad `). That
formatter depends on no desktop toolkit, so a host does not need this package for the Windows text
alone; the same formatter ships the Linux (`Ctrl+`, `Super+`) and macOS (`⌃⌥⇧⌘`) conventions as
`DesktopKeyDisplayTextFormatter.Linux` and `.MacOS`. An Avalonia host uses `Nickelony.KeyBindings.Avalonia` instead, which carries the same key
mapping, event conversion, and dispatch surface for Avalonia's input model.

`HandleKeyDown` never dispatches an event another handler already marked handled, and it
drops auto-repeat and AltGr strokes unless `KeyDownHandlingOptions` opts them back in. Pass
`new KeyDownHandlingOptions { IgnoreAutoRepeat = false }` for commands that are meant to
repeat while the key is held. IME and dead-character events are never converted to a key
combo, so they never dispatch.

A `Pending` result means the dispatcher buffered a chord stroke. The adapter contains no
timer: the host supplies the cancel triggers with `CancelPendingChord` (Escape, focus loss,
an idle timeout of its own), because the timing policy belongs to the toolkit. A host that
participates in a context chain should route the stroke with
`KeyBindingDispatcherChain.DispatchInPriorityOrder` instead of calling the adapter for one
dispatcher, and it passes the same active token to every dispatcher in the chain.

## License

MIT © 2026 Kewin Kupilas.
