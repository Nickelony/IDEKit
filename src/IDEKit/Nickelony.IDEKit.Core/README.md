# Nickelony.IDEKit.Core

Dependency-free text primitives, editing utilities, host-independent contracts,
request coordination, persistence, bookmarks, and theming for editor hosts and
language tooling. The package targets `net8.0`.

This file is the adoption guide (conventions, quick start, and the slice map);
the normative per-member contracts live in the XML documentation of the types
named below.

## Getting started

Install the package from your configured feed (the library ships from a local
feed while it is in preview):

```powershell
dotnet add package Nickelony.IDEKit.Core
```

A minimal scan: wrap the text in a snapshot, resolve the code range before the
first comment, and mask comments in place:

```csharp
using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Text;

const string line = "let total = 1; // running total";
var snapshot = new StringTextSnapshot(line);
var syntax = new CommentSyntax("//", null, StringLiteralStyle.DoubleQuoted);

TextRange codeRange = CommentOperations.GetCodeRange(line, syntax);
string code = codeRange.GetTextFrom(snapshot);                     // "let total = 1;"
string masked = CommentOperations.MaskComments(line, syntax);  // comments become spaces

Console.WriteLine($"{code.Length} code chars, masked length {masked.Length}");
```

## Requirements

.NET 8.0 or later (the package targets `net8.0` and is built with nullable reference types
enabled).

## Dependencies

None: the package targets `net8.0` and carries no package dependencies.

## What's inside

The package is organized into vertical feature slices, each with its own
namespace (the `Text` slice is listed first because every other slice builds on
it):

- `Nickelony.IDEKit.Core.Text` - the text model: immutable snapshots, lines,
  ranges, and edit operations (`ITextSnapshot`, `StringTextSnapshot`,
  `TextRange`, `TextPosition`, `TextPositionRange`, `TextEditOperation`),
  the clamped zero-based line map (`TextLineMap`), the line-terminator normalizer
  (`LineTerminatorNormalizer.NormalizeToLineFeeds`), the shared offset validation
  (`TextOffsetValidation`), plus the shared line splitter
  (`TextLineSplitter`) and plain-text
  normalization of markup (`BacktickFenceTextNormalizer.NormalizeForPlainText`, which
  takes the line terminator used to join retained lines). The normalizer stays in
  this package because it is dependency-free text tooling for documentation
  payloads: a host can strip Markdown fences without taking a dependency on an
  IntelliSense/UI package, and the language-server tier (the Lua signature-help
  parser) consumes it because it is the same dependency-free text tooling for its
  own documentation payload. Three line shapes are
  available: snapshots expose `ITextLine` (offset/length/number),
  `IndentationTextLine` carries a line's content, terminator, and start offset
  (without line lengths or numbers) for indentation workflows, and
  `TextLineEnumerator` is the internal span-based splitter that backs both.
- `Nickelony.IDEKit.Core.AutoClosing` - the editor-neutral auto-closing model
  and resolver (`TextAutoClosingResolver`, `TextAutoClosingRequest`,
  `TextAutoClosingResult`, `TextAutoClosingDeletionRequest`, `TextAutoClosingOptions`,
  `TextAutoClosingPair`,
  `TextAutoClosingPairKind`, `TextAutoClosingAction`, `TextAutoClosingActionKind`,
  `TextAutoClosingProvenance`). The resolver evaluates the
  configured pairs against an `ITextSnapshot` and resolves an insert (including wrapping a
  selection), a skip of existing closing text, or no action; it also resolves the pair that a
  pair deletion removes.
  - Resolution reads only `TextLength` and `GetCharAt` and finds line ends
    with a terminator scan, so a snapshot with lazy line storage stays lazy on the typing
    path.
  - Insertion provenance reaches the resolver through a caller-supplied tracking
    callback, and a `null` callback means nothing is tracked (no skips in `Auto` mode).
  - A token typed directly after the same token is left to normal text input, so token
    runs can be built. Every quote-like pair follows that rule, and so does a bracket-like
    pair whose opening token and closing text coincide (for example `|`); the opening token
    of an asymmetric pair still repeats, so the inner `(` of `(())` inserts its closing text.
