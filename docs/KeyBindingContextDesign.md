# Key-binding context tokens - design rationale

A binding can be scoped to a host-defined **context token**, so one control hosts several mutually
exclusive modes, such as geometry editing and texture editing, without a catalog per mode. This document
covers the model, the conflict table its one rule produces, and the rejected alternatives.

## 1. Why a token

Focus and mode are different dimensions. Focus is a stack: with one service and dispatcher per pane, the
chain asks the focused pane's dispatcher first, and a `NotHandled` outcome falls through to the next one:

```csharp
chain.Dispatch(keyStroke, context);   // one dispatcher per focusable pane
```

A mode changes inside one control. Without a token, a host would express each mode as one more service and
dispatcher pair, and that costs:

- **Pairs multiply.** The host keeps one pair per mode alive and swaps or re-orders the pairs on every mode
  change.
- **Shared bindings multiply.** Every chord the modes share (`Ctrl+S`, `Ctrl+Z`, `F5`, camera keys) is
  declared in every catalog, or moved into a shared catalog where it cannot be customized per mode.
- **A mode switch strands a chord.** The pending-chord buffer lives on a dispatcher, so a mode change between
  `Ctrl+K` and the next stroke moves another dispatcher ahead of the one that holds the buffer.
- **Nothing is declarable.** No catalog can say "this binding exists only in geometry mode", so a shortcut UI
  cannot list or filter it, `Validate` cannot report "`Ctrl+E` is taken in texture mode", and conflict
  analysis cannot see that the *same chord in two different modes is legal*.

The same gap appears for any non-focus state: a timeline versus a viewport, a numeric transform HUD that
swallows single-letter keys, or a modal drag.

## 2. The model

A **context token** is an opaque, host-authored string that names the state a binding belongs to.

- `null` means **always active**. This is where the shared `Ctrl+S`/`F5` bindings go.
- Any other value is a token the host defines, such as `"geometry"`.
- The host guarantees that at most one token is active at a time, and it publishes the active token, or `null`.

That guarantee keeps the model cheap; everything below follows from it.

### 2.1 The one rule

> **Two bindings conflict when their contexts can be active at the same time**: the same token, or either
> one is `null` (always).

The rule decides which bindings compete:

| Situation | Co-active? | Result |
|---|---|---|
| Same chord, same token, two commands | yes | conflict (`KeyBindingOutcome.Conflict` under `Reject`) |
| Same chord, one always and one context-scoped | yes | conflict; only the context-scoped side can take the chord over (2.2) |
| Same chord, two different tokens | no | **legal** |
| Chord `A` is a strict prefix of chord `B`, same token | yes | `KeyBindingOutcome.PrefixConflict` |
| Chord `A` is a strict prefix of chord `B`, one always | yes | `PrefixConflict` |
| Chord `A` is a strict prefix of chord `B`, different tokens | no | legal |

Because tokens are mutually exclusive, co-activeness is decidable in one string comparison, so **the
"one chord resolves to at most one command" invariant survives** - it becomes "one chord resolves to at
most one command *per active context*". That is why this design is small: no parser, no evaluator, and no
precedence rule beyond the two-probe lookup in section 2.2.

### 2.2 Precedence and lookup

A lookup is two exact probes: the active token first, then the always-active context.

```text
TryGetCommand(chord, null)       -> probe (always, chord)
TryGetCommand(chord, "geometry") -> probe ("geometry", chord); if miss, probe (always, chord)
```

The order decides one asymmetric case. A context-scoped command's override, under
`KeyBindingConflictPolicy.Replace` or in a loaded document, may shadow a remappable always-active default.
Both claims stay in the map: the token probe finds the override in its own context, and the default still
dispatches in every other context. The reverse is refused: an always-active override never displaces a context-scoped binding,
because that binding's command would become unreachable in its own context.

`IsChordPrefix(chord, token, predicate)` uses the same two buckets.

## 3. API surface

### 3.1 `CommandDescriptor<TCommandId>`

```csharp
/// The context token this command's bindings belong to, or <see langword="null"/> when they are always active.
public string? Context { get; init; }
```

It is an **init-only property** rather than a constructor parameter, because the constructor already ends in
`params KeyChord[] defaultBindings`: a positional `context` argument cannot follow a `params` tail, so it
would force a second public overload of the constructor.

Declaring a mode-specific command then reads:

