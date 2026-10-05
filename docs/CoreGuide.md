# Core guide

`Nickelony.IDEKit.Core` is the editor-neutral text model the rest of the family builds on. This guide covers
the conventions that model follows, the choice between the line-shaped inputs, comment and string scanning,
the text-edit kernel, request coordination, the behavior rules of auto-closing, find and replace, line
diffing, indentation, and bookmarks, and the trade-offs the package accepts deliberately. The types of every
other area document themselves.

## Text conventions

- **Offsets** are zero-based UTF-16 code-unit indexes, so a surrogate pair counts as two units.
- **Line numbers** are one-based on the line-numbered surfaces (snapshots, diffing, the incremental
  line-state cache); `TextLineMap` and `DiagnosticAnchorResolver` use zero-based line indexes.
- **Line lengths and end offsets exclude the terminator.** LF, CRLF, and lone CR terminate a line, and CRLF
  counts as one terminator.
- **A text's line terminator style** comes from `LineTerminatorDetector`, which reports `Mixed` when the text
  uses more than one terminator and `None` when it has none. `GetDisplayName` names a style for a status bar, and
  for `None` it names the platform's terminator, which the editor engines insert for the first new line.
- **A plan converts the line terminators of a snapshot without copying it.** `LineTerminatorPlanner` returns one
  edit per terminator of another style, so a binding applies a conversion as one batch. `Mixed` and `None` name no
  single terminator, so a plan for them throws.
- **A local path is an identity**, so `LocalPath.Normalize` accepts only a fully qualified path and rejects a
  relative one instead of resolving it against the process's current directory. The result keeps the input
  casing; compare normalized paths with a `LocalPathComparisonPolicy`, and test containment with
  `LocalPath.IsDirectoryOrDescendant`, which matches only at a directory separator.
- **Indentation, trailing-whitespace trimming, and line-comment toggling recognize spaces and tabs only**, so any
  other Unicode whitespace is content; comment scanning, auto-closing, and range anchoring follow
  `char.IsWhiteSpace`.
- **A blank line** is empty or holds only spaces and tabs. A `null` line text counts as blank where a member
  accepts it.
- **"Delimiter"** names comment delimiters only; CR, LF, and CRLF are line terminators.
- **Parameter vocabulary**: a raw text operand is `text`, a line's content is `lineText`, and a whole
  document passed to a formatter is `content`.
- **A `null` content argument** is empty for `StringTextSnapshot`, `TextLineSplitter.Split`,
  `TextLineMap.Build`, and `TextIncrementalEditCalculator.Compute`; every other content argument throws
  `ArgumentNullException`, except `CommentOperations.IsBlankOrStartsWithLineComment`, which treats `null` as
  blank.
- **A no-op returns the original instance** for the transform helpers (`CommentOperations` string overloads,
  `WhitespaceOperations`, `TrimTrailingWhitespaceFormatter`, `LineTerminatorNormalizer`), so a caller detects a
  no-op by reference.
- **Identifier vocabulary**: "identifier" names the feature area, "token" names a resolved span
  (`IdentifierOperations.FindTokenSpan`), and "word" names the caret-adjacent text that is being typed
  (`IdentifierOperations.GetWordEndingAt`).
- **Payload record structs** (queries, requests, contexts, results) carry `null` text members on a `default`
  instance despite the annotations, so build them through their initializers or factories.

### Out-of-range and invalid input

The package picks the policy per type, so a host selects the type whose policy matches its risk:

| Type | Policy |
|---|---|
| `StringTextSnapshot`, `TextRange` | Throws `ArgumentOutOfRangeException` |
| `TextLineMap` | Clamps line indexes and characters |
| `TextPosition`, `TextPositionRange` | Deliberately lenient; endpoints clamp when converted to offsets, and a reversed range fails |

`TextRange.GetText(string)` names `text` and parenthesizes the offending range component, so a stale range is
easy to identify.

## Choosing a line-shaped input

Pick the entry point by what the caller needs:

