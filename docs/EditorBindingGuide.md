# Editor-binding guide

This guide is for a developer adding a new editor binding to the Nickelony IDEKit
family - for example `Nickelony.IDEKit.AvaloniaEdit` on AvaloniaEdit. The editor-neutral
logic a binding needs (the text model, the edit kernel, planners, request coordination,
feature contracts, payloads, and host-state records) already lives in
`Nickelony.IDEKit.Core` and `Nickelony.IDEKit.IntelliSense`. A binding reuses those
packages and writes only the framework-typed surface; it never re-implements a planner
or kernel that already exists.

The `Nickelony.IDEKit.AvalonEdit` and `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`
packages are the verified reference implementation. Every piece named in section 3 has
a working AvalonEdit counterpart there, and the packaged tests pin its behavior; port
that shape rather than inventing a new one. The seam itself was produced by the
2026-09-18 AvaloniaEdit-readiness extraction program.

> **Second binding landed (2026-10-04).** `Nickelony.IDEKit.AvaloniaEdit` and
> `Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures` are the cross-platform Avalonia/AvaloniaEdit mirror of
> the two packages above, in `src/IDEKit/AvaloniaEdit/` with their suites in `tests/IDEKit/AvaloniaEdit/`.
> The AvalonEdit reference stays the source of the binding shape; the mirror is the second worked example of
> it. Its engine divergences - an Avalonia popup instead of a WPF window, no freezables, styled properties,
> no ambient pointer position, and no reflected completion tooltip - are the pieces section 4 marks as
> intentionally per-binding, and its engine-neutral files are shared with the reference (section 5).

## 1. The tier model and dependency direction

```
Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures   (-> AvaloniaEdit, IntelliSense, base)
        |
        v
Nickelony.IDEKit.AvaloniaEdit                    (-> AvaloniaEdit, Core)
        |
        v
Nickelony.IDEKit.IntelliSense   ------------>    Nickelony.IDEKit.Core
        (-> Core only)                           (dependency-free leaf)

Language-server packages sit on the same contracts:
Nickelony.LanguageServer.Abstractions  ->  Nickelony.IDEKit.Core, Nickelony.IDEKit.IntelliSense
```

The dependency direction is fixed:

- A binding references `Nickelony.IDEKit.Core`; a binding that hosts the IntelliSense
  controllers also references `Nickelony.IDEKit.IntelliSense`; its language-features
  package references its own base package. Nothing references a binding.
- The neutral packages are never pushed upward: `Core` and `IntelliSense` must not know
  about a toolkit, a binding, or a concrete editor control. A type that needs a toolkit
  type lives in the binding; the neutral half is the contract, planner, or record the
  binding consumes.
- `Nickelony.LanguageServer.Abstractions` references `Nickelony.IDEKit.Core` and
  `Nickelony.IDEKit.IntelliSense`, so a binding hosts a language provider directly - see the
  [consumer integration guide](ConsumerIntegration.md) for construction, event
  marshaling, and cancellation behavior.
- A binding ships as its own packages in the repository's "Editor adapters" tier (root
  README) and mirrors the AvalonEdit split: a base package (documents, editing, margins,
  renderers) plus a language-features package (IntelliSense controllers, windows, skins).

## 2. What a binding reuses

The neutral packages are the reusable half of every feature. A binding consumes them
as-is; nothing below needs a re-extraction for a second toolkit.

### `Nickelony.IDEKit.Core` (dependency-free)

- **Text model** (`Nickelony.IDEKit.Core.Text`): `ITextSnapshot`, `ITextLine`,
  `StringTextSnapshot`, `TextRange`, `TextPosition`, `TextPositionRange`,
  `TextEditOperation`, `TextLineMap`, `TextLineSplitter`, `BacktickFenceTextNormalizer`.
- **Edit kernel and edit boundary** (`Nickelony.IDEKit.Core.Editing`): `TextEditKernel`,
  `TextEditInput`, `TextEditPreparationResult`, `TextEditPreparationIssue`,
  `TextEditPreparationIssueKind`, `PreparedTextEdits`, `ITextEditTarget`, `ITextEditTargetVersion`,
  `TextIncrementalEditCalculator`, `TextIncrementalEdit`, `TextRangeOffsetResolver`.
- **Parsing** (`Nickelony.IDEKit.Core.Parsing`): `IncrementalLineStateCache`.
- **Auto-closing** (`Nickelony.IDEKit.Core.AutoClosing`): `TextAutoClosingResolver` and
  its model - `TextAutoClosingOptions`, `TextAutoClosingPair`, `TextAutoClosingPairKind`,
  `TextAutoClosingAction`, `TextAutoClosingActionKind`, `TextAutoClosingProvenance`, and the
  applied-result record `TextAutoClosingResult`.
- **Line comments** (`Nickelony.IDEKit.Core.Comments`): `TextLineCommentPlanner` (the
  edit computation) with `TextLineCommentEdit` and `TextLineCommentAction`, plus the
  comment-scanning models (`CommentOperations`, `CommentSyntax`, `CommentSpan`,
  `CommentKind`, `CommentSpanEnumerator`, `StringLiteralStyle`, `ContinuationOperations`).
- **Diagnostics vocabulary** (`Nickelony.IDEKit.Core.Diagnostics`):
  `TextDiagnosticSegment`, `TextDiagnosticSeverity`.
- **Request coordination** (`Nickelony.IDEKit.Core.Requests`): `LatestRequestCoordinator`,
  `RequestTokenSource`, `RequestOutcome`.
- **Navigation identity** (`Nickelony.IDEKit.Core.Navigation`): `NavigationLocation`.
- **Line-status contracts** (`Nickelony.IDEKit.Core.LineStatus`): `ILineStatusSource`;
  change notifications (`Nickelony.IDEKit.Core.Notifications`): `IChangeNotificationSource`.
- **Formatting** (`Nickelony.IDEKit.Core.Formatting`): `ITextDocumentFormatter`,
  `TrimTrailingWhitespaceFormatter`, `WhitespaceConverter`.