```csharp
var extrude = new CommandDescriptor<EditCommand>(
    EditCommand.Extrude,
    "edit.extrude",
    CommandRemappingPolicy.Remappable,
    new KeyStroke(KeyCode.E, KeyModifiers.None)) { Context = "geometry" };
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

### 3.3 `IKeyBindingResolver<TCommandId>` / `IKeyBindingService<TCommandId>` / `KeyBindingService<TCommandId>`

```csharp
bool TryGetCommand(KeyChord keyChord, string? context, [NotNullWhen(true)] out TCommandId? command);
bool IsChordPrefix(KeyChord keyChord, string? context, Func<TCommandId, bool> commandPredicate);
```

Both are members of `IKeyBindingResolver<TCommandId>`, which `IKeyBindingService<TCommandId>` extends and the dispatcher
depends on alone. Both take a **required** `context` parameter. There is deliberately no one-argument overload: it would
be a forwarding-only member, because `x` and `x, null` mean exactly the same thing. A host with no
contexts passes `null`.

`IsChordPrefix` takes the predicate because "is this stroke the start of a chord" has two meanings: a
longer chord is bound under it, or a command that can run completes one. The dispatcher needs the second
meaning, so the probe walks the commands of the chords under the prefix and stops at the first one the
predicate accepts. A predicate that accepts every command asks the first question alone.

`Validate` and `Apply` take no context parameter. This follows from the model: a proposal's
context comes from the proposed command's descriptor, and the contexts of everything it might collide with
come from the catalog, so the service already has both sides of the comparison.

The declared-token set is exposed as:

```csharp
/// Lists every context token the catalog declares, or an empty set when it declares none.
IReadOnlySet<string> Contexts { get; }
```

It costs one `FrozenSet<string>` on the service and it removes the most dangerous failure mode of a string
token: a host that publishes `"Geometry"` while the catalog declares `"geometry"` would silently fall back
to the always-active bindings. With this set the host can fail fast at startup.

### 3.4 `KeyBindingDispatcher<TCommandId>`

```csharp
KeyStrokeDispatchOutcome Dispatch(KeyStroke keyStroke, string? context);
```

The host supplies the active token per stroke, the same way it supplies the cancel triggers: the package
owns the buffer and the sequencing, the host owns what the current mode is. Threading it per call (rather
than taking a `Func<string?>` in the constructor) keeps the dispatcher free of a second source of truth for
host state and makes the mode-change rule below directly testable.

`HasPendingChord`, `PendingChord`, and `CancelPendingChord` are the buffer accessors. Internally the buffer
stores the token that the chord started under.

**Mode change with a pending chord: drop it.** If the buffer is non-empty and the supplied token differs,
the buffer is cleared before the stroke is considered, so the stroke starts a fresh chord in the new mode.
Re-validating the buffered strokes against the new mode would resurrect a chord the user never started there,
and the strokes' meaning is mode-specific by assumption.

**A context-scoped command that cannot execute still falls through.** The dispatcher consults its
`CanExecuteCommand` predicate after the context has matched, and a command it rejects does not consume the
stroke: the dispatcher reports `NotHandled` and leaves the stroke for the next dispatcher in a chain. The
first stroke of a chord follows the same rule: the dispatcher buffers it only when a command under that
prefix, in the active or the always-active context, passes the predicate, so a chord that cannot run never blocks a lower
dispatcher's own binding of the stroke.

A stroke that completed a chord whose command cannot execute drops the buffered chord instead of leaving it
armed, because the chord is complete and no executable bound chord extends it.

### 3.5 `KeyBindingDispatcherChain<TCommandId>`

```csharp
KeyStrokeDispatchOutcome Dispatch(KeyStroke keyStroke, string? context);
```

The first pass hands the stroke to the dispatcher whose pending chord matches the active context and
returns that dispatcher's outcome *directly*. It must not hand the stroke over when the
buffered chord belongs to a context that has since become inactive, or a mode change would silently swallow the
stroke.

The tempting rule - "a `NotHandled` outcome continues into the priority walk" - is unsound: a *wrong*
continuation stroke also reports `NotHandled`, and that stroke must end the chord rather than be offered to
a lower-priority context.

The chain compares the dispatcher's buffered token (`KeyBindingDispatcher<TCommandId>.PendingContext`, an
`internal` member) with the supplied one before it hands the stroke over. A match keeps the direct return. A
mismatch abandons the buffer and continues the pending-chord scan, so a later pending dispatcher whose token
matches still receives the stroke.

The ordinary priority walk runs only after the pending-chord scan, so a pending chord whose context matches
always receives its continuation first.

When the chain abandons the buffer, it calls `CancelPendingChord` itself rather than relying on the
dispatcher's own context-change check, so the stale buffer cannot execute the stroke inside the pending
dispatcher before the higher-priority contexts get their turn. The dispatcher keeps its own check because a
host can use it without a chain.

The chain is the **control/panel** mechanism (which pane has focus), while modes
are handled *inside* one dispatcher.

### 3.6 WPF and Avalonia adapters

```csharp
KeyStrokeDispatchOutcome HandleKeyDown(this IKeyBindingDispatcher dispatcher,
    KeyEventArgs e, string? context, KeyDownHandlingOptions? options = null);
