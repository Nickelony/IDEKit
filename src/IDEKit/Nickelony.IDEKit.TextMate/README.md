# Nickelony.IDEKit.TextMate

Engine-neutral TextMate theme model and scope-ranking rule engine: a token theme made of selector rules, and a
resolver that turns a token's scope stack into a color and font traits the way VS Code ranks theme rules. The
AvalonEdit and AvaloniaEdit TextMate packages convert the result to WPF and Avalonia brushes and text decorations,
and a host can do the same for its own toolkit. The package depends on `Nickelony.IDEKit.Core` and
`Microsoft.Extensions.Logging.Abstractions`; it ships no tokenizer and no grammar data.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.TextMate --prerelease
```

```csharp
using Nickelony.IDEKit.TextMate;

var theme = new TextMateTokenTheme
{
    Rules =
    [
        new TextMateTokenThemeRule { Scope = "keyword, storage", Foreground = "#CC99CC", FontStyle = "bold" },
        new TextMateTokenThemeRule { Scope = "comment", Foreground = "#6A9955", FontStyle = "italic" },
    ],
};

var resolver = new TextMateScopeResolver(theme);

// The scope stack of a token runs from the root scope to the innermost scope.
TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

// style.Foreground is the opaque color #CC99CC and style.IsBold is true.
```

## Remarks

- The ranking, selector, and color rules are documented on `TextMateScopeResolver` and
  `TextMateTokenThemeRule`. A malformed color, an unsupported selector, and an unrecognized font style trait
  are reported through the optional `ILogger` and ignored.
- The resolver is safe to share between threads, and it caches the styles it resolved in a bounded cache.
- The [TextMate guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/TextMateGuide.md) owns the
  selectors, the style resolution, and the session lifecycle of the bindings.

## License

MIT © 2026 Kewin Kupilas.