- **Indentation** (`Nickelony.IDEKit.Core.Indentation`): `IIndentationPolicy`,
  `IndentationContext`, `IndentationOperations`, `IndentationTextLine`.
- **Identifiers** (`Nickelony.IDEKit.Core.Identifiers`): `IdentifierOperations`,
  `IdentifierCharacterPolicy`, `IdentifierSpanMode`.
- **Bookmarks** (`Nickelony.IDEKit.Core.Bookmarks`): `IBookmarkStore`, `BookmarkSidecarStore`.
- **Supporting primitives**: `LineDiffer` (`Core.Diffing`), `LocalPathComparisonPolicy`
  (`Core.Pathing`), `FindReplaceText`/`TextSearchQuery` (`Core.FindReplace`),
  `SidecarLineFile` (`Core.Persistence`), `ThemeCatalog<TTheme>` (`Core.Themes`).

### `Nickelony.IDEKit.IntelliSense` (Core-only)

- **Completion** (`...IntelliSense.Completion`): `ITextCompletionProvider`,
  `TextCompletionRequest`, `TextCompletionTrigger`, `TextCompletionItem`,
  `TextCompletionItemKind`, `TextCompletionTextEdit`, `TextCompletionInsertTextFormat`,
  `TextCompletionFilter`, `TextCompletionItemFilter`, `TextCompletionSessionDecision`,
  `TextCompletionWordSpan`, `TextCompletionWordLocator`, the shared
  `TextCompletionSessionKernel`, `TextSnippetExpander` with `TextSnippetExpansion` and
  `TextSnippetPlaceholder`, and the host-state record `TextCompletionRequestSession`.
- **Hover** (`...IntelliSense.Hover`): `ITextHoverProvider`, `TextHoverRequest`,
  `TextHoverInfo`, `TextHoverEvaluationState`; `TextMarkupKind` in the root namespace.
- **Signature help** (`...IntelliSense.Signatures`): `ITextSignatureHelpProvider`,
  `TextSignatureHelpRequest`, `TextSignatureHelpContext`,
  `TextSignatureHelpTriggerKind`, `TextSignatureHelp`, `TextSignatureInformation`,
  `TextSignatureParameterInfo`, `TextSignatureActiveParameterSelection`.
- **Definition navigation** (`...IntelliSense.Navigation`): `ITextDefinitionProvider`,
  `TextDefinitionRequest`, `TextDefinitionLocation`, `TextDefinitionDiscriminator`,
  `TextDefinitionNavigator`, `TextDefinitionNavigationTarget`.
- **Diagnostics** (`...IntelliSense.Diagnostics`): `ITextDiagnosticsProvider`,
  `TextDiagnosticsRequest`, `TextDiagnostic`, and the neutral projection
  `TextDiagnosticSegmentProjection` (`TextDiagnostic` -> `Core.Diagnostics.TextDiagnosticSegment`).
- **Code actions** (`...IntelliSense.CodeActions`): `ITextCodeActionProvider`,
  `TextCodeActionRequest`, `TextCodeActionItem`.
- **Document symbols** (`...IntelliSense.DocumentSymbols`): `TextDocumentSymbol`,
  `TextDocumentSymbolKind`, `ITextDocumentSymbolProvider`, `TextDocumentSymbolRequest`,
  `DocumentSymbolOutlineBuilder`, `DocumentSymbolProjection<TItem>`,
  `TextDocumentSymbolKindConversion`.
- **Semantic tokens** (`...IntelliSense.SemanticTokens`): `TextSemanticToken`,
  `TextSemanticTokenTypes`, `TextSemanticTokenModifiers`.

## 3. What every binding writes (AvalonEdit reference)

Each row names the piece a binding must ship itself, its AvalonEdit counterpart, and
the neutral types it composes. Presenters and schedulers are internal collaborators in
the reference implementation; a binding is free to arrange them the same way.