```

The context is a required parameter; `options` is optional and selects the auto-repeat and AltGr filters.
The adapter never marks the event handled: the caller marks `Handled` for both `Executed` and `Pending`.
`IKeyBindingDispatcher` carries the `Dispatch` member that `KeyBindingDispatcher<TCommandId>` and
`KeyBindingDispatcherChain<TCommandId>` share, so one extension feeds a single dispatcher or a chain.

The Avalonia adapter (`Nickelony.KeyBindings.Avalonia`) has the same `HandleKeyDown` shape, with an
optional `KeyRepeatGate` in place of `options`. Avalonia's `KeyEventArgs` exposes no auto-repeat flag, so the gate tracks
the held keys; an AltGr filter would depend on how Avalonia reports the left Ctrl and right Alt pair, so none is offered.

## 4. Conflict analysis

The map build lives in the internal `KeyBindingMapBuilder<TCommandId>` and the conflict analysis in the
internal `KeyBindingConflictDetector<TCommandId>`; `KeyBindingService<TCommandId>` delegates to both. The
one-rule invariants they encode are shared with the catalog's own declaration validation through the
internal `BindingClaimRules` helper. These internal pieces carry the rule:

- `PublishedSnapshot.CommandsByChord` is a map keyed by the internal `record struct ContextChord(string?
  Context, KeyChord Chord)`, whose always-active sentinel is a `null` token. The public surface spells the
  always-active context `null` as well, so no separate sentinel value exists.
- `PublishedSnapshot.CommandsByChordPrefix` is a `FrozenDictionary<ContextChord, ImmutableArray<TCommandId>>`
  keyed by `(declaringContext, prefix)` for every strict prefix of every dispatching chord, and its value
  lists each command that owns a chord under the prefix once, so `IsChordPrefix(chord, token, predicate)` is
  the same two probes as the lookup, each followed by a walk of the bucket.
- `KeyBindingConflictDetector.Analyze` compares only co-active bindings: exact collisions are probed in the
  proposal's own context and in the always-active context, and `HasPrefixRelationship` considers only
  chords whose context is co-active with the proposal's.
- `Build` keys `defaultClaims` and `overrideClaims` by `ContextChord` and runs the prefix pass per
  bucket.
- `Build` validates every override claim against the defaults it collides with: the shadowed command must
  be remappable, and the claim must follow the shadow rule of section 2.2.
- The loader applies the same validation, so a document cannot win a chord the API refuses to hand over,
  including a chord that a `HostReserved` command owns.
- The shadowing filter that builds `BindingsByCommand` (a command's chord is dropped when another command's
  override claims it) compares `ContextChord`s too.

These behaviors are deliberately conservative:

- A preserved entry has no catalog descriptor, so the loader cannot know the context of its command and never
  assumes one.
- The check (`KeyBindingConflictDetector.CollidesWithPreservedEntry`) reports a claim that collides with a
  preserved entry as `Conflict` in every context, because the alternative is a document that loads once its
  command is cataloged and then fails.
- Only a claim the operation introduces is checked, so a collision the loaded document already carries does
  not block an unrelated mutation.
- `Replace` never reaches across contexts: with `KeyBindingConflictPolicy.Replace`, a proposal for a geometry
  command cannot take a chord from a texture command, because two different non-null tokens are never co-active.
- A context-scoped proposal's conflict analysis probes only its own context and the always-active context, so a
  chord owned by a command in another context is not a conflict, and nothing is removed from that command's
  override entry: both commands keep the chord in their own contexts.
- `conflictsByOwner` therefore collects only the co-active conflicts the policy can resolve, and for those the
  cleanup removes the chord from the other command's stored override entry exactly as it does for two commands
  in the same context.
- A collision an always-active proposal would create with a context-scoped binding is refused under either
  policy, as section 2.2 states.

## 5. Persistence and overrides

The token is never persisted:

- An override entry stores chords for a `SerializedId`, and a command's context is a property of the
  *catalog descriptor*, not of the override, so the override schema carries no context.
- A user override of a mode-specific command stays in that mode, because the context is inherited from the
  descriptor. A settings UI that shows "`Ctrl+E` (geometry)" reads the token off the descriptor it already
  has.
- Two override entries may claim the same chord when their commands belong to different contexts. The load
  path (`Build`) keys its claims per context, so load enforces the rule exactly as declaration does.

## 6. Host usage (a mode-aware editor)

One catalog, one service, and one dispatcher serve every mode:

```csharp
var descriptors = new[]
{
    new CommandDescriptor<EditCommand>(
        EditCommand.Extrude,
        "edit.extrude",
        CommandRemappingPolicy.Remappable,
        new KeyStroke(KeyCode.E, KeyModifiers.None)) { Context = "geometry" },
    new CommandDescriptor<EditCommand>(
        EditCommand.Paint,
        "edit.paint",
        CommandRemappingPolicy.Remappable,
        new KeyStroke(KeyCode.E, KeyModifiers.None)) { Context = "texture" },
    new CommandDescriptor<EditCommand>(
        EditCommand.Save,
        "file.save",
        CommandRemappingPolicy.Remappable,
        new KeyStroke(KeyCode.S, KeyModifiers.Control)), // always active
};
```

The host already knows its mode, so it passes the mode's token with each stroke:

```csharp
private void OnStroke(KeyStroke stroke, KeyEventArgs eventArgs)
{
    EditMode mode = _editor.ActiveMode;
    KeyStrokeDispatchOutcome outcome = _dispatcher.Dispatch(stroke, mode.Token);

    if (outcome != KeyStrokeDispatchOutcome.NotHandled)
        eventArgs.Handled = true;
}
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
- **No context-specific outcome or policy.** A collision between co-active contexts reports
  `KeyBindingOutcome.Conflict` or `PrefixConflict` under the ordinary `KeyBindingConflictPolicy` values.
