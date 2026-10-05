# Documentation style guide

[`AGENTS.md`](https://github.com/Nickelony/LanguageServer/blob/main/AGENTS.md) owns the documentation rules, and
this page shows them applied as before and after pairs.

## [Rule 1](https://github.com/Nickelony/LanguageServer/blob/main/AGENTS.md#rules) - one sentence, the rest in `<remarks>`

```csharp
// Avoid
/// <summary>Splits text into lines. Line terminators are not part of the returned contents.</summary>

// Prefer
/// <summary>Splits text into lines.</summary>
/// <remarks>Line terminators are not part of the returned contents.</remarks>
```

## [Rule 2](https://github.com/Nickelony/LanguageServer/blob/main/AGENTS.md#rules) - delete the restatement

```csharp
// Avoid
/// <summary>Gets the file containing the reference.</summary>
public string FilePath { get; }

// Prefer
public string FilePath { get; }
```

## [Pages](https://github.com/Nickelony/LanguageServer/blob/main/AGENTS.md#pages) - a bullet list for facts that stand alone

```text
// Avoid
Offsets are zero-based UTF-16 code-unit indexes, so a surrogate pair counts as two units, and LF, CRLF, and
lone CR each end a line, with CRLF counted as one terminator, and a line length excludes the terminator.
```

```text
// Prefer
- Offsets are zero-based UTF-16 code-unit indexes, so a surrogate pair counts as two units.
- LF, CRLF, and lone CR each end a line, and CRLF counts as one terminator.
- A line length excludes the terminator.
```

## [Rule 6](https://github.com/Nickelony/LanguageServer/blob/main/AGENTS.md#rules) - a family and a link replace a roster

```markdown
// Avoid
| Package                           | Languages           |
| --------------------------------- | ------------------- |
| `Nickelony.LanguageServer.Lua`    | Lua                 |
| `Nickelony.LanguageServer.Roslyn` | C# and Visual Basic |
```

```markdown
// Prefer
Each language server has a provider package, and the [package groups](https://github.com/Nickelony/LanguageServer/blob/main/README.md#package-groups)
table lists them.
```

## [Rule 7](https://github.com/Nickelony/LanguageServer/blob/main/AGENTS.md#rules) - a second home links the first

```markdown
// Avoid
## Shared editor source

Shared editor source is one body under `shared/Editor/`, linked into both bindings, with an `#if AVALONIAEDIT`
header that selects the engine `using` and the file-scoped namespace.
```

```markdown
// Prefer
- Shared editor source is one body linked into both bindings.
- The [editor sharing guide](EditorSharingGuide.md) owns the `#if AVALONIAEDIT` header and the namespace rules.
```

## [Pages](https://github.com/Nickelony/LanguageServer/blob/main/AGENTS.md#pages) - link each target where it is published

```markdown
// Avoid
The [editor sharing guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorSharingGuide.md)
owns shared source, and [CONVENTIONS.md](../CONVENTIONS.md) owns naming.
```

```markdown
// Prefer
The [editor sharing guide](EditorSharingGuide.md) owns shared source, and [CONVENTIONS.md](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md)
owns naming.
```

## [Shape](https://github.com/Nickelony/LanguageServer/blob/main/AGENTS.md#shape) - break at a clause end

```text
// Avoid
Cancellation is observed before a file-system mutation starts
, so a `Canceled` result means the file was not written, moved, or deleted.
```

```text
// Prefer
Cancellation is observed before a file-system mutation starts,
so a `Canceled` result means the file was not written, moved, or deleted.
```

## Verify

```powershell
dotnet test .\Nickelony.IDEKit.slnx --filter TestCategory=Repository
```

- `DocumentationGuardTests` fails the run on a banned phrase or a British spelling. `DocumentationReportTests`
  reports the runs past an aim without failing; to list them, run `--filter TestCategory=RepositoryReport`
  with `--logger "console;verbosity=detailed"`.
- Rewording a `///` block can trip `warning ZA0201`. The owner's formatting pass lays it out, so never
  hand-wrap a doc comment.
- No formatter touches Markdown, so a README or a guide is wrapped by hand, and the guard checks its wording
  but not its facts or its links.