| Piece | AvalonEdit reference | Composes |
|---|---|---|
| Snapshot and document adapters | `TextDocumentSnapshot`, `TextDocumentExtensions`, `DocumentLineStateCache`, `DocumentVersionCache` (base) | `ITextSnapshot`, `ITextLine` (`Core.Text`); `LineDiffer` for cache work |
| Edit-target adapter | `TextEditorEditTarget` (base) | `ITextEditTarget`, `ITextEditTargetVersion` (`Core.Editing`) |
| Editing and line operations | `TextEditorEditOperations`, `TextEditorLineOperations` (base) | `Core.Text`/`Core.Editing` primitives as the operation needs them |
| Auto-closing input service | `TextAutoClosingService` with `ITextAutoClosingService` (base) | `TextAutoClosingResolver` and its model (`Core.AutoClosing`) |
| Line-comment service | `TextLineCommentService` with `ITextLineCommentService` (base) | `TextLineCommentPlanner`, `TextLineCommentEdit`, `TextLineCommentAction` (`Core.Comments`) |
| Formatting service and indentation bridge | `TextDocumentFormattingService` with `ITextDocumentFormattingService`, `PolicyIndentationStrategy` (base) | `ITextDocumentFormatter`, `TrimTrailingWhitespaceFormatter`, `WhitespaceConverter`, `IIndentationPolicy` |
| Navigation helpers | `TextAreaNavigationOperations`, `TextEditorNavigationOperations` (base) | `NavigationLocation` (`Core.Navigation`) |
| Definition-navigation glue | `TextAreaDefinitionNavigation` (language features) | `TextDefinitionNavigator` and the provider contracts (`IntelliSense.Navigation`) |
| Line-status margins and markers | `LineStatusMarginBase`, `ChangeMarkerMargin`, `UnsavedChangesTracker`, `BookmarkCoordinator`, `BookmarkMargin`, `IBookmarkSource` (base) | `ILineStatusSource` (`Core.LineStatus`), `IChangeNotificationSource` (`Core.Notifications`); `LineDiffer` for the tracker |
| Bookmark persistence | `BookmarkStoreExtensions` (base) | `IBookmarkStore`, `BookmarkSidecarStore` (`Core.Bookmarks`); `SidecarLineFile` (`Core.Persistence`) |
| Diagnostics renderer | `DiagnosticsRenderer` (base) | `TextDiagnosticSegment`, `TextDiagnosticSeverity` (`Core.Diagnostics`) |
| Diagnostic projection (glue) | `TextDiagnosticSegmentFactory` (language features) | `TextDiagnosticSegmentProjection` (`IntelliSense.Diagnostics`) projected onto `TextDiagnosticSegment` (`Core.Diagnostics`) |
| Text-run styling | `ITextRunStyle`, `TextRunStyle`, `TextRunStyleApplier` (base) | none - toolkit styling seam shared by the colorizers |
| Regex highlighting | `RegexHighlightingDefinition` adapter and `RegexHighlightingStyleExtensions` (base) | `RegexHighlightingRule`, `RegexHighlightingSpan`, `RegexHighlightingStyle`, `RegexHighlightingRuleSet` (`Core.Highlighting`) |
| Dispatcher utilities (internal) | `DispatcherDebouncer`, `DispatcherInvocation` (language-features infrastructure) | none - one pair per toolkit dispatcher |
| Completion controller family | `TextCompletionController`, `TextCompletionControllerHooks`, `TextCompletionControllerOptions`, `TextCompletionPresentationState`, `CompletionWindowCoordinator`, `CompletionWindowSkin`, `CompletionTooltipSkin`, `TextCompletionItemCompletionData`, `ICommitCharacterCompletionData` (language features) | `TextCompletionSessionKernel`, `TextCompletionItemFilter`, `TextCompletionWordLocator`, `TextSnippetExpander`, `TextCompletionRequestSession`, `LatestRequestCoordinator` |
| Hover controller | `TextHoverController`, `TextHoverControllerHooks` (language features) | `TextHoverEvaluationState`, `TextHoverRequest`, `TextHoverInfo` |
| Signature-help controller | `TextSignatureHelpController`, `TextSignatureHelpControllerHooks`, `TextSignatureHelpControllerOptions`, `TextSignatureHelpPresentationState` (language features) | the payload family (`TextSignatureHelp`, `TextSignatureInformation`, `TextSignatureParameterInfo`, `TextSignatureActiveParameterSelection`) |
| Code-action controller and menu | `TextCodeActionController`, `TextCodeActionControllerHooks`, `TextCodeActionControllerOptions`, `TextCodeActionMargin`, `TextCodeActionMenuSkin`, `TextCodeActionMenuOptions` (language features) | `ITextCodeActionProvider`, `TextCodeActionContext`, `TextCodeActionRequest`, `TextCodeActionItem` |
| Semantic-token colorizer | `SemanticTokensColorizer`, `ISemanticTokenStyleResolver` (language features) | `TextSemanticToken` payloads; `TextRunStyle` for painting |

## 4. Intentional per-binding pieces

These pieces belong to the binding tier rather than `Core`/`IntelliSense`: they are the toolkit-adjacent
surface a binding owns. Where such a piece is identical modulo the engine namespace it is single-sourced
into both bindings (section 5), and only the files sections 5.4 and 5.6 list stay forked. Port the shape,
not the code:

- **Framework-typed hooks** carry toolkit values (`Point`, `ContextMenu`, windows) -
  they are the host's own editor state exposed to a controller.
- **Skins and style resolution** bind colors, brushes, and metrics to one toolkit's
  rendering model (`CompletionWindowSkin`, `CompletionTooltipSkin`,
  `TextCodeActionMenuSkin`, and the colorizer's style resolver).
- **Presenters and window/popup mechanics** (`CompletionWindow*`, the tooltip pipeline,
  the code-action menu) are toolkit windowing, not language logic.
- **Dispatcher utilities** (`DispatcherDebouncer`, `DispatcherInvocation`) wrap one
  toolkit's dispatcher and thread-affinity rules.
- **Margins and renderers** (`LineStatusMarginBase`-derived margins,
  `DiagnosticsRenderer`) draw into one toolkit's visual layers (`IBackgroundRenderer`).
- **Input services** bind to one toolkit's event shapes (text-entering, preview key,
  mouse) - `TextAutoClosingService` is the reference.
- **Projection glue** such as `TextDiagnosticSegmentFactory` composes the neutral
  `TextDiagnosticSegmentProjection` (`IntelliSense.Diagnostics`) into a toolkit renderer; only the
  renderer construction is toolkit-adjacent.
- **Text-area glue** such as `TextAreaDefinitionNavigation` and `TextEditorNavigationOperations`
  applies neutral results to a concrete editor surface.
- **`PolicyIndentationStrategy`** adapts the neutral `IIndentationPolicy` onto one
  toolkit's indentation seam.
- **`TextCompletionControllerOptions`** mixes request timing with window sizing; the
  sizing half is per-window, so the record stays in the binding tier, not `IntelliSense`
  (recorded decision). The sibling controller option records
  (`TextSignatureHelpControllerOptions`, `TextCodeActionControllerOptions`) and the
  presentation-state records (`TextCompletionPresentationState`,
  `TextSignatureHelpPresentationState`) belong to the binding tier as well; only contracts,
  payloads, and the shared session helpers stay in `IntelliSense`. The menu sizing records
  (`TextCodeActionMenuOptions`) remain binding presentation.

## 5. Sharing source and documentation between bindings

Two bindings must not carry two copies of the same file, and they must not carry two copies of the
same documentation. The mechanism already exists: a file under `shared/` is compiled into every
package that needs it through a `<Compile Include ... Link>` item, the convention `NumericValidation.cs`
documents. This section fixes when that mechanism applies and how a shared file is shaped.

### 5.1 The three levers

| Lever | Mechanism | Use when |
|---|---|---|
| **A. Move down a tier** | The type exists once, in `Core`/`IntelliSense` | The type uses no engine type at all |
| **B. Share the source** | One file under `shared/Editor/`, linked into both bindings, with an `#if` header that selects the namespace and the engine `using` | The body is identical modulo the engine namespace |
| **C. Share the doc text** | `<inheritdoc/>` on a contract member, or an `<include>` fragment for prose that cannot move with code | The code must stay per-binding but its prose repeats |

