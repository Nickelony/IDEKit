# Language features guide

`Nickelony.IDEKit.LanguageFeatures` holds the editor- and protocol-agnostic language-feature contracts and
payload records, and `Nickelony.IDEKit.LanguageFeatures.Controllers` holds the controllers, session helpers and
resolvers that drive them, none of which names an engine type; the family builds on both. This guide covers
the conventions those types follow, the completion model and its resolve lifecycle, the payload shapes, and the
per-feature notes that do not belong on a signature.

## Text and position conventions

- All request contracts are asynchronous and take immutable snapshots: a provider method takes a request record and a
  cancellation token, and the record carries the document identifier and the document snapshot text of a
  document request, and the feature-specific context (an offset, a symbol name, a symbol filter, or a workspace
  query).
- Providers must be safe to call from any thread and hold no document state.
- Every request record except `WorkspaceSymbolRequest`, which names no document, carries a required
  `DocumentId`, and a blank identifier throws `ArgumentException`. A language-server provider maps it to the
  document, and a local provider may ignore it.
- Position-addressed request records carry zero-based UTF-16 offsets (`CompletionRequest`,
  `HoverRequest`, `SignatureHelpRequest`, `ReferenceRequest`, `RenameRequest`), as does the
  code-action request (`CodeActionRequest`) for the requested range.
- An offset member is named for what it addresses:
  - `CaretOffset` for a request made at the caret (`CompletionRequest`, `SignatureHelpRequest`,
    `CodeActionSelection`);
  - `HoveredOffset` for a request made at the pointer (`HoverRequest`);
  - `SymbolOffset` for a symbol the request names (`ReferenceRequest`, `RenameRequest`, and the optional offset of
    `DefinitionRequest`).
- Requests that identify content by name or filter, and the whole-document requests, carry no position
  (`DefinitionRequest`, which also accepts an optional symbol offset, `DocumentSymbolRequest`,
  `WorkspaceSymbolRequest`, `FormattingRequest`, `DiagnosticsRequest`).
- Every response payload bound to the requested snapshot uses the same offsets (`HoverInformation.Range`,
  document symbols, semantic tokens, and completion text-edit ranges).
- The exceptions use zero-based line and character positions (`TextPositionRange`): `DefinitionLocation`,
  `ReferenceLocation`, `WorkspaceSymbolLocation`, and `TextEdit`. A definition can name a document the provider has not
  materialized, so the host converts when it opens the target.
- A `DefinitionLocation` line counts LF, CRLF and a lone CR as line terminators, as `TextLineMap` does, so
  `TextLineMap.GetOffset` over the target document's text converts its positions.
- A request always travels with the exact text its offsets index. Core's `TextLineMap` (`GetOffset` and
  `GetPosition`) converts between offsets and positions, so hosts convert at their boundary in one place.

## Vocabulary policy

- An enum vocabulary is closed. A class-based vocabulary is open: it accepts custom identifiers, and unrecognized
  values are mapped at the payload or protocol boundary.
- **The completion taxonomy mirrors the protocol's `CompletionItemKind`**: every protocol kind has a
  well-known member whose identifier matches the protocol name, so providers map protocol kinds one-to-one
  instead of collapsing them onto renderable categories (the mapping to numeric protocol values lives in
  `Nickelony.LanguageServer.Provider`).
- `Generic` is the presentation fallback for an item whose producer cannot supply a category (it is not a
  protocol kind), and a member with no protocol counterpart is a library-only extension.
- `CompletionItemKind.CreateCustom` defines a category the host's editor distinguishes. It rejects the
  identifier of a well-known kind, such as `Generic`, and equality compares the normalized identifier
  ordinally. A renderer that does not recognize a custom identifier uses `Generic` presentation while
  preserving the original `Identifier` value, so custom categories need no registry.
- `CompletionContext` is an open vocabulary (`Invoked` by default); a host defines custom triggers the same
  way, and the `Invoked` and `TriggerCharacter` identifiers are reserved. `CompletionContext.ForCharacter`
  names the character a user typed, and any character qualifies, a whitespace character included.
- `MarkupContent` rejects an undefined `MarkupKind` at construction. `Diagnostic` does not
  validate `Severity`, so an undefined value reaches the renderer, which chooses its presentation.
