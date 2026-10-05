# Key bindings guide

`Nickelony.KeyBindings` is the toolkit-neutral command and keyboard shortcut system for desktop
applications with a command surface. It owns the command catalog, key bindings, persisted overrides,
validation, rebinding, and key dispatch, and it references no UI toolkit. Its only operating-system check is
`KeyBindingPlatformProfile.ForCurrentPlatform`, which a host may skip.

## The key model

- A binding is a `KeyChord`: an ordered sequence of one or more `KeyStroke` values, each carrying its own
  modifiers, so `Control+K, S` is two strokes (three physical key presses).
- A single stroke is a one-stroke chord, so a `KeyStroke` converts implicitly to a `KeyChord` and the
  single-key form is the common case.
- Construction requires at least one initialized stroke, so an empty chord is never built; the `default`
  value is the model's reserved "no chord" chord, and `IsInitialized` reports whether a chord carries
  strokes.
- `KeyCode` owns its own dense numbering, starting at `1`; the `0` default is not a defined member. The
  numbering is part of the package contract, so a toolkit adapter translates through an explicit table rather
  than an integer cast.
- An adapter maps the keys its toolkit exposes and drops keys outside the model, such as modifier keys, lock
  keys, processing pseudo-keys, and double-byte IME keys.
- Member names are the canonical serialized names: the physical key by its unshifted US character or
  position (`Slash`, `LeftBracket`, `IntlBackslash`), not the active layout's label.
- `KeyModifiers` is a flags enum. Its modifiers are `Control`, `Shift`, `Alt`, and `Meta`, each a single
  bit in a package-owned mask. They are the model's whole modifier vocabulary, and the display presets map
  them to desktop text.
- Command identity is generic (`TCommandId`), so an enum, a string id, or any other command model drives the
  catalog; persistence always uses the stable string `SerializedId`.

## Typed input

`KeyChord.TryParse` and `KeyStroke.TryParse` read display text a user typed, so a shortcut editor can turn
input into a chord and render it back with the configured formatter.

- A stroke is a `KeyCode` member name with optional modifiers joined by `+`; strokes are separated by a
  space, a tab, or a comma, and a run of separators (a double space, a trailing comma) is tolerated.
- Matching is case-insensitive and whitespace around `+` is ignored; a repeated modifier is tolerated, and a
  modifier must precede the key.
- The modifiers accept the member names, the `ctrl` alias for `Control`, and the aliases `cmd`, `command`,
  `super`, `win`, and `windows` for `Meta`. The main-row digits are accepted as `0`-`9` and `D0`-`D9`. Aliases
  are accepted for typed input only and are never emitted.
- Text that names no key, names two keys in one stroke, or orders a modifier after the key, and a chord that
  repeats a stroke, are invalid.

The grammar is a superset of the default formatter's output, so `Control+K, S` round-trips through
`GetDisplayText` and back. It is an input grammar only: the persisted override format keeps its own strict
parser, so tolerating typed input does not loosen what a stored document may contain.

## Commands and platform defaults

A command is described by a `CommandDescriptor<TCommandId>`: its identity, its `SerializedId`, a
`CommandRemappingPolicy`, its default bindings, an optional context token, and an optional `Title` and `Category`
that a command palette, a menu or a tooltip shows. A command without a title shows its `SerializedId`, and the
catalog rejects an empty title or category. The catalog also rejects a descriptor whose command is the default value
of `TCommandId`, so an enum of commands keeps its zero value, such as `None`, for no command.

A host authors a primary default binding once and resolves it against the platform's primary shortcut modifier -
Control on Windows and Linux, Command (`Meta`) on macOS - through a `KeyBindingPlatformProfile`. The host picks the
profile once and hands it to both the catalog and the display formatter:

```csharp
KeyBindingPlatformProfile platform = KeyBindingPlatformProfile.ForCurrentPlatform;

var catalog = new CommandCatalog<MyCommandId>(
[
	new CommandDescriptor<MyCommandId>(
		MyCommandId.Save,
		"Save",
		CommandRemappingPolicy.Remappable,
		platform.PrimaryStroke(KeyCode.S)),
	new CommandDescriptor<MyCommandId>(
		MyCommandId.SaveAll,
		"SaveAll",
		CommandRemappingPolicy.Remappable,
		platform.PrimaryStroke(KeyCode.S, KeyModifiers.Shift))
]);

var service = new KeyBindingService<MyCommandId>(
	catalog,
	overridesStore,
	new KeyBindingServiceOptions { DisplayTextFormatter = new DesktopKeyDisplayTextFormatter(platform) });
```