| Shape | Use when | Policy |
|---|---|---|
| `StringTextSnapshot` | Line metadata (`Lines`, `ITextLine`) is read repeatedly | Out-of-range coordinates throw; the line table is built on first line access |
| `TextLineMap` | Coordinates may be stale or from a different revision | Out-of-range line indexes and characters clamp |
| `TextLineSplitter.Split` | A plain `string[]` is enough | No metadata; one string per line |

## Comment and string scanning

`Core.Scanning` finds comments, string literals and statement-continuation markers in text, and `Core.Comments`
plans line-comment edits on top of it. The scanning helpers are dependency-free approximations, not a parser:
each `StringLiteralStyles` flag models one quote shape and documents the dialects it approximates, so a host that
already has a tokenizer or grammar should use it for full-fidelity scanning. The styles combine as flags on
`CommentSyntax.StringStyle`.

A `CommentSyntax` carries the line delimiter, an optional block pair, and the string-literal styles. A block
pair recognizes block comments alongside line comments, so a block opener inside a `//` comment does not start
one, and `BlockCommentSyntax.AllowNesting` models nested comments:

```csharp
var cStyle = new CommentSyntax("//", new BlockCommentSyntax("/*", "*/"),
    StringLiteralStyles.DoubleQuoted | StringLiteralStyles.TripleDoubleQuoted);

var luaStyle = new CommentSyntax("--", new BlockCommentSyntax("--[[", "]]", allowNesting: true),
    StringLiteralStyles.LongBracketQuoted);
```

A `null`, empty, or whitespace-only line delimiter disables line-comment awareness, and a `null` block pair
disables block-comment awareness. An unclosed comment extends to the end of the text, and delimiters are fixed
strings, so parameterized long-bracket forms such as Lua's `--[==[` are not modeled.

String-aware scanning ignores delimiters inside multi-line strings, so a `//` inside a raw string is not a
comment:

```csharp
int start = CommentOperations.FindComment(
    "string s = \"\"\"\n// not a comment\n\"\"\";", cStyle)?.Range.Offset ?? -1;   // -1
```

`CommentOperations` offers one-shot helpers (`FindComment`, `RemoveComments`, `MaskComments`, `GetCodeRange`,
`GetCodeEnd`) and `EnumerateComments`, which returns a `ref struct` enumerator over every comment span in one
forward pass. A `ref struct` cannot cross an `await` boundary or be stored in a field, so keep its lifetime
local to a synchronous method, and prefer the one-shot helpers unless iterating the spans in order is needed.

A line comment's `CommentSpan` starts at the whitespace before its delimiter on the same line. When only
whitespace precedes the comment on its line, the span also absorbs the one line terminator before that line,
and a CRLF counts as one terminator. The span ends before the comment's own terminator, and it never starts
before the previous comment's end. A block comment's span runs from its opener through its closer, or to the
end of the text when it is unclosed.

Removal and masking differ at a line terminator because of that rule. Removal drops the whole span, so a
comment-only line vanishes together with the terminator before it, and a comment-only first line, which has no
terminator before it, leaves an empty first line. Masking keeps CR and LF, so a masked comment-only line stays
a blank line of its own instead of merging with the line above.

### Statement-continuation markers

`ContinuationMarkerProbe.EndsWithContinuationMarker` reports whether the code on a line ends with a
continuation marker, which is a single character or a multi-character sequence. It strips a trailing line or
block comment that the language's `CommentSyntax` declares first, so a marker followed by a comment still
counts and a marker inside a comment does not:

```csharp
bool continues = ContinuationMarkerProbe.EndsWithContinuationMarker(
    "total = 1 + 2 + ...",
    new CommentSyntax("%", null, StringLiteralStyles.None),
    "...");   // true
```

The single-character overload takes the marker as a `char` (PowerShell's trailing backtick); the sequence
overload's `markerMustBePrecededByWhitespace` models Visual Basic, where an identifier may itself end with the
marker character:

```csharp
bool ps = ContinuationMarkerProbe.EndsWithContinuationMarker(
    "Get-ChildItem `",
    new CommentSyntax("#", null, StringLiteralStyles.None),
    '`');   // true

