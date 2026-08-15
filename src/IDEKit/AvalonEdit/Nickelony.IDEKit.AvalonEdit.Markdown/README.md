# Nickelony.IDEKit.AvalonEdit.Markdown

Markdown rendering for AvalonEdit editors, built on the
**AvalonEdit** editor package and **Markdig**, with optional diagnostics through
`Microsoft.Extensions.Logging.Abstractions`. The package targets
`net8.0-windows`, uses WPF, and lives in the single namespace
`Nickelony.IDEKit.AvalonEdit.Markdown`. It ships a tooltip-shaped
presentation by default, and the same rendering is available as a plain
`FlowDocument` for hosts that present content in their own container:

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
  breaks render as spaces and hard breaks as line breaks. The parsed AST is
  mapped onto WPF block and inline elements.
- Code blocks: fenced and indented code renders as a read-only AvalonEdit
  `TextEditor` with syntax highlighting. Resolution tries the language as a
  definition name with the supplied casing, then case-insensitively against
  the registered definition names, then as a file extension, and finally
  through the configured aliases, which default to an empty map: a fence name
  AvalonEdit does not know (for example `json5`) is highlighted only when the
  host supplies a mapping, while names and extensions AvalonEdit already
  knows (for example `cs`, `js`, `python`, or `javascript`) need no alias.
  Definitions AvalonEdit does not ship (for example Lua or TypeScript) must
  be host-registered or supplied through `CustomHighlightingInstaller`.
  `CreateCodeBlockEditor` is available for embeddable code content; the
  displayed text has its line endings normalized to line feeds and its
  conventional final terminator removed, so authored trailing blank lines are
  preserved. Rendered blocks are measured with the editor's own
  text view, clamped to `MaxVisibleCodeBlockLines`, and scroll (or clip when
  scrolling is disabled) beyond that. Code blocks never take focus; mouse
  selection and drag-and-drop are suppressed unless `AllowCodeBlockSelection`
  is enabled.
- Presentation: `CreateContent` returns tooltip-shaped elements (passive by
  default: not focusable, not selectable, and code blocks suppress mouse
  selection and drag-and-drop); `AllowContentInteraction` opts the rendered
  content into text selection and keyboard-reachable hyperlinks, and
  `AllowCodeBlockSelection` opts code blocks into mouse selection and
  drag-and-drop; `CreateFlowDocument` returns the rendered document for hosts
  that want to present it in their own container. The plain-text fallback is
  always passive, and inline code and code blocks are embedded controls, so
  their text is outside the document's selection range.
- Theme and options: `MarkdownRenderTheme` controls fonts, sizes, brushes,
  flow direction, and block spacing. Numeric values are validated on
  assignment, font sizes must not exceed the largest value WPF accepts,
  blend ratios are clamped on assignment, and `double.PositiveInfinity`
  expresses an unbounded size; the surface background, foreground, and link
  foreground must each be a non-null `SolidColorBrush`, because the derived
  code, border, and muted colors need a single surface and foreground color.
  `MarkdownRenderOptions` controls
  scrolling, wheel chaining, content interaction (text selection and
  keyboard-reachable hyperlinks), code-block selection, hyperlink opening,
  language aliases, a custom highlighting installer, and an optional logger.
  Assigned hyperlink scheme sets and alias maps are copied and frozen on
  assignment and always compared case-insensitively. `AllowScrolling = false`
  disables scrolling entirely - overflow is clipped at the theme limits.
  Hyperlink activation runs the host's `OpenHyperlink` callback first, then
  `OpenExternalUri`; a callback that returns `false` or throws counts as not
  handled, and when no callback handles the activation the click is ignored,
  because the renderer never opens a link on its own. Plain-text fallback
  rendering is used for whitespace-only content or when parsing or rendering
  fails.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvalonEdit.Markdown
```

```csharp
// Tooltip-shaped content: passive by default, and links are opened through host callbacks.
FrameworkElement tooltip = MarkdownRenderer.CreateContent(
    "See the **guide** and `TextEditor`.",
    options: new MarkdownRenderOptions
    {
        OpenHyperlink = uri => { host.Open(uri); return true; }
    });
toolTip.Content = tooltip;

// The same rendering as a plain document for a host-controlled container.
FlowDocument document = MarkdownRenderer.CreateFlowDocument(markdown);

