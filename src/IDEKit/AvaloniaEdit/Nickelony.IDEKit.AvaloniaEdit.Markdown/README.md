# Nickelony.IDEKit.AvaloniaEdit.Markdown

Markdown rendering for AvaloniaEdit editors, built on the
**AvaloniaEdit** editor package and **Markdig**, with optional diagnostics through
`Microsoft.Extensions.Logging.Abstractions`. The package targets
`net8.0`, uses Avalonia, and lives in the single namespace
`Nickelony.IDEKit.AvaloniaEdit.Markdown`. It is the cross-platform counterpart of
`Nickelony.IDEKit.AvalonEdit.Markdown`: the Markdig parse and the toolkit-neutral mapping live once in the
shared tree (`shared/Editor.Markdown/`), and this package adds the Avalonia content emitter and the facade
over it. It ships a tooltip-shaped presentation by default, and the same rendering is available as a plain
block panel for hosts that present content in their own container:

- Rendering: CommonMark plus pipe tables (with GFM column alignment),
  strikethrough, and autolinks through a deliberately narrow Markdig
  pipeline; HTML entity references are decoded to their characters. Raw HTML
  (block and inline, for example `<br>`) is not interpreted and renders as
  literal text, so authored content is never dropped. Other
  extensions (footnotes, mathematics, definition lists, task lists, custom
  containers, and the subscript, superscript, marked, and inserted emphasis
  variants) are not enabled and render as literal text. An image whose alt
  text is empty renders a muted placeholder showing the image target instead
  of rendering as nothing. CommonMark soft line
  breaks render as spaces and hard breaks as line breaks.
- Presentation: Avalonia has no `FlowDocument`, so the neutral document model is
  emitted as a native Avalonia control tree: paragraphs, headings, quotes, lists,
  thematic breaks, and tables are `TextBlock`/`Border`/`StackPanel`/`Grid`
  controls, emphasis and inline code are document inlines, and a hyperlink is an
  inline control because Avalonia has no inline hyperlink. `CreateContent`
  returns tooltip-shaped content (passive by default: not focusable, not
  selectable, and code blocks suppress mouse selection and drag-and-drop);
  `AllowContentInteraction` opts the rendered content into text selection and
  keyboard-reachable hyperlinks, and `AllowCodeBlockSelection` opts code blocks
  into mouse selection and drag-and-drop; `CreateContentTree` returns the block
  panel for hosts that want to present it in their own container. The plain-text
  fallback is always passive, and inline code and code blocks are embedded
  controls, so their text is outside the surrounding text's selection range.
- Code blocks: fenced and indented code renders as a read-only AvaloniaEdit
  `TextEditor` with syntax highlighting. Resolution tries the language as a
  definition name with the supplied casing, then case-insensitively against
  the registered definition names, then as a file extension, and finally
  through the configured aliases, which default to an empty map: a fence name
  the engine does not know (for example `json5`) is highlighted only when the
  host supplies a mapping, while names and extensions the engine already
  knows (for example `cs`, `js`, `python`, or `javascript`) need no alias.
  Definitions the engine does not ship (for example Lua or TypeScript) must
  be host-registered or supplied through `CustomHighlightingInstaller`.
  `CreateCodeBlockEditor` is available for embeddable code content; the
  displayed text has its line endings normalized to line feeds and its
  conventional final terminator removed, so authored trailing blank lines are
  preserved. Rendered blocks are measured with the editor's own
  text view, clamped to `MaxVisibleCodeBlockLines`, and scroll (or clip when
  scrolling is disabled) beyond that. Code blocks never take focus; mouse
  selection and drag-and-drop are suppressed unless `AllowCodeBlockSelection`
  is enabled.