The `Windows`, `Linux` and `MacOS` presets carry the primary modifier and the display text of their platform, and
`Default` carries Control with the canonical neutral labels. `PrimaryStroke` builds a `<primary>+<key>` stroke with
any additional modifiers combined in. The presets carry no operating-system check, so the platform choice stays
with the host: `ForCurrentPlatform` picks the preset of the running system, and a host that targets another
platform names its preset.

A binding that is not primary-relative is declared with an ordinary `KeyStroke` or `KeyChord`, so a genuine
Control shortcut on macOS is expressed unambiguously.

The command types (`CommandCatalog<TCommandId>`, `CommandDescriptor<TCommandId>` and `CommandRemappingPolicy`)
stay in this package for good, because a later package could forward them only under the
`Nickelony.KeyBindings` namespace. A command palette therefore reads its commands from the `CommandCatalog` of
this package, and a host that adds `Nickelony.QuickPick` for the palette takes no package of IDEKit:

```csharp
using Nickelony.KeyBindings;
using Nickelony.QuickPick;

QuickPickItem[] items =
[
    .. catalog.Descriptors.Select(descriptor => new QuickPickItem(descriptor.SerializedId, descriptor.Title ?? descriptor.SerializedId)
    {
        Description = descriptor.Category,
        TrailingText = service.GetDisplayText(descriptor.Command),
        Tag = descriptor.Command,
    }),
];
```

