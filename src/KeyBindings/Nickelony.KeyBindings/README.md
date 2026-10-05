# Nickelony.KeyBindings

Toolkit-neutral (WPF/Avalonia-independent) command and keyboard shortcut system for desktop
applications with a command surface: command catalog, key bindings, persisted overrides,
validation, rebinding, and key dispatch.

## Getting started

```sh
dotnet add package Nickelony.KeyBindings --prerelease
```

A host registers a command catalog, builds a `KeyBindingService<TCommandId>` from an
overrides store, and dispatches strokes through `KeyBindingDispatcher<TCommandId>`:

```csharp
using Nickelony.KeyBindings;

var catalog = new CommandCatalog<AppCommand>(
[
    new CommandDescriptor<AppCommand>(
        AppCommand.Save,
        "file.save",
        CommandRemappingPolicy.Remappable,
        new KeyStroke(KeyCode.S, KeyModifiers.Control)),
]);

var service = new KeyBindingService<AppCommand>(catalog, new MemoryOverridesStore());

var dispatcher = new KeyBindingDispatcher<AppCommand>(
    service,
    new KeyBindingDispatcherHooks<AppCommand>(
        CanExecuteCommand: command => true,
        ExecuteCommand: command => Console.WriteLine($"Run {command}")));

// A toolkit adapter converts the key events of the host into strokes; this call dispatches one by hand.
KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control), context: null);
// outcome is Executed, and the hook wrote "Run Save".
```

The command id is the host's own type, and the catalog rejects the default value of that type, so an enum
reserves its zero member for "no command". The host persists the overrides; this store keeps them in memory:

```csharp
using Nickelony.KeyBindings;

enum AppCommand { None, Save }

sealed class MemoryOverridesStore : IKeyBindingOverrideStore
{
    private KeyBindingOverrides _overrides = new();

    public KeyBindingOverrides Load() => _overrides;

    public bool Save(KeyBindingOverrides snapshot)
    {
        _overrides = snapshot;
        return true;
    }
}
```

## Remarks

- The core references no UI toolkit. Its `Display/` slice is opt-in desktop convention data with neutral
  defaults, so a host selects the convention it targets: the presets of `KeyBindingPlatformProfile` carry no
  operating-system check, and `KeyBindingPlatformProfile.ForCurrentPlatform` picks one.
- A WPF host adds `Nickelony.KeyBindings.Wpf` for `KeyEventArgs` conversion, key-down dispatch, and the WPF
  gesture display formatter. An Avalonia host adds `Nickelony.KeyBindings.Avalonia` for key-down conversion and
  dispatch on Avalonia's input model.
- A binding is a `KeyChord`: an ordered sequence of one or more `KeyStroke` values, so `Control+K, S` is two
  strokes and a single stroke is a one-stroke chord.
- `KeyCode` owns its own dense numbering and canonical serialized names, so a toolkit adapter translates
  through an explicit table instead of a cast.
- The [key bindings guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/KeyBindingsGuide.md)
  owns the key model, the typed-input grammar, the override and context rules, the persistence model,
  display text, and the dispatch and hosting contracts.

## License

MIT © 2026 Kewin Kupilas.
