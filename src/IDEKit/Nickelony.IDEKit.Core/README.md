# Nickelony.IDEKit.Core

Dependency-free text primitives and editor-neutral building blocks for editor hosts and language tooling,
targeting `net8.0` with nullable reference types. It holds the text model that every package reads (snapshots,
ranges, positions and the edit kernel), the engines that bindings and hosts call, such as comment scanning,
auto-closing, indentation, find and replace, diffing, bookmarks and line status, and the engine-free
primitives that several packages share, such as request coordination, local paths, color and threading. The
namespaces are the inventory. The language-feature payloads are in `Nickelony.IDEKit.LanguageFeatures`, the
provider lifecycle in `Nickelony.LanguageServer.Provider.Abstractions`, and the workspace-edit result contracts
in `Nickelony.IDEKit.Workspace.Views`.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.Core --prerelease
```

A minimal scan: wrap the text in a snapshot, resolve the code range before the
first comment, and mask the comments in a copy:

```csharp
using Nickelony.IDEKit.Core.Scanning;
using Nickelony.IDEKit.Core.Text;

const string line = "let total = 1; // running total";
var snapshot = new StringTextSnapshot(line);
var syntax = new CommentSyntax("//", null, StringLiteralStyles.DoubleQuoted);

TextRange codeRange = CommentOperations.GetCodeRange(line, syntax);
string code = codeRange.GetTextFrom(snapshot).TrimEnd();       // "let total = 1;"
string masked = CommentOperations.MaskComments(line, syntax);  // comments become spaces

Console.WriteLine($"{code.Length} code chars, masked length {masked.Length}");
```

## Remarks

- **Offsets are zero-based UTF-16 code-unit indexes**; line numbers are one-based on the line-numbered
  surfaces and zero-based on `TextLineMap` and `DiagnosticAnchorResolver`. Line lengths and end offsets exclude
  terminators, and LF, CRLF, and lone CR terminate a line.
- **Indentation, trailing-whitespace trimming, and line-comment toggling recognize spaces and tabs only**, so any
  other Unicode whitespace is content; comment scanning and auto-closing follow `char.IsWhiteSpace`.
- **Out-of-range input splits by type.** `StringTextSnapshot` and `TextRange` throw. `TextLineMap` clamps
  out-of-range lines, offsets, and positions, and `TryGetOffsets` returns `false` for a range whose end
  precedes its start.
- **A no-op returns the original instance.** The `CommentOperations` string overloads, `WhitespaceOperations`,
  `TrimTrailingWhitespaceFormatter`, and `LineTerminatorNormalizer` return the same instance when nothing
  changed, so a no-op is detectable by reference.
- **Comment scanning is an approximation, not a parser.** Each `StringLiteralStyles` flag models one quote
  shape; a host that has a tokenizer or grammar should use that tokenizer or grammar for full-fidelity scanning.

The [Core guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/CoreGuide.md) owns the text
conventions, the line-shaped inputs, the behavior rules the signatures do not carry, and the deliberate
trade-offs.

## License

MIT © 2026 Kewin Kupilas.
