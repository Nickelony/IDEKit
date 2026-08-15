# Nickelony.IDEKit.AvaloniaEdit

A UI-coupled package in the Nickelony IDEKit family. It bridges the dependency-free
`Nickelony.IDEKit.Core` contracts to an AvaloniaEdit `TextEditor` control:
programmatic edits, line operations, configurable auto-closing, navigation,
line comments, bookmarks, unsaved-change markers, offset clamping, and
diagnostic underlines.

## Shared source

This package does not duplicate the reference AvalonEdit binding file-for-file. The files that are identical
modulo the engine namespace are a single source under `shared/Editor/` at the repository root, linked into
this project and compiled with the `AVALONIAEDIT` constant defined; each such file opens with an
`#if AVALONIAEDIT` header that selects the AvaloniaEdit `using` and this package's namespace. Only the files
whose bodies genuinely diverge live here as their own AvaloniaEdit siblings, and their causes are recorded in
`docs/EditorBindingGuide.md`.

## Getting started

Install the package from your configured feed (the library ships from a local
feed while it is in preview):

```powershell
dotnet add package Nickelony.IDEKit.AvaloniaEdit
```

A minimal editor setup: compose the auto-closing service, attach a change-marker
margin, and restore bookmarks from the sidecar for the document.

```csharp
using Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
using Nickelony.IDEKit.AvaloniaEdit.ChangeMarkers;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Bookmarks;

var autoClosing = new TextAutoClosingService();
var options = TextAutoClosingOptions.Default;

editor.TextArea.TextEntering += (_, e) => autoClosing.HandleTextEntering(editor, e, options);
editor.TextArea.KeyDown += (_, e) => autoClosing.HandleBackspace(editor, e, options);

var tracker = new UnsavedChangesTracker(() => editor.Document);
tracker.SetBaseline(originalText);                       // after load or save
editor.TextArea.LeftMargins.Add(new ChangeMarkerMargin(tracker));

var coordinator = new BookmarkCoordinator(() => editor.Document);
// ".bookmarks" is a sample sidecar extension; the host supplies the real one.
var store = new BookmarkSidecarStore(".bookmarks");
coordinator.RestoreBookmarks(store, filePath);           // on load, for the new document
```

## Requirements

.NET 8 (`net8.0`) and Avalonia.

## Dependencies

`Avalonia.AvaloniaEdit` 12.0.0 and `Nickelony.IDEKit.Core`. The package does not depend on
`Nickelony.IDEKit.Workspace`, so consuming it never pulls the workspace document
authority in transitively.

### Logging

