# Nickelony.KeyBindings

Host-neutral command and keyboard shortcut system for editors and any application with
a command surface: command catalog, key bindings, persisted overrides, validation,
rebinding, and key dispatch.

## Getting started

```sh
dotnet add package Nickelony.KeyBindings
```

A host registers a command catalog, builds a `KeyBindingService<TCommandId>` from an
overrides store, and dispatches strokes through `KeyBindingDispatcher<TCommandId>`:

```csharp
var service = new KeyBindingService<MyCommandId>(catalog, myOverridesStore);

var dispatcher = new KeyBindingDispatcher<MyCommandId>(
	service,
	new KeyBindingDispatcherHooks<MyCommandId>(
		canExecuteCommand: commandId => CanRun(commandId),
		executeCommand: commandId => Run(commandId)));
```

## Dependencies

- `Microsoft.Extensions.Logging.Abstractions` (for the optional logger).

The package targets `net8.0`, references no UI toolkit, and depends on nothing else beyond the
logging abstractions, so it can back any UI toolkit. The core carries no toolkit or operating-system
dependency: even its `Display/` slice is opt-in platform *convention data* with neutral defaults
(`Control`, `Meta`, `Numpad `), and the package never inspects the operating system, so a host selects
the desktop convention it targets. A WPF host adds `Nickelony.KeyBindings.Wpf` for `KeyEventArgs`
conversion, key-down dispatch, and the WPF gesture display formatter; an Avalonia host adds
`Nickelony.KeyBindings.Avalonia` for the same mapping, conversion, and dispatch on Avalonia's
input model.

## What's inside

This assembly ships the following public surface (the five slices the types are organized into are in
"Project structure" below):

- `KeyCode`, `KeyModifierSet`, `KeyCombo`, and `KeyChord` - the key model (`KeyModifierMasks`
  names the defined modifier set),
- `IKeyDisplayTextFormatter` with `KeyDisplayTextFormatter` - the display-text seam and its
  platform-neutral default, and `DesktopKeyDisplayTextFormatter` with its
  `DesktopKeyDisplayConventions` value, which renders the Windows, Linux, or macOS desktop
  conventions without a toolkit dependency,
- `KeyBindingServiceOptions` - the optional display formatter and logger configuration,
- `KeyBindingPlatformProfile` - the platform-convention seam for default bindings (the primary
  modifier a host resolves a primary shortcut against),
- `CommandDescriptor<TCommandId>` with its `CommandRemappingPolicy` and
  `CommandCatalog<TCommandId>` (defaults + remapping policy),
- `IKeyBindingOverridesStore` with `KeyBindingOverrides`, `KeyBindingOverrideEntry`,
  `KeyBindingOverrideBinding`, and `KeyBindingOverrideStroke` (the persistence seam and
  the override model; the same values serialize to XML or JSON),
- `KeyBindingOutcome` (operation results) and `KeyBindingConflictPolicy` (conflict handling),
- `IKeyBindingService<TCommandId>` / `KeyBindingService<TCommandId>` (lookup, display text,
  validation, apply, reset/reset-all), and
- `KeyBindingDispatcher<TCommandId>` with `KeyBindingDispatcherHooks<TCommandId>`,
  `KeyChordDispatchResult`, and `KeyBindingDispatcherChain` (key dispatch, the pending-chord state,
  and the priority-ordered chain across context dispatchers).

A binding is a `KeyChord`: an ordered sequence of one or more `KeyCombo` strokes. `Ctrl+K, Ctrl+S`
is two strokes (stroke one `Control+K`, stroke two `Control+S`) and three physical key presses, and
each stroke carries its own modifiers. A single keystroke is a one-stroke chord, so a `KeyCombo`
converts implicitly to a `KeyChord` and the single-key form is the common case of the same model.

`KeyCode` owns its own dense numbering: the neutral key model defines the values, which are
neither the WPF `System.Windows.Input.Key` numbering nor the Win32 virtual-key numbering, so a
toolkit adapter translates through an explicit table instead of a cast. The set is a desktop-key
superset modeled on the WPF/desktop key set rather than a portable minimum, so a toolkit adapter
maps the subset its own key model exposes. The member names are the canonical
serialized names: they describe the physical key by its unshifted US character or
position (`Slash`, `LeftBracket`, `IntlBackslash`), not the label of the active layout.
Command identity is generic (`TCommandId`), so any command model (an enum, a string id,
etc.) can drive the catalog; persistence always uses the stable string `SerializedId`.