- **No context-specific cancel trigger.** The host owns Escape, focus loss, and the idle timeout whatever the
  active token; the only token-driven cancellation is the mode-change drop in sections 3.4 and 3.5.

## 8. How this composes with the priority chain

A host picks its mechanism per dimension:

| Need | Mechanism |
|---|---|
| Mutually exclusive modes inside one control | one dispatcher, the active context token |
| Several panes that can each hold focus, with a fallback order | one dispatcher per pane, `KeyBindingDispatcherChain`, which passes the one active token to each |
| Nothing context-specific | pass `null` everywhere; every binding is always active |

Modes and panes compose without interference: every pane's dispatcher receives the one active token, so a
mode-scoped binding resolves only while its mode is active, and the chain still routes a chord continuation to
the dispatcher that owns it.

## 9. Rejected alternatives

- **A generic `when` expression engine** (parser, evaluator, error surface, precedence). Rejected: no
  host needs arbitrary conditions - a mode is a single-valued host state - and it would widen the public
  surface, add a second source of truth for host state, and make "are these two bindings co-active?"
  undecidable. VS Code answers that with an ordered rule list in which the last matching rule wins, which
  makes dispatch order-sensitive.
- **Per-binding conditions stored in the user override file.** Rejected: predicates are application
  structure, and a persisted condition language would let a settings file change dispatch semantics; it
  would also break the guarantee that a settings document only maps commands to chords.
- **A token hierarchy** (`geometry` implies `viewport`). Rejected: it reintroduces overlap ambiguity, and the
  always-active context already expresses "applies everywhere".
- **Expressing modes with one service/dispatcher pair per mode** (a host-side workaround). Rejected as
  the *design* for the costs section 1 lists. It remains a valid host-side workaround for a host that does
  not use the context model.
- **A separate `KeyboardBindingMode` enum shipped by the package.** Rejected: the package does not know the
  host's modes, and a closed enum would force every host into the same vocabulary.
