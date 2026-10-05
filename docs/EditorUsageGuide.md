# Editor usage guide

This guide is for a host consuming a Nickelony IDEKit editor binding. The WPF binding is
`Nickelony.IDEKit.AvalonEdit`; the cross-platform binding is `Nickelony.IDEKit.AvaloniaEdit`. Both
expose the same surface over their engine's editor control, except where the engine forces a difference.
Two examples are the completion tooltip, which only the WPF binding exposes (query
`CompletionPresenter.IsTooltipSupported`; see
[tooltip availability](EditorLanguageFeaturesGuide.md#tooltip-availability)), and
`TextEditorNavigationExtensions.TryMoveCaretToMousePosition`, which reads the ambient mouse position in WPF and takes
the `PointerEventArgs` that carries the position in Avalonia. The wiring below reads the same in either.

The sample hosts wire these services to a running editor: see
[`SampleEditingFeatures`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Editing/SampleEditingFeatures.cs) for typing assistance and
[`SampleLineStatus`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Editing/SampleLineStatus.cs) for bookmarks and change markers.

The [editor binding guide](EditorBindingGuide.md) covers the structure of the binding packages and adding a new
binding.

## Thread affinity

The editor-facing helpers, services, margins, and renderers are called on the UI thread that owns the
editor, like all engine document access. The documented cross-thread exceptions are the members whose
own documentation states the rule: a `TextDocumentSnapshot` may be read from any thread after it is
captured, and `UnsavedChangesTracker.SetBaseline` may be called from any thread.

`UnsavedChangesTracker.Changed` is raised on the thread that caused the change. For an edit it arrives
inside the document's own change notification, so a subscriber must not edit the document synchronously
(the engine rejects a change inside another change).

Shared controller code marshals across the boundary through the toolkit-neutral
`Nickelony.IDEKit.Core.Threading.IUiDispatcher` interface. The binding adapts it with
`UiDispatcher.From(dispatcher)` for a specific dispatcher, or with `UiDispatcher.FromCurrent()`, which wraps
the calling thread's dispatcher on WPF and `Dispatcher.UIThread` on Avalonia.

## Lifecycle

- A margin detaches when it is removed from the editor's margins: the source-notification subscription
  ends with the text-view connection.
- `UnsavedChangesTracker` and `DocumentLineStateCache<TState>` dispose optionally. Both keep working
  without disposal, and a disposed instance degrades to reporting no marked lines or `default` states
  instead of throwing from a render pass. A write to a disposed instance still fails:
  `UnsavedChangesTracker.SetBaseline` raises `ObjectDisposedException`.
- Without a document, the editing helpers throw `InvalidOperationException` ("The editor has no document
  assigned."), the auto-closing handlers report no action, and the read-only members report the absence:
  `GetMarkedLineNumbers` returns an empty list, `TryGetOffsetFromPoint` returns `false`, and the bookmark
  tracker reports no bookmarks.

## Programmatic edits and the edit-target contract

The editing operations apply through an optional `ITextEditTarget` (the Core interface for targets that own
document authority), so the generic helpers stay reusable while a host wires the target where it needs
it. With no target, edits apply directly to the editor's document through the built-in
`TextEditorEditTarget`; with a target, the edit goes through it.

A host-supplied target must satisfy the contract:

- It holds the same content as the editor's document when the call is made.
- It applies synchronously; the document is updated before the call returns.
- The whole batch is a single undo step, or the target documents its own granularity; the built-in target
  applies one undo step.
- A target that is not the built-in one is free to raise its notifications per operation, while the built-in
  target replaces the span the batch covers in one document change, so a `Changed` handler of the document runs
  once per batch and the engine's anchors still move per operation.

The requested caret and selection offsets are clamped to the editor's current document length, not to the
target's content. A target that does not update the editor's document leaves that length stale, so the
host sets the view state after it publishes the change.

The built-in `TextEditorEditTarget` derives its change stamp from the editor's current document, so a
document swap, including clearing the document, invalidates a batch prepared against the previous
document once `CaptureVersion` observes it. It holds no event subscriptions. `Text` and `Version` never
throw: an editor without a document reports empty text, and `Version` changes only when `CaptureVersion`
or an apply observes a change.

`Apply` throws `InvalidOperationException` and `TryApply` reports `false` for an editor without a
document, unless the batch is entirely no-ops, which changes neither the document nor the undo stack and
needs no document.

The same optional target is exposed by the other editing surfaces:

- `LineCommentService.ApplyEdit` and `DocumentFormatterService.FormatDocument` route comment
  transformations and whole-document formatting through it; the formatter receives the editor's text, and
  `FormatDocument` throws `InvalidOperationException` before it runs the formatter when the target's content
  differs from the editor's document.
- `TextEditorEditingExtensions` exposes it on its replacement operations; its selection and caret helpers
  act on the editor directly, so a host that owns document authority must observe those calls and keep
  its own content in sync.
- `AutoClosingService.ApplyTextEntering` and `TryDeletePair` apply the typed pair (or the pair
  deletion) through it as one batch, so a single undo removes or restores the whole pair. `TryDeletePair` belongs
  in a tunneling key-down handler (`PreviewKeyDown` on WPF, a tunneling `KeyDown` handler on Avalonia), because
  the text area marks Backspace handled in its own bubbling handling and the method then does nothing.

`FormattingService` in the language-features package is the formatting service for a provider: it asks an
`IFormattingProvider` to format the document, converts the answer's line and character edits to offsets, validates
them as one batch, and hands the batch to `IDocumentFormatterService.ApplyEdits`. It reports the result as a
`FormattingOutcome` instead of throwing, and an edit the user makes while the provider works makes the
outcome `Stale`, so the answer never overwrites it.

A line handle passed to the line operations is resolved against the editor's document first, so a
deleted line or a line from another document fails fast instead of editing an unrelated range.

The editor-facing services implement `IAutoClosingService`, `ILineCommentService`, and
`IDocumentFormatterService`, so a host that composes services per editor can substitute its own.
Fixed behavior is a static helper class; behavior a host may substitute per editor is an instance service
behind an interface.

## Bookmarks

`BookmarkTracker` is in-memory only: it tracks bookmarks as document anchors in one document and
raises `Changed` after explicit toggles, clears, and restores. Anchors move with the text, so an edit
before a bookmark keeps it on its line.

Around document swaps, reads for a swapped-in document report no bookmarks, and stale anchors are
discarded on the next mutation. `SaveTo` throws while the bookmarks are not bound to the current document,
instead of persisting an empty set that would delete the sidecar.

`IsBoundToCurrentDocument` reports that state, so a host can guard a save without a catch.
`ClearBookmarks` binds the tracker to the current document first, so a cleared set can be persisted
for it.

Persistence is optional. `Nickelony.IDEKit.Core.Bookmarks` ships `IBookmarkStore` with
`BookmarkSidecarStore`; a host restores bookmarks by passing one-based line numbers to
`BookmarkTracker.RestoreBookmarks`, or through `BookmarkTracker.RestoreFrom`, which leaves
the tracker unchanged when the store reports a failed load. A host saves through `SaveTo`, which
persists the numbers `GetMarkedLineNumbers` reports.

While a `BookmarkMargin` is attached to a text view and the source implements
`IChangeNotificationSource`, the margin subscribes to the source's `Changed` event and invalidates
itself, so programmatic toggles and restores need no manual invalidation. A guarded save reads:

```csharp
bookmarks.Changed += (_, _) =>
{
    // While the bookmarks are not bound to the current document (for example after a document
    // swap and before a restore), SaveTo throws instead of deleting the sidecar; guard
    // the save with IsBoundToCurrentDocument (or skip/defer it) in that window.
    if (bookmarks.IsBoundToCurrentDocument)
        bookmarks.SaveTo(store, filePath);
};
editor.TextArea.LeftMargins.Add(new BookmarkMargin(bookmarks));
```

## Change markers

`ChangeMarkerMargin` is driven by a pluggable `ILineStatusSource` from `Nickelony.IDEKit.Core.LineStatus`.
The built-in `UnsavedChangesTracker` marks the lines that differ from a recorded baseline: deletion-only
edits mark nothing, and a final line terminator is part of the comparison, so adding one marks the
trailing empty line. Line terminators are normalized before comparison, so CRLF, LF, and lone CR compare
equal.

A host records the baseline after a load or save with `UnsavedChangesTracker.SetBaseline`. A different
source can render other kinds of change markers, such as git diff lines; the tracker follows the document
its provider returns, reattaching when the provider returns a different one.

Because the tracker implements `IChangeNotificationSource`, an attached margin invalidates itself after
`SetBaseline`; a source that does not raise that notification requires the host to invalidate the margin
after it changes.

## Diagnostics

`DiagnosticsRenderer` is an `IBackgroundRenderer` that underlines the segments `ApplySegments` hands it and
redraws its layer. A host that underlines language-feature diagnostics creates the renderer through the
[diagnostics controller](EditorLanguageFeaturesGuide.md#diagnostics) of its LanguageFeatures binding, and a host
that draws its own diagnostics projects them with `DiagnosticSegmentConversion.Project`:

```csharp
var renderer = new DiagnosticsRenderer(editor.TextArea.TextView);
editor.TextArea.TextView.BackgroundRenderers.Add(renderer);

// myDiagnostics is the host's IReadOnlyList<Diagnostic>, for example the latest diagnostics
// its language-feature provider published.
renderer.ApplySegments(DiagnosticSegmentConversion.Project(myDiagnostics));
```

- Each segment is anchored to the document the view shows, so an underline follows the edits made after it until
  the next `ApplySegments`, and a deletion over a whole segment removes its underline.
- `TryFindSegmentAt` answers the segment at an offset of the edited text, as its index in the applied list, and
  `TryGetSegmentRange` answers where the segment of an index sits now.
- A document replaced in the view draws nothing until the next `ApplySegments`.
- The renderer draws in the selection layer by default; assign `DiagnosticsRenderer.Layer` before attaching it
  when another layer is needed.
- Assigning a pen does not redraw, so invalidate the view after it, for example with `TextView.Redraw()`.

## Indentation

`PolicyIndentationStrategy` bridges a Core `IIndentationPolicy` onto the engine's indentation strategy:

```csharp
editor.TextArea.IndentationStrategy = new PolicyIndentationStrategy(editor.Options, myPolicy);
```

`DetectIndentation` guesses what the document indents with and sets the options of the editor to it, and the
conversions change the text and the options together:

```csharp
IndentationDetection detection = editor.DetectIndentation(defaultInsertSpaces: true, defaultSize: 4);

editor.ConvertIndentationToTabs();
```

- `ConvertIndentationToSpaces` and `ConvertIndentationToTabs` read the tab size from `Options.IndentationSize`, and
  `ResizeIndentation` takes the current and the new size. Each one sets `ConvertTabsToSpaces` to match the text,
  even when no line changes, and `ResizeIndentation` also sets `IndentationSize` to the new size.
- A conversion replaces only the leading whitespace of the lines that change, as one batch in one undo step, so
  the scroll position stays and the engine's anchors keep the caret and the selection, a rectangle included, on
  their text. The options are not part of the undo step, so an undo restores the text and keeps the options.
- A conversion that changes nothing leaves the document, the undo stack and the selection untouched, and returns
  `false`.
- `DetectIndentation` copies the lines that the detector reads and no more, and returns what the text showed, so a
  host can show a detected value differently from a default.
- An optional `ITextEditTarget` routes the batch through a host-owned target that holds the same content as the
  editor.

## Line terminators

`ConvertLineTerminators` gives every line terminator of the document one style:

```csharp
editor.ConvertLineTerminators(LineTerminatorStyle.Lf);
```

- It replaces only the terminators, as one batch in one undo step, so the scroll position stays and the engine's
  anchors keep the caret and the selection, a rectangle included, on their text. A caret at the end of a line stays
  there, and a caret at the start of the next line stays on it.
- A conversion that changes nothing leaves the document, the undo stack and the selection untouched, and returns
  `false`. `Mixed` and `None` name no single terminator, so they throw an `ArgumentException`.
- The engine inserts the terminator of the caret's line when the user presses Enter, or of the line before it on the
  last line, so a converted document gets the new style for its new lines. A document without any line
  terminator gets `Environment.NewLine` for its first new line, whichever style the host recorded for it.
- The conversion changes the text only. A host with a [workspace view](WorkspaceViewsGuide.md#an-editor-view) needs
  no other call, because the view publishes the style of the text it holds; `GetDisplayName` of
  `LineTerminatorStyle` gives the label for a status bar.
- An optional `ITextEditTarget` routes the batch through a host-owned target that holds the same content as the
  editor.

## Highlighting

`RegexHighlightingDefinition` builds a validated `RegexHighlightingRuleSet` from regex-based rules and
delimiter-based spans (the neutral model lives in `Nickelony.IDEKit.Core.Highlighting`) and converts it
to the engine's rule set. A definition over live data rebuilds lazily when its cache version changes, and the
editor does not observe a rebuild by itself, so the host refreshes it from `RuleSetChanged`. The event is raised
on the thread that triggered the rebuild, so the handler marshals to the editor's thread:

```csharp
// Created on the editor's thread, so the handler can post back to it.
UiDispatcher ui = UiDispatcher.FromCurrent();

definition.RuleSetChanged += (_, _) => ui.Post(() => editor.SyntaxHighlighting = definition);
```

A style names its foreground as an `ArgbColor`, and `null` keeps the editor's theme color. A host that holds a
color string reads it with `ArgbColor.TryParse`, which accepts `#RGB`, `#ARGB`, `#RRGGBB`, and `#AARRGGBB` and
nothing else.

Its build-time rejections and its scan order follow the engine's highlight engine:

- The engine scans each line from left to right and, at each position, applies the earliest match of any
  rule. When two rules match at the same position, the rule returned first by `BuildRules` wins, so order
  the most specific rules first.
- The engine cannot advance past a zero-length match, so a pattern that can match empty text is rejected
  when the rule set is built.
- The check runs a fixed list of probe strings, so a pattern that matches empty only on other text is not
  caught here. It then fails inside the engine during rendering, where no layer above the engine catches
  the exception.
- A pattern that uses `RegexOptions.RightToLeft` is rejected at build time: the engine scans left to
  right, so a right-to-left pattern produces matches behind the scan position.
- A match timeout that fires while the build-time probe runs the pattern surfaces from the rule set
  build; one that fires during rendering surfaces in the colorizer, where no layer above the engine
  catches it.
- A rule match spans only the current line (the engine evaluates each line separately); `BuildSpans`
  provides the delimiter-based spans, such as block comments and long strings, that stay highlighted
  across lines.

## Host-owned defaults and toolkit-neutral contracts

"Host-owned" describes a type or authority the consuming host supplies and the package never creates
itself. Where the package needs a starting point, it ships a documented sample default the host overrides:
the margin brushes, geometry, and metrics, the diagnostic pens, and the default auto-closing pairs.

- `BookmarkSidecarStore` requires the sidecar extension to be supplied by the host (for example
  `.bookmarks`) and uses it for every sidecar it reads or writes.
- `NavigationLocation.IsEquivalentTo` compares document identifiers ordinally by default; a host whose document
  identities are case-insensitive passes `StringComparison.OrdinalIgnoreCase`.
- The Core `AutoClosingOptions` defaults to the language-neutral pairs (parentheses, braces,
  brackets, double quotes, and single quotes); language-specific tokens such as angle brackets and
  backticks are opt-in through `AutoClosingPair` presets, and quote-like pairs are configured
  explicitly with `AutoClosingPair.Kind`.
  - Auto-closing applies when the character after the caret is the end of the text, whitespace, or
    belongs to the pair kind's default auto-close-before preset
    (`AutoClosingOptions.DefaultBracketAutoCloseBefore` or `...DefaultQuoteAutoCloseBefore`).
  - `AutoCloseBefore` replaces that preset rather than narrowing it, so an empty set admits only
    whitespace and the end of the text.
  - Skipping existing closing text and Backspace pair deletion default to
    `AutoClosingTrackingMode.TrackedOnly`. Under that mode, both apply only to closing text the service
    inserted and tracked itself. A host that applies resolutions through its own edit path selects
    `Always` or `Never` instead.
- The engine-independent contracts live in the toolkit-neutral `Nickelony.IDEKit.Core` package:
  `IIndentationPolicy`/`IndentationRequest`, `BookmarkSidecarStore`, and `LineCommentPlanner`.

## Provided by the engine

Some editor features need no bridge. Engine-provided features such as search, folding, and current-line
highlighting are installed directly on the editor, not wrapped by this package.