## Project structure

The package uses a **single flat namespace** (`Nickelony.KeyBindings`) for its entire public and
internal surface: folders express slice ownership only, and a file keeps its namespace when it
moves between slices. The slice map:

| Folder | Contents |
|---|---|
| `Keys/` | The key model: `KeyCode`, `KeyModifierSet`, `KeyCombo`, `KeyChord`, and the modifier mask helper |
| `Commands/` | The command model: `CommandCatalog<TCommandId>`, `CommandDescriptor<TCommandId>`, `CommandRemappingPolicy`, and `KeyBindingPlatformProfile` |
| `Overrides/` | The persistence seam and the override model: `IKeyBindingOverridesStore`, `KeyBindingOverrides`, and its binding and stroke values |
| `Bindings/` | The service contract and implementation with its options and outcome, `KeyBindingConflictPolicy`, the binding-map build with its claim rules and published maps, and the dispatcher family (`KeyBindingDispatcher<TCommandId>`, its chain, hooks, and `KeyChordDispatchResult`) |
| `Display/` | `IKeyDisplayTextFormatter` with the platform-neutral `KeyDisplayTextFormatter` and the configurable `DesktopKeyDisplayTextFormatter` / `DesktopKeyDisplayConventions` |

The test project mirrors the same five slices under `tests/KeyBindings/Nickelony.KeyBindings.Tests`.
A new type belongs to the slice that owns its feature, and an internal shared by several slices
stays with the rules it implements - the claim rules and the published-map cache sit in
`Bindings/` because the catalog, the map build, and the service share them.

## Typed text

`KeyChord.TryParse` and `KeyCombo.TryParse` read shortcut text a user typed, so a shortcut-editor UI can
turn input into a chord and render it back with the configured formatter.

- A stroke is a `KeyCode` member name with optional modifiers joined by `+`; strokes are separated by
  a space, a tab, or a comma.
- Matching is case-insensitive, whitespace around `+` is ignored, a repeated modifier is tolerated, and a
  run of separators (a double space, a trailing comma) is tolerated as well.
- The modifiers accept the member names, the `ctrl` alias for `Control`, and the operating-system
  aliases `cmd`, `command`, `super`, `win` and `windows` for `Meta`; the main-row digits are accepted as
  `0`-`9` as well as `D0`-`D9`.
- Text that names no key, names two keys in one stroke, or orders a modifier after the key is invalid, and
  so is a chord that repeats a stroke.

The grammar is a superset of the default formatter's output, so `Control+K, S` round-trips through
`GetDisplayText` and back. It is an *input* grammar: the persisted override format keeps its own strict
parser, which accepts only the exact canonical member names and never a numeric form, so tolerating typed
input does not loosen what a stored document may contain.

## Platform default bindings

A host authors a *primary* default binding once and resolves it against the platform's primary
shortcut modifier - Control on Windows and Linux, Command (`Meta`) on macOS - through a
`KeyBindingPlatformProfile`:

```csharp
var profile = OperatingSystem.IsMacOS()
	? new KeyBindingPlatformProfile { PrimaryModifier = KeyModifierSet.Meta }
	: KeyBindingPlatformProfile.Default;

var catalog = new CommandCatalog<MyCommandId>(
[
	new CommandDescriptor<MyCommandId>(
		MyCommandId.Save,
		"Save",
		CommandRemappingPolicy.Remappable,
		profile.PrimaryStroke(KeyCode.S)),
	new CommandDescriptor<MyCommandId>(
		MyCommandId.SaveAll,
		"SaveAll",
		CommandRemappingPolicy.Remappable,
		profile.PrimaryStroke(KeyCode.S, KeyModifierSet.Shift))
]);
```

`KeyBindingPlatformProfile.Default` carries the default convention (Control, which is the Windows and
Linux convention) and `PrimaryStroke` builds a `<primary>+<key>` stroke (with any additional modifiers
combined in). A host
that targets another convention configures the modifier itself - for macOS,
`new KeyBindingPlatformProfile { PrimaryModifier = KeyModifierSet.Meta }` - so the convention stays with
the host and the neutral core never inspects the operating system. A binding that is not
primary-relative is declared with an ordinary `KeyCombo` or `KeyChord`, so a genuine Control shortcut
on macOS is expressed unambiguously.

