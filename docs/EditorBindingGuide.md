# Editor-binding guide

This guide is for a developer adding an editor binding to the IDEKit family. The
editor-neutral logic a binding needs already lives in `Nickelony.IDEKit.Core`,
`Nickelony.IDEKit.LanguageFeatures` and `Nickelony.IDEKit.LanguageFeatures.Controllers`: the text model, edit
kernel, planners, request coordination, feature contracts and payloads, and the controllers that drive them.

The `Nickelony.IDEKit.AvalonEdit` and `Nickelony.IDEKit.AvalonEdit.LanguageFeatures` packages are the WPF
binding; the `Nickelony.IDEKit.AvaloniaEdit` and `Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures` packages
are the cross-platform Avalonia/AvaloniaEdit binding.

They are the worked example of the shape this guide describes: every piece named in section 3 has a
counterpart in both, and the binding test suites pin their behavior.

The AvaloniaEdit binding lives in `src/IDEKit/AvaloniaEdit/` with its suites in `tests/IDEKit/AvaloniaEdit/`.
Its engine-neutral files are shared with the WPF binding ([editor sharing guide](EditorSharingGuide.md)), and
its engine divergences are the pieces section 4 marks as intentionally per-binding:

- It shows completion in an Avalonia popup instead of a WPF window.
- It has no freezables, and its margin properties are `StyledProperty<T>` fields instead of
  `DependencyProperty` fields.
- It has no ambient pointer position, so its `TryMoveCaretToMousePosition` takes the `PointerEventArgs` that carries one.
- It has no reflected completion tooltip.

## 1. The tier model and dependency direction

The [architecture guide](ArchitectureGuide.md#tiers) states the tier rule, and the [dependency
diagram](https://github.com/Nickelony/LanguageServer/blob/main/README.md#dependency-diagram) of the root README
lists the references of every package. This section applies the rule to a binding.

The dependency direction is fixed:

- A binding references `Nickelony.IDEKit.Core`; a binding that hosts the language-feature controllers also
  references `Nickelony.IDEKit.LanguageFeatures` and `Nickelony.IDEKit.LanguageFeatures.Controllers`; its
  language-features package references its own base package. Each TextMate and Markdown binding package
  references its base package the same way, and no neutral package references a binding.
- The neutral packages are never pushed upward: `Core`, `LanguageFeatures` and its controllers must not know
  about a toolkit, a binding, or a concrete editor control. A type that needs a toolkit
  type lives in the binding; the neutral half is the contract, planner, or record the
  binding consumes.
- `Nickelony.LanguageServer.Provider.Abstractions` references `Nickelony.IDEKit.LanguageFeatures` only, so a
  binding hosts a language provider directly - see the
  [consumer integration guide](ConsumerIntegration.md) for construction, event
  marshaling, and cancellation behavior.
- A binding ships as its own packages in the repository's "Editor bindings" group (root
  README) and mirrors the AvalonEdit split: a base package (documents, editing, margins,
  renderers) plus a language-features package (controller factories and sources, the completion presenter
  and its windows, the code-action presenter, themes).
- The engine-neutral Markdown content model lives in `Nickelony.IDEKit.Markdown`, a
  Core-adjacent package that references `Core`, Markdig, and the logging abstractions; the Markdown
  bindings reference it. It stays out of `Core` because `Core` is a dependency-free leaf and must not take
  Markdig.
- The engine-neutral TextMate theme model and scope-ranking rule engine live in `Nickelony.IDEKit.TextMate`,
  a Core-adjacent package that references `Core` and the logging abstractions; the TextMate bindings
  reference it and convert its `ArgbColor` results to brushes. It takes no TextMateSharp reference, because
  the rule engine never reads a token model.

### Terminology

This guide uses these nouns in a precise sense:

- **binding** - the [architecture guide](ArchitectureGuide.md#binding) defines it; the two bindings are
  `Nickelony.IDEKit.AvalonEdit` and `Nickelony.IDEKit.AvaloniaEdit` with their feature layers.
- **adapter**, **bridge** - not synonyms for a binding; each names a type that adapts a neutral
  contract to an engine API (the `UiDispatcher` adapter, the `TextEditorEditTarget` adapter, the
  `PolicyIndentationStrategy` indentation bridge). A type named `...Adapter` works the other way, as the
  [vocabulary table](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md#vocabulary-table) defines.
- **seam** - the [editor sharing guide](EditorSharingGuide.md#2-the-shared-source-shape-lever-b) defines it.
- **engine** - the underlying text-editor toolkit a binding targets (AvalonEdit over WPF, AvaloniaEdit
  over Avalonia). This guide uses "engine-neutral" and "engine-typed" in this sense.

One naming policy follows from that vocabulary:

- **A presentation-state visibility flag is qualified only to disambiguate.** A qualifier appears only
  where the record exposes more than one hideable surface: `CompletionPresentationState.IsListVisible`
  names the list because the record also reports a tooltip, while the single-popup
  `SignatureHelpPresentationState.IsVisible` stays plain.
- **A type's role suffix names its part in a feature, not its size.** Keep one suffix per role across a
  feature's family.
  - `Controller` - drives one language feature for one editor, as the
    [vocabulary table](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md#vocabulary-table) defines it:
    `HoverController`.
  - `Coordinator` - sequences collaborators that it does not own, as the
    [naming rules](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md#vocabulary-table) define it:
    `WorkspaceViewCoordinator`.
  - `Tracker` - follows changing state and answers queries about it, as the same rules define it:
    `UnsavedChangesTracker`.
  - `Presenter` - maps neutral state onto a toolkit surface.
- **"presentation" carries inputs; "presentation state" carries observable state.** A `*PresentationOptions`
  record holds what a controller presents; a `*PresentationState` record holds what a host reads back. Keep
  the suffixes distinct.
- **A debounced unit takes the *Scheduled* qualifier.** Its cancel verb is `CancelScheduledRequest`.
- **Write "text run style" for `TextRunStyle` and the types around it.**

## 2. What a binding reuses

The neutral packages are the reusable half of every feature, consumed as-is by a binding.

### `Nickelony.IDEKit.Core` (dependency-free)

The dependency-free leaf: the neutral text model and edit kernel, the resolve/apply planners
(auto-closing, line comments, formatting, indentation, identifiers), and the contracts a
binding implements. The package README and `PublicAPI.*.txt` list the full surface; one
exemplar per slice:

- **Text model** (`Core.Text`): the snapshot/line/range primitives, the line helpers and the line-state cache -
  `ITextSnapshot`, `LineTextOperations`, `IncrementalLineStateCache`.
- **Edit kernel and boundary** (`Core.Editing`): `TextEditKernel`.
- **Auto-closing** (`Core.AutoClosing`): `AutoClosingPlanner`.
- **Comment and string scanning** (`Core.Scanning`): `CommentOperations`.
- **Line comments** (`Core.Comments`): `LineCommentPlanner`.
- **Diagnostics vocabulary** (`Core.Diagnostics`): `DiagnosticSegment`.
- **Request coordination** (`Core.Requests`): `LatestRequestGate`, and `KeyedLatestRequestGate` for a host that
  supersedes requests per key.
- **Navigation identity** (`Core.Navigation`): `NavigationLocation`.
- **Line-status contracts** (`Core.LineStatus`): `ILineStatusSource`.
- **Change notifications** (`Core.Notifications`): `IChangeNotificationSource`.
- **Formatting** (`Core.Formatting`): `IDocumentFormatter`.
- **Indentation** (`Core.Indentation`): `IIndentationPolicy`.
- **Identifiers** (`Core.Identifiers`): `IdentifierOperations`.
- **Bookmarks** (`Core.Bookmarks`): `IBookmarkSource`, with the sidecar persistence behind `BookmarkSidecarStore`.
- **Highlighting** (`Core.Highlighting`): `RegexHighlightingDefinitionOptions`.
- **Supporting primitives**: `LineDiffer` (`Core.Diffing`) and its sibling pathing, find/replace,
  and theming helpers.

### `Nickelony.IDEKit.LanguageFeatures` (Core-only)

The neutral feature contracts, one namespace per feature. Each namespace holds its provider contract (or its
source, for a push-model feature) and its request and payload records. A feature has one contract, and a controller
takes that contract from the host.

The package README and `PublicAPI.*.txt` list the full surface; one exemplar per feature:

- **Completion**: `ICompletionProvider`.
- **Hover**: `IHoverProvider`.
- **Signature help**: `ISignatureHelpProvider`.
- **Definition navigation**: `IDefinitionProvider` and `IReferenceProvider`.
- **Diagnostics**: `IDiagnosticsProvider` and `IDiagnosticsSource`.
- **Code actions**: `ICodeActionProvider`.
- **Document symbols**: `IDocumentSymbolProvider`.
- **Workspace symbols**: `IWorkspaceSymbolProvider` and `IWorkspaceSymbolResolver`.
- **Semantic tokens**: `ISemanticTokensSource` with the `SemanticToken` payload.
- **Rename and formatting**: `IRenameProvider` and `IFormattingProvider`.

### `Nickelony.IDEKit.LanguageFeatures.Controllers` (Core + LanguageFeatures + logging)

The engine-free behavior that drives those contracts, in one namespace per feature: the completion, hover,
signature-help, code-action and diagnostics controllers with their options, hooks and presentation state, the
shared `CompletionSessionKernel` and `SnippetExpander`, the definition resolvers (`DefinitionResolver`,
`HoverDefinitionResolver`), the `DocumentSymbolOutlineBuilder` projection, and the quick-pick modes that list
document and workspace symbols, open editors and lines. A language-server package never references it.

### `Nickelony.IDEKit.Markdown` (Core + Markdig + logging)

The package is the public Markdown content model and its render settings: its `PublicAPI.Unshipped.txt` lists
them, and a binding reads them as any host does, because only `Nickelony.IDEKit.Markdown.Tests` reaches the
package's internals.

- **Content model** (`Nickelony.IDEKit.Markdown.Model`): the Markdig parse and AST walk
  (`MarkdownDocumentModelBuilder`), rooted at `MarkdownDocumentModel`, with the block and inline node
  records it produces and their alignment/emphasis enums.
- **Render settings** (`Nickelony.IDEKit.Markdown`): `MarkdownRenderTheme` and `MarkdownRenderOptions` name
  no toolkit type, so a host builds one of each for either binding; the one engine-typed callback, the
  code-block highlighting installer, is an argument of the binding's renderer entry points.
- **The render plan is in the package** (`Nickelony.IDEKit.Markdown.Planning`). `MarkdownRenderPlanner` turns the
  content model into plan nodes that both emitters read, so the layout decisions exist once. The block-layout
  measurements, heading-scale lookup and code-surface inner width it applies are internal to the package, so
  tuning one changes no published contract.
- **The highlighting resolution order is in the package.** `MarkdownHighlightingResolver` applies the
  language-to-definition resolution order over a binding-supplied registry.
- **Block text is normalized inside the package.** The builder normalizes raw HTML block text, and
  `MarkdownCodeBlock.NormalizedCode` hands a code block's text to the bindings' code-block factory, so no
  binding links a copy of the rule.

### `Nickelony.IDEKit.TextMate` (Core + logging)

The package holds the TextMate token theme and the rule engine that ranks it, and a binding reads them as any
host does, because only `Nickelony.IDEKit.TextMate.Tests` reaches the package's internals.

- **Theme model** (`Nickelony.IDEKit.TextMate`): `TextMateTokenTheme` and its `TextMateTokenThemeRule`
  declarations, which hosts usually deserialize from theme data.
- **Rule engine**: `TextMateScopeResolver` parses the rules once, ranks the matches the way VS Code does, and
  returns a `TextMateTokenStyle`, an `ArgbColor` with font traits.
- **Conversion is the binding's job.** `TextMateThemeStyleResolver` of each binding turns a style into its
  brush and text decorations once per distinct style, so the package holds no toolkit type.

## 3. What every binding writes (AvalonEdit reference)

A binding ships each piece itself; the menu and window collaborators behind a piece stay internal, so a
binding is free to arrange them differently.

| Piece | AvalonEdit reference | Composes |
|---|---|---|
| Snapshot and document adapters | `TextDocumentSnapshot` (base) | `Core.Text` snapshot/line contracts (`ITextSnapshot`) |
| Edit-target adapter | `TextEditorEditTarget` (base) | `ITextEditTarget` (`Core.Editing`) |
| Editing and line operations | `TextEditorEditingExtensions` (base) | `Core.Text`/`Core.Editing` primitives |
| Auto-closing input service | `AutoClosingService` (base) | `AutoClosingPlanner` (`Core.AutoClosing`) |
| Line-comment service | `LineCommentService` (base) | `LineCommentPlanner` (`Core.Comments`) |
| Formatting service and indentation bridge | `DocumentFormatterService` (base) | `Core.Formatting`/`Core.Indentation` contracts, `EditorSelectionMapper` (`Core.Editing`) |
| Indentation conversion and detected options | `TextEditorIndentationExtensions` (base) | `IndentationPlanner`, `IndentationDetector` (`Core.Indentation`), `TextEditKernel` (`Core.Editing`) |
| Line terminator conversion | `TextEditorLineTerminatorExtensions` (base) | `LineTerminatorPlanner`, `TextEditKernel` (`Core.Editing`) |
| Provider-backed formatting | `FormattingService` (language features) | `IFormattingProvider` (`LanguageFeatures.Formatting`), `TextEditKernel` (`Core.Editing`) |
| Navigation helpers | `TextAreaExtensions` (base) | `NavigationLocation` (`Core.Navigation`) |
| Definition-navigation glue | `TextAreaNavigationExtensions` (language features) | `DefinitionResolver` (`Controllers.Navigation`), `HoverDefinitionResolver` (`Controllers.Hover`) |
| Line-status margins and markers | `LineStatusMarginBase` (base) | `Core.LineStatus`/`Core.Bookmarks` contracts |
| Bookmark persistence | `BookmarkTracker.SaveTo` and `RestoreFrom` (base) | `Core.Bookmarks` store and sidecar contracts |
| Diagnostics renderer | `DiagnosticsRenderer` (base) | `Core.Diagnostics` segment and severity, fed by `DiagnosticSegmentConversion` (`LanguageFeatures.Diagnostics`) |
| Text run styling | `TextRunStyle` (base) | none - toolkit styling |
| Regex highlighting | `RegexHighlightingDefinition` adapter (base) | `Core.Highlighting` rule/span/style model |
| Dispatcher adapter | `UiDispatcher` adapter (base, public) | `IUiDispatcher` (`Core.Threading`) |
| Completion controller factory | `CompletionControllerFactory` and `CompletionPresenter` (language features) | `CompletionController`, `ICompletionPresenter`, `CompletionEditorContext` and the shared session kernel (`Controllers.Completion`), with the `LanguageFeatures.Completion` contracts |
| Hover controller factory | `HoverControllerFactory` (language features) | `HoverController`, `IHoverPointerSource`, `HoverPointerOptions` (`Controllers.Hover`) and the `LanguageFeatures.Hover` provider and request contracts |
| Signature-help controller factory | `SignatureHelpControllerFactory` (language features) | `SignatureHelpController` and its options (`Controllers.SignatureHelp`), with the `LanguageFeatures.SignatureHelp` provider and payload family |
| Diagnostics controller factory | `DiagnosticsControllerFactory` (language features) | `DiagnosticsController` and its hooks and options (`Controllers.Diagnostics`), with the `LanguageFeatures.Diagnostics` provider and source contracts |
| Code-action controller factory | `CodeActionControllerFactory` (language features) | `CodeActionController`, `ICodeActionSelectionSource` and the hook contracts (`Controllers.CodeActions`), with the `LanguageFeatures.CodeActions` provider and request contracts |
| Hover and signature-help presenters | `HoverPresenter`, `SignatureHelpPresenter` (language features) | The display hooks of `HoverControllerHooks` and `SignatureHelpControllerHooks` |
| Code-action presenter | `CodeActionPresenter` (language features) | The controller's `Actions`, marked lines and `Changed` notification |
| Semantic-token colorizer | `SemanticTokensColorizer` (language features) | `LanguageFeatures.SemanticTokens` payloads; `TextRunStyle` for painting |

## 4. Intentional per-binding pieces

These pieces belong to the binding packages rather than `Core`/`LanguageFeatures`: the toolkit-adjacent surface a
binding owns. A piece identical modulo the engine namespace is single-sourced into both bindings, and the
[editor sharing guide](EditorSharingGuide.md) lists the files that stay forked. Port the shape, not the code:

- **Framework-typed hooks** carry toolkit values (`ContextMenu`, windows) - they are the host's own editor
  state exposed to a binding's presenter or controller (`CodeActionMenuHooks` for the menu).
- **The binding supplies the engine facts** its text area holds, such as the caret, the selection, and the offset
  under the pointer, to the controller through a delegate or a source (`IHoverPointerSource`,
  `ICodeActionSelectionSource`), so the host is never asked for them.
- **Themes and style resolution** bind colors, brushes, and metrics to one toolkit's
  rendering model (`CompletionWindowTheme`, `CompletionTooltipTheme`,
  `CodeActionMenuTheme`, and the colorizer's style resolver).
- **Run styles stay engine-typed.** `TextRunStyle` carries brushes, typefaces and decoration collections,
  because a neutral run format would make the binding create an engine brush or typeface for every element on
  every paint pass.
- **Presenters and window/popup mechanics** (`CompletionWindow*`, the tooltip pipeline,
  `CodeActionPresenter` and its menu) are toolkit windowing, not language logic.
- **Dispatcher adaptation** wraps one toolkit's dispatcher and thread-affinity rules behind the neutral
  `IUiDispatcher` interface: each binding's public `UiDispatcher` adapter (base) implements it over the engine
  dispatcher, so shared controller code never names a toolkit dispatcher type.
- **Margins and renderers** (`LineStatusMarginBase`-derived margins,
  `DiagnosticsRenderer`) draw into one toolkit's visual layers: the margins derive from the engine's
  `AbstractMargin`, and the renderer implements `IBackgroundRenderer`.
- **Input services** bind to one toolkit's event shapes (text-entering, preview key,
  mouse) - `AutoClosingService` is the reference.
- **Text-area glue** such as `TextAreaNavigationExtensions` and `TextEditorNavigationExtensions`
  applies neutral results to a concrete editor surface.
- **`PolicyIndentationStrategy`** adapts the neutral `IIndentationPolicy` onto one
  toolkit's indentation strategy.
- **An options record that names no engine type is a `LanguageFeatures.Controllers` type instead.** A controller
  options record holds request timing and trigger policy, such as a debounce delay, the pointer rest delay or the
  trigger rule.
- **The controller options records** are `CompletionControllerOptions` (`Controllers.Completion`),
  `SignatureHelpControllerOptions` (`Controllers.SignatureHelp`), `CodeActionControllerOptions`
  (`Controllers.CodeActions`) and `HoverPointerOptions` (`Controllers.Hover`), which the binding's pointer source
  reads, so a host that targets both bindings names one type.
- **Window and menu options are plain numbers and flags.** `CompletionWindowOptions` and `CodeActionMenuOptions`
  hold geometry as plain numbers and the activation policy as a flag, and the binding applies them with its own
  toolkit.
- **The presentation options stay per binding**, because they also carry the themes. The window and menu options
  reach the completion presenter and the code-action presenter through them
  (`CompletionPresentationOptions.WindowOptions`, `CodeActionPresentationOptions.MenuOptions`).
- **A presentation-state record that names no toolkit type is a `LanguageFeatures.Controllers` type too**:
  `CompletionPresentationState` and `SignatureHelpPresentationState` hold what a host reads back from
  a controller. The other engine-free controller types and shared session helpers live there as well.
- **Engine-free pieces do not stay in a binding package.** A type that names no engine type belongs in a neutral
  package (`Core`, `LanguageFeatures`, `LanguageFeatures.Controllers`, or a Core-adjacent package such as
  `Markdown`): the scrolling and selection enums are the `Core.Editing` types `LineScrollingMode` and
  `LineReplacementSelectionMode`, and the focus enum is `Core.Navigation.EditorFocusMode`.
- **Shared engine-free helpers are not binding types.** One that stays linked source lives in an internal-helper
  home of `shared/`, whose placement and namespace rules the [editor sharing guide](EditorSharingGuide.md#1-levers-for-sharing)
  owns.
- **The completion tooltip exists in the WPF binding only.** AvaloniaEdit's completion window publishes no tooltip
  member, so the Avalonia binding declares no TooltipTheme option, no ConfigureTooltip, ResolveDescriptionAsync or
  BuildMarkdownContent hook, and no IsTooltipSupported query; the parity allow-list names each. The [language-features
  guide](EditorLanguageFeaturesGuide.md#tooltip-availability) describes the WPF query.
- **Markdown documentation shows as raw text in the Avalonia tooltip.** AvaloniaEdit shows an item's Description in
  its own tooltip, so a Markdown documentation shows there as raw text until the engine exposes the tooltip to
  render it.

## 5. Sharing source and documentation between bindings

A binding pair keeps one copy of every file, test, and documentation block that is the same in both. The
[editor sharing guide](EditorSharingGuide.md) owns the sharing levers, the shape of a shared file, and the
list of files that stay forked.

## 6. Binding patterns to copy

### 6.1 Snapshot over the editor document

`TextDocumentSnapshot` implements the Core `ITextSnapshot`: it captures an immutable document snapshot on
the document's owner thread and stays readable from any thread. Line metadata is materialized lazily from
a full text copy on first line access, so keystroke-rate code reads only `TextLength`/`GetCharAt`.

The auto-closing resolver reads only those two members; the line-comment planner accepts the
materialization because toggling is a user-rate command. Copy both the shape and the cost discipline.

### 6.2 Edit target over the editor document

`TextEditorEditTarget` implements `ITextEditTarget` and its version contract. The binding's editing
operations accept an optional host target and apply through the binding's own target over the document
when none is supplied. The [edit-target contract](EditorUsageGuide.md#programmatic-edits-and-the-edit-target-contract)
states what a host target must guarantee.

### 6.3 The resolve/apply split

Every per-keystroke or per-command transform follows the same shape: capture a snapshot, call the neutral
planner or resolver (`LineCommentPlanner`, `AutoClosingPlanner`, `IDocumentFormatter`), then
apply the result through the edit target.

The binding keeps only input glue, read-only policy, document edits, and insertion tracking
(`LineCommentService` and `AutoClosingService` are the references). `DocumentFormatterService` keeps the
same split: `EditorSelectionMapper` decides where the caret and the selection go, and the service applies
the result. If a new feature needs text-only logic that does not exist yet, add the planner to Core with
this shape instead of keeping logic in the binding.

### 6.4 Margin over a line-status source

A margin takes a Core `ILineStatusSource`, draws a marker for each one-based marked line (scaling with the
editor's font metrics), and subscribes to `IChangeNotificationSource` when the source implements it to
invalidate itself. The Core `LineStatusInvalidationGate` owns that subscription and coalesces the invalidations
of a margin into one posted action, so a margin supplies only its connection check, its invalidate callback and
its dispatcher post.

`UnsavedChangesTracker` is the reference source for unsaved-change lines (built on `LineDiffer`); the
bookmark family shows the same pattern with persistence through the Core `BookmarkSidecarStore` (the host
supplies the sidecar extension).

### 6.5 Request lifecycle and dispatcher marshaling

Controllers run each request through `RequestLifetime` of `Core.Requests`, which owns one `LatestRequestGate`
and gates it on the controller's disposal. Its `RunAsync` admits the request only while the controller is alive,
awaits the work, and applies the result only while the controller is still alive. The controller's apply step
then runs on its dispatcher and checks the generation that `RequestLifetime` counts, so a result that a newer
request, a cancellation, an invalidation or a context change made stale is dropped.

A host that completes a request itself, outside a controller, admits it with the gate's `BeginRequest` and
validates it with `CanPublish`, using the `RequestHandle` that admission returns; `IsCurrent` alone also accepts a
canceled request. The [request coordination semantics](EditorLanguageFeaturesGuide.md#request-coordination-semantics) section
defines how each controller cancels and rejects requests, and the [core guide](CoreGuide.md#request-coordination)
defines the continuation context and cancellation-source ownership of `RunAsync`.

Debounce input through `DispatcherDebouncer` (`Core.Threading`), marshal results to the UI thread explicitly
through the `IUiDispatcher` (`Invoke`/`InvokeAsync`), and expose state through the controller's state records
(`CompletionPresentationState`, `SignatureHelpPresentationState`). The hover controller exposes no
state, for the reason the [language-features guide](EditorLanguageFeaturesGuide.md#feature-slices) gives.

### 6.6 Signature parameter highlight

Signature help delivers parameter entries (`ParameterInformation`), each with a pre-resolved label
and, when the provider resolved it from the protocol's label-offset form, an exact
`TextRange` (`Range`). Highlight the active parameter by that range when it is present.

When the range is absent, the provider contract only guarantees that the label is a plain-text fragment of
`SignatureInformation.Label`, so locate the label text inside the signature label by search. A plain
label can repeat (`f(a, a)`) or appear earlier in the label, so the search is a fallback, not an exact
position.

When a label cannot be located (for example because the server omitted parameter text from the signature
label, or because the provider left the label blank), present the signature without an active-parameter
highlight instead of guessing a range. An empty label is never locatable, so treat it as unresolved rather
than matching it at position zero.

### 6.7 Log event ids

A binding package that logs through `LoggerMessage` takes its event ids from the block that the [log event
ids](ArchitectureGuide.md#log-event-ids) registry lists for it.

### 6.8 Factory over a neutral controller

A controller whose request logic names no engine type lives in `Nickelony.IDEKit.LanguageFeatures.Controllers` and takes
the editor's facts through a source or a delegate. A binding writes one factory per such controller. The factory
adapts the text area's dispatcher to `IUiDispatcher`, builds the engine source (`IHoverPointerSource` or
`ICodeActionSelectionSource`) or the caret delegate, and creates the controller. It disposes the source when the
controller's constructor throws, because the controller owns the source only once its constructor returns.

A source that subscribes to the text area is built after the thread check, so a call from another thread
subscribes to nothing. `CodeActionControllerFactory` is the reference. `DiagnosticsControllerFactory` passes the
editor's facts as hooks instead of a source, so its `Detach` hook releases the text-area subscriptions and the
renderer when the controller is disposed.

## 7. Bring-up order and test layering

Dependency-respecting order (each step builds on the previous):

1. Snapshot and document adapters (section 6.1) - the foundation everything else reads.
2. Edit-target adapter and the editing/line operations (section 6.2).
3. Services that compose neutral planners: auto-closing, line comments, formatting,
   indentation bridge.
4. Navigation helpers over `NavigationLocation`, plus the mouse helpers.
5. Line-status margins, the unsaved-changes tracker, and the bookmark family with
   sidecar persistence.
6. Diagnostics renderer, fed from the neutral diagnostic projection.
7. Language features: completion first (window presenter, themes, item bridge, hooks), then
   the factories of the neutral controllers (section 6.8), the diagnostics factory among them, with the hover and
   signature-help presenters, the code-action presenter, definition navigation, and the semantic-token colorizer.
   - The hover and signature-help presenters own how their popups anchor and close.
   - The completion controller and the presenter overload of the signature-help factory open on typed characters by
     default, through one internal typed-text observer that evaluates a character after its input applied
     ([typing triggers](EditorLanguageFeaturesGuide.md#typing-triggers)).
8. Host wiring: key gestures, and which popup wins when two compete, stay host policy - the
   repository's `Nickelony.KeyBindings` package (with
   `Nickelony.KeyBindings.Wpf` for WPF hosts and `Nickelony.KeyBindings.Avalonia` for
   Avalonia hosts, and its toolkit-free `DesktopKeyDisplayTextFormatter` for
   desktop-convention display text) or the
   host's own command code.

**Files that would fork first.** Most shared files port as-is behind the `#if` header. A third binding
forks the files whose bodies carry engine-shaped structure rather than a type-name alias or a localized
call shape, and forking one is an expected porting step. The drawing and measurement margins and the
window and tooltip access pairs are listed under
[what stays per-binding](EditorSharingGuide.md#4-what-stays-per-binding-and-why), and the Markdown facade,
scroller, and emitter under [the optional-integration mirrors](EditorSharingGuide.md#6-the-optional-integration-mirrors).

Only the engine-shaped body forks; the neutral decision logic it drives stays in `Core`/`LanguageFeatures`.

Test layering:

- The neutral logic is already covered in `tests/IDEKit/Nickelony.IDEKit.Core.Tests` (`AutoClosing`,
  `Comments`, `Requests`, ...), `tests/IDEKit/Nickelony.IDEKit.LanguageFeatures.Tests` and
  `tests/IDEKit/Nickelony.IDEKit.LanguageFeatures.Controllers.Tests`; a new binding does not re-test or
  duplicate those suites.
- Binding suites test only the adapters, factories and presenters: document
  application, edit targets, selection restoration, margin invalidation, and what the engine half of each
  feature reads and shows.
  `tests/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit.Tests` and
  `tests/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests` are the reference suites, and
  their `tests/IDEKit/AvaloniaEdit/Nickelony.IDEKit.AvaloniaEdit*.Tests` mirror covers the same shape.
- Pin the toolkit UI thread: the AvalonEdit suites use MSTest `[STATestClass]` because WPF controls need
  STA. The AvaloniaEdit mirror runs its bodies on the Avalonia UI thread through the shared
  `AvaloniaTestHost` (`Avalonia.Headless`), whose `[AvaloniaTestClass]` dispatches each test method.
- Tests live in flat per-package namespaces; filter by type-name prefix, for example
  `--filter "FullyQualifiedName~LineComment"`.

## 8. Do not port

- Host-owned behavior: key gestures such as Ctrl+Space, the choice of which popup wins when two compete, the
  scope at the caret (`CompletionControllerHooks.ScopeAt`), and undo grouping are host code, not binding surface.
  How a hover or signature-help popup anchors and closes, and which typed characters open completion and
  signature help, are binding surface, which the presenters and the completion controller own.
- Workspace-edit application is not binding surface either: `WorkspaceEditApplier` of
  `Nickelony.IDEKit.Workspace.Views` applies a language-server edit to every view of the documents it names.
- Composition is host code too: no binding offers a per-editor object that installs the auto-closing service,
  the trackers and the margins in one call, a one-call workspace setup, or a lookup that picks a language
  server by file extension. Each host composes those pieces, as the base package readme shows, because the
  pieces a host installs and their order differ between hosts.
- The workspace family (`Nickelony.IDEKit.Workspace`, `Nickelony.IDEKit.Workspace.QuickPick`,
  `Nickelony.IDEKit.Workspace.Views`) is independent of editor bindings; combine it with a binding in host code.
- Never fork neutral logic into a binding. Extend `Core`/`LanguageFeatures` with the
  resolve/apply split (section 6.3) so every binding benefits.