- `SignatureHelpTriggerKind` is closed because signature triggering is a protocol context (invoked, trigger
  character, content change), unlike completion triggering.
- Semantic-token type and modifier names are plain string constants because the token vocabulary is open-ended
  and hosts map unknown names onto a default presentation.
- `IdentifierVocabulary.Normalize` trims and rejects blank, and the rule is identical for every open-vocabulary
  type.
- **Extension points are shaped deliberately**: a typed discriminator (`DefinitionDiscriminator`) names a
  known navigation target; opaque host data (`CodeAction.Data`, `DocumentSymbol.Data`) carries
  data the library never inspects and hands back unchanged. A new seam prefers the typed form when the set of
  values is known.
- Controller option records and presentation-state records live in `LanguageFeatures.Controllers` because they
  name no toolkit type. Window geometry as plain numbers (`CompletionWindowOptions`) lives there too, whereas
  window mechanics and brush-typed themes stay with the editor bindings, in their presentation options.
- A presentation state (`CompletionPresentationState`, `SignatureHelpPresentationState`) is an immutable
  record struct that its controller publishes through `CurrentPresentation`. The controller replaces the whole
  value on each change, so a host reads one consistent value and never changes it.

## Completion model

- The completion types sit in two packages: `LanguageFeatures.Completion` holds the item, request and provider
  contracts, and `LanguageFeatures.Controllers.Completion` holds the controller, the session kernel with its filters, the
  options and the presentation state, with `Commit` for the commit policy and planner, and `Snippets` for the
  snippet expander and its model.
- Completion items carry the protocol ordering data (`SortText`, `IsPreselected`, `Priority`), and a
  snippet-format item keeps its insertion or edit text verbatim with raw tabstop syntax, which
  `SnippetExpander.Expand` turns into the visible text plus a flat placeholder model.