## Overrides and precedence

An override entry replaces its command's catalog defaults; an empty binding list
explicitly unbinds the command. An override binding also takes precedence over another
command's catalog default for the same chord: the default stays stored but is shadowed
until the override is removed, so `Reset` and `ResetAll` restore the shadowed defaults; a
restored default that collides with a binding another command holds is refused as
`Conflict` (or `PrefixConflict` for a strict prefix) with no state change. `GetBindings`
reports the bindings that currently dispatch to a command, so a shadowed default is omitted
while its shadowing override exists.

`Validate` returns the `KeyBindingOutcome` for a proposed binding set without changing
state; a `Conflict` result can still be applied with `KeyBindingConflictPolicy.Replace`
when every conflicting command is remappable. `Apply` stores the set as an override,
persists it through the overrides store, and publishes the rebuilt runtime maps.
`Replace` lets the proposal take precedence over remappable commands: a conflicting
catalog default is shadowed, and a conflicting binding is removed from the other
command's stored override entry (the entry is deleted when it empties). A conflict with
a `HostManaged` or `HostReserved` command is rejected. `HostManaged` exists so a host
can supply a per-installation binding through persisted settings while users may not
rebind it, so a loaded override is honored even though a proposal is refused;
`HostReserved` commands are fully host-owned and additionally ignore every loaded
override. `Reset`/`ResetAll` remove overrides for every cataloged command regardless of
policy. The candidate state is validated before
it is persisted, so a failed operation never changes runtime state and persisted
overrides always load again.

A chord that is a strict prefix of another bound chord is a declaration error rather
than a competing claim: the shorter chord always resolves first, so the longer one could
never dispatch. That is reported as `KeyBindingOutcome.PrefixConflict`, and `Replace`
deliberately does not resolve it - silently removing the other command's binding to
"fix" it would be wrong. An *unbound* stroke shared as the prefix of several chords is
legal: that is the whole point of chords.

`Reset` and `ResetAll` restore catalog defaults and validate the state they produce the same way, so a
default they would restore that collides with a binding another command holds is reported as
`Conflict` (or `PrefixConflict`, for a strict prefix relationship) instead of being written. Removing an
owner's entry can also restore that owner's defaults, and the outcome describes the state the operation
produces rather than only the proposal it started from.

## Persistence

The library never reads or writes files. A host implements `IKeyBindingOverridesStore`
and passes it to the service: `Load` supplies the initial overrides once, in the
constructor, and `Save` receives every committed mutation as a snapshot to persist.
The service keeps its own copy of the runtime state, so the instances the store returns
and receives are never mutated by the library and the file format stays a host decision:

```csharp
sealed class SettingsOverridesStore : IKeyBindingOverridesStore
{
	public KeyBindingOverrides Load()
		=> _settings.KeyBindings ??= new KeyBindingOverrides();

	public bool Save(KeyBindingOverrides snapshot)
	{
		_settings.KeyBindings = snapshot;
		return _settings.TrySave();
	}
}
```

The model is serialization-ready for both formats with one vocabulary. XML uses the
`Version` attribute, `<Command>` elements, `<Binding>` elements that hold one or more
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

JSON uses the members `version`, `commands`, `id`, `bindings`, `strokes`, `key`, and
`modifiers`:

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

Either format, or both, can be used; round-trips and both examples are covered by the
test suite. Version `3` is the only schema version this package reads or writes: a
binding nests its `Stroke` elements (`strokes` in JSON) because a binding is now an
ordered chord rather than a single stroke. A document with any other version is
rejected instead of being tolerated. Entries whose serialized identifier is not
cataloged are ignored (event id `4005`) and are preserved in the snapshots the service
writes. A mutation is refused when it would introduce a claim that collides with a
preserved entry, because a later run that catalogs the command would then reject the
document; the writer therefore never produces a document the loader refuses.

## Display text