The base package takes no logging dependency. A helper called without an editor
document throws the documented `InvalidOperationException` ("The editor has no
document assigned."), while the read-only members report the absence instead of
logging it; there is nothing for a logger to receive.

## Thread affinity

The editor-facing helpers, services, margins, and renderers must be called on the UI thread that owns
the editor, like all AvaloniaEdit document access. The documented exceptions are the cross-thread members:
a `TextDocumentSnapshot` may be read from any thread after capture, and
`UnsavedChangesTracker.SetBaseline` may be called from any thread. `UnsavedChangesTracker.Changed` is
raised on the thread that caused it; for an edit it arrives inside the document's own change
notification, so a subscriber must not edit the document synchronously (AvaloniaEdit rejects a change
inside another change).

## Lifecycle

- A margin detaches when it is removed from the editor's margins: the source-notification subscription
  ends with the text-view connection.
- `UnsavedChangesTracker` and `DocumentLineStateCache<TState>` dispose optionally; both keep working
  without disposal, and a disposed instance degrades to reporting no marked lines or `default` states
  instead of throwing from a render pass. A write to a disposed instance still fails:
  `UnsavedChangesTracker.SetBaseline` raises `ObjectDisposedException`.
- Without a document, the editing helpers and services throw `InvalidOperationException`
  ("The editor has no document assigned."), while the read-only members report the absence instead:
  `GetMarkedLineNumbers` returns an empty list, `TryGetOffsetFromPoint` and the auto-closing handlers
  report no action, and the bookmark coordinator reports no bookmarks.

## Package layout

The package is organized into vertical feature slices, each with its own namespace. Where a slice
ships a visible default (the bookmark and change-marker brushes and the diagnostic pens), it is a
non-normative sample the host overrides; see "Host-neutral boundary".

- `Nickelony.IDEKit.AvaloniaEdit.Documents` - document helpers
  (`TextDocumentExtensions.ClampOffset`), the immutable
  `TextDocumentSnapshot` (captured on the document's owner thread, readable from
  any thread; it wraps AvaloniaEdit's own document snapshot and materializes line
  metadata on first access), and the incrementally updated
  `DocumentLineStateCache<TState>` for line-start parser states.
- `Nickelony.IDEKit.AvaloniaEdit.Editing` - programmatic edits and line
  operations (`TextEditorEditOperations`, `TextEditorLineOperations`,
  `TextEditorEditTarget`) and whole-document formatting
  (`TextDocumentFormattingService`).

  Auto-closing (`TextAutoClosingService`, returning the Core `TextAutoClosingResult`)
  composes the Core `TextAutoClosingResolver` over a host-supplied pair list and
  applies its actions as one edit per typed pair (through the host-owned edit
  target when one is supplied; the built-in target groups the pair into one undo
  step). It covers:

  - per-kind gates for the character after the caret;
  - bracket-like and quote-like pair rules;
  - selection wrapping that keeps the enclosed text selected;
  - Backspace pair deletion through `HandleBackspace`;
  - per-document insertion tracking that feeds the resolver's provenance callback.
- `Nickelony.IDEKit.AvaloniaEdit.Navigation` - clamped caret, selection, and
  scroll-location operations (`TextAreaNavigationOperations`, extensions on AvaloniaEdit's
  `TextArea`; a host with a `TextEditor` passes `editor.TextArea`) and pointer-to-offset
  helpers (`TextEditorNavigationOperations`; the host supplies the point from its own
  pointer event), over the
  `Nickelony.IDEKit.Core.Navigation.NavigationLocation` record from Core.
- `Nickelony.IDEKit.AvaloniaEdit.Comments` - line-comment transformations
  (`TextLineCommentService`) driven by an explicit Core `CommentSyntax`. The edit
  computation lives in the Core `TextLineCommentPlanner` (the service captures the
  document into a snapshot and delegates); the service applies the edit to the editor
  document or a host edit target, preserves each line's original line terminator, and
  skips a transformation that would not change the selected text.
- `Nickelony.IDEKit.AvaloniaEdit.Bookmarks` - bookmark tracking
  (`BookmarkCoordinator` raising a `Changed` event on every explicit bookmark
  mutation: toggle, clear, and restore) and
  icon margin rendering (`BookmarkMargin`, driven by an
  `IBookmarkSource` and scaling its icons and reserved width with the margin's
  font size). Optional persistence through the Core `IBookmarkStore` seam in
  `Nickelony.IDEKit.Core.Bookmarks` (that slice ships the store implementations),
  and `BookmarkStoreExtensions` glue the coordinator to a store.
- `Nickelony.IDEKit.AvaloniaEdit.ChangeMarkers` - change-marker margin
  (`ChangeMarkerMargin`, scaling its marker and reserved width with the margin's
  font size) over a pluggable line-status source
  (`Nickelony.IDEKit.Core.LineStatus.ILineStatusSource`), with
  `UnsavedChangesTracker` as the ready-made source for unsaved-change lines.
  Sources that also implement `IChangeNotificationSource` (the tracker
  does) invalidate the margin automatically when they raise a change
  notification; the tracker raises one after `SetBaseline` and after every
  document edit and maintains its line view incrementally from those edits.
- `Nickelony.IDEKit.AvaloniaEdit.Diagnostics` - diagnostic underlines
  (`DiagnosticsRenderer`) over generic
  `Nickelony.IDEKit.Core.Diagnostics.TextDiagnosticSegment` values, themed
  through per-severity `ErrorPen`/`WarningPen`/`InformationPen`/`HintPen`
  properties. The segment and its `TextDiagnosticSeverity` are Core types, so
  producers, renderers, and hosts share one range model and one severity enum
  whose `Error`-`Hint` values mirror the Language Server Protocol numbering
  (protocol severities are still mapped explicitly; `None` is the enum default).
- `Nickelony.IDEKit.AvaloniaEdit.Rendering` - `LineStatusMarginBase`, the shared
  base for line-status margins that paint a marker beside the marked document
  lines their source reports (`ILineStatusSource` lives in
  `Nickelony.IDEKit.Core.LineStatus`, `IChangeNotificationSource` in
  `Nickelony.IDEKit.Core.Notifications`), plus `LineStatusIconMarginBase` on top
  of it for margins whose marker is a replaceable icon
  (`IconBrush`/`IconGeometry`) that runs a click action for the marked line;
  subclass either base to build custom margins over any line source. The namespace also
  carries the shared text-run styling contract (`ITextRunStyle`), its ready-made
  `TextRunStyle` record (foreground, bold, italic, and text decorations, with an
  `Empty` default), and the paint-time application helper (`TextRunStyleApplier`)
  used by the colorizing transformers in the `Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures`
  (semantic tokens) package.
- `Nickelony.IDEKit.AvaloniaEdit.Highlighting` - the AvaloniaEdit adapter for
  code-first, regex-based highlighting (`RegexHighlightingDefinition`). The
  neutral model (`RegexHighlightingRule`, `RegexHighlightingSpan`,
  `RegexHighlightingStyle`) and its validated set (`RegexHighlightingRuleSet`)
  live in `Nickelony.IDEKit.Core.Highlighting`; the adapter converts the set to
  AvaloniaEdit's rule set and `RegexHighlightingStyleExtensions` converts the
  neutral style to AvaloniaEdit's `HighlightingColor`. The definition covers both
  per-line rules and delimiter-based spans, so block comments and long strings
  stay highlighted across lines without an XSHD file; a span without an end
  pattern stays open to the document end.
- `Nickelony.IDEKit.AvaloniaEdit.Indentation` - a bridge that adapts an
  `IIndentationPolicy` from `Nickelony.IDEKit.Core.Indentation` to AvaloniaEdit's
  indentation seam (`PolicyIndentationStrategy`).

## Host integration seams

Each seam is a small wiring step; the snippets below show the intended shape.

### Programmatic edits

The editing operations are AvaloniaEdit-only. `TextEditorEditOperations` exposes
an optional `ITextEditTarget` (the Core seam for targets that own document
authority), so the generic helpers stay reusable while a host wires it exactly
where it needs it.

```csharp
editor.InsertText(insertOffset, newText, editTarget: myEditTarget);
```

`myEditTarget` stands for a host-provided target; this package neither defines
nor wraps the host's own target - it ships the default `TextEditorEditTarget`
that is used when no target is supplied. With no target, edits apply directly to
the editor's document through that built-in target; with a target, the edit goes
through it. A host that owns document authority supplies its own target and
updates its own state after the call when it tracks content changes.

A supplied target must satisfy the edit-target contract:

- Same content as the editor's document before the call.
- Synchronous apply; the document is updated before the call returns.
- The whole batch should be a single undo step, which the built-in target
  guarantees.

The requested caret and selection offsets are applied to the editor's document
and clamped to its current length, so a target that violates the contract by not
updating the document leaves the final view state to the host, which must set it
after publishing the change.

The built-in `TextEditorEditTarget` derives its change stamp from the
editor's current document, so a document swap (including clearing the document)
invalidates a batch prepared against the previous document. It holds no event
subscriptions, and its `Text` and `Version` never throw: an editor without a
document reports an empty text, and a document swap advances the stamp. `Apply`
throws `InvalidOperationException` and `TryApply` reports `false` for an editor
without a document, unless the batch is entirely no-ops - which changes neither
the document nor the undo stack and needs no document.

The same optional target is exposed by the other editing surfaces:

- `TextLineCommentService.ApplyEdit` and
  `TextDocumentFormattingService.FormatDocument` route comment transformations
  and whole-document formatting through a host-owned target; when a target is
  supplied, the formatter receives the target's current content.
- `TextEditorLineOperations` exposes it on its replacement operations; its
  selection and caret helpers act on the editor directly, so a host that owns
  document authority must observe those calls and keep its own content in sync.
- `TextAutoClosingService.HandleTextEntering` and `HandleBackspace` apply the
  typed pair (or the pair deletion) through the target as one batch, which the
  built-in target makes one undo step, so a single undo removes or restores the
  whole pair.

A line handle passed to the line operations is resolved against the editor's
document first, so a deleted line or a line from another document fails fast
instead of editing an unrelated range. The editor-facing services implement
`ITextAutoClosingService`, `ITextLineCommentService`, and
`ITextDocumentFormattingService`, so a host that composes services per editor can
substitute its own implementations: fixed behavior is a static helper class, while
behavior a host may substitute per editor is an instance service behind an
interface. The auto-closing service is wired as shown in the getting-started
snippet above.

### Bookmarks

The bookmark coordinator is in-memory only: it tracks bookmarks as document
anchors in one document and raises `Changed` after explicit toggles, clears, and
restores. Anchors move with the text, so an edit before a bookmark keeps it on
its line. Its lifecycle around document swaps is documented on the type: reads
for a swapped-in document report no bookmarks, stale anchors are discarded on the
next mutation, and `SaveBookmarks` fails (throws) while the bookmarks are not
bound to the current document instead of persisting an empty set that would
delete the sidecar (`IsBoundToCurrentDocument` reports that state, so a host can
guard a save without a catch). `ClearBookmarks` binds the coordinator to the current document first,
so a cleared set can be persisted for it. Persistence is optional: the
editor-neutral `Nickelony.IDEKit.Core.Bookmarks` slice ships `IBookmarkStore` with
`BookmarkSidecarStore`, a host restores
bookmarks by passing one-based line numbers to `BookmarkCoordinator.RestoreBookmarks`
(or through `BookmarkStoreExtensions.RestoreBookmarks`, which leaves the
coordinator unchanged when the store reports a failed load), and persists the
numbers read from `GetMarkedLineNumbers` (see
`BookmarkStoreExtensions`). While the `BookmarkMargin` is attached to a text
view and the source implements `IChangeNotificationSource`, the margin
subscribes to the source's `Changed` event and invalidates itself, so
programmatic toggles and restores do not require a manual invalidation call.
With the `coordinator` and `store` from the getting-started snippet, the
guarded save reads:

```csharp
coordinator.Changed += (_, _) =>
{
    // While the bookmarks are not bound to the current document (for example after a document
    // swap and before a restore), SaveBookmarks throws instead of deleting the sidecar; guard
    // the save with IsBoundToCurrentDocument (or skip/defer it) in that window.
    if (coordinator.IsBoundToCurrentDocument)
        coordinator.SaveBookmarks(store, filePath);
};
editor.TextArea.LeftMargins.Add(new BookmarkMargin(coordinator));
```

### Change markers

The change-marker margin is driven by a pluggable `ILineStatusSource` from
`Nickelony.IDEKit.Core.LineStatus`. The
built-in `UnsavedChangesTracker` marks the lines that differ from a recorded
baseline (deletion-only edits mark nothing, and a final line terminator is part
of the comparison, so adding one marks the trailing empty line); a host records
the baseline (for example after a load or save) by
calling `UnsavedChangesTracker.SetBaseline`, and a different source can be
supplied to render other kinds of change markers (such as git diff lines).
Because the tracker implements `IChangeNotificationSource`, an attached
`ChangeMarkerMargin` invalidates itself after `SetBaseline`; sources that do not
raise that notification require the host to invalidate the margin after they
change. The tracker and margin wiring is the getting-started snippet above.

### Diagnostics

The diagnostic renderer is an `IBackgroundRenderer`: hosts add it to the text
view's background renderers and invalidate the text view after the diagnostic
segments or pens change (for example with `TextView.Redraw()`). It draws in the
selection layer by default; assign `DiagnosticsRenderer.Layer` before attaching
it when another layer is needed. The
`Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures` package projects IntelliSense
diagnostics for it through `TextDiagnosticSegmentFactory`.

```csharp
// myDiagnostics is the host's IReadOnlyList<TextDiagnostic> (for example the latest diagnostics
// its IntelliSense provider published); the factory projects it on every render pass.
var renderer = TextDiagnosticSegmentFactory.CreateRenderer(() => myDiagnostics);
editor.TextArea.TextView.BackgroundRenderers.Add(renderer);
editor.TextArea.TextView.Redraw();                      // after the diagnostics change
```

### Indentation

Indentation bridges a Core policy onto AvaloniaEdit's strategy seam:

```csharp
editor.TextArea.IndentationStrategy = new PolicyIndentationStrategy(editor.Options, myPolicy);
```

### Highlighting

Highlighting definitions rebuild lazily; refresh the editor after a rebuild:

```csharp
definition.RuleSetChanged += (_, _) => editor.SyntaxHighlighting = definition;
```

`RegexHighlightingDefinition` builds a validated `RegexHighlightingRuleSet` from
regex-based rules and delimiter-based spans (the neutral model lives in
`Nickelony.IDEKit.Core.Highlighting`) and converts it to AvaloniaEdit's rule set.
Its build-time rejections and its scan order follow AvaloniaEdit's highlight engine:

- The engine scans each line from left to right and, at each position, applies
  the earliest match of any rule; when two rules match at the same position, the
  rule returned first by `BuildRules` wins, and a match never overrides or
  suppresses an earlier one - so order the most specific rules first.
- The engine cannot advance past a zero-length match, so a pattern that can match
  empty text is rejected when the rule set is built. The probe is best-effort: it
  rejects only patterns that cannot match empty on any input, so a pattern that
  matches empty only on other text fails later, inside the engine, during
  rendering, where no layer above the engine catches the exception.
- A pattern that uses `RegexOptions.RightToLeft` is rejected for the same
  build-time reason: the engine scans left to right, so a right-to-left pattern
  produces matches behind the scan position.
- A match timeout that fires while the build-time probe runs the pattern surfaces
  from the rule set build; one that fires during rendering fails the same way.
- A rule match spans only the current line (the engine evaluates each line
  separately); `BuildSpans` provides the delimiter-based spans (block comments,
  long strings) that stay highlighted across lines.

## Provided by AvaloniaEdit

Some editor features need no bridge and are installed directly:
`SearchPanel.Install(editor)` for incremental search, `FoldingManager.Install(editor.TextArea)` plus a
`FoldingMargin` for folding, and `TextEditorOptions.HighlightCurrentLine` for the current-line
highlight. This package intentionally does not wrap them.

## Host-neutral boundary

"Host-owned" describes a type or authority that the consuming host supplies and
this package never creates itself. The package keeps host-supplied values as
host decisions; where it needs a starting point it ships a documented sample
default that the host overrides (the margin brushes, authored geometry, and
metrics, the diagnostic pens, and the default auto-closing pairs):

- `BookmarkSidecarStore` requires the sidecar extension to be supplied by the
  host (for example `.bookmarks`) and passes it explicitly to the Core
  `SidecarLineFile` writer.
- `NavigationLocation.IsEquivalentTo` compares file paths ordinally by default;
  hosts whose document identities are case-insensitive pass
  `StringComparison.OrdinalIgnoreCase`.
- The Core `TextAutoClosingOptions` defaults to the language-neutral pairs (parentheses,
  braces, brackets, double quotes, and single quotes); language-specific tokens
  such as angle brackets and backticks are opt-in through `TextAutoClosingPair`
  presets, and quote-like pairs are configured explicitly with
  `TextAutoClosingPair.Kind`. Auto-closing applies when the character after the
  caret is the end of the text, whitespace, or belongs to the pair kind's default
  auto-close-before preset (`TextAutoClosingOptions.DefaultBracketAutoCloseBefore`
  or `...DefaultQuoteAutoCloseBefore`); `AutoCloseBefore`
  replaces that preset when a host wants the language-configured behavior (an empty
  set admits no character besides whitespace and the end of the text).
  Skipping existing closing text and Backspace pair deletion default to
  `TextAutoClosingProvenance.Auto`; under that mode both apply only to closing text
  the service inserted and tracked itself, while hosts that apply resolutions
  through their own edit path select `Always` or `Never` instead.
- The AvaloniaEdit-independent contracts live in the toolkit-agnostic packages:
  `IIndentationPolicy`/`IndentationContext`, `SidecarLineFile`, and the line-comment
  planner (`TextLineCommentPlanner`) in `Nickelony.IDEKit.Core`.

## License

MIT (c) 2026 Kewin Kupilas.