var vb = new CommentSyntax("'", null, StringLiteralStyles.None);
bool vbc = ContinuationMarkerProbe.EndsWithContinuationMarker(
    "Dim total As Integer = 1 + _", vb, "_", markerMustBePrecededByWhitespace: true);   // true
bool vbi = ContinuationMarkerProbe.EndsWithContinuationMarker(
    "Dim foo_", vb, "_", markerMustBePrecededByWhitespace: true);   // false
```

## Text edits

An edit has one shape per stage, and each type's summary states its stage. `TextIncrementalEdit` is both the diff
that `TextIncrementalEditCalculator` computes and the batch input of `TextEditKernel`: it is a value type because
a document change and a completion commit each build one on the typing path, and a `default` instance carries
`null` text, which the kernel reports as a preparation issue instead of throwing. `TextEditOperation` is the
validated unit that a prepared batch holds. `TextEditRequest` is the command an editor binding applies to its
document, and `LineCommentEdit` is the line-comment planner's output. The language-feature `TextEdit` replaces a
line and character range, and `CompletionTextEdit` carries a completion item's insert and optional replace
ranges.

`PreparedTextEdits` takes operations ordered from highest to lowest source offset and validates them once at
construction, so a target applies the list in order without sorting. `TextEditKernel.Prepare` resolves the
caller's edits against the document and reports the invalid edits and conflicts it finds as
`TextEditPreparationIssue` values on the returned `TextEditPreparationResult`.

`MapOffset` maps an offset from the prepared content to the text after the batch. An offset that coincides with an
insertion stays before the inserted text, so an insertion at the end of the text leaves the old end offset before it.

## Diagnostic anchoring

`DiagnosticAnchorResolver` turns the position range of a diagnostic into an offset range that selects text, and it
falls back to an anchor only when the range maps to zero length or is reversed. The anchor is the word at the
start position, then the start line's non-whitespace content, then one character at the start position, so a
whitespace-only start line anchors one whitespace character. When the start line is empty, the resolver instead
searches forward for the first line with non-whitespace content, then backward for the last non-whitespace
character of a preceding line.

## Request coordination

`LatestRequestGate` publishes a result only while its request is still the newest and no `Invalidate`
call has discarded it:

- A superseded result is discarded even when its compute delegate ignored cancellation.
- A run (`RunAsync`) and a host-driven request (`BeginRequest`) share one request lifetime, so only the newest
  request stays current. The continuation runs on the caller's captured synchronization context by default,
  and blocking on the returned task can deadlock.
- Core stays editor- and document-agnostic: the host supplies the operation state and a `canApply` predicate
  that runs separately from `apply`, so it can reject a result without side effects.
- `RunIfAsync` takes an admission predicate that runs under the gate's lock, so an owner can refuse a
  run atomically with its own state, such as a disposal flag. A refused run reports `Canceled`, calls no
  delegate, and leaves the outstanding request alone.
- A host that awaits its own work begins a request with `BeginRequest`, then publishes only while `IsCurrent`
  or `CanPublish` accepts the handle; `CanPublish` also rejects a handle whose token a newer request or
  `CancelPendingRequest` canceled.
- Cancellation-source ownership belongs to the gate on both paths. A run's source is canceled
  on supersede and by `CancelPendingRequest`, and it is disposed when the run completes. A host-driven
  request's source is canceled on supersede and by `CancelPendingRequest`, and it is never disposed.
- The host holds only the token of a host-driven request and may still register on it after the request is
  superseded, so disposing its source at supersede time would break those registrations.

`RequestLifetime` is the owner-side wrapper of one gate for a controller: it admits a request only while the
owner is alive, applies the result only while the owner is alive, counts the generations that a controller checks
in the step it dispatches to its thread, and holds the token that disposal cancels. A request that the owner or a
newer request canceled reports `Canceled` or `Superseded`, and a request that its compute delegate canceled by
itself while it is still the newest reports `Canceled` too, so a controller tells a cancellation that it asked
for from one that it did not by the state it holds, not by the outcome alone.

`KeyedLatestRequestGate<TKey>` applies the same rule per key. A comparer given to the constructor decides which
keys share a slot, and `Begin` returns a lease whose token a newer request for the key, `Cancel` or the caller's
token cancels, so a host awaits its work inside `using` and releases the slot when the request settles.
`CancelAll` closes the gate: a request that begins afterwards gets an already canceled lease. Closing never
waits for a request in flight.

## Auto-closing

`AutoClosingPlanner` is the stateless half of auto-closing. It reads a snapshot, the caret offset, the typed
text, and a `AutoClosingOptions`, and returns the insert or skip action for the editor binding to apply. The
binding owns input handling, the read-only policy, document edits, and the tracking of text it inserted. The gates
that decide between insert, skip, and wrap are documented on `AutoClosingOptions` and `AutoClosingPair`.
Those flags, such as `SuppressAfterWordCharacter` and `CheckUnmatchedClosingText`, are the whole resolution
policy: no policy interface replaces them until a second host needs rules the flags cannot express.

- **Pair order decides.** The pairs are tried in the order of `AutoClosingOptions.Pairs` and the first pair
  that resolves wins, so each opening token should appear once.
- **`TrackedOnly` needs a tracker.** The default `TrackedOnly` mode skips existing closing text, and
  `TryPlanPairDeletion` removes a pair, only when the request's `IsTrackedClosingText` callback reports the
  text at the caret as inserted by the binding. Under `TrackedOnly`, a caller that resolves without that callback gets
  neither, so it selects `Always` or `Never` explicitly.

## Find and replace

`FindReplaceOperations` runs a `SearchQuery` over a string and has no document or UI dependency. The rules the
signatures do not show:

- **An empty pattern is no search text.** The helpers report no match instead of matching at every position, and
  `ReplaceAll` returns the text unchanged.
- **The helpers own the search direction.** A non-empty pattern with `RegexOptions.RightToLeft` throws an
  `ArgumentException`.
- **A finite `MatchTimeout` can throw.** A zero timeout means no timeout, and a match attempt that exceeds a
  finite one throws `RegexMatchTimeoutException`; for `FindAllMatches` it surfaces while the collection is
  enumerated.
- **`FindPreviousMatch` rescans from the start of the text on every call**, so a host that steps through many
  matches enumerates `FindAllMatches` once instead.

## Line diffing

`LineDiffer.GetChangedLineNumbers` compares two line lists and returns a `LineDiffResult` that holds the one-based
numbers of the current lines that are not part of a longest common subsequence with the baseline.

- **It is a marker diff.** The result names inserted or modified current lines, so a deletion-only edit marks no
  line, and it carries no hunks.
- **The search is bounded.** When it exhausts its work budget or recursion depth, it marks the whole affected
  section and sets `LineDiffResult.IsApproximate`, so an approximate result is a superset of the changed lines.
- **The caller's splitter decides what a line contains.** Lines compare ordinally as supplied; the
  [deliberate trade-offs](#deliberate-trade-offs) show how terminators take part.

## Indentation

`IndentationOperations` builds indentation text, `WhitespaceOperations` converts between spaces and tabs,
`IndentationDetector` guesses what a text indents with, `LineTextOperations.GetLeadingWhitespaceLength` measures
indentation, and `IIndentationPolicy` is where a language supplies its own rules. Only spaces and tabs count as
indentation, as the [text conventions](#text-conventions) state.

- **A policy returns the whole indentation.** The returned string replaces the line's leading whitespace, and
  returning the current leading whitespace means no change. With `IndentationRequest.UseSmartIndent` set to
  `false`, a policy returns the current leading whitespace instead of applying language rules.
- **Truncation is by length, not by match.** `TruncateIndentationByUnitLength` removes as many trailing characters
  as the unit has, so a tab unit removes one character from two spaces. An empty unit removes one character, and
  the removal never exceeds the indentation; an outdent that works in columns computes its own result.
- **A non-positive size or level does not throw.** `CreateIndentationUnit` falls back to a fixed width when
  spaces are requested with a non-positive size, and `BuildIndentation` returns the current indentation unchanged
  for a non-positive level.
- **A detection reports its two halves apart.** `IndentationDetector.Detect` follows the guess of VS Code, and
  `IndentationDetection.InsertSpacesDetected` and `SizeDetected` say whether the text showed the style and the size
  or the defaults supplied them. The remarks of `IndentationDetector` give the evidence each half needs, and the
  defaults are parameters, not process state.
- **A size is guessed whenever the style is spaces.** The style can be the default, so a text that shows its size
  but not its style gets a detected size with the default style.
- **A conversion of the indentation leaves the rest of the line alone.** `ConvertIndentationToSpaces` converts the
  tabs of the leading whitespace only, so a tab inside a string or after code stays, while `ExpandTabs` expands
  every tab.
- **A resize works on spaces.** `ResizeIndentation` gives each whole level the new size and keeps a remainder that
  is not a whole level, and it leaves a line whose leading whitespace holds a tab as it is.
- **A plan converts a snapshot without copying it.** `IndentationPlanner` returns one edit per line whose leading
  whitespace changes, by the same rules as `WhitespaceOperations`, so a binding applies a conversion as one batch.
- **A size is bounded.** `IndentationOperations.MaximumSize` is the limit of the engines' own indentation size.
  `WhitespaceOperations`, `IndentationPlanner` and `IndentationDetector` reject a larger size or a non-positive one,
  while `CreateIndentationUnit` takes any size.

## Bookmarks

`IBookmarkStore` persists the one-based line numbers of bookmarks, which a bookmark model otherwise keeps in
memory. `BookmarkSidecarStore` is the bundled implementation: it writes the numbers to a sidecar file next to the
document, so the `documentId` it receives is the path of the document file.

- **`TryLoad` separates failure from empty.** It returns `true` with an empty list when nothing was stored and
  `false` when the load could not be performed. `TrySave` returns `false` for any save that did not persist the
  numbers, and the reason is not part of the contract.
- **The file holds a set, not a sequence.** The numbers are written sorted and de-duplicated, an empty set
  deletes the sidecar, and a non-empty set with no number at or above one fails and leaves the existing sidecar
  untouched.
- **A restore can land on a different line.** The bookmark model matches the numbers against the document when it
  restores them, so edits between a save and a restore can move a bookmark.
- **The I/O is synchronous**, so keep the store off per-change handlers; the
  [deliberate trade-offs](#deliberate-trade-offs) describe what a sidecar save guarantees.

## Deliberate trade-offs

- **Tab columns are UTF-16 based.** `WhitespaceOperations` computes a tab stop from UTF-16 code units, so text
  with wide or zero-width characters is approximated; use host font metrics when a display-accurate column is
  required.
- **`StringTextSnapshot` builds its line table on first line access** and caches it, so repeated `Lines` reads
  allocate nothing, but a very large document pays that build cost on its first line read; use the
  metadata-light `TextLineMap` when the richer line model is not needed.
- **Line comparison follows the caller's splitter.** `LineDiffer` compares, ordinally, the lines the caller
  has already split. Lines from `TextLineSplitter.Split` drop their terminators, so a CRLF-to-LF-only change
  reports no difference; `string.Split('\n')` keeps the CR, so the same change marks every line whose terminator
  changed.
- **`ThemeCatalog` compares names ordinally and case-insensitively** and offers no comparer option, because a
  theme name is an identifier; duplicate names are kept, so keeping names unique and applying display order are
  the host's job.
- **`BookmarkSidecarStore` saves are crash-safe, not power-loss durable.** The write replaces the file
  without flushing it to disk first and does not preserve the previous file's access control list or
  attributes. A reader of a concurrently saved sidecar observes the old or the new contents, and a save blocked
  by another process that holds the destination open without sharing is reported as a failed operation.