`KeyBindingService<TCommandId>` renders shortcut text through an
`IKeyDisplayTextFormatter`. The package default is `KeyDisplayTextFormatter`, which
renders the canonical member names (`Control+Shift+S`, `Meta+Enter`, `Slash`) and
carries no platform conventions, so every host gets predictable text until it opts in;
a bare `DesktopKeyDisplayConventions` renders the same neutral labels (`Control`, `Meta`, `Numpad `).
A chord's stroke texts are joined with `, `, so `Control+K, Control+S` is two strokes, and a command
with several bindings renders them joined with ` / ` in stored order.
`DesktopKeyDisplayTextFormatter` renders a desktop convention from a `DesktopKeyDisplayConventions`
value: `Windows` (`Ctrl+`, `Win+`, `Num `), `Linux` (`Ctrl+`, `Super+`), and `MacOS` (the `⌃⌥⇧⌘` glyphs
with no modifier separator and a space between a chord's strokes) ship as presets, each with the
US-layout punctuation glyphs, the shared `Num ` numpad prefix, and `Esc` (macOS additionally uses its
word and arrow glyph labels), and no desktop-toolkit dependency. The `Ctrl+`, `Win+`, `Super+`, and
`Num ` labels are preset *data*, not the package default, so a host opts into a desktop convention by
choosing a preset. A host can start from a preset and adjust one label
(`DesktopKeyDisplayConventions.Windows with { MetaLabel = "Cmd" }`). A formatter that only relabels
the keys and modifiers, or changes the order or the separators, derives from
`KeyDisplayTextFormatter` and overrides its text hooks. A host that wants localized or
toolkit-specific text supplies its own formatter through `KeyBindingServiceOptions.DisplayTextFormatter`;
the WPF adapter package ships `KeyGestureDisplayTextFormatter` (culture-aware WPF gesture text,
joining chord strokes with a comma).

The same four modifiers are named differently by each convention; the table maps the model's name to
the text the desktop presets render, so the vocabulary is defined once:

| Modifier | `KeyModifierSet` | Windows / Linux preset | macOS preset |
|---|---|---|---|
| Control | `Control` | `Ctrl` | `⌃` |
| Shift | `Shift` | `Shift` | `⇧` |
| Alt (Option on macOS) | `Alt` | `Alt` | `⌥` |
| The operating-system key | `Meta` | `Win` (Windows), `Super` (Linux) | `⌘` (Command) |

`KeyModifierSet` is the only name the model uses: the four canonical flags are `Control`, `Shift`,
`Alt`, and `Meta`. The display and typed-input aliases are `Ctrl`, `Win`/`Windows`, `Super`, and
`Cmd`/`Command` (the macOS preset renders the glyphs `⌃`, `⇧`, `⌥`, and `⌘` instead of a word). Typed
text accepts the `Alt`/`Shift`/`Control` member names, the `Ctrl` alias, plus the
`Cmd`/`Command`/`Meta`/`Super`/`Win`/`Windows` aliases for `Meta`.

## Contexts

A command's bindings can be scoped to a **context token**: an opaque, host-authored string that names a
mutually exclusive state of the host, such as one of an editor's view modes. The token is declared on the
catalog descriptor and never persisted, so an override keeps working across the change and no settings
document needs a migration.

- `null` means **always active**, which is the default and where the shared `Ctrl+S`, `F5`, and camera
  bindings belong. A non-empty value is a token the host defines, for example `"geometry"`. Only `null` is
  the always-active context token: an empty or whitespace-only token is not declared by the catalog, so a
  lookup with one resolves only the always-active bindings, like any other undeclared token.
- **The host guarantees that at most one token is active at a time.** It publishes the active token when it
  dispatches a stroke; a host with no contexts passes `null` everywhere. `Contexts` lists the declared
  tokens so the host can fail fast on a typo instead of silently falling back to the always-active
  bindings - the comparison is ordinal.
- **Two bindings conflict when their contexts can be active at the same time**: the same token, or either
  `null`. That single rule covers every declaration and remapping rule, so the same chord may be bound in
  geometry mode and in texture mode - the point of the feature - while a chord claimed by a token-scoped and
  an always-active command is rejected.

The four-mode editor therefore needs one catalog, one service, and one dispatcher:

```csharp
new CommandDescriptor<EditCommand>(EditCommand.Extrude, "edit.extrude", CommandRemappingPolicy.Remappable,
	new KeyCombo(KeyCode.E, KeyModifierSet.None)) { Context = "geometry" }
new CommandDescriptor<EditCommand>(EditCommand.Paint, "edit.paint", CommandRemappingPolicy.Remappable,
	new KeyCombo(KeyCode.E, KeyModifierSet.None)) { Context = "texture" }
new CommandDescriptor<EditCommand>(EditCommand.Save, "file.save", CommandRemappingPolicy.Remappable,
	new KeyCombo(KeyCode.S, KeyModifierSet.Control))   // always active

// Dispatch: the host already knows its mode.
if (_dispatcher.Dispatch(combo, _editor.ActiveMode.Token) != KeyChordDispatchResult.NotHandled)
	e.Handled = true;
```

`E` now means extrude in geometry mode and paint in texture mode, with no duplicated catalogs and no
host-side re-ordering. The token is a plain string, so no expression language, no token hierarchy, and no
per-binding condition in the override file is involved.

Two deliberate asymmetries keep the model small:

- A context-scoped binding shadows an always-active one for the same chord, but an always-active proposal
  never displaces a context-scoped binding: it would leave the owner unreachable in that context. See
  `KeyBindingOutcome.Conflict`.
- The strokes buffered at the start of a chord belong to the context it was started in. When the token changes
  between two strokes, the buffer is abandoned and the new stroke starts a fresh chord instead of completing
  a chord the user never began in that mode.

One token per binding is the whole model: a binding is scoped to one `(context, chord)` pair, so there is
no `when`-expression subset and no per-binding condition. A condition that varies independently of a mode -
a popup being open, a read-only document - belongs in the dispatcher's `CanExecuteCommand` predicate
instead, which is where a reader coming from VS Code's `when` clauses should look.

The repository records the full model, the conflict table, the non-goals, and the rejected alternatives in
[`docs/KeyBindingContextDesign.md`](https://github.com/Nickelony/LanguageServer/blob/main/docs/KeyBindingContextDesign.md)
(see its §7 "Non-goals" and §9 "Rejected alternatives").

## Hosting notes

- One chord resolves to at most one command per active context. A host that needs context-specific routing
  combines the two mechanisms "Contexts" describes: mutually exclusive modes inside one control use one
  dispatcher and the active context token, while several panes that can each hold focus compose one service
  and dispatcher per context and evaluate them as a chain with
  `KeyBindingDispatcherChain.DispatchInPriorityOrder`. The chain stops at the first dispatcher that executes
  the stroke or starts a chord, and it routes a stroke that continues a pending chord to the dispatcher that
  started it, so a lower-priority context can never steal a chord continuation.
- `KeyBindingDispatcher<TCommandId>` owns the pending-chord state but not the clock; its own remarks are the
  authoritative result contract. The package never times a chord out, so a host that never calls
  `CancelPendingChord` leaves the chord armed until the next stroke: wire the call to Escape, to focus
  loss, and to an idle timer in the range of one to two seconds. `HasPendingChord` and
  `PendingChord` expose the buffer for a status-bar hint.
- A condition that can vary independently of a mode - a popup being open, a read-only document -
  belongs in the `CanExecuteCommand` predicate of the dispatcher's `KeyBindingDispatcherHooks<TCommandId>`
  rather than in the context token, because the token is a single value. A command whose predicate
  returns `false` does not consume the stroke, so the next dispatcher in a chain still gets its turn.
- Mutations (`Apply`, `Reset`, `ResetAll`) are expected from a single owning thread; the
  published maps are swapped as one immutable snapshot, so readers never observe data
  from two different rebuilds.
- The constructor throws `InvalidOperationException` when a loaded document cannot be applied: two
  entries for one command, two entries that claim the same chord in co-active contexts, a chord an
  unremappable command owns, two chords where one is a strict prefix of the other in a co-active
  context, or a claim that would leave a context-scoped command unreachable. A host
  that lets users hand-edit the document should
  catch the failure and construct the service again from an empty document, so the application still
  starts with the catalog defaults instead of failing to open.
- Every successful `Apply` writes an override entry and raises `BindingsChanged`, even when the
  proposed set equals the current bindings; a refused or persistence-failed operation writes nothing
  and raises nothing.

## Logging

`KeyBindingServiceOptions.Logger` accepts an optional
`Microsoft.Extensions.Logging.ILogger`; diagnostics are discarded when none is
supplied. Event ids are stable within the package and occupy the `4000`-`4005` range:

| Event id | Meaning |
|---|---|
| `4000` | A host-reserved override was ignored |
| `4001` | Every override binding for an entry was invalid |
| `4002` | An override stroke has an empty key name |
| `4003` | An override stroke has an invalid key name |
| `4004` | An override stroke sets unknown modifier bits |
| `4005` | An override targets a command the catalog does not contain (the entry is preserved) |

## License

MIT © 2026 Kewin Kupilas.
