# Nickelony.IDEKit.AvalonEdit.TextMate

TextMate syntax highlighting for AvalonEdit editors, built on `Nickelony.IDEKit.AvalonEdit` and
TextMateSharp. It is a thin layer over TextMateSharp: it ships no tokenizer, no grammar data, and no
host-specific concepts - a host supplies the token theme and the grammar. It targets `net8.0-windows`
(WPF), depends on the neutral `Nickelony.IDEKit.TextMate` package (the token theme model and the
scope-ranking rule engine), and references TextMateSharp and `Microsoft.Extensions.Logging.Abstractions`
for the session's optional logger.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvalonEdit.TextMate --prerelease
```

```csharp
using Nickelony.IDEKit.AvalonEdit.TextMate;

// The host supplies the AvalonEdit `editor`, a TextMateSharp `IGrammar` for the language (for example from
// the TextMateSharp.Grammars package) and two `TextMateTokenTheme` values (see the Nickelony.IDEKit.TextMate readme).
TextMateHighlightingSession session = TextMateHighlightingSession.Start(
    editor.TextArea.TextView,
    grammar,
    lightTheme);

// A theme switch recolors without tokenizing again; a document swap starts a new session.
session.Theme = darkTheme;

session.Dispose();
```

## Remarks

- The package does not reference the `TextMateSharp.Grammars` package, so a host chooses its own grammar data,
  for example from that package.
- The session inserts its colorizer first among the view's `LineTransformers`, so semantic tokens paint over
  the syntax colors, and disposing the session removes it. The session owns the model and line list it composes.
- The paint path never drives the tokenizer: lines render uncolored until the model's background pass
  completes them and queues a redraw scoped to the changed lines.
- The session's theme resolver wraps the neutral `TextMateScopeResolver`, which applies the theme per scope push, and
  converts the result to `TextRunStyle` from the base binding's `Rendering` namespace,
  which the colorizer applies through `TextRunStyleApplier`.
- The session keeps a copy of the document text for the tokenizer thread, one string per line ended with a line
  feed, until it is disposed, so a highlighted document costs about twice its text in memory.
- Measured on 50,000 lines of 40 characters, the document held about 8.0 MB and the line list about 8.2 MB: two
  bytes per character (the line feed included) plus about 83 bytes per line. The figures are the managed heap
  after a full collection on 64-bit .NET 8, without TextMateSharp's tokens.
- The [TextMate guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/TextMateGuide.md) owns the
  session lifecycle, the selectors, the style resolution, and thread affinity.

## License

MIT © 2026 Kewin Kupilas.