// An embeddable read-only code editor; the host owns it and can make it focusable again.
TextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", code);
```

The renderer never opens a link on its own: pass `OpenHyperlink` and/or
`OpenExternalUri` to route activation into the host.

## Requirements

.NET 8 (`net8.0-windows`) and WPF; the package renders through AvalonEdit 6.3.1.120.

## Dependencies

This package intentionally brings **Markdig** (for Markdown parsing) on top
of AvalonEdit, and references the dependency-free **`Nickelony.IDEKit.Core`**
for line-ending normalization (`LineTerminatorNormalizer`) and other Core
contracts it maps. The Markdig dependency is confined here; the general editing
helpers live in `Nickelony.IDEKit.AvalonEdit`, so a host that does not
use Markdown rendering never receives Markdig. The shared `NumericValidation`
and `BrushHelpers` helpers are compiled into the package as linked sources, so
they add no package dependency either.

### Logging

Markdown rendering accepts an optional `ILogger` through
`MarkdownRenderOptions.Logger`. Rendering failures and hyperlink-open failures
are reported as warnings through it, and a code-block language that is left to
built-in resolution and finds no highlighting is reported at debug level.
Without a logger these failures are not reported; a rendering failure still
falls back to plain text. Event ids come from the package's allocated `2000` id
block and are stable: `2000` (markdown rendering failed), `2001` (hyperlink open
failed), and `2002` (unresolved code-block language).

## Thread affinity

Every rendering entry point creates and touches WPF elements and must run on
the UI thread that owns (or will own) the created elements. A theme can be
constructed on a different thread as long as its brushes are frozen; the
derived code and border brushes are frozen when they are first created.

## Design notes

- `MarkdownRenderTheme.Default` is a platform baseline, not an integration
  with a host's color palette or editor theme; themed hosts should build a
  theme from their own palette.
- The render options record carries the render callbacks (`OpenHyperlink`,
  `OpenExternalUri`, `CustomHighlightingInstaller`, and `Logger`) rather than a
  separate hooks object: `MarkdownRenderer` is a static facade, so every render
  call supplies its own options and a hooks object would have no distinct
  lifetime. Reconsider the grouping only if an instance-renderer API gives the
  callbacks a construction-time owner.
- Link affordances are fixed: a link always carries the link brush and an
  underline, an openable link shows the hand cursor and takes keyboard focus
  when content interaction is enabled, and a link the options cannot open
  shows the arrow cursor and stays out of the tab order.
- Embedded code blocks are never focusable, so they cannot receive keyboard
  copy; `AllowCodeBlockSelection` controls mouse selection and drag-and-drop
  only. A host that needs keyboard-reachable code presents its own editor
  created through `CreateCodeBlockEditor` and makes it focusable itself,
  because the returned editor is host-owned.
- The Markdown renderer is intentionally custom instead of delegating to
  `Markdig.Wpf`: its last release is `0.5.0.1` (January 2021), and it does not
  provide this package's tooltip semantics (focus and selection policy,
  embedded AvalonEdit code blocks, wheel chaining, and hyperlink gating).
  General-purpose alternatives (for example `Neo.Markdig.Xaml`, listed by
  Markdig) target document display rather than these semantics.
- WPF does not chain the mouse wheel from a nested scroll viewer to an outer
  one, so `MarkdownScrollChaining` routes the wheel event; it can disappear if
  WPF or AvalonEdit ever chains the wheel itself. A wheel over a code block
  (including its border and padding) scrolls the block before the viewer, and
  chained scrolling follows the operating system's lines-per-notch setting: a
  code-block editor moves by one text line per line step (three lines per
  notch by default), the viewer by its fixed 16-DIP line unit, and both by one
  page per notch in page-scroll mode. A wheel the viewer cannot consume is
  forwarded to an enclosing host scroller (the rendered viewer otherwise
  consumes every wheel event it receives), and a viewer whose scrolling is
  disabled forwards every wheel event. Set `AllowWheelChaining = false` to
  attach no wheel handlers and keep WPF's default wheel consumption instead.
- GFM table column alignment is applied to cell paragraphs. `BlockSpacing`
  drives the top and bottom margins of headings, thematic breaks, and code
  blocks and the bottom margins of paragraphs, quotes, lists, and tables;
  table cells suppress the vertical spacing of their content and tight lists
  render their items compactly. Other layout values (list indentation, quote
  padding and bar, table padding and borders, corner radii) are fixed and are
  not theme properties.
- The renderer is split internally: `MarkdownRenderer` is the public facade and `MarkdownScrollChaining`
  routes the wheel; those two stay per-binding. `MarkdownFlowDocumentEmitter` emits the WPF document from
  the shared document model with its blocks and inlines (including hyperlink activation), while the shared
  `MarkdownCodeBlockFactory` creates the code-block editors and their measured host elements and the shared
  `MarkdownDerivedBrushCache` owns the per-theme derived-brush cache. Line-ending normalization uses
  `LineTerminatorNormalizer` from `Nickelony.IDEKit.Core`, and validation and brush helpers come from the
  shared `NumericValidation` and `BrushHelpers` files; the shared Markdown files are compiled into the
  package as linked files. All helpers are internal, so the public surface stays rendering-oriented.
- The Markdig parse and the toolkit-neutral mapping decisions (heading levels, list markers, table
  alignment, link openability, code-text normalization, highlighting resolution) live once in
  `shared/Editor.Markdown/`, shared with the cross-platform `Nickelony.IDEKit.AvaloniaEdit.Markdown`
  mirror. The shared `MarkdownDocumentModelBuilder` produces an internal, toolkit-free document model and
  `MarkdownRenderOptions` is the shared options record, and the render theme (`MarkdownRenderTheme`), the
  derived-brush cache (`MarkdownDerivedBrushCache`), and the code-block factory (`MarkdownCodeBlockFactory`)
  are shared with it; this package adds the WPF emitter over the model. The model stays internal, so the
  public surface does not double, and the mapping is covered by one shared test suite that runs in both
  bindings.

## License

MIT © 2026 Kewin Kupilas.
