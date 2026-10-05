# Markdown guide

`Nickelony.IDEKit.AvalonEdit.Markdown` (WPF) and `Nickelony.IDEKit.AvaloniaEdit.Markdown` (Avalonia) render
Markdown inside an editor for a host. Both are a static facade over the neutral `Nickelony.IDEKit.Markdown`
package (the Markdig parse and the toolkit-free content model) and emit that model into their engine's
controls.

The wiring reads the same in either binding; the divergences are noted where they are real. The tier model
is in the [editor binding guide](EditorBindingGuide.md), and the shared-source rules are in the
[editor sharing guide](EditorSharingGuide.md). The hover tooltip of
[`SampleHover`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Hover/SampleHover.cs) renders its content with the facade,
and so does the completion tooltip of
[`SampleCompletion`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Completion/SampleCompletion.cs) in the WPF binding, through
`CompletionPresenterHooks.BuildMarkdownContent`.

## What renders

The parser is a deliberately narrow Markdig pipeline: CommonMark plus pipe tables (with GFM column
alignment), strikethrough, and autolinks. HTML entity references are decoded to their characters.
CommonMark soft line breaks render as spaces and hard breaks as line breaks. Raw HTML (block and inline, for
example `<br>`) is not interpreted and renders as literal text, so authored content is never dropped.

Extensions left disabled render as literal text: footnotes, mathematics, definition lists, task lists,
custom containers, and the subscript, superscript, marked, and inserted emphasis variants. An image whose alt
text is empty renders a muted placeholder showing the image target (or the literal word `image` when the
target is also empty) instead of nothing.

## The content model

`Nickelony.IDEKit.Markdown` is public API, so a host can render Markdown into its own toolkit instead of the
two bindings' controls. `MarkdownDocumentModelBuilder.Build` parses the text into a `MarkdownDocumentModel`: a
list of `MarkdownBlock` records, each holding `MarkdownInline` records where it carries text. The model types
sit in `Nickelony.IDEKit.Markdown.Model` and the render plan in `Nickelony.IDEKit.Markdown.Planning`, while the
theme, the options and the pipeline stay in `Nickelony.IDEKit.Markdown`. The API reference lists the record
types.

```csharp
MarkdownDocumentModel model = MarkdownDocumentModelBuilder.Build(markdown, supportedSchemes);

foreach (MarkdownBlock block in model.Blocks)
{
    switch (block)
    {
        case MarkdownHeading heading:
            // heading.Level, heading.Inlines
            break;

        case MarkdownCodeBlock code:
            // code.Language, code.NormalizedCode
            break;
    }
}
```

A code block's `NormalizedCode` is the text to show: it has line feeds only and no final line feed, whereas
`Code` holds the source text.

The model carries no brush, font size, or layout value, so the host's renderer resolves those from its own
theme. A link's `OpenableUri` is set only when its scheme is in `supportedSchemes`. The measurements the
bindings share, such as list indentation and table padding, are decided by `MarkdownRenderPlanner`, which both
bindings read, and the plan it returns carries the resulting values.

## Code blocks

Fenced and indented code renders as a read-only editor with syntax highlighting.

The fence name is resolved in order:

1. the definition name with the supplied casing;
2. the registered definition names, matched case-insensitively;
3. a file extension;
4. the `HighlightingAliases` map (empty by default).

