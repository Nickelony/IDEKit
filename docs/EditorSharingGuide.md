# Editor sharing guide

The editor bindings keep one copy of every file, test, and documentation block they have in common: a file
under `shared/` is compiled into each package that needs it through a `<Compile>` item, and a test file that
two suites share lives under `tests/IDEKit/` the same way.

This guide says when a piece is shared that way, how a shared file is shaped, and which pieces stay
per-binding. The tier model and the pieces a binding reuses and writes are in the [editor binding
guide](EditorBindingGuide.md), and the [architecture guide](ArchitectureGuide.md) owns the reasons, in its
[one home for a rule](ArchitectureGuide.md#one-home-for-a-rule) and
[internals](ArchitectureGuide.md#internals) sections.

## 1. Levers for sharing

| Lever | Mechanism | Use when |
|---|---|---|
| **A. Move down a tier** | The type exists once, in a neutral package | The type uses no engine type at all |
| **B. Share the source** | One file under `shared/Editor/`, linked into both bindings, with an `#if` header that selects the namespace and the engine `using` | The body is identical modulo the engine namespace |
| **C. Share the doc text** | `<inheritdoc/>` on a contract member, or an `<include>` fragment for prose that cannot move with code | The code must stay per-binding but its prose repeats |

Prefer A, then B, then C. Lever A removes the duplicate type entirely; lever B removes the duplicate
file; lever C leaves both files and removes only the repeated text.

A type moves down a tier when it names no engine type. The [one-home
rule](ArchitectureGuide.md#one-home-for-a-rule) says when engine-free source stays linked instead.

Lever A extends to shared helpers:

- An engine-free type that needs a dependency `Core` must not take moves to a Core-adjacent package that can
  hold it; `Nickelony.IDEKit.Markdown` is the worked example
  ([tier model](EditorBindingGuide.md#1-the-tier-model-and-dependency-direction)).
- An engine-free helper that several packages need becomes public API of the lowest package they reference,
  with a direct test: `NumericValidation` is `Core.Validation`, `TextMatchProbe` is `Core.Text`,
  `CallbackContainment` is `Core.Callbacks`, `ActionSubscription` is `Core.Notifications`, and `DispatcherDebouncer` and
  `StopGate` are `Core.Threading`.
- A Core primitive that a standalone family needs as well keeps its public type in Core and compiles from
  `shared/Primitives/`.
- The `#if QUICKPICKFAMILY` header of such a primitive selects the slice namespace and `public` for Core, and the
  flat `Nickelony.QuickPick` namespace and `internal` for the family; the tests of Core cover the one body
  ([one home for a rule](ArchitectureGuide.md#one-home-for-a-rule)).
- A helper that stays linked source across packages lives in an internal-helper home of `shared/`: the
  local-path rules in `shared/Infrastructure/Paths/` are the worked example.
- A helper that only the language-features bindings use, such as `CompletionWindowSizing`, is editor source of its
  slice: it sits in `shared/Editor.LanguageFeatures/Completion/` and declares the binding namespace in its `#if`
  header.
- Every helper that stays linked has a direct test in an assembly that compiles it.
- An `internal` helper in an internal-helper home declares the flat `Nickelony.Infrastructure` namespace in
  every package that compiles it, so it has one identity instead of a folder-derived one. An internal helper
  that belongs to one editor feature takes that feature's binding namespace through an `#if` header instead
  (`MarkedLineWalker` in `...LineStatus`).
- A **public** type takes its feature's binding namespace through an `#if` header, so the type compiled
  into both bindings does not collide (`CS0433`) for a consumer that references both; `UiDispatcher` in
  `...Threading` is the worked example.

Placement rules a new binding follows:

- `IBookmarkSource` is a `Core.Bookmarks` contract, because it names only `Core` types.
- The engine-free completion contracts and the `RegexHighlightingDefinitionOptions` record are neutral types
  (lever A), so a binding declares neither and their surface stays symmetric in both builds. A binding that
  maps items to custom completion data implements the completion contracts, and its regex-highlighting
  factory takes the Core options record.
- The Markdown content model lives in the `Nickelony.IDEKit.Markdown` project as public API, not in
  `shared/Editor.Markdown/` source compiled twice, and so do `MarkdownRenderTheme` and
  `MarkdownRenderOptions`, because neither names an engine type. The theme carries a neutral
  `MarkdownFlowDirection`, and the one engine-typed callback, the code-block highlighting installer, is an
  argument of the renderer entry points.
- The render planner and the plan nodes it builds are public API of that package as well, because the emitters of
  both bindings read them; the block-layout metrics stay internal to the package, so a visual tweak is not a
  contract.
- The block-text normalization stays inside the model package and reaches the code-block factory through
  `MarkdownCodeBlock.NormalizedCode`.
- The shared Markdown binding tree holds only the files that name an engine type, because each Markdown `.csproj`
  links the whole tree with one wildcard.
- Test hooks stay in their own `*TestHooks.cs` file, for example `WorkspaceDocumentStoreTestHooks`, even
  when the type has a single member; do not fold them into the shipping type's file.
- A hook marks an interleaving point only. State that a test reads is an `internal` property of its owner
  (`CompletionPresenter.IsTooltipUpdatePending`), a dependency that a test replaces is a constructor
  parameter (the show action of `CompletionWindowPresenter`), and a click that a test cannot position goes
  through `LineStatusIconMarginBase.TryHandleClickAt`, which the press handler calls too.
- Shared controller code holds the dispatcher only as the neutral `IUiDispatcher` and debounces through
  `DispatcherDebouncer` of `Core.Threading`. A controller that serves a text area, or the factory of a neutral
  one, adapts the text area's dispatcher once, with `UiDispatcher.From`, so no controller field or parameter
  names a toolkit dispatcher type.
- `BrushAndPenFactory` is a fork per base binding (step 7 of section 2): its body diverges throughout, so each
  base binding project keeps its own `Media/BrushAndPenFactory.cs` with the same member names.
- The feature bindings cannot see `BrushAndPenFactory`, so they build their brushes with the public `ToBrush`
  conversion of `ArgbColor` in the base binding's `Media` namespace.
- `UiDispatcher` is a single shared file, not a sibling pair: the engine `using`, `FromCurrent`, `Post`, the
  timer constructor, and the task of `InvokeCoreAsync` are the only engine-specific sites, so they sit behind
  `#if AVALONIAEDIT` and both bindings link the one file.
- `TextEditorNavigationExtensions.TryMoveCaretToMousePosition` has a different signature in each binding, so each
  binding keeps its overload in an own file, a `partial` half of the shared static class, rather than behind
  `#if AVALONIAEDIT` in the shared file. WPF reads the ambient mouse position; Avalonia exposes none, so its
  overload takes the `PointerEventArgs` that carries the position.

## 2. The shared-source shape (lever B)

A shared file opens with an `#if` block for the engine `using` and one for the file-scoped namespace. A binding
project states its engine once, as `<EditorEngine>Wpf</EditorEngine>` or `<EditorEngine>Avalonia</EditorEngine>`, and
`Directory.Build.targets`, which MSBuild imports after the project body, derives the rest from it: it defines
`AVALONIAEDIT` for an Avalonia project, so the AvalonEdit projects compile the `#else` branch.

```csharp
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Text;
#else
namespace Nickelony.IDEKit.AvalonEdit.Text;
#endif
```

The key-binding adapters use the same header with their own symbol: `Nickelony.KeyBindings.Avalonia` defines
`AVALONIAKEYBINDINGS`, which selects the toolkit `using` and the namespace of a file under `shared/KeyBindings/`
(`Nickelony.KeyBindings.Wpf` compiles the `#else` branch). The quick-pick windows do the same with
`AVALONIAQUICKPICK`, which `Nickelony.QuickPick.Avalonia` defines: `shared/QuickPick/` holds the one source of the
window that both toolkits compile, `Nickelony.QuickPick.Wpf` compiles the `#else` branch, and neither project states
an editor engine, because the family references no editor.

The header is one of several mechanisms. Take the first of these that fits, in this order:

1. Only the engine namespace and the product namespace differ: a header alias. The `#if AVALONIAEDIT` header
   holds the engine `using`, the aliases and the file-scoped namespace.
2. One or two isolated differences: a body `#if`. A site is at most 8 lines, and a file holds at most 15 lines
   of body `#if` in all; `StructureReportTests` lists the files past 15.
3. The same divergence repeats (more than twice in one file), or the engine code needs no private state of the
   shared type, has the same members in both engines and is called only from shared code: a seam, an
   internal static `{Feature}Seam` per binding.
4. The engine code needs the shared type's private state: a partial half, an own file in each binding
   project, never under `shared/`.
5. Some members read the same in both engines, in a type whose other members diverge: a shared partial half,
   `X.Common.cs` under `shared/`, beside the engine half that each binding keeps as `X.cs` in its project.
6. Several test assemblies of one engine need a helper: a twin pair in a `TestSupport` folder (`X.Wpf.cs` and
   `X.Avalonia.cs`), which each test project links by name. A helper that more than one family needs has its twins
   in the `Wpf/` and `Avalonia/` subfolders of `tests/TestSupport`. A type named for its engine takes a prefix
   (`WpfTestHost`, `AvaloniaTestHost`).
7. The whole type diverges: a fork per binding, with the parity test guarding the public surface.
8. Real platform interop exists in one engine only: `...Interop` in that binding only, called from that
   binding's seam.

Rules for a shared file:

- The body keeps **no** engine word in prose. Write "the editor's `<see cref="TextDocument"/>`", not
  "an AvalonEdit `TextDocument`": the header's `using` resolves the `cref` to each engine's own type, so the
  sentence stays accurate in each package.
- A pure **type-name** difference is absorbed by a conditional alias in the same header, so the body
  reads one name in both builds: `using Brush = Avalonia.Media.IBrush;` in the `#if` branch and
  `using Brush = System.Windows.Media.Brush;` in the `#else` branch.
- The header is the single place that states the mapping; a type whose name is already the same in both engines
  keeps its plain `using`.
- In the body, name an engine type through the header's `using` or alias, never qualified from the engine
  root, because the product namespaces `Nickelony.IDEKit.AvaloniaEdit` and `Nickelony.KeyBindings.Avalonia`
  shadow the engine roots `AvaloniaEdit` and `Avalonia`.
- A difference in a **call or a shape**, not a name (a null-returning translation, a hosting check, a
  font-weight constant), stays at its call site behind a body `#if` (step 2 above). A file whose body needs
  `#if` scattered through it past that bound is a fork per binding instead (step 7).
- A divergence that **repeats at several call sites** moves behind a per-binding seam rather than a
  block at every site.
- A *seam* is an `internal static` class with the same members in each binding, written twice (once per engine)
  and called only from shared code; this section is the one home of the term.
- `CompletionSeam` is the worked example: it wraps the engines' text-input stages, list-box click events and
  window construction. Every other seam is named `{Feature}Seam` and sits in each binding project beside the shared
  code it serves.
- A seam that creates a subscription returns it as an `IDisposable`, so the shared body never names an
  engine event or delegate type.
- Reach for a seam when the same divergence occurs more than twice in one file or one site outgrows the 8-line
  bound of step 2; a smaller single site stays a `#if`.
- A **public member that only one engine has** goes into a partial half of its type, as an own file in that
  binding.
- A sealed options or hooks record cannot take a partial property before C# 13, so it declares the
  member behind `#if !AVALONIAEDIT` in the shared file (`CompletionPresentationOptions.TooltipTheme`,
  `CompletionPresenterHooks.ConfigureTooltip`), and the binding parity allow-list names it with its reason.
- Such a declaration-level block is outside the 15-line measure of step 2, which counts member bodies.
- A **doc comment cannot be split** by a preprocessor directive: Roslyn reports `CS1587` ("XML comment is
  not placed on a valid language element") because the halves stop attaching to the member.
- When only the documented exception depends on the engine's type shape, state the condition in one neutral
  sentence ("... is `<see langword="null"/>`, where the binding's typeface type is a reference type.")
  instead of two `#if` branches.
- `PublicAPI.Unshipped.txt` stays per-project. A shared file that declares a public type is listed
  under each binding's own namespace, and the analyzer proves the surface did not move.
- A binding links a shared home with one wildcard item, so a new file of the home compiles without a project
  edit and a dropped file cannot hide in a hand-kept list:

  ```xml
  <Compile Include="..\..\..\..\shared\Editor\**\*.cs" LinkBase="." />
  ```

  `LinkBase="."` keeps the folder below the home, so `shared/Editor/Text/X.cs` lands in the project's
  `Text\` folder beside the project's own files of that folder. A file of another home keeps an explicit item
  with its own `Link`, so the compile items of a project are its wildcard and its few cross-home links. The
  comment above them names the shared homes whose folders merge into the project's, so a reader who finds half
  a feature knows where the rest is.
- A file whose body genuinely diverges is a fork: each binding project keeps its own file of the same name,
  as `Media/BrushAndPenFactory.cs` does. Do not force a divergent file through `#if` branches; move the
  divergence to the engine-specific call and share the rest.

## 3. Sharing the documentation (lever C)

| Doc location | Engine-free type | Engine-typed, implements a neutral contract | Engine-typed, no contract |
|---|---|---|---|
| Type `<summary>`/`<remarks>` | Lives once in `Core`/`LanguageFeatures` | `/// <inheritdoc/>` inherits the contract's type doc | Lever B, or an `<include>` fragment |
| Interface member | n/a | `/// <inheritdoc/>` | n/a |
| Own member (ctor, static, internal) | Lever A | `<inheritdoc cref="Neutral.Member"/>` when a neutral twin exists | Lever B, or `<include>` |
| Engine-specific behavior | n/a | Per-binding `<remarks>` after the `<inheritdoc/>` | Per-binding `<remarks>` |

Rules:

- **Never** cref from one binding into the other (`Nickelony.IDEKit.AvalonEdit...` from AvaloniaEdit, or the
  reverse). Only `Core`/`LanguageFeatures` members are valid `cref` targets from both bindings.
- `<inheritdoc/>` first, then a per-binding `<remarks>` for the divergence - the existing convention.
- Measured behavior (Roslyn): `<inheritdoc/>` satisfies CS1591 but is emitted into the
  XML **verbatim** as `<inheritdoc />`; documentation tools and the IDE expand it, the compiler does
  not. A single edit to the contract therefore changes both bindings' *rendered* docs, but not the
  literal XML text of the binding that inherits.
- `<include file=... path=.../>` **is** expanded by the compiler: the fragment text lands in the emitted
  XML of every package that includes it, and the path is relative to the including file.
- Put the fragments under `shared/docs/`, one file per mirrored type, one `<member name="...">` per doc
  block, and reference a block with `path="doc/members/member[@name='...']/*"`. A block both bindings share
  has exactly one copy.
- Name the members so the intent reads: `X.Class` is a class `<summary>` (with its `<remarks>` when both
  bindings share the whole thing), `X` is a normal member's block, and `X.Remarks` holds the shared `<para>`
  children when one binding appends its own paragraph.
- When only some of a member's paragraphs are shared, because the rest are engine-only or reference a
  binding-only member, give each shared `<para>` its own `<member>` keyed `X.Remarks.<Key>` (or
  `Type.Member.Remarks.<Key>`). The shared paragraphs can then be included one by one, and the divergent ones
  stay inline. The forked Markdown facade uses this shape.
- A block with an engine-only tail keeps that tail **inline** - `<include .../>` followed by the binding's
  own `<para>` or `<exception>` - because `<include>` expands in place anywhere in the comment, including
  inside `<remarks>`.
- A block that differs only by engine wording ("WPF"/"Avalonia", "metadata"/"default value",
  "click"/"press") is neutralized and shared; one that documents a real engine difference (Avalonia's
  inherited-font-size and frozen-geometry notes, the WPF dispatcher-shutdown guard and `ArgumentException`
  blocks) stays inline, where it cannot drift.
- A silent compiler trap: an `<include>` whose XPath is valid but matches nothing is left **verbatim** in
  the emitted XML with no warning and no CS1591. No build step or test counts these, so after changing a
  fragment, search each built XML file for `<include` by hand and expect no match.
- A `<param>` tag delivered through an `<include>` is not matched against a **record primary constructor**
  (CS1572), so document such parameters as `<remarks>` prose.
- The trade-off is IDE hover showing the `<include>` element and F12 not navigating into the fragment.
- Lever B beats lever C whenever it applies: a shared source file gives real text in both XML files
  and removes the duplicate code in the same move.

The hand-maintained Markdown documents (for example the base `README.md` files) have no include mechanism,
so the halves of a pair stay separate files. Keep such a pair in lockstep by drift reconciliation: the
common body reads identically modulo the engine name, and only the intentionally per-binding sections (for
example the tooltip bullets in the LanguageFeatures README `Remarks` section) differ.

## 4. What stays per-binding, and why

A mirrored file is shared unless its divergence is real. The pairs that stay per-binding have distinct
causes:

- **An engine drawing or measurement surface** (the engine half of `BookmarkMargin`, `ChangeMarkerMargin`,
  `LineStatusIconMarginBase` and `LineStatusMarginBase`, and the whole of `CodeActionMargin`): the body draws
  into or measures one engine's visual layers, and the geometry-figure, styled-property, and typeface-shape
  APIs differ beyond their type names.
- Their `*Property` fields are a `DependencyProperty` on WPF and a `StyledProperty<T>` on Avalonia, so the
  declaration site, its invalidation metadata, and the figure geometry all differ. Whatever reads the same in
  both engines sits in a shared partial half, `X.Common.cs`, in the margin's folder under `shared/Editor/`.
- **An engine window access model** (`CompletionSeam`): the code *is* the difference - WPF installs a Win32
  activation hook through `CompletionWindowInterop`, while Avalonia does nothing for activation, because the
  engine owns the popup.
- **The tooltip pipeline is not shared at all**: `CompletionTooltipResolver` and the presenter live in the WPF
  binding only, because AvaloniaEdit exposes no tooltip member.

The engine-typed **public** members that stay on purpose are the engine-typed themes (for example
`CompletionWindowTheme`), the margin `*Property` fields, the diagnostics renderer, the Markdown facade, and
the framework-typed host hooks; none can be re-shaped neutrally without losing the host-facing capability it
exists for.

The drawing and measurement pairs share their documentation blocks through `shared/docs/` fragments
(lever C in section 3): every block such a pair holds in common has one copy, and only the engine-only
paragraphs and exceptions stay inline, so a forked pair carries each shared block once.

The per-binding seam types (for example `CompletionSeam`) share their common member blocks the same
way, and the forked Markdown facade, scroller, and emitter pair share their common paragraphs and member
blocks (section 6).

The class remarks of the window seams stay inline, because each documents its own engine's access
model and the two share no substantive block.

An engine-neutral body that needs only a collaborator or a call-shape adapter rather than an `#if` region is
shared too:

- `LineStatusMarginBase`'s source-subscription and coalesced-invalidation state machine, its marker walk and
  its icon placement rules name no engine type, so the [one-home
  rule](ArchitectureGuide.md#one-home-for-a-rule) decides where each lives. Each twin keeps the engine-bound
  parts, such as its property registration, the font-size and arranged-width reads, dispatcher binding, and
  paint entry point.
- `DocumentFormatterService` takes the new caret and selection from the public Core
  `EditorSelectionMapper`, and `TextAreaExtensions.GetWordFromOffset` takes its word from
  `IdentifierOperations.FindTokenSpan` in `IdentifierSpanMode.AtOffset`. The binding captures the editor
  state and applies the result.
- The Markdown emitters' layout decisions are one render plan: `MarkdownRenderPlanner` builds it from the content
  model in `Nickelony.IDEKit.Markdown`, and each emitter only creates toolkit elements from its nodes.
- The Markdown block-layout metrics are internal to `Nickelony.IDEKit.Markdown`; the plan carries the values
  an emitter needs.

Inside a shared file, an operation with several entry points keeps one body. The asynchronous
`TextAreaNavigationExtensions` entry points pass their resolver and cross-file hook to the private
`NavigateToDefinitionAsync`, which owns the snapshot capture and the applied target, and the pointer gesture calls
`GoToDefinitionAsync`. Route a new entry point through that body rather than repeating its steps.

What stays forked is what the levers cannot share: the head-to-head `*Property` declarations and font
metadata, the figure geometry, and the element emit and the engine block factory calls.

The mirrored editor test suites follow the same shape (section 5).

## 5. Sharing the test source

The mirrored editor test suites (`Nickelony.IDEKit.AvalonEdit.Tests` / `...AvaloniaEdit.Tests` and their
`LanguageFeatures` siblings) share their bodies the same way: one test body lives under
`tests/IDEKit/Shared/Editor.Tests/` or `tests/IDEKit/Shared/Editor.LanguageFeatures.Tests/` and both suites
compile it. The helpers that these suites link live in `tests/IDEKit/TestSupport/` and in the neutral
`tests/TestSupport/`, and each project links them into its virtual `TestSupport\` folder.

Some mirrored pairs share an abstract base and keep only a thin per-binding subclass (section 5.2); some
keep one full file per binding and some files are one-sided (section 5.1).

A test project cannot list the shared tree file by file the way the library projects do. A forgotten link
would not fail the build - it would silently drop a test from one binding - so each test project includes
the whole tree with one wildcard statement instead. `LinkBase="."` shows each file in the project folder of
the same relative path, so a per-binding subclass sits beside its shared base:

```xml
<Compile Include="..\..\Shared\Editor.Tests\**\*.cs" LinkBase="." />
```

The AvaloniaEdit test projects state `<EditorEngine>Avalonia</EditorEngine>` exactly like the mirror libraries, which
defines `AVALONIAEDIT` and adds the Avalonia test packages, and the shared body opens with the same `#if AVALONIAEDIT`
header (lever B, section 2). The header carries the binding's `using`
set and file-scoped `namespace`. What the test suites cannot share directly lives in the project file, as
`Using` items with an `Alias`, so every file of the project reads the same way:

- a `TestHost` alias (`WpfTestHost` / `AvaloniaTestHost`), so a call site reads `TestHost.CreateEditor(...)`
  in both builds;
- a `TestClassAttribute` / `STATestClassAttribute` alias in the Avalonia projects that maps the WPF class
  attribute onto `AvaloniaTestClassAttribute`
  ([test layering](EditorBindingGuide.md#7-bring-up-order-and-test-layering)), so the attribute line and the
  test-body shape stay identical.

An alias of an engine type (`Brush`, `FontWeights`) stays in the header of the file that uses it.

Only the WPF build carries the interactive-window category, wrapped at the class:

```csharp
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class ...
```

A shared test names no engine in prose or in a test name: write "the editor behavior" and
`ApplyEdit_WithEditorTarget_...`, not "the AvalonEdit behavior" and `ApplyEdit_WithAvalonEditTarget_...`, so
the bodies differ only where the engines do. A genuine API difference stays as a localized `#if` region
until the file reaches the bound of section 2, step 2, and then it moves into a per-binding helper.

Where only a helper's *call shape* differs between the engines, the helper exposes one call shape in both
bindings, so the shared call site needs no `#if`. Give a helper for a new shared body the same treatment
rather than adding a region at each call:

- `TestHost.TranslatePointTo` unwraps the nullable result of Avalonia's `TranslatePoint`, so
  `Navigation/TextEditorNavigationExtensionsTests.cs` reads one non-nullable call.
- `EditingTestHelpers.CreateTextArgs` builds each engine's text-input arguments, and the shared editing
  tests name that one call.
- `CreateBackspaceArgs(hostWindow, modifiers)` and `CreateKeyArgs(hostWindow, key)` take the same arguments
  in both bindings, so the WPF `PresentationSource` and `FakeKeyboardDevice` plumbing stays inside the
  helper.
- `AvaloniaTestHost.PumpDispatcher` takes a nullable dispatcher and falls back to `Dispatcher.UIThread`,
  because an element's dispatcher is nullable on Avalonia, so a shared test passes an element's dispatcher
  directly.
- `DispatcherTestHelpers` has one overload set in both toolkits' twins, so a shared test can
  name the priority without naming the timeout (section 5.1 gives the cause).
- `CompletionWindowTestHelpers` and `CodeActionTestHelpers` are declared in the `TestSupport/` folder of each
  binding test project under one type name and one member set. A setup, an assertion or an event that the
  engines spell differently lives there once, and the shared test calls it without an `#if`.
- `TestThreads.CaptureFailure` runs an action on a dedicated thread in both bindings, because `Task.Run`
  can run inline on the headless Avalonia dispatcher thread, which leaves a thread-affinity check unexercised.

The presenter tests of the quick-pick windows share their body the same way, under
`tests/QuickPick/Shared/Presenter.Tests/`, which `Nickelony.QuickPick.Wpf.Tests` and
`Nickelony.QuickPick.Avalonia.Tests` compile with the `AVALONIAQUICKPICK` header.

The tests of the sample heads share their body under `tests/Samples/Shared/Samples.Tests/`, which
`Nickelony.Samples.Wpf.Tests` and `Nickelony.Samples.Avalonia.Tests` compile with the `AVALONIAEDIT` header. Each
project declares `SampleTestHost` in its `TestSupport\` folder with the members that differ by head: `RunAsync`,
which starts the dispatcher a test runs on (a headless session on Avalonia, a pumped frame on WPF), and
`CloseWindowAsync`.

The shared half, `SampleTestHost.Common.cs`, opens the window and the workbench and waits for a
condition. `SampleWindowTestHelpers` reads the parts of the window that the toolkit decides, such as the explorer
rows, the tab groups and the tool tips, through one member set in each project.

A shared body awaits only inside the body that `RunAsync` pumps, because the WPF test runs on its STA thread and a continuation outside that pump
resumes on the thread pool. A test that asserts the behavior of one toolkit stays in the project of that toolkit.

**The shared test trees are guarded.** The wildcard include lets a dropped tree fail silently, so a
`Repository` guard test checks that every test project that compiles a shared test tree includes it with one
wildcard and keeps each of its files, with no `Compile Remove` that drops one. A shared editor test type is
declared in the flat test-project namespace that its `#if` header selects.

**Check a merged pair by reconstruction, not by a green build.** `#if` regions are placed by line position,
and a misplaced region can produce a *different* program for one binding than the forked file it replaces -
it still compiles, so nothing turns red. When you merge a forked pair into one shared file, reconstruct each
binding's branch from the shared file and compare it with that binding's forked file; a mismatch keeps the
pair forked.

### 5.1 The test forks

A mirrored pair stays per-binding either as thin subclasses of a shared abstract base (section 5.2) or as
one full file per binding. A thin subclass, one file per fork such as `Bookmarks/BookmarkMarginTests.cs`,
holds only the class attribute, the hooks, and the scenarios whose bodies genuinely differ. The full-file
pairs group by their dominant cause:

- **The window access model** (a Win32 activation hook against a deliberate no-op, because the Avalonia engine
  owns the popup): the WPF-only window-interop tests.
- **The routed-event model** (a click raised on the item container against one raised on a non-visual inline
  through the list box): each binding's `CompletionPresenterWindowTests.Host.cs` partial completes the
  shared `CompletionPresenterWindowTests` class and keeps the host-composition and interaction branches.

The dispatcher helper is a twin pair, not a fork: `tests/TestSupport/Wpf/DispatcherTestHelpers.cs` and
`tests/TestSupport/Avalonia/DispatcherTestHelpers.cs` hold one overload set, and only the usings and the dispatcher
calls differ. Every binding test project that pumps links its twin, so the base suites and the language-feature
suites pump the same way. The pair needs no `#if` because its overload difference, a struct against an optional
parameter, is a shape that one overload set covers rather than a genuine API or behavioral difference.

Further one-sided files (not forks) exist. The base editor suites hold a WPF-only test environment that disables
the UI Automation bridge, and an Avalonia-only headless-host test. The language-features suites hold the
AvalonEdit-only tooltip tests, which pin the reflected WPF tooltip member.

The test trees are the source of truth for the full set.

A fork is valid only when the difference is a genuine API or behavioral one - do not force such a pair
through `#if`. A pair whose bodies are the same modulo the engine calls uses the shared abstract base of
section 5.2.

### 5.2 The shared abstract base

When a mirrored pair's test bodies are the same but their engine calls differ, the pair does not need
interleaved `#if` regions. One shared abstract base holds every test body and the private test doubles;
each binding keeps a thin sealed subclass that supplies the class attribute and overrides a small set of
hooks. It is the `LineStatusMarginContractTestsBase` idiom applied to a binding pair.

Each base follows the same shape (one per fork, for example `LineStatus/LineStatusMarginBaseTestsBase.cs`):
the bodies that are the same modulo the hooks, plus the operation-named hooks the twins override - a draw
pass (`RunDrawPass`), an arrange-width read, marker-bounds capture, and the engine-specific assertions.

The engine-divergence facts the hooks encode stay load-bearing: Avalonia reports the arrange-invalidation
case inconclusive, its `Typeface` is a value type that cannot represent the null case, and WPF needs the
COM single-threaded apartment where Avalonia does not.

A scenario whose body genuinely differs stays as its own test in the subclass: a WPF
`FrameworkPropertyMetadata` flag read against an Avalonia behavioral check, or an Avalonia-only value-type
case.

The base carries only the bodies that are the same modulo the hooks, and the header aliases (`Brush`,
`FontWeights`/`FontStyles`/`FontStretches`, `Pen`, `TestHost`) live in its `#if AVALONIAEDIT` header exactly
as in a lever-B file.

Rules that keep a shared base honest:

- **Name a hook for the operation, not the engine.** `GetArrangedWidth` (WPF reads `ActualWidth`; Avalonia
  reads `Bounds.Width`) reads the same at every call site where the two property reads would not.
- **Check each hook against its fork.** Each hook body makes the same engine calls its forked test made.
- **Verify by test-count parity and green suites.** A base is not a line-for-line reconstruction of either
  fork, so the reconstruct-and-compare check of section 5 does not apply. Both suites keep the same test
  count and pass.

A test that reads a dependency property's per-type metadata must force the owning type's static
registration first. The metadata override runs in the type's static constructor, and MSTest lists the
most-derived class's own tests before the base's, so a reordering can silently read the base metadata
instead of the override. Read the type's sample property, or create an instance, before the metadata read.

## 6. The optional-integration mirrors

The optional-integration packages follow the same levers, with new shared trees:

- `Nickelony.IDEKit.TextMate` holds the TextMate theme model and the scope-ranking rule engine with colors as
  `ArgbColor` (lever A), so the ranking exists once and each binding converts the resolved color and traits to
  its brush, typeface, and text decorations.
- `shared/Editor.TextMate/` holds the TextMate sources that name an engine type, one body compiled into both
  `Nickelony.IDEKit.AvalonEdit.TextMate` and `Nickelony.IDEKit.AvaloniaEdit.TextMate` behind the
  `AVALONIAEDIT` header; its divergences are the toolkit `Brush`/`Typeface` aliases, a localized dispatcher
  adapter, and the WPF freeze/clone of the shared decoration values.
- `tests/IDEKit/Shared/Editor.TextMate.Tests/` shares the suite the same way, so both suites run one body, and
  `Nickelony.IDEKit.TextMate.Tests/` runs the ranking, selector, and color rules once in the neutral project.
- `Nickelony.IDEKit.Markdown` holds the Markdig parse and the toolkit-neutral mapping.
- Avalonia has no `FlowDocument`, so the Markdown integration is **not** a like-for-like port: its neutral
  half is the public document model in `Nickelony.IDEKit.Markdown`, with the walk that produces it
  (`MarkdownDocumentModelBuilder`).
- The shared binding tree keeps the toolkit-typed files (the per-theme engine-resource cache, the code-block
  factory, the flow-direction, thickness and text-alignment conversions, and the highlighting installer
  delegate). The engine half of the code-block factory is an own file of each binding project.
- `MarkdownRenderPlanner` is public API of `Nickelony.IDEKit.Markdown`, compiled and tested once. It walks the
  content model once and builds a tree of plan nodes (`MarkdownBlockPlan` and `MarkdownInlinePlan`).
- The tree holds every decision the bindings would take the same way: which blocks and inlines render, the
  margins, the heading size and weight, the list markers and indent, which blocks lose their vertical spacing in a
  tight list or a table cell, the table cell structure, and which links are keyboard stops.
- The nodes carry numbers, strings and neutral enums only.
- Each binding keeps a **toolkit emitter** over the plan (`MarkdownFlowDocumentEmitter` for WPF,
  `MarkdownPanelEmitter` for Avalonia) plus its scroll chaining and the renderer facade.
- An emitter creates one element per node and resolves brushes and fonts from the theme, so it holds only what the
  toolkits do differently:
  - a `FlowDocument` block tree against a control tree;
  - a `Table` against a `Grid`;
  - an inline hyperlink against a link control;
  - the walk of a created table cell that applies column alignment through that toolkit's own nested containers.
- The emitters share their type remarks block. A plan node of an unknown kind throws, because the planner
  already leaves unknown model nodes out.
- The brush cache and the code-block factory are shared behind a per-binding brush alias and a handful of
  `#if` sites.
- The model is public and exists once, so neither binding's public surface doubles, and the mapping decisions
  live in the builder rather than in either emitter. The package grants its internals to its test project
  only, so a binding reads the model as any host does.
- The facade and the scroller keep forked code - their public surface and mechanism genuinely differ - but
  share their common prose through `shared/docs/` fragments (for example `MarkdownRenderer.xml`).
- The facade's common steps (line-terminator normalization, the whitespace test, the guarded render, and the
  plain-text fallback) run in the shared `MarkdownRenderPipeline`.
- `Nickelony.IDEKit.Markdown.Tests/` runs the toolkit-free tests (the model mapping, the block-text
  normalization, the render planner and its layout, and the theme and options) once in the neutral project.
- `tests/IDEKit/Shared/Editor.Markdown.Tests/` holds the tests of the toolkit-typed render helpers,
  and the viewer-level renderer, global-highlighting, and code-block-editor scenarios; both binding
  suites compile it.
- The same folder holds `MarkdownRendererTestsBase`, the shared abstract base of section 5.2 for
  the rendered-content scenarios: the content, theme, code-block, highlighting, hyperlink, and wheel-routing
  scenarios that both content models share are declared once.
- Each binding's `MarkdownRendererTests` answers probes that read its own content tree (a `FlowDocument` or a
  control tree) or raise its own input event into engine-neutral values.
- A scenario that reads one content model's own structure, such as a flow-document block, a routed hyperlink
  event or a control's pointer handler, stays in that binding's partial class - a split of the same kind section
  5.1 records for the editor suites.

The lever-B prose rule bites hardest here: the shared Markdown files must not name a toolkit, so they say
"the editor's" and "the toolkit" rather than "WPF" or "AvalonEdit". A decision only the toolkit can make
(a font-size cap, a brush type, a control type) stays in the emitter; everything the bindings would
decide the same way lives in the shared walk.
