# Nickelony.IDEKit.AvalonEdit.TextMate

TextMate syntax-highlighting infrastructure for AvalonEdit editors, built on
`Nickelony.IDEKit.AvalonEdit` and **TextMateSharp**. The package targets
`net8.0-windows`, uses WPF, and hosts the
`Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting` namespace:

- `Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting` groups the package's
  public types: the colorizing transformer (`TextMateColorizingTransformer`)
  that translates TextMate tokenization into AvalonEdit visual-line runs, the
  incremental document line-list adapter (`TextMateDocumentLineList`), the
  session that composes them for one view (`TextMateHighlightingSession`), the
  token-theme data model (`TextMateTokenTheme` / `TextMateTokenThemeRule`), and
  the theme style resolver (`TextMateThemeStyleResolver`) that maps token scopes
  to styles. It consumes the shared `TextRunStyle` contract (the ready-made
  `ITextRunStyle` implementation) from
  `Nickelony.IDEKit.AvalonEdit.Rendering` rather than defining it.
- Lifetime: `TextMateHighlightingSession.Start(view, grammar, theme)` creates
  the line list, model, resolver, and transformer for one view and the
  document it renders, and starts tokenization; the session exposes them and
  disposes the transformer and the model (which also disposes the line list)
  in the safe order. It never installs the transformer or follows document
  swaps: the host adds `session.Transformer` to the view's `LineTransformers`,
  removes it again, and starts a new session for a new document. Lines render
  uncolored until the model's background tokenizer completes them, and
  token-change notifications then coalesce into a single queued redraw scoped
  to the changed lines, so the paint path never drives TextMateSharp's
  tokenizer directly. Resolved styles are cached per resolver instance with a
  bounded, synchronized cache, so one resolver can be shared across editors
  and threads; the line list is safe to read from the tokenizer thread.
- Selectors support comma-separated alternatives and space-separated
  descendant chains (for example `source.lua keyword.control`), including
  the `>` child combinator (`source.lua > keyword.control`), which requires
  the scope named before the `>` to directly enclose the scope named after
  it. The TextMate exclusion (`-`), wildcard (`*`), priority (`L:`), and
  parenthesised group operators belong to injection selectors;
  selectors that use them never match in theme data and are reported through
  the resolver logger, as is a misplaced child combinator.
- Styles resolve the way VS Code resolves them: scope push by scope push,
  anchoring the selector's rightmost part at each scope. Each push applies the
  most specific matching rule, which overrides the less specific rules of that
  push for the properties it sets; a parent-scoped rule inherits the properties
  it does not set from the theme's bare rules, and everything they leave unset
  falls back to shallower pushes and the defaults. The theme's bare rules form
  one merged candidate, and rules of equal specificity resolve in selector
  order. A rule with a blank scope - or a null scope from deserialized data -
  provides the defaults that apply to every token.
- Theme values follow the TextMate data conventions: a rule's foreground also
  accepts the eight-digit `#RRGGBBAA` form and the four-digit `#RGBA` form
  (both read in TextMate order and normalized to WPF order; the WPF forms that
  put the alpha component first - `#AARRGGBB` and `#ARGB` - are not
  recognized), and a present `fontStyle` value - including
  an empty string - resets the inherited traits before applying the
  recognized ones, while an absent value keeps them. Token rules carry
  foreground colors and font traits only; rule backgrounds are not represented
  and are ignored.

Hosts provide the token theme (`TextMateTokenTheme`) and start one session per
view (for that view's document). The package is a thin layer over
TextMateSharp: it ships no tokenizer, no grammar data, and no host-specific
concepts.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvalonEdit.TextMate
```

```csharp
TextMateHighlightingSession session = TextMateHighlightingSession.Start(
	editor.TextArea.TextView,
	grammar,
	theme,
	logger);

editor.TextArea.TextView.LineTransformers.Add(session.Transformer);

// Tear down in the reverse order; a document swap starts a new session.
editor.TextArea.TextView.LineTransformers.Remove(session.Transformer);
session.Dispose();
```

## Requirements

.NET 8 (`net8.0-windows`) and WPF; the package renders through AvalonEdit 6.3.1.120.

## Dependencies

This package intentionally brings the **TextMateSharp** core runtime on top of
AvalonEdit; hosts supply grammar data themselves, for example through the
`TextMateSharp.Grammars` package, so the bundled grammar set is not forced on
hosts that provide their own. The dependency is confined here; the general
editing helpers live in `Nickelony.IDEKit.AvalonEdit`, so a host that does not
use TextMate highlighting never receives TextMateSharp.

### Logging

`TextMateThemeStyleResolver` accepts an optional
`Microsoft.Extensions.Logging.ILogger` and defaults to `NullLogger` when none
is supplied, so hosts control where malformed theme data is logged. Invalid
foreground colors, unsupported selectors, unrecognized font style traits,
misplaced child combinators, and null theme rules are reported as warnings.
Event ids are stable within the package: `3000` (invalid foreground color),
`3001` (unsupported selector), `3002` (unrecognized font style trait), `3003`
(misplaced child combinator), and `3004` (null theme rule).

## Thread affinity

A session is created and used on the UI thread that owns the text view: the
transformer is added to and removed from the view's `LineTransformers` there, and
token-change redraws marshal to it. The line list is safe to read from the
tokenizer thread, and the resolved-style cache is synchronized so one resolver can
be shared across editors and threads.

## Design notes

- The TextMate theme resolver is intentionally custom instead of delegating to
  `TextMateSharp.Themes`: that dependency's ranking predates the current VS
  Code behavior (it treats the `>` child combinator as an ordinary parent
  scope and orders a different specificity tie-break), so delegating would
  mis-rank selectors. The resolver applies the theme per scope push, so a
  deeper token scope takes precedence over a more deeply nested selector that
  matched an enclosing scope - the same precedence VS Code uses.
- `TextMateDocumentLineList` keeps a full snapshot of line texts so the
  tokenizer thread can read the document without touching the UI-thread
  document; large documents pay for one extra copy of their text. Changed line
  ranges are derived from the document's line geometry, so edits that split or
  form a CRLF pair keep the snapshot, the model's line-state list, and the
  document at the same line count, with every snapshot line text mirroring the
  document line including its terminator.
- The colorizing transformer never tokenizes from the paint path:
  TextMateSharp's tokenizer is not safe to drive from two threads at once, so
  lines render with the base style until the model's background pass (up to
  three seconds and 10,000 characters per line) completes them and raises the
  token-change notification that queues a redraw of the changed lines.
- `TextMateDocumentLineList` guards its snapshot with a private lock so the
  tokenizer thread can read it while the UI thread updates it; a
  tokenizer-thread read can briefly observe an update in flight, but reads are
  range-guarded and an out-of-range read returns empty text and a zero length
  instead of throwing. The transformer coalesces a burst of token-change
  notifications into one queued redraw scoped to the changed line range, so it
  never drives the tokenizer from the paint path.

## License

MIT © 2026 Kewin Kupilas.
