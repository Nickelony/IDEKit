# Nickelony.KeyBindings.Avalonia

Avalonia adapter for [`Nickelony.KeyBindings`](../Nickelony.KeyBindings/README.md).
It maps Avalonia input to the host-neutral key model and provides the Avalonia entry point.
The package is cross-platform: it targets `net8.0`, unlike the Windows-only
`Nickelony.KeyBindings.Wpf` sibling.

## Getting started

```sh
dotnet add package Nickelony.KeyBindings.Avalonia
```

An Avalonia host dispatches key events through the adapter:

```csharp
using Nickelony.KeyBindings.Avalonia;

private void OnEditorKeyDown(object? sender, KeyEventArgs e)
{
	// Both results consume the keystroke: Executed ran a command, Pending buffered a chord.
	// The second argument is the host's active context token, or null when it has none.
	if (_shortcutDispatcher.HandleKeyDown(e, _editor.ActiveMode.Token) != KeyChordDispatchResult.NotHandled)
		e.Handled = true;
}
```

## Dependencies

`Nickelony.KeyBindings` (the host-neutral command and key model the adapter maps onto) and
`Avalonia` (whose `Avalonia.Input` model it uses). The package targets `net8.0` and uses only the
`Avalonia.Input` types, so a cross-platform Avalonia host takes no Windows-only dependency.

## What's inside

- `KeyCodeMapper` - converts between `Avalonia.Input.Key` and `KeyCode`, and between
  `Avalonia.Input.KeyModifiers` and `KeyModifierSet`.
- `KeyEventArgsExtensions.ToKeyCombo` - creates a `KeyCombo` from a `KeyEventArgs`, reading the key
  from `KeyEventArgs.Key` and the modifiers from `KeyEventArgs.KeyModifiers`.
- `KeyBindingDispatcherExtensions.HandleKeyDown` - dispatches a `KeyEventArgs` through
  `KeyBindingDispatcher<TCommandId>` and returns `KeyChordDispatchResult`: `Executed` when a command ran,
  `Pending` when the stroke started or continued a chord, and `NotHandled` otherwise. The caller marks the
  event `Handled` for both `Executed` and `Pending`. The active context token is a required argument, so a
  host whose control has view modes passes the token of its current mode and a host with no contexts
  passes `null`. An event another handler already marked handled is never dispatched.

The adapter carries the input axis only - key mapping, event conversion, and dispatch. Two parts of
the `Nickelony.KeyBindings.Wpf` surface are deliberately absent, because Avalonia cannot express what
they need:

- **No `KeyDownHandlingOptions`.** Avalonia's `KeyEventArgs` exposes no auto-repeat flag (there is no
  auto-repeat signal anywhere in Avalonia) and no left/right key state (`IKeyboardDevice` is empty), so
  neither an auto-repeat filter nor a correct AltGr filter can be written. A Ctrl+Alt heuristic would
  silently drop genuine Ctrl+Alt bindings, so a host that needs either filter applies its own policy
  before it calls the adapter.
- **No display formatter.** Display text is toolkit-free: the neutral `KeyDisplayTextFormatter` renders
  canonical names, and the `DesktopKeyDisplayTextFormatter` in the same `Nickelony.KeyBindings` package
  renders the Windows, Linux, and macOS desktop conventions from the `Windows`/`Linux`/`MacOS` presets
  (a bare `DesktopKeyDisplayConventions` carries the neutral `Control`/`Meta`/`Numpad ` labels). A host
  assigns one of those to `KeyBindingServiceOptions.DisplayTextFormatter`; there is no Avalonia-specific
  text to add.

A `Pending` result means the dispatcher buffered a chord stroke. The adapter contains no timer: the
host supplies the cancel triggers with `CancelPendingChord` (Escape, focus loss, an idle timeout of its
own), because the timing policy belongs to the toolkit. A host that participates in a context chain
should route the stroke with `KeyBindingDispatcherChain.DispatchInPriorityOrder` instead of calling
the adapter for one dispatcher, and it passes the same active token to every dispatcher in the chain.

## License

MIT © 2026 Kewin Kupilas.