- Theme and options: `MarkdownRenderTheme` controls fonts, sizes, brushes,
  flow direction, and block spacing. Numeric values are validated on
  assignment, font sizes must not exceed the largest value the renderer
  accepts, blend ratios are clamped on assignment, and
  `double.PositiveInfinity` expresses an unbounded size; the surface
  background, foreground, and link foreground must each be a non-null
  `ISolidColorBrush`, because the derived code, border, and muted colors need a
  single surface and foreground color. `MarkdownRenderOptions` is the same
  shared record the WPF binding uses; it controls
  scrolling, wheel chaining, content interaction (text selection and
  keyboard-reachable hyperlinks), code-block selection, hyperlink opening,
  language aliases, a custom highlighting installer, and an optional logger.
  Assigned hyperlink scheme sets and alias maps are copied and frozen on
  assignment and always compared case-insensitively. `AllowScrolling = false`
  disables scrolling entirely - overflow is clipped at the theme limits.
  Hyperlink activation runs the host's `OpenHyperlink` callback first, then
  `OpenExternalUri`; a callback that returns `false` or throws counts as not
  handled, and when no callback handles the activation the press is ignored,
  because the renderer never opens a link on its own. Plain-text fallback
  rendering is used for whitespace-only content or when parsing or rendering
  fails.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvaloniaEdit.Markdown
```

```csharp
// Tooltip-shaped content: passive by default, and links are opened through host callbacks.
Control tooltip = MarkdownRenderer.CreateContent(
    "See the **guide** and `TextEditor`.",
    options: new MarkdownRenderOptions
    {
        OpenHyperlink = uri => { host.Open(uri); return true; }
    });
toolTip.Content = tooltip;

// The same rendering as a plain block panel for a host-controlled container.
Control panel = MarkdownRenderer.CreateContentTree(markdown);