Prefer A, then B, then C. Lever A removes the duplicate type entirely; lever B removes the duplicate
file; lever C leaves two files and removes only the repeated text.

### 5.2 The shared-source shape (lever B)

A shared file starts with two `#if` blocks, one for the engine `using` and one for the file-scoped
namespace. The AvaloniaEdit mirror project defines `AVALONIAEDIT` in its `<PropertyGroup>`; every
other binding compiles the `#else` branch.

```csharp
#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif
using Nickelony.IDEKit.Core.Text;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Documents;
#else
namespace Nickelony.IDEKit.AvalonEdit.Documents;
#endif
```

Rules for a shared file:

- The body keeps **no** engine word in prose. Write "the editor's `<see cref="TextDocument"/>`", not
  "an AvalonEdit `TextDocument`": the `cref` resolves to each engine's own type when the XML is
  emitted, so the shared sentence stays accurate in both packages.
- A pure **type-name** difference is absorbed by a conditional alias in the same header, so the body
  reads one name in both builds: `using Brush = Avalonia.Media.IBrush;` in the `#if` branch and
  `using Brush = System.Windows.Media.Brush;` in the `#else` branch. The header is the single place that
  states the mapping, and a type whose name is already the same in both engines keeps its plain `using`.
- A difference that is a **call or a shape** rather than a name - a null-returning translation, a hosting
  check, a font-weight constant - stays at its call site behind a short `#if` block. Keep the total small;
  a file whose body needs `#if` scattered through it stays two siblings instead.
- A divergence that **repeats at several call sites** moves behind a per-binding *seam type* rather than a
  block at every site: an `internal static` type with the same members in both bindings - written twice,
  once per engine - is called once per site from the shared body. `CompletionTooltipHost` wraps WPF's
  instance tooltip members and Avalonia's attached properties this way, `TextCodeActionMenuHost` does the
  same for the menu placement, open/close, and focus APIs, and `TextCompletionHost` for the text-input
  stages, the completion-list click, and the dispatcher post; a seam that creates a subscription returns it
  as an `IDisposable`, so the shared body never names an engine event or delegate type. Reach for a seam
  when the same divergence occurs more than twice in one file; a single site stays a `#if`.
