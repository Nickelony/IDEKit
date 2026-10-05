# Nickelony.IDEKit.AvalonEdit.Markdown

Markdown rendering for AvalonEdit editors, built on AvalonEdit and Markdig: it parses a narrow
CommonMark pipeline and emits tooltip-shaped WPF content or a `FlowDocument`. It targets
`net8.0-windows` (WPF) and depends on `Nickelony.IDEKit.AvalonEdit`, `Nickelony.IDEKit.Core` and the neutral
`Nickelony.IDEKit.Markdown` package (the Markdig parse, the toolkit-free content model, and the render theme
and options). It references `Microsoft.Extensions.Logging.Abstractions` for the optional logger.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvalonEdit.Markdown --prerelease
```

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Markdown;
using Nickelony.IDEKit.Markdown;

const string markdown = "See the **guide** and `TextEditor`.";

// Tooltip-shaped content: passive by default, and links are opened through host callbacks.
FrameworkElement tooltip = MarkdownRenderer.CreateContent(
    markdown,
    options: new MarkdownRenderOptions
    {
        OpenHyperlink = uri =>
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
    });

var button = new Button { Content = "Hover me", ToolTip = tooltip };

// The same rendering as a plain document for a host-controlled container.
FlowDocument document = MarkdownRenderer.CreateContentTree(markdown);

// An embeddable read-only code editor; the host owns it and can make it focusable again.
TextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "print(\"hello\")");
```

The renderer never opens a link on its own: pass `OpenHyperlink` and/or `OpenExternalUri` to route
activation into the host.

## Remarks

- The parser is a deliberately narrow Markdig pipeline - CommonMark plus pipe tables, strikethrough, and
  autolinks; raw HTML and unenabled extensions render as literal text, so authored content is never
  dropped.
- Rendering is passive by default; `AllowContentInteraction` opts content into text selection and
  keyboard-reachable hyperlinks, and `AllowCodeBlockSelection` opts code blocks into mouse selection.
- Markdig is confined to the Markdown packages, so a host that does not use Markdown rendering never
  receives it.
- A parse or render failure falls back to plain text, and a rendering or hyperlink failure is reported
  through the optional `ILogger`.
- The [Markdown guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/MarkdownGuide.md) owns
  the rendering support, the code-block resolution order, the theme and options, hyperlink activation,
  wheel chaining, and the per-binding presentation.

## License

MIT © 2026 Kewin Kupilas.