- `Nickelony.IDEKit.Core.Bookmarks` - the bookmark persistence contract
  (`IBookmarkStore`) and its sidecar implementation (`BookmarkSidecarStore`), which
  stores sorted, de-duplicated one-based line numbers in a per-document sidecar whose
  extension the host supplies. Bookmark models keep their bookmarks in memory; the store
  only persists them, and saves and loads perform synchronous I/O on the calling thread,
  so a host should not call it from a per-change handler.
- `Nickelony.IDEKit.Core.Comments` - comment scanning, masking, removal, and
  continuation-marker detection (`CommentOperations`, `CommentSyntax`, `CommentSpan`,
  `CommentKind`, `CommentSpanEnumerator`, `StringLiteralStyle`, `ContinuationOperations`),
  plus the editor-neutral line-comment planner (`TextLineCommentPlanner` with its
  `TextLineCommentRequest`, `TextLineCommentEdit`, and `TextLineCommentAction` model), which computes commenting,
  uncommenting, and toggling edits for the lines a selection touches.
  Line comments end at the next
  line terminator (CR, LF, or CRLF), which stays outside the comment span, and
  whitespace immediately before the delimiter belongs to the span. A line
  that holds only a comment also absorbs its indentation and the single line
  terminator that precedes it, so removing it removes the whole comment line while
  blank lines above it stay intact. A comment-only first line has no preceding
  line terminator; removing it leaves an empty first line.
- `Nickelony.IDEKit.Core.Diagnostics` - the shared diagnostic range model and
  severity vocabulary (`TextDiagnosticSegment`, `TextDiagnosticSeverity`). A segment
  pairs a zero-based document range with a severity; `None` marks a diagnostic that
  does not specify one, and `Error`, `Warning`, `Information`, and `Hint` have stable
  numeric values that are not a precedence order. Protocol packages map their own
  numbers explicitly instead of casting them.
- `Nickelony.IDEKit.Core.Diffing` - changed-line detection between two
  line-split texts (`LineDiffer`).
- `Nickelony.IDEKit.Core.Editing` - the edit kernel and the edit-application
  boundary: neutral edit preparation,
  incremental editing, and line-state caching (`TextEditKernel`,
  `TextEditInput`, `TextEditPreparationResult`, `TextEditSkipResult`,
  `TextEditPreparationIssue`,
  `TextEditPreparationIssueKind`, `PreparedTextEdits`, `TextEditRequest`, `ITextEditTarget`,
  `IVersionedTextEditTarget`, `ITextEditTargetVersion`,
  `TextIncrementalEditCalculator`,
  `TextIncrementalEdit`, `TextRangeOffsetResolver`). `TextRangeOffsetResolver` resolves a selectable
  range for empty or reversed positions; its word fallback takes a caller-supplied
  character rule and shares its boundary walk with `IdentifierOperations`.
  `TextEditPreparationResult` carries the prepared
  `PreparedTextEdits` batch, and `PreparedTextEdits.MapOffset` maps offsets over
  it without repeating the ordering validation. `TextEditKernel.Prepare` rejects a
  batch with any malformed or conflicting entry, while
  `TextEditKernel.PrepareSkippingInvalidEntries` skips such entries and reports
  them through `TextEditSkipResult`, so a caller that must keep a usable batch (a
  completion commit) still gets one.
- `Nickelony.IDEKit.Core.Formatting` - document formatting contracts and
  whitespace conversion (`ITextDocumentFormatter`,
  `TrimTrailingWhitespaceFormatter`, `WhitespaceConverter`).
- `Nickelony.IDEKit.Core.Highlighting` - the editor-neutral, regex-based
  highlighting model consumed by editor bindings (`RegexHighlightingRule`,
  `RegexHighlightingSpan`, `RegexHighlightingStyle`) and the validated set built
  from it (`RegexHighlightingRuleSet`), which rejects a pattern that can match
  empty text or uses `RegexOptions.RightToLeft` before any rule enters the set.
- `Nickelony.IDEKit.Core.Identifiers` - identifier-extraction helpers for
  completion, hover, and definition navigation scenarios (`IdentifierOperations`,
  `IdentifierCharacterPolicy`, `IdentifierSpanMode`).
- `Nickelony.IDEKit.Core.Indentation` - language-independent indentation
  helpers (`IndentationOperations`, `IndentationTextLine`) and the policy
  contracts (`IIndentationPolicy`, `IndentationContext`) consumed by editor
  toolkits.