- A **doc comment cannot be split** by a preprocessor directive: Roslyn reports `CS1587` ("XML comment is
  not placed on a valid language element") because the halves stop attaching to the member. When only the
  documented exception depends on the engine's type shape, state the condition in one neutral sentence
  ("... is `<see langword="null"/>`, where the binding's typeface type is a reference type.") instead of
  two `#if` branches.
- `PublicAPI.Unshipped.txt` stays per-project. A shared file that declares a public type is listed
  under each binding's own namespace, and the analyzer proves the surface did not move.
- The link target mirrors the binding-relative path (`shared/Editor/Documents/X.cs` linked as
  `Documents\X.cs`), so the file lands in the same project folder in both packages.
- A file whose body genuinely diverges stays two siblings (`X.cs` / `X.Avalonia.cs`), the convention
  `BrushHelpers.cs` / `BrushHelpers.Avalonia.cs` already uses. Do not force a divergent file through
  `#if` branches; move the divergence to the engine-specific call and share the rest.

### 5.3 Sharing the documentation (lever C)

| Doc location | Engine-free type | Engine-typed, implements a neutral contract | Engine-typed, no contract |
|---|---|---|---|
| Type `<summary>`/`<remarks>` | Lives once in `Core`/`IntelliSense` | `/// <inheritdoc/>` inherits the contract's type doc | Lever B, or an `<include>` fragment |
| Interface member | n/a | `/// <inheritdoc/>` | n/a |
| Own member (ctor, static, internal) | Lever A | `<inheritdoc cref="Neutral.Member"/>` when a neutral twin exists | Lever B, or `<include>` |
| Engine-specific behaviour | n/a | Per-binding `<remarks>` after the `<inheritdoc/>` | Per-binding `<remarks>` |

Rules:

- **Never** cref into the reference binding from the mirror (`Nickelony.IDEKit.AvalonEdit...`). Only
  `Core`/`IntelliSense` members are valid `cref` targets from both bindings.
- `<inheritdoc/>` first, then a per-binding `<remarks>` for the divergence - the existing convention,
  already used over 500 times across `src`, `shared`, and `tests`.
- Measured behaviour (Roslyn, 2026-10-04): `<inheritdoc/>` satisfies CS1591 but is emitted into the
  XML **verbatim** as `<inheritdoc />`; documentation tools and the IDE expand it, the compiler does
  not. A single edit to the contract therefore changes both bindings' *rendered* docs, but not the
  literal XML text of the binding that inherits.
- `<include file=... path=.../>` **is** expanded by the compiler: the fragment text lands in the
  emitted XML of every package that includes it, and the path is relative to the including file. Put the
  fragments under `shared/docs/`, one file per mirrored type, one `<member name="...">` per doc block, and
  reference a block with `path="doc/members/member[@name='...']/*"`. The 2026-10-04 passes took the five
  forked pairs' documentation the whole way: a block both bindings share has exactly one copy, and the
  pairs' inline prose fell from 760 to 43 lines. Name the members so the intent reads: `X.Class` is a
  class `<summary>` (with its `<remarks>` when both bindings share the whole thing), `X` is a normal
  member's block, and `X.Remarks` holds the shared `<para>` children when one binding appends its own
  paragraph. When only some of a member's paragraphs are shared - because the rest are engine-only or
  reference a binding-only member - each shared `<para>` gets its own `<member>` keyed `X.Remarks.<Key>`
  (or `Type.Member.Remarks.<Key>`), so the shared paragraphs can be included one by one while the divergent
  ones stay inline; the forked Markdown facade uses this shape. A block with an engine-only tail keeps that
  tail **inline** - `<include .../>` followed by the
  binding's own `<para>` or `<exception>` - because `<include>` expands in place anywhere in the comment,
  including inside `<remarks>`. A block that differs only by engine wording ("WPF"/"Avalonia",
  "metadata"/"default value", "click"/"press") is neutralised and shared; one that documents a real
  engine difference (Avalonia's inherited-font-size and frozen-geometry notes, the WPF dispatcher-shutdown
  guard and `ArgumentException` blocks) stays inline, where it cannot drift. Two silent compiler traps: an
  `<include>` whose XPath is valid but matches nothing is left **verbatim** in the emitted XML with no
  warning and no CS1591, so the gate is to count `<include` in each built XML and require zero; and a
  `<param>` tag delivered through an `<include>` is not matched against a **record primary constructor**
  (CS1572), so document such parameters as `<remarks>` prose. The trade-off is IDE hover showing the
  `<include>` element and F12 not navigating into the fragment.
- Lever B beats lever C whenever it applies: a shared source file gives real text in both XML files
  and removes the duplicate code in the same move.

### 5.4 What stays per-binding, and why

A mirrored file is shared unless its divergence is real. The 7 pairs that stay per-binding divide into
two causes:

- **An engine drawing or measurement surface** (`BookmarkMargin`, `ChangeMarkerMargin`,
  `LineStatusIconMarginBase`, `LineStatusMarginBase`, `TextCodeActionMargin`): the body draws into or
  measures one engine's visual layers, and the geometry-figure, styled-property, and typeface-shape APIs
  differ rather than just their type names. Their `*Property` fields are a `DependencyProperty` on WPF and
  a `StyledProperty<T>` on Avalonia, so the declaration site, its invalidation metadata, and the figure
  geometry all differ; no seam is small enough to be worth writing twice.
- **An engine window or tooltip access model** (`CompletionWindowInterop`, `CompletionWindowTooltipAccess`):
  the code *is* the difference - one installs a Win32 activation hook and reflects a private tooltip field,
  the other is a deliberate no-op because the engine owns the popup and exposes no tooltip member at all.

The five drawing and measurement pairs share their documentation blocks through `shared/docs/` fragments
(lever C in section 5.3): every block such a pair holds in common has one copy, and only the engine-only
paragraphs and exceptions stay inline, so a forked pair carries each shared block once. The three
per-binding seam types (`CompletionTooltipHost`, `TextCodeActionMenuHost`, `TextCompletionHost`) share the
member blocks they hold in common the same way, and the forked Markdown facade, scroller, and emitter pair
share their common paragraphs and member blocks (section 5.6). Only the two window and tooltip access pairs
share no substantive block - each documents its own engine's access model, and only a one-line method
summary and a parameter name repeat verbatim - so their prose stays inline.

The residual is recorded as `BDS-01` in `future-backlog-2026-09-17.md`. The mirrored editor test suites
follow the same shape with their own residual, recorded as `BDS-04` in section 5.5.

Phase 1 of the sharing program (2026-10-04) shared the 45 files identical modulo the engine namespace;
phase 2 shared the 8 whose divergence was a type name, engine-neutral prose, or a handful of localized
call sites; phase 3 shared the 6 whose divergence was a type alias, a WPF-only guard, or one localized
geometry shape; phase 4 shared the 7 whose divergence was a localized call shape plus the two per-binding
seam types that replaced the repeated tooltip and menu call sites; phase 5 shared the last pair
(`TextCompletionController`) behind a third seam type, `TextCompletionHost`, and began extracting the
documentation blocks of the forked pairs into `shared/docs/` fragments, and phase 6 finished that
documentation share so each block a pair holds in common has one copy. Those six passes are why
`ITextRunStyle`, `TextRunStyle`, `ITextAutoClosingService`, `RegexHighlightingStyleExtensions`,
`BookmarkMarginTestHooks`, `TextCodeActionController`, `TextCodeActionMarginTestHooks`,
`TextCompletionItemCompletionData`, `TextHoverControllerHooks`, `CompletionTooltipSkin`,
`TextRunStyleApplier`, `CompletionWindowSizing`, `TextEditorNavigationOperations`, `DiagnosticsRenderer`,
`TextCodeActionMenuSkin`, `CompletionWindowSkin`, `TextHoverController`, `CompletionWindowCoordinator`,
`TextAutoClosingService`, `CompletionTooltipPresenter`, `TextCodeActionMenuPresenter`, and
`TextCompletionController` are no longer
listed above.

A seventh pass, the test-source-sharing wave (2026-10-04), applied the same lever-B shape to the mirrored
editor test suites and is described in section 5.5.

### 5.5 Sharing the test source

The mirrored editor test suites (`Nickelony.IDEKit.AvalonEdit.Tests` / `...AvaloniaEdit.Tests` and their
`LanguageFeatures` siblings) are de-duplicated the same way: one test body lives under
`shared/Editor.Tests/` (or `shared/Editor.LanguageFeatures.Tests/`) and both suites compile it. 71 of the
100 mirrored test pairs share one body this way, six more pairs share an abstract base and keep only a
thin per-binding subclass (section 5.5.2), and the remaining 23 keep one full file per binding
(section 5.5.1).

A test project cannot list the shared tree file by file the way the library projects do. A forgotten link
would not fail the build - it would silently drop a test from one binding - so each test project includes
the whole tree with one wildcard statement instead:

```xml
<Compile Include="..\..\..\..\shared\Editor.Tests\**\*.cs"
         Link="shared\Editor.Tests\%(RecursiveDir)%(Filename)%(Extension)" />
```

The two Avalonia test projects define `AVALONIAEDIT` exactly like the mirror libraries, and the shared body
opens with the same `#if AVALONIAEDIT` header (lever B, section 5.2). It carries three things the two test
suites cannot share directly:

- the binding's `using` set and file-scoped `namespace`;
- a `TestHost` alias (`WPFTestHost` / `AvaloniaTestHost`), so a call site reads `TestHost.CreateEditor(...)`
  in both builds;
- a `TestClassAttribute` / `STATestClassAttribute` alias that maps the WPF class attribute onto
  `AvaloniaTestClass` (which also runs the body on the Avalonia UI thread), so the attribute line and the
  test-body shape stay identical.

Only the WPF build carries the interactive-window category, wrapped at the class:

```csharp
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class ...
```

The engine proper noun is neutralised where it appears only in prose or a test name ("the AvalonEdit
behavior" becomes "the editor behavior"; `ApplyEdit_WithAvalonEditTarget_...` becomes
`ApplyEdit_WithEditorTarget_...`), so the two bodies compare equal on their real differences only. A
genuine API difference stays and becomes an `#if` region. When the difference is only a *call shape* - a
helper whose signature cannot be identical because one engine's priority type cannot be an optional
parameter - give both helpers the same overload set instead, so the shared call site needs no `#if`; the
dispatcher helper of section 5.5.1 is the worked example.

**A green build is not enough to trust a shared test.** The merge places `#if` regions by line position,
and a naive placement can reconstruct a *different* program for one binding than the file it replaced - it
still compiles, so nothing turns red. The gate is therefore the reconstruct-and-compare check
(`artifacts/verify-test-share.ps1`): it rebuilds both binding branches from each shared file and
byte-compares them against the originals, and a file that does not verify stays forked. Two candidates
failed the build and fourteen failed this check, and they are part of the residual below.

#### 5.5.1 The test forks

Twenty-nine mirrored file pairs stay per-binding. Six of them are the thin subclasses of the extracted
bases of section 5.5.2 - `Bookmarks/BookmarkMarginTests.cs`, `ChangeMarkers/ChangeMarkerMarginTests.cs`,
`Rendering/LineStatusMarginBaseTests.cs`, `Rendering/TextRunStyleApplierTests.cs`,
`Diagnostics/DiagnosticsRendererTests.cs`, `CodeActions/TextCodeActionValidationTests.cs` - and hold only the
class attribute, the hooks, and the scenarios whose bodies genuinely differ. The other twenty-three keep one
full file per binding, grouped by their dominant cause:

- **Styled-property and margin metadata** (a `DependencyProperty` with `FrameworkPropertyMetadata`,
  `AffectsMeasure`/`AffectsRender` flags, `Freeze`, and `InputHitTest` against the Avalonia styled-property
  shape): `CodeActions/TextCodeActionMarginTests.cs`.
- **The window and tooltip access model** (a Win32 activation hook and a reflected private tooltip field
  against a deliberate no-op, because the Avalonia engine owns the popup and exposes no tooltip member):
  `Completion/CompletionWindowInteropTests.cs`, `Completion/CompletionWindowInteropEffectTests.cs`,
  `Completion/CompletionWindowTooltipAccessTests.cs`, `Completion/CompletionTooltipPresenterTests.cs`,
  `Completion/TextCompletionControllerTooltipTests.cs`,
  `Completion/TextCompletionControllerTooltipTests.ReplacedWindow.cs`,
  `Completion/TextCompletionControllerTooltipTests.Skin.cs`,
  `Completion/TextCompletionControllerTooltipTests.Supersession.cs`,
  `Completion/TextCompletionControllerWindowTests.Host.cs`, `TestSupport/CompletionTestHost.cs`.
- **The dispatcher shape** (`DispatcherPriority` is an enum with default parameters on WPF and a struct with
  no usable default on Avalonia, and the current dispatcher is ambient on WPF and `UIThread` on Avalonia):
  `TestSupport/DispatcherTestUtils.cs`. The helper keeps one file per binding because its bodies call the two
  engines' dispatchers directly, but both helpers now expose the same call shape, so a shared test can name
  the priority without also naming the timeout and needs no `#if` (section 5.5).
- **The window and list model the dispatcher tests also pin**:
  `Completion/TextCompletionControllerWindowRefreshTests.cs` and
  `Completion/TextCompletionControllerWindowTests.Selection.cs` read the engine's list API
  (`ItemsSource`/`Items.Count` against `ItemCount`) and the mirror applies the best-match selection
  asynchronously, so one mirror scenario is `Assert.Inconclusive`; and
  `Completion/CompletionWindowCoordinatorTests.cs` reads the window chrome that the mirror holds on its
  completion list (`MaxHeight`, `BorderThickness`, `Background`, and a `ResizeMode`/`WindowStyle` the popup
  does not carry).
- **The pointer and input-event model** (`MouseEventArgs` over `Mouse.PrimaryDevice` and the ambient mouse
  position against the Avalonia pointer events, whose position cannot be synthesised in a test; and a text
  composition event against a text-input event): `Navigation/TextAreaNavigationOperationsTests.cs`,
  `Navigation/TextEditorNavigationOperationsTests.cs`, `Hover/TextHoverControllerTests.RequestLifecycle.cs`,
  `Completion/TextCompletionControllerCommitCharacterTests.cs`, `Editing/TextAutoClosingServiceTests.cs`,
  `Editing/NoDocumentBehaviorTests.cs`.
- **Window measurement and the remaining engine shape**: `Completion/CompletionWindowSizingTests.cs`,
  `Completion/TextCompletionControllerDisposalTests.cs`.

Two further files are one-sided rather than forked and are not counted here: `TestSupport/TestEnvironment.cs`
(disables the WPF UI Automation bridge and is WPF-only) and `TestSupport/AvaloniaTestHostTests.cs` (pins the
Avalonia headless host and is Avalonia-only).

The residual is `BDS-04` in `future-backlog-2026-09-17.md`, closed as a deliberate residual. Do not force
these through `#if`: the dispatcher helper's call shape is unified behind a seam (section 5.5), and a
behavioural pair whose bodies are the same
modulo the engine calls uses the shared abstract base of section 5.5.2. A normalized comparison of all 29
residual pairs finds none that is the same modulo the engine scaffolding - each pins a genuine API or
behavioural difference - so the residual is deliberate, not unfinished.

#### 5.5.2 The shared abstract base

When a mirrored pair's test bodies are the same but their engine calls differ, the pair does not need a
dozen interleaved `#if` regions. One shared abstract base holds every test body and the private test
doubles; each binding keeps a thin sealed subclass that supplies the class attribute and overrides a small
set of hooks. It is the `LineStatusMarginContractTests` idiom applied to a binding pair. Six pairs use it:

- `Rendering/LineStatusMarginBaseTestsBase.cs` - `RunDrawPass`, `GetArrangedWidth`, `PumpRenderFrame`, and a
  virtual `AssertArrangeInvalidated` (Avalonia reports the arrange-invalidation case inconclusive);
- `ChangeMarkers/ChangeMarkerMarginTestsBase.cs` - `GetArrangedWidth`, `CaptureMarkerBounds`;
- `Rendering/TextRunStyleApplierTestsBase.cs` - `GetFontFamilyName`, `CreateNamedTypeface`, and the abstract
  null-argument assertions (Avalonia's `Typeface` is a value type and cannot represent the null case);
- `Bookmarks/BookmarkMarginTestsBase.cs` - `ClickMarginRow`, `GetArrangedWidth`, `CaptureMarkerBounds`;
- `Diagnostics/DiagnosticsRendererTestsBase.cs` - `Render`, `CreateFrozenPen`, `AssertUnderlineGeometryShape`;
- `CodeActions/TextCodeActionValidationTestsBase.cs` - `ConfigureWorkerThread` (WPF needs the COM
  single-threaded apartment; Avalonia does not).

A scenario whose body genuinely differs stays as its own test in the subclass: a WPF
`FrameworkPropertyMetadata` flag read against an Avalonia behavioral check, an `InputHitTest` Avalonia
cannot express, or an Avalonia-only value-type case. The base carries only the bodies that are the same
modulo the hooks, and the header aliases (`Brush`, `FontWeights`/`FontStyles`/`FontStretches`, `Pen`,
`TestHost`) live in its `#if AVALONIAEDIT` header exactly as in a lever-B file.

Two rules keep an extraction honest:

- **Name a hook for the operation, not the engine.** `ClickMarginRow` (WPF raises a routed argument;
  Avalonia calls the margin's seam) reads the same at every call site where `RaiseEvent` and
  `TryToggleBookmarkAt` would not.
- **Verify by test-count parity and green suites.** `artifacts/verify-test-share.ps1` cannot check this
  shape - the shared file is not a reconstruction of either original - so the gate is that both suites keep
  the same test count and pass, and that every hook body is copied verbatim from the original.

A test that reads a dependency property's per-type metadata must force the owning type's static
registration first. The metadata override runs in the type's static constructor, and MSTest lists the
most-derived class's own tests before the base's, so a reordering can silently read the base metadata
instead of the override. Read the type's sample property, or create an instance, before the metadata read.

### 5.6 The optional-integration mirrors

The two optional-integration packages were ported on 2026-10-04 (plan
`__041026_avaloniaedit-markdown-textmate-plan.md`); the earlier "do not port" note in section 8 is
superseded. They follow the same levers, with new shared trees:

- `shared/Editor.TextMate/` - all 11 TextMate sources, one body compiled into both
  `Nickelony.IDEKit.AvalonEdit.TextMate` and `Nickelony.IDEKit.AvaloniaEdit.TextMate` behind the
  `AVALONIAEDIT` header; the only divergences are the toolkit `Brush`/`Typeface` aliases and a localized
  dispatcher seam. `shared/Editor.TextMate.Tests/` shares the 12-file suite the same way, so both suites
  run one body.
- `shared/Editor.Markdown/` - the Markdig parse and the toolkit-neutral mapping. Because Avalonia has no
  `FlowDocument`, the Markdown package is **not** a like-for-like port: the shared half is an **internal
  neutral document model** (`MarkdownDocumentModel` and its node records) plus the walk that produces it
  (`MarkdownDocumentModelBuilder`), the shared options record (`MarkdownRenderOptions`), the render theme
  (`MarkdownRenderTheme`), the derived-brush cache (`MarkdownDerivedBrushCache`), the code-block factory
  (`MarkdownCodeBlockFactory`), and the neutral helpers (`MarkdownCodeText`, `MarkdownCodeLayout`,
  `MarkdownHighlightingResolver`, `MarkdownHeadingSizes`). Each binding keeps a **toolkit emitter** over the
  model (`MarkdownFlowDocumentEmitter` for WPF, `MarkdownContentEmitter` for Avalonia) plus its scroll
  chaining and the renderer facade; the theme, brush cache, and code-block factory are shared behind a
  per-binding brush alias and a handful of `#if` sites. The model stays internal, so neither binding's public
  surface doubles, and the mapping decisions live in the builder rather than in either emitter. The facade
  and the scroller keep forked code - their public surface and mechanism genuinely differ - but share their
  common prose through `shared/docs/` fragments (`MarkdownRenderer.xml`, `MarkdownScrollChaining.xml`,
  `MarkdownEmitter.xml`).
- `shared/Editor.Markdown.Tests/` - the toolkit-free tests (model mapping, options, theme numbers, the
  neutral helpers, and the highlighting resolver) run unchanged in both suites. The render assertions stay
  per-binding because they walk each binding's own content model - the residual the plan anticipated, of
  the same kind section 5.5.1 records for the editor suites.

The lever-B prose rule bites hardest here: the shared Markdown files must not name a toolkit, so they say
"the editor's" and "the toolkit" rather than "WPF" or "AvalonEdit". A decision only the toolkit can make
(a font-size cap, a brush type, a control type) stays in the emitter; everything the two bindings would
decide the same way lives in the shared walk.

## 6. Adapter patterns to copy

### 6.1 Snapshot over the editor document

`TextDocumentSnapshot` implements the Core `ITextSnapshot`: it captures an immutable
document snapshot on the document's owner thread and stays readable from any thread.
Line metadata is materialized lazily from a full text copy on first line access - so
keystroke-rate code reads only `TextLength`/`GetCharAt` (the auto-closing resolver does
exactly that; the line-comment planner accepts the materialization because toggling is a
user-rate command). Copy both the shape and the cost discipline.

### 6.2 Edit target over the editor document

`TextEditorEditTarget` implements `ITextEditTarget` and its version contract. The
edit-target contract documented on `TextEditorEditOperations` is the pattern: a supplied
target must hold the same content as the editor document when the call is made and must
update the editor document before returning, because requested caret and selection
offsets are applied to the document clamped to its current length. When no target is
supplied, apply through the binding's own target over the document directly.

### 6.3 The resolve/apply split

Every per-keystroke or per-command transform follows the same shape: capture a snapshot,
call the neutral planner or resolver (`TextLineCommentPlanner`, `TextAutoClosingResolver`,
`ITextDocumentFormatter`), then apply the result through the edit target. The binding
keeps only input glue, read-only policy, document edits, and provenance tracking
(`TextLineCommentService` and `TextAutoClosingService` are the references). If a new
feature needs text-only logic that does not exist yet, add the planner to Core with this
shape instead of keeping logic in the binding.

### 6.4 Margin over a line-status source

A margin takes a Core `ILineStatusSource`, draws a marker for each one-based marked line
(scaling with the editor's font metrics), and subscribes to `IChangeNotificationSource`
when the source implements it to invalidate itself. `UnsavedChangesTracker` is the
reference source for unsaved-change lines (built on `LineDiffer`); the bookmark family
shows the same pattern with persistence through the Core `SidecarLineFile` writer (the
host supplies the sidecar extension).

### 6.5 Request lifecycle and dispatcher marshaling

Controllers run their request lifecycle on `LatestRequestCoordinator`: `RunAsync` awaits
work and publishes through a current-state check, or `BeginRequest`/`IsCurrent` admit
and validate a request the controller completes itself; admission returns a
`RequestHandle` that pairs the request's identifier with its cancellation token, so
the manual path never pairs one request's identifier with another request's token.
Debounce input in the binding
(`DispatcherDebouncer` pattern), marshal results to the UI thread explicitly
(`DispatcherInvocation` pattern), and expose state through the controller's state records
(`TextCompletionPresentationState`, `TextSignatureHelpPresentationState`); the hover
controller deliberately exposes no state and reports every decision through its single
display callback instead.

### 6.6 Signature parameter highlight

Signature help delivers pre-resolved parameter labels (`TextSignatureParameterInfo`), and
the provider contract only guarantees that a label is a plain-text fragment of
`TextSignatureInformation.Label`. A binding highlights the active parameter by locating
the parameter's label text inside the signature label. When a label cannot be located
(for example because the server omitted parameter text from the signature label, or
because the provider left the label blank), present the signature without an
active-parameter highlight instead of guessing a range. An empty label is never
locatable, so treat it as unresolved rather than matching it at position zero.

## 7. Suggested porting order and test layering

Dependency-respecting order (each step builds on the previous):

1. Snapshot and document adapters (section 6.1) - the foundation everything else reads.
2. Edit-target adapter and the editing/line operations (section 6.2).
3. Services that compose neutral planners: auto-closing, line comments, formatting,
   indentation bridge.
4. Navigation helpers over `NavigationLocation`, plus the mouse helpers.
5. Line-status margins, the unsaved-changes tracker, and the bookmark family with
   sidecar persistence.
6. Diagnostics renderer and the diagnostic projection glue.
7. Language-features controllers: completion first (window coordinator, skins, item
   bridge, hooks), then hover, signature help, code actions, definition navigation, and
   the semantic-token colorizer.
8. Host wiring: triggers, gestures, and popup precedence stay host policy - the
   repository's `Nickelony.KeyBindings` package (with
   `Nickelony.KeyBindings.Wpf` for WPF hosts and `Nickelony.KeyBindings.Avalonia` for
   Avalonia hosts, and its toolkit-free `DesktopKeyDisplayTextFormatter` for
   platform-convention shortcut text) or the
   host's own command code.

Test layering:

- The neutral logic is already covered in `tests/IDEKit/Nickelony.IDEKit.Core.Tests`
  (`AutoClosing`, `Comments`, `Requests`, ...) and `tests/IDEKit/Nickelony.IDEKit.IntelliSense.Tests`.
  A new binding does not re-test planners or kernels and never duplicates those suites.
- Binding suites test only the adapters and controllers: document application, edit
  targets, selection restoration, margin invalidation, and controller decisions.
  `tests/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit.Tests` and
  `tests/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests` are the references, and
  their `tests/IDEKit/AvaloniaEdit/Nickelony.IDEKit.AvaloniaEdit*.Tests` mirror is the second worked
  example.
- Pin the toolkit UI thread: the AvalonEdit suites use MSTest `[STATestClass]` because
  WPF controls need STA. The AvaloniaEdit mirror instead runs its bodies on the Avalonia UI thread through
  the shared `AvaloniaTestHost` (`Avalonia.Headless`), whose `[AvaloniaTestClass]` dispatches each test
  method; both are the way a binding pins its toolkit thread.
- Tests live in flat per-package namespaces; filter by type-name prefix, for example
  `--filter "FullyQualifiedName~TextLineComment"`.

## 8. Do not port

- The two optional-integration packages (`Nickelony.IDEKit.AvalonEdit.Markdown` and
  `Nickelony.IDEKit.AvalonEdit.TextMate`) were ported to their AvaloniaEdit mirrors on 2026-10-04
  (section 5.6); the earlier "stay untouched" decision is superseded. Everything else in this section
  still holds.
- Host-owned behavior: trigger gestures, popup precedence, workspace-edit application,
  and undo grouping are host code, not binding surface.
- The workspace family (`Nickelony.IDEKit.Workspace`, `Nickelony.IDEKit.Workspace.Views`)
  is independent of editor bindings; combine it with a binding in host code.
- Never fork neutral logic into a binding. Extend `Core`/`IntelliSense` with the
  resolve/apply split (section 6.3) so every binding benefits.
