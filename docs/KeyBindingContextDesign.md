# Key-binding context tokens - design rationale

A binding can be scoped to a host-defined **context token**, so one control can host several mutually
exclusive modes (geometry edit, texture edit, lighting edit, brush placement) without duplicating the
catalog per mode. The model is implemented; this document records why it is shaped the way it is, the
conflict table that follows from its single rule, and the alternatives that were rejected.

## 1. The problem

A binding is active in a context because a *separate service and dispatcher pair* declares it, and
the host walks those pairs in priority order:

```csharp
chain.DispatchInPriorityOrder(stroke, context);   // one dispatcher per focus context
```

That covers "which control has focus" well, because focus really is a stack: the focused editor's
dispatcher is asked first and a `NotHandled` result falls through to the next one. It does **not** cover a
single control whose *mode* changes:

- **A mode is not focus.** Nothing about the four modes is visible to `KeyBindingDispatcher`, so the host
  must keep four service/dispatcher pairs alive and swap or re-order them on every mode change.
- **Shared bindings multiply.** Every chord all four modes share (`Ctrl+S`, `Ctrl+Z`, `F5`, camera keys)
  must be duplicated into all four catalogs, or moved into a fifth "always" context where it can no longer
  be customized per mode.
- **A mode change between two strokes is unhandled.** The chord model put the pending-chord buffer on the dispatcher,
  so if the user starts `Ctrl+K` in geometry mode and switches mode before the second stroke, the buffer
  lives on a dispatcher that is no longer first in the chain.
- **Nothing is declarable.** `CommandCatalog` cannot say "this binding only exists in geometry mode", so a
  shortcut UI cannot list or filter it, `Validate` cannot explain "`Ctrl+E` is taken in texture mode", and
  conflict analysis cannot see that the *same chord in two different modes is perfectly legal*.

The four modes make this concrete, but the same gap appears for any non-focus state: a timeline versus a
viewport, a numeric transform HUD that swallows single-letter keys, or a modal drag.

## 2. The model

The model adds one declaration dimension: a **context token** - an opaque, host-authored string that names the state a
binding belongs to.

- `null` means **always active**. This is where the shared `Ctrl+S`/`F5` bindings go.
- Any other value is a token the host defines, such as `"geometry"`.
- **The host guarantees at most one token is active at a time.** Tokens are mutually exclusive by contract;
  there is no hierarchy and no expression language. The host publishes the active token, or `null`.

That single rule keeps the whole thing cheap. Everything below follows from it.

### 2.1 The one rule

> **Two bindings conflict when their contexts can be active at the same time**: the same token, or either
> one is `null` (always).

Every validation rule is an instance of that sentence:

| Situation | Co-active? | Result |
|---|---|---|
| Same chord, same token, two commands | yes | conflict (`KeyBindingOutcome.Conflict`) |
| Same chord, one always and one token-scoped | yes | conflict |
| Same chord, two different tokens | no | **legal** - this is the point of the feature |
| Chord `A` is a strict prefix of chord `B`, same token | yes | `KeyBindingOutcome.PrefixConflict` |
| Chord `A` is a strict prefix of chord `B`, one always | yes | `PrefixConflict` |
| Chord `A` is a strict prefix of chord `B`, different tokens | no | legal |

Because tokens are mutually exclusive, co-activeness is decidable in one string comparison, so **the
"one chord resolves to at most one command" invariant survives** - it becomes "one chord resolves to at
most one command *per active context*". That is why this design is small: no parser, no evaluator, no
first-match-wins precedence puzzle.

### 2.2 Lookup

With the rule above, at most one of the two candidate bindings for an active token can exist, so a lookup
is two exact probes with no precedence ambiguity:

```text
TryGetCommand(chord, null)      -> probe (always, chord)
TryGetCommand(chord, "geometry")-> probe ("geometry", chord); if miss, probe (always, chord)
```

`IsChordPrefix(chord, token)` uses the same two buckets.

## 3. API surface

### 3.1 `CommandDescriptor<TCommandId>`

```csharp
/// Gets the context token this command's bindings belong to, or null when they are always active.
public string? Context { get; init; }
```

It is an **init-only property** rather than a constructor parameter, because the constructor already ends in
`params KeyChord[] defaultBindings`, and a required parameter in front of a `params` tail is a shape that can
trip `RS0026`; an init-only property also keeps every existing call site source-compatible.

Declaring a mode-specific command then reads:

```csharp
new CommandDescriptor<EditCommand>(EditCommand.Extrude, "edit.extrude", CommandRemappingPolicy.Remappable,
    new KeyCombo(KeyCode.E, KeyModifierSet.None)) { Context = "geometry" }
```

Validation (in `CommandCatalog`'s constructor, where the other declaration rules live): `Context` must be
`null` or a non-empty, non-whitespace string. An empty token is rejected rather than normalized, because
silently treating it as the always-active context would make a mistyped token resolve the always-active
bindings instead of failing fast.

Comparison is `StringComparison.Ordinal`, matching `SerializedId`.

### 3.2 `CommandCatalog<TCommandId>`

- The duplicate-default-chord set is keyed by `(context, chord)`.
- The strict-prefix check is computed per co-active bucket rather than globally.
- Rejection: the same chord declared both always and in a token context (an instance of the one rule).
- The exception messages name the token, so a declaration error is actionable:
  `The default key binding Control+E is declared in context 'geometry' and always.`

### 3.3 `IKeyBindingService<TCommandId>` / `KeyBindingService<TCommandId>`

```csharp
bool TryGetCommand(KeyChord keyChord, string? context, [NotNullWhen(true)] out TCommandId? command);
bool IsChordPrefix(KeyChord keyChord, string? context);
```

Both take a **required** `context` parameter. There is deliberately no one-argument overload: it would
be a forwarding-only member, because `x` and `x, null` mean exactly the same thing. A host with no
contexts passes `null`.

`Validate` and `Apply` take no context parameter. This follows from the model: a proposal's
context comes from the proposed command's descriptor, and the contexts of everything it might collide with
come from the catalog, so the service already has both sides of the comparison.

The declared-token set is exposed as:

```csharp
/// Gets every context token the catalog declares; empty when the catalog declares none.
IReadOnlySet<string> Contexts { get; }
```

It costs one `FrozenSet<string>` on the service and it removes the loudest footgun of a string token:
a host that publishes `"Geometry"` while the catalog declares `"geometry"` would silently fall back to the
always-active bindings. With this set the host can fail fast at startup.

### 3.4 `KeyBindingDispatcher<TCommandId>`

```csharp
KeyChordDispatchResult Dispatch(KeyCombo keyCombo, string? context);
```

The host supplies the active token per stroke, the same way it supplies the cancel triggers: the package
owns the buffer and the sequencing, the host owns what the current mode is. Threading it per call (rather
than taking a `Func<string?>` in the constructor) keeps the dispatcher free of a second source of truth for
host state and makes the mode-change rule below directly testable.

`HasPendingChord`, `PendingChord`, and `CancelPendingChord` are the buffer accessors. Internally the buffer records the
token it was started under.

**Mode change with a pending chord: drop it.** If the buffer is non-empty and the supplied token differs,
the buffer is cleared before the stroke is considered, so the stroke starts a fresh chord in the new mode.
Re-validating the buffered strokes against the new mode would resurrect a chord the user never started there,
and the strokes' meaning is mode-specific by assumption.

**A context-scoped command that cannot execute still falls through.** The dispatcher consults its
`CanExecuteCommand` predicate after the context has matched, and a command it rejects does not consume the
stroke: the dispatcher reports `NotHandled` and hands the resolved command back, so the next dispatcher in a
chain still gets its turn. A stroke that completed a chord whose command cannot execute drops the buffered
chord instead of leaving it armed, because the chord can never run.

### 3.5 `KeyBindingDispatcherChain`

```csharp
KeyChordDispatchResult DispatchInPriorityOrder(this IReadOnlyList<KeyBindingDispatcher<TCommandId>> dispatchers,
    KeyCombo keyCombo, string? context);
```

The first pass hands the stroke to the dispatcher that holds a pending chord and returns that
dispatcher's result *directly*. It must not hand the stroke over when the
buffered chord belongs to a context that is no longer active, or a mode change would silently swallow the
stroke.

The tempting rule - "a `NotHandled` result continues into the priority walk" - is unsound: a *wrong*
continuation stroke also reports `NotHandled`, and the chord model requires that stroke to end the chord
rather than be offered to a lower-priority context (pinned by
`DispatchInPriorityOrder_WrongContinuation_IsNotOfferedToAnotherContext`). The chain therefore compares
the dispatcher's buffered token (`KeyBindingDispatcher<TCommandId>.PendingContext`, an `internal` member)
with the supplied one before it hands the stroke over: a match keeps the direct return, a mismatch abandons
the buffer there and falls through to the ordinary priority walk. Both chord-model rules stay intact.

When the chain abandons the buffer, it calls `CancelPendingChord` itself rather than relying on the
dispatcher's own context-change check, so the stale buffer cannot execute the stroke inside the pending
dispatcher before the higher-priority contexts get their turn. The dispatcher keeps its own check because a
host can use it without a chain.

The chain is the **control/panel** mechanism (which pane has focus), while modes
are handled *inside* one dispatcher. Section 8 records how the two compose.

### 3.6 WPF and Avalonia adapters

```csharp
KeyChordDispatchResult HandleKeyDown<TCommandId>(this KeyBindingDispatcher<TCommandId> dispatcher,
    KeyEventArgs e, string? context, KeyDownHandlingOptions? options = null);
```

The context is a required parameter; `options` is optional. `KeyDownHandlingOptions`, the auto-repeat and
AltGr filters, and the "caller marks `Handled` for both `Executed` and `Pending`" contract all stay.

The Avalonia adapter (`Nickelony.KeyBindings.Avalonia`) has the same `HandleKeyDown<TCommandId>`
shape without the optional `options`: Avalonia's `KeyEventArgs` exposes neither an auto-repeat flag nor
left/right key state, so no auto-repeat or AltGr filter can be written and none is offered.

### 3.7 Surfaces outside the context model

`KeyChord`, `KeyCombo`, both `TryParse` members, `GetBindings`, `GetDisplayText`, the display
formatters (`DesktopKeyDisplayTextFormatter` and its `DesktopKeyDisplayConventions` presets ship from
`Nickelony.KeyBindings`),
`KeyBindingOverrides` and the whole persistence schema, `IKeyBindingOverridesStore`,
`KeyBindingOutcome` (`Conflict` and `PrefixConflict` already say the right things),
`KeyBindingConflictPolicy`, and `KeyBindingServiceOptions`.

## 4. Conflict analysis

The map build and the conflict analysis live in the internal `KeyBindingMapBuilder<TCommandId>`, which
`KeyBindingService<TCommandId>` delegates to; the one-rule invariants they encode are shared with the
catalog's own declaration validation through the internal `BindingClaimRules` helper. Four internal pieces
carry the rule:

- `PublishedMaps.CommandsByChord` is a map keyed by the internal `record struct ContextChord(string?
  Context, KeyChord Chord)`, whose always-active sentinel is a `null` token. The public surface spells the
  always-active context `null` as well, so no separate sentinel value exists.
- `PublishedMaps.ChordPrefixes` is a `FrozenSet<ContextChord>` holding `(declaringContext, prefix)` for
  every strict prefix of every dispatching chord, so `IsChordPrefix(chord, token)` is the same two probes as
  the lookup.
- `AnalyzeConflicts` compares only co-active bindings: exact collisions are probed in the proposal's own
  context and in the always-active context, and `HasPrefixRelationship` iterates only chords whose context
  is co-active with the proposal's.
- `Build` keys `defaultClaims` and `overrideClaims` by `ContextChord` and runs the prefix pass per
  bucket. It also validates every override claim against the defaults it collides with: the shadowed
  command must be remappable, and an always-active claim may never displace a context-scoped default,
  because that default's command would become unreachable in its own context. The loader applies the same
  rules, so a document cannot win a chord the API refuses to hand over - a `HostReserved` command's key
  included. The shadowing filter that builds `BindingsByCommand` (a command's chord is dropped when another
  command's override claims it) compares `ContextChord`s too.

Two behaviors are deliberately conservative:

- **Preserved entries stay conservative.** A preserved entry has no catalog descriptor, so the loader
  cannot know the context of its command and its context is never assumed. The check
  (`HasNewPreservedEntryConflict`) therefore reports a claim that collides with a preserved entry as
  `Conflict` in every context - but only for a claim the operation *introduces*, so a collision the loaded
  document already carries does not block an unrelated mutation. This is the right default: the
  alternative is a document that loads once the command is cataloged and then fails.
- **`Replace` never reaches across contexts.** With `KeyBindingConflictPolicy.Replace`, a proposal for a
  geometry command cannot take a chord from a texture command, because two different non-null tokens are never
  co-active: conflict analysis probes only the proposal's own context and the always-active context, so a chord owned
  by a command in another context is not a conflict and nothing is removed from its override entry - both
  commands simply keep the chord in their own contexts. `conflictsByOwner` therefore collects only the
  co-active conflicts the policy can resolve, and for those the cleanup removes the chord from the other
  command's stored override entry exactly as it does for two commands in the same context. A collision an
  always-active proposal would create with a context-scoped binding is refused outright rather than
  resolved, because that command would otherwise become unreachable in its own context.

## 5. Persistence and overrides

The persistence schema does not get involved, which is the strongest part of this design:

- The override schema stays at version `3`. An override entry stores chords for a `SerializedId`, and a
  command's context is a property of the *catalog descriptor*, not of the override, so an existing
  settings document keeps working and no migration exists.
- A user overriding a mode-specific command keeps it in that mode automatically, because the context is
  inherited from the descriptor. A settings UI that wants to show "`Ctrl+E` (geometry)" reads the token off
  the descriptor it already has.
- Two override entries may claim the same chord as long as they belong to commands in different contexts.
  The load path (`Build`) keys its claims per bucket, so this is enforced at
  load exactly as it is at declaration.

## 6. Host usage (the four-mode editor)

```csharp
// Declaration: one catalog, one service, one dispatcher, four modes.
new CommandDescriptor<EditCommand>(EditCommand.Extrude, "edit.extrude", CommandRemappingPolicy.Remappable,
    new KeyCombo(KeyCode.E, KeyModifierSet.None)) { Context = "geometry" }
new CommandDescriptor<EditCommand>(EditCommand.Paint, "edit.paint", CommandRemappingPolicy.Remappable,
    new KeyCombo(KeyCode.E, KeyModifierSet.None)) { Context = "texture" }
new CommandDescriptor<EditCommand>(EditCommand.Save, "file.save", CommandRemappingPolicy.Remappable,
    new KeyCombo(KeyCode.S, KeyModifierSet.Control))   // always active

// Dispatch: the host already knows its mode.
EditMode mode = _editor.ActiveMode;
KeyChordDispatchResult result = _dispatcher.Dispatch(combo, mode.Token);
if (result != KeyChordDispatchResult.NotHandled)
    eventArgs.Handled = true;
```

`E` means extrude in geometry mode and paint in texture mode with no context objects, no duplicated
catalogs, and no host-side re-ordering.

## 7. Non-goals

- **No expression language.** No `editorTextFocus && !inSnippetMode`. A condition is one token.
- **No multiple tokens per command** and **no token hierarchy**. "Applies in several modes" is expressed by
  declaring the command's chord in the always-active context, which is the same thing for a mutually exclusive
  dimension.
- **No per-binding conditions in the user override file.** A predicate is application structure, not user
  data; a persisted predicate would need a serializable condition language and would let an edited settings
  file change dispatch semantics.
- **No new outcome, policy, or `KeyBindingOutcome` member.**
- **No change to the pending-chord cancel contract** (the host still owns Escape, focus loss and the idle
  timeout).

## 8. How this composes with the priority chain

A host picks its mechanism per dimension:

| Need | Mechanism |
|---|---|
| Mutually exclusive modes inside one control | one dispatcher, the active context token |
| Several panes that can each hold focus, with a fallback order | one dispatcher per pane, `DispatchInPriorityOrder`, each with its own token |
| Nothing context-specific | pass `null` everywhere; behavior is exactly the chord model's |

The two compose without interference: a pane's dispatcher is handed the token of the mode that pane is in,
and the chain still routes a chord continuation to the dispatcher that owns it.

## 9. Rejected alternatives

- **A generic `when` expression engine** (parser, evaluator, error surface, precedence). Rejected: no
  consumer needs arbitrary conditions - a mode is a single-valued host state - and it would triple the
  public surface, add a second source of truth for host state, and make "are these two bindings
  co-active?" undecidable. VS Code answers that with first-match-wins, which makes dispatch order-sensitive.
- **Per-binding conditions stored in the user override file.** Rejected: predicates are application
  structure, and a persisted condition language would let a settings file change dispatch semantics; it
  would also break the current guarantee that a settings document only maps commands to chords.
- **A token hierarchy** (`geometry` implies `viewport`). Rejected: it reintroduces overlap ambiguity, and the
  always-active context already expresses "applies everywhere".
- **Expressing modes with one service/dispatcher pair per mode** (a host-side workaround). Rejected as
  the *design*: it duplicates every shared binding, gives no declarable answer, and loses the pending chord
  on a mode switch. It remains a valid host-side workaround for a host that does not use the context model.
- **A separate `KeyboardBindingMode` enum shipped by the package.** Rejected: the package does not know the
  host's modes, and a closed enum would force every host into the same vocabulary.
