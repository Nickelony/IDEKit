# TextMate guide

`Nickelony.IDEKit.AvalonEdit.TextMate` (WPF) and `Nickelony.IDEKit.AvaloniaEdit.TextMate` (Avalonia) add
TextMate syntax highlighting to an editor for a host. Both are a thin layer over TextMateSharp: the package
ships no tokenizer, no grammar data, and no host-specific concepts - a host supplies the token theme and the
grammar.

The wiring is the same in either binding; the engine-specific lines sit behind `#if AVALONIAEDIT` in the
shared sources. The tier model is in the [editor binding guide](EditorBindingGuide.md), and the shared-source
rules are in the [editor sharing guide](EditorSharingGuide.md). A running version of the session wiring is
[`TextMateHighlighter`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Highlighting/TextMateHighlighter.cs).

## The pieces

The neutral package `Nickelony.IDEKit.TextMate` holds the token theme model and the scope-ranking rule
engine: `TextMateTokenTheme` and `TextMateTokenThemeRule` declare a theme, and `TextMateScopeResolver`
ranks its rules and returns a `TextMateTokenStyle`, an `ArgbColor` with font traits. It references only
`Core` and the logging abstractions, so a host with another toolkit converts the style itself. Each binding
declares its own types in the one namespace that names it.

- A colorizer translates TextMate tokenization into the engine's visual-line runs and applies
  each resolved style through the base binding's `TextRunStyleApplier`.
- An incremental document line-list adapter feeds the tokenizer, and a session composes the line list, the
  model, the resolver, and the colorizer for one view.
- A theme style resolver wraps the neutral scope resolver and converts its result to `TextRunStyle` values
  (a brush and text decorations) from the base binding's `Rendering` namespace.

The file tree and the generated API reference are the inventory.

The binding packages bring the TextMateSharp core runtime on top of the base binding, so a host that does not
use TextMate highlighting never receives TextMateSharp. Hosts supply grammar data themselves (for example
through the `TextMateSharp.Grammars` package), so the bundled grammar set is not forced on a host that
provides its own.

## Session lifecycle

`TextMateHighlightingSession.Start(view, grammar, theme)` creates the line list, model, resolver, and
colorizer for one view and the document it renders, starts tokenization, and inserts the colorizer first
among the view's `LineTransformers`. The syntax colors are the bottom layer, so semantic tokens or any other
transformer paints over them, whichever was added first. The optional logger reaches the style resolver.

Setting `session.Theme` recolors the view without tokenizing the document again. The session does not
follow a document swap, so a host starts a new session for a new document. The session keeps the
TextMateSharp model and line list it composes internal, and disposing the session tears them down in the safe
order and removes the colorizer from the view.

```csharp
TextMateHighlightingSession session = TextMateHighlightingSession.Start(
	editor.TextArea.TextView,
	grammar,
	theme,
	logger);

session.Theme = darkTheme;

session.Dispose();
```

Lines render with the base style until the model's background tokenizer completes them, and token-change
notifications then coalesce into a single queued redraw scoped to the changed lines.

## Selectors

Selectors support comma-separated alternatives and space-separated descendant chains (for example
`source.lua keyword.control`), including the `>` child combinator (`source.lua > keyword.control`), which
requires the scope named before the `>` to directly enclose the scope named after it.

The TextMate exclusion (`-`), wildcard (`*`), priority (`L:`), and parenthesized group operators belong to
injection selectors; a selector that uses them never matches in theme data and is reported through the
resolver logger, as is a misplaced child combinator.

## Style resolution

`TextMateScopeResolver` resolves styles one scope push at a time, the way VS Code does. Its remarks own the
ranking rules, and `TextMateTokenThemeRule` owns the theme value conventions: the color forms, the
`fontStyle` reset, and the defaults rule. A foreground is read as a hexadecimal color only; a color name is
reported as invalid, because the neutral package parses no toolkit color.

A host declares a theme as a list of rules, and the resolver turns a scope sequence into a style:

```csharp
using Nickelony.IDEKit.TextMate;

var theme = new TextMateTokenTheme
{
    Rules =
    [
        // A rule without a scope sets the default of every token.
        new TextMateTokenThemeRule { Foreground = "#D4D4D4" },
        new TextMateTokenThemeRule { Scope = "comment", Foreground = "#6A9955", FontStyle = "italic" },
        new TextMateTokenThemeRule { Scope = "keyword.control, storage", Foreground = "#C586C0" },
    ],
};

var resolver = new TextMateScopeResolver(theme);

// The comment rule matches the last scope, so the style is italic with the foreground of that rule.
TextMateTokenStyle style = resolver.Resolve(["source.lua", "comment.line.double-dash.lua"]);
```

`TextMateHighlightingSession.Start` takes the same theme and builds its own resolver, so a host calls `Resolve`
itself only to convert styles for another toolkit.

The resolver ranks matches itself rather than delegating to `TextMateSharp.Themes`. A rule that matches a
deeper scope wins over a rule that matches only an enclosing scope. The internal style resolver of each
binding converts the result once per distinct style, so two scope sequences that resolve alike share one
brush and one decoration collection.

## Thread affinity

A session is created and its theme is set on the UI thread that owns the text view, and token-change redraws
marshal to it. A session disposed on another thread posts the removal of its colorizer to that thread. The line list is
safe to read from the tokenizer thread, and the resolved-style cache is synchronized.

## Logging

`TextMateScopeResolver` and `TextMateHighlightingSession.Start` accept an optional
`Microsoft.Extensions.Logging.ILogger` and default to `NullLogger` when none is supplied, so hosts control
where malformed theme data is logged.

Invalid foreground colors, unsupported selectors, unrecognized font style traits, misplaced child combinators,
and null theme rules are reported as warnings. Event ids follow the [log event
ids](ArchitectureGuide.md#log-event-ids) registry.

## Design notes

- The document line-list adapter keeps a full snapshot of line texts so the tokenizer thread can read the
  document without touching the UI-thread document; a large document pays for one extra copy of its text.
  The remarks of the internal line list own the snapshot's locking, invalidation, and line-count rules.
- The colorizer never tokenizes from the paint path, because TextMateSharp's tokenizer is not
  safe to drive from two threads at once.