- `Nickelony.IDEKit.Core.LineStatus` - the line-status source contract
  (`ILineStatusSource`) consumed by margins and
  indicators that mark document lines: sources report one-based marked line
  numbers sorted ascending, must not throw during a consumer's read pass, and
  may raise change notifications from any thread (the subscriber marshals).
- `Nickelony.IDEKit.Core.Notifications` - the generic change-notification contract
  (`IChangeNotificationSource`) that consumers subscribe to so they can refresh when
  a source's marked content may have changed.
- `Nickelony.IDEKit.Core.Navigation` - the text-editor navigation identity
  (`NavigationLocation`): the logical file path, caret offset, selection range,
  and an optional preferred document line for scroll restoration, with identity
  equality that excludes the preferred line and a configurable path comparison.
- `Nickelony.IDEKit.Core.Parsing` - the incremental line-state cache
  (`IncrementalLineStateCache`) that carries a parser continuation state across
  the lines of a document. States are computed lazily under a lock, so its
  transition delegate should be fast and pure - readers serialize while a
  transition runs, the first affected line's start state survives `ApplyEdit`,
  and the remainder is recomputed from the new snapshot.
- `Nickelony.IDEKit.Core.Pathing` - the default case-comparison policy for local
  file-system identity paths (`LocalPathComparisonPolicy`): case-insensitive on Windows
  and macOS and ordinal elsewhere, with explicit `CaseSensitive` and
  `CaseInsensitive` overrides. The policy covers case only; it normalizes nothing.
- `Nickelony.IDEKit.Core.Requests` - asynchronous request invalidation
  and request classification (`RequestTokenSource`, `LatestRequestCoordinator`,
  `RequestHandle`, `RequestOutcome`). The coordinator covers both coordination
  levels: `RunAsync` awaits work and publishes results through a current-state
  check, while `BeginRequest`/`IsCurrent` admit and validate a request that the
  caller completes itself (a canceled `RequestHandle.CancellationToken` is a
  rejection, the same rule a run applies). Admission returns the request's
  `RequestHandle`, which pairs its identifier with its cancellation token, so a
  manual pipeline never has to pair a "latest token" read with an identifier.
  A run's cancellation source belongs to the coordinator, which releases it when
  the run completes; a host-driven request's source belongs to the host that holds
  its handle. `RequestTokenSource` stays the token-only option for
  callers that validate results themselves and need no cancellation; its tokens
  are `long` values.
- `Nickelony.IDEKit.Core.FindReplace` - text-level find and replace
  primitives (`FindReplaceText`, `TextSearchQuery`). Every match helper accepts a
  `TextSearchQuery` that bundles the pattern, options, and timeout; the two builders
  (`BuildPattern`, `BuildRegexOptions`) take their raw inputs instead. The default is
  the infinite timeout and a negative timeout is rejected (the framework's
  `Regex.InfiniteMatchTimeout` is accepted
  and means the same). The helpers reuse a bounded internal cache keyed by pattern,
  options, timeout, and - for case-insensitive patterns that keep the current culture - the captured culture (`RegexCache`), so repeated searches over one pattern do not
  re-parse it. Hosts that pass user-entered patterns should supply a
  finite timeout to bound catastrophic backtracking; a timeout throws
  `RegexMatchTimeoutException`.
- `Nickelony.IDEKit.Core.Persistence` - sidecar line persistence for
  line-marker features (`SidecarLineFile`). Entries are one-based line numbers,
  one per line with a `\n` terminator; an empty set deletes the sidecar, while a non-empty set
  whose entries are all below one fails without touching the existing sidecar;
  `Save` replaces it through a temporary file; and `Save`/`TryRestore` report
  file-operation failures as a failed result instead of throwing (an invalid
  extension is still rejected with `ArgumentException`). The line numbers have no built-in meaning; the
  caller supplies the sidecar extension (for example `.bkmrk`), and it is
  validated per call on every platform.
- `Nickelony.IDEKit.Core.Themes` - named theme resolution with alias and
  default-selection support (`ThemeCatalog<TTheme>`). The catalog carries theme identity
  only (names, aliases, ordering, default selection), so a host without a view tier can
  resolve its configured theme before a presentation layer exists; the view tier builds
  the actual theme objects.

The IntelliSense feature contracts and payloads (completion, diagnostics,
hover, navigation, and signature help) live in the separate
`Nickelony.IDEKit.IntelliSense` package, which depends on this package for
the text primitives such as `TextRange`. The provider lifecycle (document
open/update/close/rename, cancellation, and startup/capability events) stays in
the `Nickelony.LanguageServer.Abstractions` package.