A fence name the engine does not know (for example `json5`) is highlighted only when the host supplies a
mapping; names and extensions the engine already knows (for example `cs`, `js`, `python`, or `javascript`) need no
alias. `cs` and `js` resolve through their file extensions, and `python` and `javascript` match the `Python` and
`JavaScript` definition names case-insensitively. Definitions the engine does not ship
(for example Lua or TypeScript) must be host-registered or supplied through the custom highlighting installer
(see [Theme and options](#theme-and-options)).

`CreateCodeBlockEditor` is available for embeddable code content. The displayed text has its line endings
normalized to line feeds and only its single conventional final terminator removed, so authored trailing blank
lines are preserved.

```csharp
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Markdown;
using Nickelony.IDEKit.Markdown;

var options = new MarkdownRenderOptions
{
    // csx is neither a definition name nor an extension the engine knows, so the alias names the C# definition.
    HighlightingAliases = new Dictionary<string, string> { ["csx"] = "C#" },
};

TextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("csx", "Console.WriteLine(1);", MarkdownRenderTheme.Default, options);
```

The Avalonia binding returns its own `TextEditor` from the `Nickelony.IDEKit.AvaloniaEdit.Markdown` namespace.

Rendered blocks are measured with the editor's own text view, clamped to `MaxVisibleCodeBlockLines`, and
scroll (or clip when scrolling is disabled) beyond that. Code blocks never take focus, and mouse selection
and drag-and-drop are suppressed unless `AllowCodeBlockSelection` is enabled.

## Presentation

`CreateContent` returns tooltip-shaped elements that are passive by default: not focusable, not selectable,
with code blocks suppressing mouse selection and drag-and-drop. `AllowContentInteraction` opts the rendered
content into text selection and keyboard-reachable hyperlinks, and `AllowCodeBlockSelection` opts code blocks
into mouse selection and drag-and-drop.

`CreateContentTree` returns the rendered content for a host that presents it in its own container. The
plain-text fallback is always passive, and inline code and code blocks are embedded controls, so their text
is outside the document's selection range.

`CreatePlainTextContent` shows its content literally, without parsing it as Markdown. It returns the viewer
that the plain-text fallback uses, so it is always passive too.

### Per-binding presentation

The emission of the neutral model is toolkit-specific. The WPF binding maps the model onto WPF block and
inline elements, and its `CreateContentTree` returns a `FlowDocument`.

The Avalonia binding has no document model, so it builds a native control tree (the standalone
`CreateContentTree` returns a block panel). Tables, quotes, and lists emit as a `Grid`, a `Border` with a
left bar, and a `StackPanel` of marker-and-content rows; an inline hyperlink is hosted in an
`InlineUIContainer` as a small `TextBlock`-derived control, because Avalonia has no inline hyperlink.

A code block is an AvaloniaEdit `TextEditor`, and in a `Popup` without a logical parent it renders as an empty
frame. The hover and signature-help presenters of the language-features binding give their popup the text area
as its logical parent. A host that shows the content in a popup of its own does the same through
`ISetLogicalParent.SetParent`.

## Theme and options

`MarkdownRenderTheme` and `MarkdownRenderOptions` are types of `Nickelony.IDEKit.Markdown`, so one theme and
one options value serve both bindings and a host that targets both builds them once.

`MarkdownRenderTheme` controls font families, sizes, colors, flow direction, and block spacing. Numeric
values are validated on assignment, font sizes must not exceed the largest value the renderer accepts, blend
ratios are clamped on assignment, and `double.PositiveInfinity` expresses an unbounded size.

`FlowDirection` is a `MarkdownFlowDirection`, which each binding converts to its toolkit's own enum.
`Foreground`, `SurfaceBackground`, and `LinkForeground` are neutral `Nickelony.IDEKit.Core.Media.ArgbColor`
values, and the font families are family-name strings: a null or blank body family selects each binding's own
default font.

`MarkdownRenderTheme.Default` is a platform baseline, not an integration with a host's color palette or editor
theme; a themed host builds a theme from its own palette.

A theme is a neutral value that can be constructed on any thread, and the engine brushes and font families
it resolves to are created lazily on first use, so they are first materialized on the thread that renders
with the theme.

`MarkdownRenderOptions` controls scrolling, wheel chaining, content interaction (text selection and
keyboard-reachable hyperlinks), code-block selection, hyperlink opening, highlighting aliases, and an
optional logger. Assigned hyperlink scheme sets and alias maps are copied and frozen on assignment and
always compared case-insensitively.

`AllowScrolling = false` disables scrolling entirely - overflow is clipped at the theme limits.

The render options record carries the render callbacks (`OpenHyperlink`, `OpenExternalUri`, and `Logger`)
rather than a separate hooks object, because `MarkdownRenderer` is a static facade: every render call
supplies its own options and a hooks object would have no distinct lifetime.

The custom highlighting installer is the one callback that is not an option, because it is typed to the
engine editor and the options name no engine type. Pass it as the `customHighlightingInstaller` argument of
`CreateContent`, `CreateContentTree`, or `CreateCodeBlockEditor`; the `CodeBlockHighlightingInstaller`
delegate documents its contract. The installer stays typed to the engine editor on purpose: a neutral context
cannot attach the cleanup to that editor's lifecycle, so a callback whose state is disposable attaches its
cleanup to the editor itself.

GFM table column alignment is applied to the text of every block a cell holds, including text nested in
lists and quotes. `BlockSpacing` drives the top and bottom margins of
headings, thematic breaks, and code blocks and the bottom margins of paragraphs, quotes, lists, and tables;
table cells suppress the vertical spacing of their content and tight lists render their items compactly.

Other layout values (list indentation, quote padding and bar, table padding and borders, corner radii) are
fixed and are not theme properties.

Every table cell draws its border on all four sides, because a WPF `Table` cannot collapse borders and both
bindings render one layout plan, so an interior grid line reads twice as thick as an outer edge.

## Hyperlinks

The renderer never opens a link on its own. Only links whose scheme is in `SupportedHyperlinkSchemes`
(HTTP and HTTPS by default) can be activated. Activation runs the host's `OpenHyperlink` callback first,
then `OpenExternalUri`; a callback that returns `false`, or throws a non-fatal exception, counts as not
handled, and when no callback handles the activation the click is ignored.

The renderer never passes a link to the operating system's protocol handler. The target comes from untrusted
content, and a shell open would hand it to whichever handler is registered for its scheme, so a link opens
only through the host's callbacks.

Pass the callback in the options that `CreateContent` takes:

```csharp
var options = new MarkdownRenderOptions
{
    // OpenInHostBrowser is the host's own method; it returns true when it opened the link.
    OpenHyperlink = uri => OpenInHostBrowser(uri),
};

FrameworkElement content = MarkdownRenderer.CreateContent(
    "[docs](https://example.com)",
    MarkdownRenderTheme.Default,
    options
);
```

The Avalonia binding's `CreateContent` returns a `Control` instead of a `FrameworkElement`.

Link affordances are fixed: a link always carries the link brush and an underline, an openable link
shows the hand cursor and takes keyboard focus when content interaction is enabled, and a link the
options cannot open shows the arrow cursor and stays out of the tab order.

## Wheel chaining

The engine does not chain the mouse wheel from the nested content viewer to an outer one, so
`MarkdownScrollChaining` routes the wheel event. A wheel over a code block (including its border and padding)
scrolls the block before the viewer.

The WPF binding follows the operating system's lines-per-notch setting: a code-block editor moves by one text
line per line step, three lines per notch by default, the viewer by its fixed 16-DIP line unit, and both by
one page per notch in page-scroll mode. It forwards a wheel the viewer cannot consume to an enclosing host
scroller.

The Avalonia binding has no lines-per-notch setting, so it scrolls the viewer by a fixed pixel step per notch. A
wheel the viewer cannot consume goes to the nearest ancestor `ScrollViewer` that can still scroll.

In both bindings the distance follows the size of the wheel event, so a precision-touchpad event of half a notch
scrolls half as far as a full notch.

In both bindings a viewer whose scrolling is disabled forwards every wheel event. Set
`AllowWheelChaining = false` to attach no wheel handlers and keep the engine's default wheel consumption
instead.

## Failure handling and logging

Whitespace-only content renders an empty document. A parsing or rendering failure falls back to plain-text
rendering.

The renderer accepts an optional `ILogger` through `MarkdownRenderOptions.Logger`: rendering failures and
hyperlink-open failures are reported as warnings, and a code-block language left to built-in resolution that
finds no highlighting is reported at debug level.

Without a logger these failures are not reported, and a rendering failure still falls back to plain text.
Event ids follow the [log event ids](ArchitectureGuide.md#log-event-ids) registry.

## Thread affinity

Every rendering entry point creates and touches engine elements and must run on the UI thread that owns
(or will own) the created elements.