The [quick pick guide](QuickPickGuide.md#the-command-palette-recipe) shows the provider around these items.

## Overrides, precedence, and contexts

### Overrides and precedence

An override entry replaces its command's catalog defaults; an empty binding list explicitly unbinds the
command.

An override binding takes precedence over another command's catalog default for the same chord in the override's
context. The default stays stored but is shadowed until the override is removed, and `Reset` and `ResetAll`
remove overrides, which restores it.
`GetBindings` reports the bindings that currently dispatch to a command, so a shadowed default is omitted
while its shadowing override exists in that context.

An override may shadow only a remappable command's default, and the [context rules](#contexts) limit which
contexts it shadows the default in. A loaded document obeys the same rules, including when a preserved
entry's command becomes cataloged, so a document can never win a chord the catalog refuses to give it.

`Validate` returns the `KeyBindingOutcome` for a proposed binding set without changing state. `Apply` stores
the set as an override, persists it through the overrides store, and publishes the rebuilt runtime maps. Under
the default `KeyBindingConflictPolicy.Reject`, any conflict is refused.

`KeyBindingConflictPolicy.Replace` lets the proposal take precedence over remappable commands. A conflicting
catalog default is shadowed, and a conflicting binding is removed from the other command's stored override
entry, which is deleted when it empties. A conflict with a `HostManaged` or `HostReserved` command is rejected
under either policy.

`HostManaged` lets a host supply a per-installation binding through persisted settings while users may not
rebind it, so a loaded override is honored even though a proposal is refused. `HostReserved` commands are
fully host-owned and additionally ignore every loaded override.

`Reset` removes one command's override and `ResetAll` removes every cataloged command's. Neither takes a
conflict policy, but each validates the state it produces:

- A restored default that collides with another command's binding or with a preserved entry is refused as
  `Conflict` (or `PrefixConflict`), and the state does not change.
- A restored default that another command's override legally shadows is restored and stays shadowed, so the
  reset reports `Succeeded` and the command can end up with no dispatching binding.

A chord that is a strict prefix of another bound chord is a declaration error rather than a competing claim:
the shorter chord always resolves first, so the longer one could never dispatch. That is reported as
`KeyBindingOutcome.PrefixConflict`, and `Replace` deliberately does not resolve it - silently removing the
other command's binding to "fix" it would be wrong.

An unbound stroke shared as the prefix of several chords is legal: that is the point of chords.

### Contexts

A command's bindings can be scoped to a context token: an opaque, host-authored string that names a mutually
exclusive state of the host, such as one of an editor's view modes. The token is declared on the catalog
descriptor and never persisted, so an override keeps working regardless of the active token.

- `null` is the only always-active token, which is the default and where bindings shared by every mode belong,
  for example `Ctrl+S`. An empty or whitespace-only token cannot be declared (the catalog rejects it), so
  a lookup with one resolves only the always-active bindings.
- The host guarantees that at most one token is active at a time, and publishes it when it dispatches a
  stroke. A host with no contexts passes `null` everywhere.
- `Contexts` lists the declared tokens, so the host can fail fast on a typo instead of silently falling back
  to the always-active bindings. The comparison is ordinal.
- Two bindings conflict when their contexts can be active at the same time: the same token, or either
  `null`. The same chord may therefore be bound in geometry mode and in texture mode, while a catalog that
  declares one chord for a context-scoped and an always-active command is rejected.

A lookup probes the active token first and the always-active context second. An override, under `Replace`
or in a loaded document, follows that order: a context-scoped override shadows a remappable always-active
default only in its own context, so the default still dispatches, and `GetBindings` still reports it, in
every other context. An always-active override never displaces a context-scoped binding, because that
binding's command would become unreachable in its own context.

The strokes buffered at the start of a chord belong to the context it was started in, so a token change
between strokes abandons the buffer and the next stroke starts a fresh chord.

The full model, its conflict table, and the rejected alternatives are in the
[context-token design](KeyBindingContextDesign.md).

## Persistence

The library never reads or writes files. A host implements `IKeyBindingOverrideStore` and passes it to the
service: `Load` supplies the initial overrides once, in the constructor, and `Save` receives every committed
mutation as a snapshot to persist.

The service keeps its own copy of the runtime state, so the instances the store returns and receives are
never mutated by the library, and the file format stays a host decision.

```csharp
sealed class SettingsOverridesStore : IKeyBindingOverrideStore
{
	// _settings is the host's own settings object.
	public KeyBindingOverrides Load()
		=> _settings.KeyBindings ??= new KeyBindingOverrides();

	public bool Save(KeyBindingOverrides snapshot)
	{
		_settings.KeyBindings = snapshot;
		return _settings.TrySave();
	}
}
```

The model is serialization-ready for both formats with one vocabulary. `KeyBindingOverrides.CurrentVersion`
is the only schema version this package reads or writes, and a document with any other version is rejected
rather than tolerated: the `KeyBindingService` constructor throws `NotSupportedException`. The samples below
show that version as an example value; read it from `CurrentVersion` instead of copying it.

XML uses the `Version` attribute, `<Command>` elements, `<Binding>` elements that hold one or more
`<Stroke>` elements, and the `Id`/`Key`/`Modifiers` attributes:

```xml
<KeyBindingOverrides Version="3">
  <Command Id="Save">
    <Binding>
      <Stroke Key="K" Modifiers="2" />
      <Stroke Key="S" Modifiers="2" />
    </Binding>
  </Command>
</KeyBindingOverrides>
```

JSON uses the members `version`, `commands`, `id`, `bindings`, `strokes`, `key`, and `modifiers`:

```json
{
  "version": 3,
  "commands": [
    {
      "id": "Save",
      "bindings": [
        { "strokes": [ { "key": "K", "modifiers": 2 }, { "key": "S", "modifiers": 2 } ] }
      ]
    }
  ]
}
```

Either format, or both, can be used. A binding nests its strokes because a binding is an ordered chord, and
`Modifiers` stays a number because the schema stores the flag combination as one in both formats, so the
`KeyModifiers` numbering is a host-facing contract.

Loading accepts only exact canonical `KeyCode` member names: numeric strings and foreign alias names such as
`Oem1`, `Next`, or `Snapshot` are ignored. A stroke that cannot be parsed is skipped with a diagnostic, and a
binding with a skipped stroke is skipped whole, because a chord with a missing stroke is not the declared
chord. An entry whose bindings are all skipped falls back to its command's catalog defaults,
unlike an entry with an empty binding list, which unbinds the command.

Entries whose serialized identifier is not cataloged are ignored with a log entry and are preserved in the
snapshots the service writes. A mutation is refused when it would introduce a claim that collides with a
preserved entry, because a later run that catalogs the command would then reject the document; the writer
therefore never produces a document the loader refuses.

`Entries` and its nested collections are mutable `List<T>` properties with public setters so the model can
round-trip through `System.Xml.Serialization` and `System.Text.Json`; this is the deliberate exception to
the package rule that a public payload collection is typed `IReadOnlyList<T>`. The entry types live in the
`Nickelony.KeyBindings.OverrideEntries` namespace, which code that reads or edits an entry imports.

The service persists before it publishes, so a store that writes the snapshot and then throws leaves the
persisted document ahead of the runtime state; that divergence is not repaired and is reconciled the next
time the service is constructed. A store that must not diverge makes `Save` atomic and reports failure by
returning `false`.

## Display text

`KeyBindingService<TCommandId>` renders display text through an `IKeyDisplayTextFormatter`. The package
default is `KeyDisplayTextFormatter`, which renders the canonical member names (`Control+Shift+S`,
`Meta+Enter`, `Slash`) and carries no desktop conventions, so every host gets predictable text until it opts
in.

It renders the modifier order `Control`, `Shift`, `Alt`, `Meta` with `+` between modifiers, joins a chord's
stroke texts with `, `, and joins a command's several bindings with ` / ` in stored order.

`DesktopKeyDisplayTextFormatter` renders the text of a `KeyBindingPlatformProfile` and carries no toolkit
dependency. A host assigns `new DesktopKeyDisplayTextFormatter(platform)` to
`KeyBindingServiceOptions.DisplayTextFormatter`, with the profile it already picked for its catalog. The presets
carry no operating-system check, because the host chooses the platform.

`KeyBindingPlatformProfile.Default` renders the neutral labels (`Control`, `Meta`, `NumPad `), and a host can adjust
one label from a preset (`KeyBindingPlatformProfile.Windows with { MetaLabel = "Cmd" }`). A profile compares by
value over every member, so it can be a cache key. The numeric keypad is where `Default` and
`KeyDisplayTextFormatter` differ: the formatter renders the member name (`NumPad0`, `Add`), and a profile renders its
`NumPadPrefix` and the symbol (`NumPad 0`, `NumPad +`).

The same modifiers are named differently by each platform:

| Modifier | `KeyModifiers` | Windows / Linux preset | macOS preset |
|---|---|---|---|
| Control | `Control` | `Ctrl` | `⌃` |
| Shift | `Shift` | `Shift` | `⇧` |
| Alt (Option on macOS) | `Alt` | `Alt` | `⌥` |
| The operating-system key | `Meta` | `Win` (Windows), `Super` (Linux) | `⌘` (Command) |

The numeric keypad's digit and operator keys render as the profile's `NumPadPrefix` followed by the
symbol, so one value relabels them all, for example
`KeyBindingPlatformProfile.Windows with { NumPadPrefix = "Keypad " }`. A `KeyTextOverrides` entry replaces
one key's whole text. macOS uses glyph labels with no modifier separator and a single space between a chord's
strokes.

A formatter that only relabels the keys and modifiers, or changes the order or the separators, derives from
`KeyDisplayTextFormatter` and overrides its text hooks. A formatter that renders through a toolkit converter
overrides `GetStrokeText` and keeps the base's stroke sequencing and uninitialized-stroke guard.

A host that wants localized or toolkit-specific text supplies its own formatter through
`KeyBindingServiceOptions.DisplayTextFormatter`; the WPF package ships `KeyGestureDisplayTextFormatter`.

## Shortcut editor

A shortcut editor parses the typed text, validates the proposal, applies it, and refreshes the shortcut text
it shows:

```csharp
private void OnShortcutTyped(IKeyBindingService<MyCommandId> service, MyCommandId command, string typedText)
{
	if (!KeyChord.TryParse(typedText, out KeyChord chord))
	{
		ShowError("The text is not a valid shortcut.");
		return;
	}

	KeyBindingOutcome outcome = service.Validate(command, [chord]);

	// Reject, the default policy, refuses a conflict; Replace takes the chord over, so the host asks first.
	if (outcome == KeyBindingOutcome.Conflict && ConfirmTakeOver())
		outcome = service.Apply(command, [chord], KeyBindingConflictPolicy.Replace);
	else if (outcome == KeyBindingOutcome.Succeeded)
		outcome = service.Apply(command, [chord]);

	if (outcome != KeyBindingOutcome.Succeeded)
		ShowError($"The shortcut was not applied: {outcome}.");
}

// Subscribed with service.BindingsChanged += HandleBindingsChanged.
private void HandleBindingsChanged(object? sender, BindingsChangedEventArgs<MyCommandId> e)
{
	foreach (MyCommandId command in _shownCommands)
		_labels[command] = _service.GetDisplayText(command, fallbackDisplayText: "Unbound");
}
```

- `Validate` changes nothing, and a `Succeeded` outcome means the proposal passes the checks `Apply` makes
  before it persists; `Apply` can still return `PersistenceFailed`. A rejection under `Reject` does not say
  whether `Replace` would accept it.
- `Apply`, `Reset`, and `ResetAll` raise `BindingsChanged` after they publish the new bindings, so
  `GetDisplayText` returns the new text inside a handler. A `Reset` of a command with no override and a
  `ResetAll` with no cataloged override to clear change nothing and raise nothing.
- `BindingsChangedEventArgs.Command` names the command the call targeted.
- `AffectsAllCommands` is `true` after a `ResetAll`, and after an `Apply` or a `Reset` that leaves another command
  with different bindings: an `Apply` under `Replace` that took its chord, or a `Reset` or narrowing `Apply` that
  gives a command back a default the override had shadowed.
- `Command` holds the identity's default value when `AffectsAllCommands` is `true`, so read `Command` only when
  `AffectsAllCommands` is `false`.
- A host that shows several commands' shortcut text re-reads each of them on every event, as the handler above
  does.

## End-to-end wiring

The pieces connect in this order, and each one takes the one before it:

1. A `CommandCatalog<TCommandId>` holds one `CommandDescriptor<TCommandId>` per command, with its default
   bindings.
2. An `IKeyBindingOverrideStore` supplies the saved overrides. The service calls `Load` once, in its
   constructor, and calls `Save` for each committed change.
3. `KeyBindingService<TCommandId>` takes the catalog, the store, and optional `KeyBindingServiceOptions`.
   It answers lookups and runs `Apply`, `Reset`, and `ResetAll`.
4. `KeyBindingDispatcher<TCommandId>` takes the service (any `IKeyBindingResolver<TCommandId>`) and a
   `KeyBindingDispatcherHooks<TCommandId>` that holds the `CanExecuteCommand` and `ExecuteCommand` callbacks.
5. The key-down handler calls the `HandleKeyDown` extension of the WPF or Avalonia adapter with the active
   context token.

```csharp
sealed class EditorHost
{
	// CanRun, Run, and the _editor field belong to the host.
	private readonly KeyBindingDispatcher<MyCommandId> _dispatcher;

	public EditorHost(
		IReadOnlyList<CommandDescriptor<MyCommandId>> descriptors,
		IKeyBindingOverrideStore overridesStore)
	{
		var catalog = new CommandCatalog<MyCommandId>(descriptors);
		var service = new KeyBindingService<MyCommandId>(catalog, overridesStore);

		_dispatcher = new KeyBindingDispatcher<MyCommandId>(
			service,
			new KeyBindingDispatcherHooks<MyCommandId>(CanRun, Run));
	}

	private void OnEditorKeyDown(object sender, KeyEventArgs e)
	{
		if (_dispatcher.HandleKeyDown(e, _editor.ActiveMode.Token) != KeyStrokeDispatchOutcome.NotHandled)
			e.Handled = true;
	}
}
```

`HandleKeyDown` extends `IKeyBindingDispatcher`, the `Dispatch` member that `KeyBindingDispatcher<TCommandId>` and
`KeyBindingDispatcherChain<TCommandId>` share, so the same call feeds a single dispatcher or a chain. [`SampleKeyBindings`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Keys/SampleKeyBindings.cs)
is a running version of this wiring.

The WPF `HandleKeyDown` takes an optional `KeyDownHandlingOptions`, which ignores auto-repeat and AltGr
strokes by default. The Avalonia overload takes an optional `KeyRepeatGate` instead, because Avalonia reports no
auto-repeat flag: the gate admits a press once, forgets the key on its release, and forgets every key when focus
leaves its scope or its window is deactivated. A call without a gate dispatches every repeat, and the Avalonia
adapter has no AltGr filter; the [Avalonia adapter readme](https://github.com/Nickelony/LanguageServer/blob/main/src/KeyBindings/Nickelony.KeyBindings.Avalonia/README.md)
gives the reason.

`Apply`, `Reset`, and `ResetAll` call `Save` before they publish the rebuilt maps, so the next key-down uses
the new bindings. When `Save` returns `false`, the call returns `KeyBindingOutcome.PersistenceFailed` and
publishes nothing.

## Dispatch and hosting

- `KeyBindingDispatcher<TCommandId>` owns the pending-chord state but not the clock; its own remarks are the
  authoritative outcome contract. The package never times a chord out, so a host that never calls
  `CancelPendingChord` leaves the chord armed until the next stroke.
- A host wires `CancelPendingChord` to Escape, to focus loss, and to an idle timer in the range of one to
  two seconds. The host owns the timing policy because it belongs to the toolkit that raises the key events.
  `HasPendingChord` and `PendingChord` expose the buffer for a status-bar hint.
- One chord resolves to at most one command per active context.
- A host that needs context-specific routing combines the mechanisms. Mutually exclusive modes inside one
  control use one dispatcher and the active context token. Several panes that can each hold focus compose one
  service and dispatcher per pane and evaluate them as a `KeyBindingDispatcherChain<TCommandId>`, which copies
  and validates its dispatchers when it is built.
- The chain stops at the first dispatcher that executes the stroke or starts a chord.
- A stroke that continues a pending chord goes to the dispatcher that started it, which is the first dispatcher
  whose pending context matches the active context. A lower-priority context can never steal a
  chord continuation.
- The chain cancels a stale buffer itself rather than relying on the dispatcher's own context check, so the
  stroke is not executed inside the pending dispatcher before the higher-priority contexts get their turn.
- A condition that can vary independently of a mode - a popup being open, a read-only document - belongs in
  the `CanExecuteCommand` predicate of `KeyBindingDispatcherHooks<TCommandId>`, not in the context token,
  because the token is a single value.
- A command whose predicate returns `false` does not consume the stroke, so the next dispatcher in a chain
  still gets its turn.
- The first stroke of a chord follows the same rule. The dispatcher buffers it only when at least one command
  under that prefix, in the active or the always-active context, passes the predicate, so a chord that cannot run never blocks a
  lower dispatcher's own binding of the stroke.
- The predicate can run for several commands on one stroke, because the dispatcher asks each command under a
  prefix until one passes. Keep it cheap and free of side effects.
- Call `Apply`, `Reset`, and `ResetAll` from one owning thread, typically the host's input thread. The
  remarks of [`IKeyBindingService<TCommandId>`](https://github.com/Nickelony/LanguageServer/blob/main/src/KeyBindings/Nickelony.KeyBindings/Bindings/IKeyBindingService.cs)
  state what a mutation publishes, when it raises `BindingsChanged`, and which malformed calls throw instead
  of returning a `KeyBindingOutcome`.
- The constructor throws `InvalidOperationException` when a loaded document cannot be applied:
  - the document has two entries for one command;
  - two entries claim the same chord in co-active contexts;
  - the document binds a chord that an unremappable command owns;
  - two chords in a co-active context make one a strict prefix of the other;
  - a claim leaves a context-scoped command unreachable.
- A host that lets users hand-edit the document catches the failure and constructs the service again from an
  empty document, so the application still starts with the catalog defaults instead of failing to open.

## Logging

The `logger` parameter of the `KeyBindingService` constructor accepts an optional
`Microsoft.Extensions.Logging.ILogger`; diagnostics are discarded when none is supplied. The package takes its event ids from its block of the [log event
ids](ArchitectureGuide.md#log-event-ids) registry, and each message declares its id and text on the
`LoggerMessage` attribute that produces it, in `KeyBindingMapBuilder`, `KeyBindingOverrideCodec` and
`KeyBindingService`.
