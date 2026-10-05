# Nickelony.IDEKit.Markdown

Engine-neutral Markdown content model: a Markdig-backed parse of Markdown text into a tree of block and inline
records, with no UI toolkit types, and the `MarkdownRenderTheme` and `MarkdownRenderOptions` that both editor
bindings render with. The AvalonEdit and AvaloniaEdit Markdown packages render the model into WPF and
Avalonia content, and a host can render it into its own toolkit the same way.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.Markdown --prerelease
```

```csharp
using Nickelony.IDEKit.Markdown.Model;

MarkdownDocumentModel model = MarkdownDocumentModelBuilder.Build(
    "# Title\n\nSome **bold** text.",
    supportedSchemes: new HashSet<string> { "http", "https" });

foreach (MarkdownBlock block in model.Blocks)
{
    switch (block)
    {
        case MarkdownHeading heading:
            Console.WriteLine($"Heading, level {heading.Level}");
            break;

        case MarkdownParagraph paragraph:
            Console.WriteLine($"Paragraph of {paragraph.Inlines.Count} inline nodes");
            break;
    }
}
```

## Remarks

- The pipeline is deliberately narrow: CommonMark plus pipe tables, strikethrough, and autolinks. Raw HTML and
  every extension the pipeline leaves disabled appear as literal text.
- A link carries an absolute `OpenableUri` only when its scheme is in `supportedSchemes`; any other link keeps
  its text and has no target.
- A code block's `NormalizedCode` is the text to show, with line feeds only and no final line feed, whereas
  `Code` holds the source text.
- `MarkdownRenderTheme` holds fonts, sizes, colors, and layout limits as plain values, and
  `MarkdownRenderOptions` holds the interaction switches, the hyperlink openers, and the logger. Neither
  names a toolkit type, so a host that targets both bindings builds each once.

The [Markdown guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/MarkdownGuide.md) owns the
rendering behavior.

## License

MIT © 2026 Kewin Kupilas.