- The library never rewrites snippet text on the wire. The editor bindings' default adapter inserts the
  expansion through the editor's snippet, which owns tabstop navigation and mirroring, while the commit's one
  document update groups the undo ([snippet commits](EditorLanguageFeaturesGuide.md#snippet-commits)).
- `CompletionInsertionResolver.ResolveSnippet` gives a host with its own completion data the expansion to navigate.
- **The kernel keeps no per-call state and no completion cache**: every decision invokes the provider
  again, so a host that re-runs the kernel while typing re-queries the provider each time, whether or not
  the underlying protocol marked the list incomplete.
- Incompleteness, caching, and retriggering are host policy. `GetDirectiveAsync` passes its cancellation token to the
  provider and throws `OperationCanceledException` for a canceled token even when the provider ignores it, and a host
  that supersedes in-flight work cancels the token and discards stale results with a Core `LatestRequestGate` or the
  item stamps.
- Building the request materializes the snapshot as a string, so a call may allocate a document-sized copy. The
  caret is validated before the snapshot text is materialized, and an invalid word or replacement range is
  rejected before the copy, so neither pays for the string.
- `CompletionSessionKernel.GetDirectiveForWordSpanAsync` takes a `CompletionWordSpan` in place of the
  configured locator, and its range must contain the caret, as the LSP requires of a replacement range; a span
  elsewhere, such as an unset `default` one away from the document start, throws.

### Snippet grammar

- The expander understands numbered tabstops (`$1`, `${1}`), default-text placeholders (`${1:default}`), choice
  placeholders (`${1|a,b|}`, with the first choice embedded as the default), nesting, and the escapes `\$`,
  `\}`, `\\`, plus `\,` and `\|` inside a choice.
- A construct outside that subset is emitted literally with the `$` kept, and scanning continues inside the
  construct. Such constructs are a named placeholder or variable, a digit body with unsupported syntax, a choice
  with a zero index, an empty choice element, a missing `|}` terminator, and a `|` not followed by `}`.
- A `${` without a matching close brace follows the same literal rule, and a default-text body at the
  maximum nesting depth is kept literal while its text is still scanned.
- The expansion is a data model only: it holds no navigation, rendering, or undo behavior.

## Completion resolve lifecycle and merge rules

A language-server-backed provider attaches `CompletionItem.ResolveCallback`; a host resolves lazily with
`ResolveAsync` and merges the richer response with `WithResolvedContent`. The lifecycle runs on the item and
in the host: `CompletionController` neither resolves nor merges an item. The table below is the normative
per-field rule set.

The resolved item's value wins for the display and metadata fields (`Detail`, `Documentation` with its
kind, `Kind`, and `InsertTextFormat`) whenever it supplies one. The commit and edit fields keep the current
item's value and fall back to the resolved item only when the current item left them unset; a never-assigned
value counts as unset. `Tags` is the union of both:

| Field | Merge rule |
| --- | --- |
| `Detail` | resolved when non-blank |
| `Documentation` (text and kind) | resolved when the resolved documentation is non-blank |
| `Kind` | resolved when the resolved item explicitly set one |
| `TextEdit`, `SortText` | adopted only when the current item lacks them |
| `InsertTextFormat` | resolved when the resolved item explicitly set one |
| `Tags` | union of both |
| `CommitCharacters`, `AdditionalTextEdits` | adopted only when the current item has none |
| `InsertText` | adopted when the current item has no non-null value and no edit payload (a null assignment is the absent state; assign the label itself to pin the fallback) |
| `FilterText` | adopted when the current item never set it |
| `Label`, `Priority`, `IsPreselected`, request stamps | always from the current item |

### Host-side lifecycle

A host runs the completion calls in this order:

1. `CompletionSessionKernel.GetDirectiveAsync` filters the provider's items by the word at the caret. It never
   resolves an item. A host whose answer arrives after the user typed on passes it through
   `CompletionSessionKernel.TryRetarget`, which moves it onto the grown word or reports that the caret left the
   word, so the host discards the answer ([late answers](EditorLanguageFeaturesGuide.md#answers-that-arrive-after-typing)).
2. `CompletionItem.ResolveAsync` runs the item's resolve callback when the host needs richer content. It
   returns the item unchanged when no callback is attached.
3. `CompletionItem.WithResolvedContent` merges the resolved item into the current one, by the table above.
   Skip this step when the callback already returned a merged item, as the language-server providers do.
4. The commit path uses `TextEdit?.NewText` when it is non-null, and `InsertText` otherwise (the label when
   `InsertText` is unset). It uses the edit's range while that range still fits the current document, and
   `CompletionCommitPlanner` extends a range that ends before the caret up to the caret, never past the live completion segment's end.

Steps 2 and 3 run on an item whose provider attached a resolve callback. The callback below answers with the
detail that the list left out, and the merge adopts it:

```csharp
using Nickelony.IDEKit.LanguageFeatures.Completion;

async Task<CompletionItem> ResolveDetailAsync(CancellationToken cancellationToken)
{
    var item = new CompletionItem("Parse")
    {
        ResolveCallback = _ => Task.FromResult(new CompletionItem("Parse") { Detail = "int Parse(string text)" }),
    };

    CompletionItem resolved = await item.ResolveAsync(cancellationToken);

    // The merge keeps the label and the insertion data of item and adopts the detail of resolved.
    return item.WithResolvedContent(resolved);
}
```

The merged item carries no resolve callback, so a second `ResolveAsync` returns it unchanged.

- A host that supersedes in-flight pipeline runs admits them on the core `LatestRequestGate`.
- Item stamps (`RequestDocumentVersion`, `RequestGeneration`) keep items that outlive the pipeline, such as
  entries of an open completion session resolved asynchronously, attributable to the document state they
  were produced for. `WithRequestContext` rebases the stamps, and `WithoutTextEdit` drops a resolved edit
  payload.
- A host that rebases an open item onto a newer document version applies both, so the commit uses the plain
  insertion text instead of an edit range captured against older text. `AdditionalTextEdits` stay on the item with
  their ranges of the older text, so the host drops such an item instead of committing it.

## Payload conventions

- **Value-like payloads are records** (for example `CompletionRequest`) and record structs (for example
  `CompletionTextEdit`). Payload constructors store supplied values as-is apart from rules such as:
  - required-argument null guards;
  - the documented checks and normalizations: `CompletionRequest`, `HoverRequest`, `SignatureHelpRequest`,
    `ReferenceRequest`, and `RenameRequest` reject an offset outside the document text, `CodeActionRequest`
    rejects a range that extends past the document text, `Diagnostic` rejects a blank message,
    `SignatureHelpContext` rejects an undefined trigger kind, and `SemanticToken` trims and deduplicates
    modifiers;
  - the blank-identifier rejection on the custom completion-trigger and item-kind factories;
  - the symbol-name trimming on `DefinitionRequest`;
  - the word normalization on `CompletionWordSpan`.
- Request records keep record equality, which compares the document text, so two requests over different text
  stay unequal. They are call data rather than high-volume dictionary keys, and a host that keys a dictionary
  by request projects the members it needs.
- **Payloads such as `SemanticToken`, `DocumentSymbol`, and `SignatureInformation` are classes**
  whose construction validates or normalizes their arguments and captures their collections.
- `CompletionItem` is also a class and normalizes in its accessors; create it with
  `new CompletionItem("label")` and set optional fields with an object initializer. Required reference
  arguments throw `ArgumentNullException`.
- Value-like payloads compare structurally; a host that needs another comparison projects the relevant
  components. A record whose member is a collection compares that member by reference unless the type
  documents otherwise: `SnippetExpansion.Placeholders` compares by reference, while `TextDocumentEdit` and
  `WorkspaceEdit` compare their edits element-wise.
- The signature payloads (`SignatureInformation`, `SignatureHelpInformation`) use reference equality rather
  than structural equality, because a structural comparison would compare the owned parameters or signatures
  list by reference and give a partial result; a caller compares the members it needs.
- Payload collections are owned read-only snapshots. The shared helpers (`CompletionFilter`,
  `DocumentSymbolOutlineBuilder`) return caller-owned lists, except that a non-empty word that matches nothing
  yields a shared empty array from the filter, which callers must not mutate.
- Blank or whitespace text values are generally tolerated and passed through unless a type documents otherwise
  (for example `CompletionItem` trims `Detail`, and `MarkupContent` trims plain text and keeps
  Markdown unchanged). Required display strings reject blank input; optional strings normalize blank to absent.
- Documentation carries its markup kind in one value: `CompletionItem`, `HoverInformation`, `SignatureInformation`
  and `ParameterInformation` hold a `MarkupContent`, and a string converts implicitly to plain-text content. A
  server's Markdown signature documentation keeps its kind and its code fences, so a host that shows signature help
  as plain text strips the fences itself.

## Feature notes

### Code actions

- `CodeActionSelection` is host request-policy input, not a provider contract: a host builds one from the
  editor's document, caret, and selection for a request that settled after the debounce delay, and passes it
  to its request builder.
- The selection carries an `ITextSnapshot`, so building it copies no text. A request builder that vetoes the
  request never pays for a copy, and one that builds a `CodeActionRequest` reads the text once with `GetText`.
- An action carries an edit, a `CodeActionCommand`, opaque `Data`, or any of them.
- A provider that offers actions cheaply and computes their edits on demand also implements `ICodeActionResolver`,
  and one whose actions carry commands implements `ICodeActionCommandRunner`; the language-server provider
  implements both explicitly, as it does the trigger-character contracts.
- The controller of the editor tier calls the resolver and the command runner only for the action the user chose,
  as the [editor language-features guide](EditorLanguageFeaturesGuide.md#code-actions) says.
- A resolver returns the action unchanged when the document text differs from the text it was offered for, whether
  the document changed before the call or while the provider computed the edit.
- The language-server provider sends a command once, without a request timeout and without a retry across a
  connection boundary, because a second send could repeat the command's change.
- `CodeActionRequest.Diagnostics` names the diagnostics the host reports for the range, and `Only` filters the
  action kinds. A provider that is given none of them uses what it knows: the language-server provider sends the
  diagnostics it cached for the request's exact text.

### Diagnostics

- `Diagnostic.ContainsOffset` treats an empty range as containing exactly its offset, so a zero-length
  diagnostic stays reachable through exact-offset selection. A host that looks for the diagnostics near a
  range calls `TextRange.Intersects` on `Diagnostic.Range`, where touching ranges intersect.
- `DiagnosticSegmentConversion.Project` throws for a `null` list or a `null` entry, so a host finds a bad
  list where it projects it.
- `DiagnosticRangeConversion.FromLineAndCharacter` turns the zero-based line, the zero-based character and the
  optional length of a host analysis into a `TextRange` against a `TextLineMap`, which the caller builds once per
  text. A `null` length covers the rest of the line, so character 0 with a `null` length is the whole line.
- The conversion clamps a coordinate instead of throwing, so one bad coordinate cannot fail the diagnostics of a
  whole project:
  - a negative value counts as zero;
  - a line past the end gives an empty range at the end of the text;
  - a character past the end of its line gives an empty range at the end of the line.
- A line past the end differs from `TextLineMap.GetOffset`, which clamps to the last line, because an empty range
  at the end keeps a compiler's end-of-file diagnostic visible.

### Editing

- `WorkspaceEdit` represents text edits only, so a file create, rename, or delete cannot be expressed.
  A provider fails such a result closed instead of applying a partial edit: the language-server provider returns
  `null` for the whole response, so a rename request yields no edit.

### Hover

- `HoverInformation.DefinitionDiscriminator` is the single typed extension channel: a provider that recognizes
  a navigation target supplies a language-specific discriminator, a host passes it to `DefinitionRequest`
  unchanged, and a host that does not recognize it ignores it.
- `HoverDecision` is hover evaluation input, not a provider contract or request snapshot; the
  host selects which diagnostic to surface when its provider offers more than one, because the presentation
  renders exactly one message.

### Document symbols

- `DocumentSymbolRequest.FilterText` has no protocol counterpart. `DocumentSymbolFilter.FilterByName` is the
  shared rule: a blank filter keeps every symbol, a symbol matches when its name contains the filter without
  regard to case, and a symbol whose descendant matches stays visible. A provider applies it to the response
  or documents its own rule.
- Unlike the protocol's document symbol, both ranges are optional and deprecation annotations are not
  modeled. Providers should keep `SelectionRange` contained in `Range` when both are known, but the library
  stores ranges as supplied and does not validate containment, because a provider may supply partial range
  data.
- `DocumentSymbolOutlineBuilder` produces one grouping level; a caller that needs deeper nesting constructs
  `DocumentSymbol` instances directly. `DocumentSymbolOutlineHooks` holds the selectors
  of one item type; the name and kind selectors are required.

### Workspace symbols

- `IWorkspaceSymbolProvider` answers a query with the symbols of a whole workspace. The provider owns the matching
  rule, so a host ranks symbols that the provider already chose and hides none of them.
- A `WorkspaceSymbol` is flat. It names the symbol that contains it in `ContainerName` and carries no children, and
  it classifies itself with `DocumentSymbolKind`, the one symbol vocabulary of the library.
- `WorkspaceSymbolLocation.Range` is `null` while the provider has not computed it, as when a language server lists
  symbols without ranges. A host that opens such a symbol asks `IWorkspaceSymbolResolver` for the range first.
- The resolver returns a symbol unchanged when it already has a range, when another provider listed it, and when an
  earlier connection of the server listed it, because the opaque data of a symbol means something to one server on
  one connection.
- A language server lists the symbols of every file of its workspace, so `WorkspaceSymbolLocation.DocumentId` names
  documents that no editor has open.
- The language-server provider starts its stopped server for a search and answers once the server has initialized,
  bounded by the request timeout, and an empty list ends the wait while the start goes on.
- LuaLS and Roslyn answer with ranges, and the entries that carry a bare `{ uri }` location and a resolve round trip
  are specified but not sent by either, so the tests of those paths use synthetic answers.

### Signature help

- `SignatureHelpInformation.ActiveParameterIndex` applies the LSP precedence. The active signature's own
  selection wins when specified, and an explicit `None` means no active parameter. Otherwise the payload-level
  selection applies: `NotSpecified` and an out-of-range index select the first parameter, and `None` reports
  none. A signature without parameters always reports `null`.
- `WithActiveSignature` re-resolves the active parameter for the newly selected signature using the same
  rule.

### Navigation

- `DefinitionLocation.DocumentId` is an opaque provider-defined identifier (a path, a URI, or a logical
  key); `null` or blank means the requested document, and a non-empty value names another document whose
  opening is the host's.
- `SelectionRange` identifies the definition's name, is expected to be contained in `TargetRange`, and the
  host navigates to its start, or to `TargetRange.Start` when it is `null`.
- `DefinitionResolver` resolves through asynchronous callbacks and resolves an offset outside the snapshot to
  `null`.
- `HoverDefinitionResolver`, in `Hover`, composes a hover provider and a definition provider to resolve the symbol
  under the pointer.
- `DefinitionRequest` names the symbol and carries its `SymbolOffset` when the caller has one, so a language server
  resolves by position and a name-catalog provider resolves by name.

### Semantic tokens

The slice ships payloads, well-known name constants, and the `ISemanticTokensSource` contract, because tokens
reach a host as a push: the source raises an event when the tokens of a document change. The language-server
provider also requests `textDocument/semanticTokens/full` after each synchronization and when the server asks
for a refresh. Hosts convert their own token models, and `Nickelony.LanguageServer.Provider` ships the shared
protocol-token conversion.

## Providers and sources

The `I{Feature}Provider` interfaces are the shared contract for in-process and custom providers: one interface
per feature, each with one method that takes a request record and a cancellation token and returns a task. A
host that owns local catalogs (an editor host, or a language package that answers from local state) implements
them directly and completes them synchronously where the work is cheap. A language-server-backed provider
projects server payloads onto the shared values.

A feature whose results reach the host without a request is a source instead: `IDiagnosticsSource` and
`ISemanticTokensSource` raise an event when the current values of a document change, and return the latest
values on demand. A producer that answers a diagnostics request for one snapshot implements
`IDiagnosticsProvider`. An editor of one document subscribes through the `Subscribe` extension of
`Nickelony.IDEKit.LanguageFeatures.Controllers`, which delivers the current values first and then each change of
that document on the UI thread:

```csharp
IDisposable subscription = source.Subscribe(documentId, dispatcher, diagnostics => Underline(diagnostics), pathComparer);
```

The first delivery matters for a second editor of an open document, which would otherwise show nothing until the
source publishes again. A [diagnostics
controller](EditorLanguageFeaturesGuide.md#diagnostics) subscribes this way and pulls from a provider too.

A consumer depends on the one interface of the feature it uses, so it mocks that interface alone. The aggregate
language-server provider implements every interface at once.

### A diagnostics source over a project analysis

`ProjectDiagnosticsSource` is the `IDiagnosticsSource` of a host whose analysis covers a whole project, such as a
compiler that reads every script and reports problems in any of them. The host calls `HandleProjectChanged` for
each change, and the source calls the analysis once the project has been quiet for
`ProjectDiagnosticsSourceOptions.SettleDelay`.

- **The analysis is a host hook.** `ProjectDiagnosticsSourceHooks.AnalyzeAsync` takes a cancellation token and
  returns the diagnostics of each document that has any, keyed by document id.
- **The call stays on the dispatcher thread.** The source calls the hook there and does not move the call off it,
  so the hook captures the text it needs synchronously, such as the text of the open editors, and then offloads the
  compile itself.
- **At most one analysis runs and one waits.** The source cancels the token of the run that a later change
  supersedes, and it starts the next analysis only after that call returned, so a hook that ignores its token never
  runs twice at once.
- **A raised set is current.** The source drops the result of a superseded run even if the run completes, so it
  raises a set only when no change was reported after the analysis started. The
  [`IDiagnosticsSource` contract](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.LanguageFeatures/Diagnostics/IDiagnosticsSource.cs)
  states this rule for every source.
- **Each changed document raises once.** The source raises `DiagnosticsUpdated` on the dispatcher thread for each
  document whose diagnostics differ from the previous analysis, and a document that had diagnostics and has none
  now gets an empty set, so an editor clears its underlines.
- **A change that a subscriber reports during the raise takes effect from the next analysis.** The rest of the raise
  still happens.
- **A failed analysis keeps the previous diagnostics.** An analysis that throws, returns `null`, returns a `null`
  list, entry or task, or cancels itself without a request is logged, and the source raises nothing for it. The set
  that stays describes the text of the last successful analysis.
- **Disposal is final.** `Dispose` runs on the dispatcher thread, cancels the pending and the running analysis, and
  the source raises nothing afterwards.

The source has no analysis until the first `HandleProjectChanged`, so a host calls it once when it opens the
project. The adapter below wraps a compiler that reads unsaved text and reports a path, a one-based line, a
zero-based character and a length for each problem, where a length of 0 means the rest of the line. It passes the
text of every open editor as the unsaved text, because a document store such as `IWorkspaceDocumentReader` trails
the editors by the publish delay of their views, and a result computed from it can describe text from before the
last keystroke:

```csharp
var source = new ProjectDiagnosticsSource(new ProjectDiagnosticsSourceHooks(AnalyzeAsync), dispatcher, logger: logger);

// From each editor's change event, and after a save or a reload from disk.
source.HandleProjectChanged();

Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>> AnalyzeAsync(CancellationToken cancellationToken)
{
    // On the dispatcher thread: the text of every open editor, which the compiler reads in place of the file.
    Dictionary<string, string> editors = openEditors.ToDictionary(editor => editor.FilePath, editor => editor.Text);

    return Task.Run(() => CompileAsync(editors, cancellationToken), cancellationToken);
}

async Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>> CompileAsync(
    Dictionary<string, string> editors,
    CancellationToken cancellationToken)
{
    CompilerReport report = await compiler.AnalyzeAsync(editors, cancellationToken).ConfigureAwait(false);

    // The compiler canceled its run: that is a cancellation, not a project without problems.
    if (report.IsCanceled)
        throw new OperationCanceledException(cancellationToken);

    var result = new Dictionary<string, IReadOnlyList<Diagnostic>>();

    // A problem with no file or no line belongs in an output pane, so the adapter skips it.
    foreach (var file in report.Problems.Where(problem => problem.Path.Length > 0 && problem.Line >= 1).GroupBy(problem => problem.Path))
    {
        // One line map per file, over the text the compiler read: the editor's text, else the file on disk.
        string text = editors.TryGetValue(file.Key, out string? open)
            ? open
            : await File.ReadAllTextAsync(file.Key, cancellationToken).ConfigureAwait(false);
        var lineMap = TextLineMap.Build(text);

        result[file.Key] = [.. file.Select(problem => new Diagnostic(
            problem.IsError ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            problem.Message,
            DiagnosticRangeConversion.FromLineAndCharacter(lineMap, problem.Line - 1, problem.Column, problem.Length == 0 ? null : problem.Length)))];
    }

    return result;
}
```

A file that no editor shows is read from disk, by the compiler and by the adapter alike. A change on disk between
the two reads shows at worst until the next analysis, which the host's file watcher triggers with
`HandleProjectChanged`.

An editor of one document takes the sets of the source through the `Subscribe` extension, or through a
[diagnostics controller](EditorLanguageFeaturesGuide.md#diagnostics), which also says how the controller shows a
set. The keys of the result are the ids that the editors subscribe with. A host whose ids differ only in case sets
`DocumentIdComparer` on `ProjectDiagnosticsSourceOptions` and on `DiagnosticsControllerOptions`, and passes the
comparer to the `documentIdComparer` parameter of `Subscribe` for an editor it subscribes itself.

A set reaches a `Subscribe` callback one dispatcher turn after the source raised it, so a keystroke handled in
between shows the set shifted by that edit until the next analysis replaces it.

## What stays outside these packages

- **Framework-typed presentation surface** ships from editor binding packages such as
  `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`, because it is written against one editor framework's
  types. The [editor binding guide](EditorBindingGuide.md#4-intentional-per-binding-pieces) lists those pieces,
  among them the controller factories and the sources that read the pointer, the caret, and the selection of a
  text area, brush-typed themes, and presenters.
- The window and menu options that hold only plain numbers and flags (the completion window geometry, the
  code-action menu height) live in the controllers package.
- The framework-neutral state those surfaces display (the hover evaluation and the code-action request
  records) lives in the feature slices of the contracts and controllers packages, and the controllers whose
  request logic names no engine type live in the controllers package.
- A binding supplies the engine to such a controller through a source or a delegate, and a factory builds the
  controller over a text area.
- `CompletionPresenter` stays in the bindings because it drives an engine window, and it takes its commit policy
  and commit planner from the controllers package.
- The **language-server provider lifecycle** (document open/update/close/rename, startup events, the capability
  snapshot) lives in `Nickelony.LanguageServer.Provider.Abstractions`, which depends on the contracts package
  for the request contracts and the shared payload values, and never on the controllers package.
- **LSP protocol and connection** types live in `Nickelony.LanguageServer.Client` and stay at the protocol
  boundary as DTOs; the protocol kind bridges live in `Nickelony.LanguageServer.Provider`.
- **Text primitives** (snapshots, ranges, line maps, edit kernels) live in `Nickelony.IDEKit.Core`; these packages
  depend on it for `TextRange` and related primitives.