## Conventions

| Concept | Policy |
|---|---|
| Offsets | Zero-based UTF-16 code-unit indexes |
| Line numbers | One-based for snapshots, diffing, and the incremental line-state cache; `TextLineMap` and `TextRangeOffsetResolver` use zero-based line indexes |
| Line lengths and end offsets | Exclude line terminators |
| Line terminators | LF, CRLF, and lone CR are recognized by the line-aware types; CRLF counts as one terminator |
| Whitespace alphabets | Indentation, trailing-whitespace trimming, and line-comment handling (`CommentOperations.IsBlankOrStartsWithLineComment`, `TextLineCommentPlanner`) recognize spaces and tabs only, so any other Unicode whitespace is content |
| Blank lines | A blank line is empty or consists only of whitespace; the row above decides which whitespace, and a `null` line text counts as blank where a member accepts it |
| Line terminators vs delimiters | "Delimiter" names comment delimiters only (`CommentSyntax.LineCommentDelimiter`); CR, LF, and CRLF are line terminators (`IndentationTextLine.Terminator`) |
| Parameter vocabulary | A raw text operand is `text`, a line's content is `lineText`, and a whole document passed to a formatter is `content` |
| Null content text | `StringTextSnapshot`, `TextLineSplitter.Split`, `TextLineMap.Build`, and `TextIncrementalEditCalculator.Compute` treat a `null` content argument as empty; `BacktickFenceTextNormalizer` returns `null` when the input or the result is blank; other content arguments throw `ArgumentNullException` (`CommentOperations.IsBlankOrStartsWithLineComment` accepts `null` and treats it as blank) |
| No-op identity | The transform helpers (`CommentOperations` string overloads, `WhitespaceConverter`, `TrimTrailingWhitespaceFormatter`, `LineTerminatorNormalizer`) return the original instance when nothing changed, so a no-op is detectable by reference |
| Identifier vocabulary | "identifier" names the feature area, "token" names a resolved span (`IdentifierOperations.FindTokenSpan`), and "word" names the caret-adjacent text that is being typed (`IdentifierOperations.GetWordEndingAt`) |
| Record-struct defaults | Payload record structs (queries, requests, contexts, results) document that a `default` instance carries `null` text members despite the annotations; construct payloads through their initializers or factories |
| Out-of-range coordinates | `StringTextSnapshot` throws; `TextLineMap` clamps (see [Choosing a line-shaped input](#choosing-a-line-shaped-input)), so hosts that may pass stale coordinates pick the type whose policy matches |
| Invalid ranges | `TextRange` throws `ArgumentOutOfRangeException`; `TextRange.GetText(string)` names `text` and parenthesizes the offending range component so a stale range is easy to identify. The line/character boundary types (`TextPosition`, `TextPositionRange`) are deliberately lenient and clamp when converted to offsets (see `TextPosition`) |

## Choosing a line-shaped input

Three entry points produce the line shapes the slices consume; pick by what the
caller needs:

| Shape | Use when | Policy differences |
|---|---|---|
| `StringTextSnapshot` | Line metadata (`Lines`, `ITextLine`) is read repeatedly | Out-of-range coordinates **throw**; the line table is materialized on first line access |
| `TextLineMap` | Coordinates may be stale or come from a different revision | Out-of-range line indexes and characters **clamp** |
| `TextLineSplitter.Split` | A plain `string[]` is enough | No metadata; one string per line |

The package is deliberately toolkit-agnostic: editor-generic concepts and
contracts that every editor binding reuses (the `NavigationLocation` record,
the line-status source contracts, the diagnostic segment) live here, because
they are text-editor concepts - caret, selection, and scroll positions - that
no framework-typed API is required to express. Only framework-typed editor
surfaces stay in the editor-binding packages (for example the AvalonEdit
binding); a host whose editor model does not match those concepts adapts at its
binding boundary instead. The multi-file workspace-edit
result contracts (`WorkspaceEditApplicationResult`,
`WorkspaceDocumentChange`, and related types) live in
`Nickelony.IDEKit.Workspace`. Host-independent editor utilities that any host
can reuse (for example sidecar line persistence) live here.

## Comment scanning in depth

The scanning helpers are dependency-free approximations, not a parser: each
`StringLiteralStyle` flag models one quote shape and documents the dialects it
approximates, so a host that already has a tokenizer or grammar should use it for
full-fidelity scanning. The styles combine as flags on `CommentSyntax.StringStyle`.

`ContinuationOperations.EndsWithContinuationMarker` detects a statement-continuation
marker at the end of a line - a single character or a multi-character sequence, and
after a trailing line or block comment the language's `CommentSyntax` declares:

```csharp
bool continues = ContinuationOperations.EndsWithContinuationMarker(
    "total = 1 + 2 + ...",
    new CommentSyntax("%", null, StringLiteralStyle.None),
    "...");   // true
```

The single-character overload takes the marker as a `char` (PowerShell's trailing
backtick), and the sequence overload's `markerMustBePrecededByWhitespace` models Visual
Basic, where an identifier may itself end with the marker character:

```csharp
bool ps = ContinuationOperations.EndsWithContinuationMarker(
    "Get-ChildItem `",
    new CommentSyntax("#", null, StringLiteralStyle.None),
    '`');   // true

var vb = new CommentSyntax("'", null, StringLiteralStyle.None);
bool vbc = ContinuationOperations.EndsWithContinuationMarker(
    "Dim total As Integer = 1 + _", vb, "_", markerMustBePrecededByWhitespace: true);   // true
bool vbi = ContinuationOperations.EndsWithContinuationMarker(
    "Dim foo_", vb, "_", markerMustBePrecededByWhitespace: true);   // false
```

A `CommentSyntax` that carries a block pair recognizes block comments alongside line
comments, so a block opener inside a `//` comment does not start a block comment.
`BlockCommentSyntax.AllowNesting` models nested comments such as Lua's
(`new CommentSyntax("--", new BlockCommentSyntax("--[[", "]]", allowNesting: true), StringLiteralStyle.LongBracketQuoted)`).
The string-literal styles are stated explicitly: the C family passes
`DoubleQuoted | TripleDoubleQuoted`, while a language without string literals passes
`None`:

```csharp
var cStyle = new CommentSyntax("//", new BlockCommentSyntax("/*", "*/"),
    StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted);

int start = CommentOperations.FindComment(
    "let x = 42; /* note */", cStyle)?.DelimiterStart ?? -1;   // 12 (the first comment, line or block)

string cleaned = CommentOperations.RemoveBlockComments(
    "a /* c */ b", cStyle);   // "a  b" (surrounding whitespace kept)

string masked = CommentOperations.MaskBlockComments(
    "a /* c */ b", cStyle);   // same length, comment replaced by spaces
```

An unclosed comment extends to the end of the text, and comment delimiters are fixed
strings, so parameterized long-bracket forms such as Lua's `--[==[` are not modeled.
Passing `null` or an empty string as the line-comment delimiter disables line-comment
awareness; passing `null` for the block pair disables block-comment awareness.
String-aware scanning also ignores delimiters inside multi-line strings, so a
C-family syntax includes `TripleDoubleQuoted` and a `//` inside a raw string is not a
comment:

```csharp
int start = CommentOperations.FindComment(
    "string s = \"\"\"\n// not a comment\n\"\"\";", cStyle)?.StartOffset ?? -1;   // -1
```

`CommentOperations.EnumerateComments` returns a `ref struct` enumerator that walks
every comment span in a single forward pass and supports `foreach`; like all
`ref struct`s it cannot cross an `await` boundary or be stored in a field, so keep
its lifetime local to a synchronous method. Prefer the one-shot `CommentOperations`
methods (`FindComment`, `RemoveComments`, `MaskComments`, `GetCodeRange`,
`GetCodeEnd`) unless iterating the spans in order is required.

## Deliberate trade-offs

- `WhitespaceConverter` computes tab columns from UTF-16 code units, so text
  containing wide or zero-width characters is approximated; use host font
  metrics when a display-accurate column is required.
- `StringTextSnapshot` materializes one line entry per line on first line access
  and caches it, so repeated `Lines` reads allocate nothing; a very large document
  pays that build cost on its first line read, so use the metadata-light `TextLineMap`
  when the richer line model is not needed.
- `LatestRequestCoordinator` never releases a cancellation source before the owner's lifetime ends:
  releasing a superseded host-driven source would break the documented late registration, and
  releasing a run's source at supersede time would break it for a delegate that is still running.
  Neither release is observable through a token, so the sources are held instead.

## License

MIT © 2026 Kewin Kupilas.