// An embeddable read-only code editor; the host owns it and can make it focusable again.
TextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", code);
```

The renderer never opens a link on its own: pass `OpenHyperlink` and/or
`OpenExternalUri` to route activation into the host.

## Requirements

.NET 8 (`net8.0`) and Avalonia; the package renders through Avalonia.AvaloniaEdit 12.0.0.

## Dependencies

This package intentionally brings **Markdig** (for Markdown parsing) on top
of AvaloniaEdit, and references the dependency-free **`Nickelony.IDEKit.Core`**
for line-ending normalization (`LineTerminatorNormalizer`) and other Core
contracts it maps. The Markdig dependency is confined to the Markdown packages;
a host that does not use Markdown rendering never receives Markdig. The shared
`NumericValidation` and `BrushHelpers` helpers are compiled into the package as
linked sources, so they add no package dependency either.

### Logging

Markdown rendering accepts an optional `ILogger` through
`MarkdownRenderOptions.Logger`. Rendering failures and hyperlink-open failures
are reported as warnings through it, and a code-block language that is left to
built-in resolution and finds no highlighting is reported at debug level.
Without a logger these failures are not reported; a rendering failure still
falls back to plain text. Event ids come from the package's allocated `2000` id
block and are stable: `2000` (markdown rendering failed), `2001` (hyperlink open
failure), and `2002` (unresolved code-block language).

## Thread affinity

Every rendering entry point creates and touches Avalonia controls and must run on
the UI thread that owns (or will own) the created controls. A theme can be
constructed on a different thread as long as its brushes are immutable; the
derived code and border brushes are immutable when they are first created.

## Divergences from the WPF binding

The two Markdown packages share their Markdig walk, their content model, and their options; the
presentation layer is necessarily per-binding:

- **No `FlowDocument`.** WPF returns a `FlowDocument` (wrapped in a
  `FlowDocumentScrollViewer`) and exposes `CreateFlowDocument`; Avalonia has no document model, so the
  emitter builds a native control tree and the standalone entry point is `CreateContentTree`, which
  returns the block panel.
- **Tables, quotes, and lists are layout controls.** WPF maps them onto `Table`/`Section`/`List`; the
  Avalonia emitter uses a `Grid`, a `Border` with a left bar, and a `StackPanel` of marker-and-content
  rows instead. The observable structure therefore differs even though the rendered result is the same.
- **Inline hyperlinks are inline controls.** Avalonia has no inline hyperlink, so a link is a small
  `TextBlock`-derived control (exposing its openable target) hosted in an `InlineUIContainer`.
- **Wheel chaining walks to an ancestor scroll viewer.** WPF re-raises the routed wheel event on the
  parent; the Avalonia helper finds the nearest ancestor `ScrollViewer` and scrolls it directly, and
  Avalonia exposes no lines-per-notch setting, so the content viewer's step is supplied locally.
- **Brushes are `ISolidColorBrush` and immutable by default.** Avalonia has no `Freeze`; the derived
  brushes are immutable instances, and a theme's brushes are `ISolidColorBrush` rather than WPF's
  `SolidColorBrush`.

## Design notes

- `MarkdownRenderTheme.Default` is a platform baseline, not an integration
  with a host's color palette or editor theme; themed hosts should build a
  theme from their own palette.
- The Markdig parse and the toolkit-neutral mapping decisions (heading levels, list markers, table
  alignment, link openability, code-text normalization, highlighting resolution) live once in
  `shared/Editor.Markdown/`. The shared `MarkdownDocumentModelBuilder` produces an internal, toolkit-free
  document model, and each binding has an emitter over it: `MarkdownFlowDocumentEmitter` (WPF) and
  `MarkdownContentEmitter` (Avalonia). The shared tree also holds the render theme (`MarkdownRenderTheme`),
  the derived-brush cache (`MarkdownDerivedBrushCache`), and the code-block factory
  (`MarkdownCodeBlockFactory`), so only the emitter, the facade, and the wheel routing stay per-binding.
  The model stays internal, so neither package's public surface doubles, and the mapping is covered by one
  shared test suite that runs in both bindings.
- The render options record carries the render callbacks (`OpenHyperlink`,
  `OpenExternalUri`, `CustomHighlightingInstaller`, and `Logger`) rather than a
  separate hooks object: `MarkdownRenderer` is a static facade, so every render
  call supplies its own options and a hooks object would have no distinct
  lifetime.
- Link affordances are fixed: a link always carries the link brush and an
  underline, an openable link shows the hand cursor and takes keyboard focus
  when content interaction is enabled, and a link the options cannot open
  shows the default cursor and stays out of the tab order.
- Embedded code blocks are never focusable, so they cannot receive keyboard
  copy; `AllowCodeBlockSelection` controls mouse selection and drag-and-drop
  only. A host that needs keyboard-reachable code presents its own editor
  created through `CreateCodeBlockEditor` and makes it focusable itself,
  because the returned editor is host-owned.
- The Markdown renderer is intentionally custom instead of delegating to a
  third-party Avalonia Markdown control: the package needs tooltip semantics
  (focus and selection policy, embedded AvaloniaEdit code blocks, wheel
  chaining, and hyperlink gating) that a general-purpose control does not
  provide.
- Avalonia consumes a wheel event at the content's own scroll viewer before it can bubble to a host, so
  `MarkdownScrollChaining` routes the wheel; it can disappear if the toolkit ever chains the wheel
  itself. A wheel over a code block (including its border and padding) scrolls the block before the
  viewer, and a wheel the viewer cannot consume is forwarded to the nearest ancestor scroll viewer. Set
  `AllowWheelChaining = false` to attach no wheel handlers and keep the toolkit's default wheel
  consumption instead.
- The renderer is split internally: `MarkdownRenderer` is the public facade and `MarkdownScrollChaining`
  routes the wheel; those two stay per-binding. `MarkdownContentEmitter` emits the content tree with its
  blocks and inlines (including hyperlink activation), while the shared `MarkdownCodeBlockFactory` creates
  the code-block editors and their measured host elements and the shared `MarkdownDerivedBrushCache` owns
  the per-theme derived-brush cache. Line-ending normalization uses `LineTerminatorNormalizer` from
  `Nickelony.IDEKit.Core`, and validation and brush helpers come from the shared `NumericValidation` and
  `BrushHelpers` files; the shared Markdown files are compiled into the package as linked files. All
  helpers are internal, so the public surface stays rendering-oriented.

## License

MIT (c) 2026 Kewin Kupilas.
