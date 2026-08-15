# Nickelony.IDEKit.IntelliSense

Editor- and protocol-agnostic IntelliSense contracts, payloads, and framework-neutral host-state
records for editor hosts and language tooling. Depends only on `Nickelony.IDEKit.Core`.

## Getting started

Install the package from your configured feed (the library ships from a local
feed while it is in preview):

```powershell
dotnet add package Nickelony.IDEKit.IntelliSense
```

```csharp
using Nickelony.IDEKit.Core.Text;              // StringTextSnapshot, TextRange
using Nickelony.IDEKit.IntelliSense.Completion;

// A provider implements ITextCompletionProvider and returns shared items.
var kernel = new TextCompletionSessionKernel();
var provider = new MyCompletionProvider();
TextCompletionSessionDecision decision = kernel.GetDecision(
    new StringTextSnapshot(documentText),
    caretOffset,
    provider,
    TextCompletionTrigger.Invoked);

if (decision.StartOffset is int startOffset
    && decision.EndOffset is int endOffset
    && decision.Items is { Count: > 0 } items)
{
    ShowCompletionWindow(items, startOffset, endOffset);
}
else if (decision.AllItemsFilteredOut)
{
    // The typed word filtered every candidate out; a host may dismiss a narrowed session
    // instead of keeping stale entries.
    HideCompletionWindow();
}
```

Diagnostics follow the same pattern: a provider returns `TextDiagnostic`
values, and the host selects them by probing the spans with
`TextDiagnostic.ContainsOffset` or `TextDiagnostic.Intersects` and formats them with
its own presentation rules; a hover tooltip that falls back from the exact offset to
the containing line composes both probes. Signature help, hover, definition, and document symbols
expose analogous provider interfaces and request records.

## Requirements

.NET 8.0 or later (the package targets `net8.0` and is built with nullable reference types
enabled).

## Dependencies

`Nickelony.IDEKit.Core` for snapshots, ranges, and text primitives.

## What's inside

This package owns the neutral IntelliSense feature contracts, payloads, and host-state
records shared by editor hosts, synchronous local providers, and asynchronous language-server
providers. It is organized into vertical feature slices, each with its own namespace:

- `Nickelony.IDEKit.IntelliSense.CodeActions` - code-action contracts and payloads:
  `ITextCodeActionProvider` (the synchronous provider contract), `TextCodeActionRequest`
  (the document snapshot plus the zero-based range the provider is asked about), and
  `TextCodeActionItem` (title, kind, preferred flag, opaque host payload). The editor-state
  context (`TextCodeActionContext`) lives in this slice; the controller's timing policy lives
  with the editor binding.
- `Nickelony.IDEKit.IntelliSense.Completion` - completion payloads and
  contracts: `TextCompletionItem`, `TextCompletionItemKind`,
  `TextCompletionTextEdit`, plus the
  session contracts (`ITextCompletionProvider`, `TextCompletionRequest`,
  `TextCompletionTrigger`, `TextCompletionFilter`, `TextCompletionItemFilter`,
  `TextCompletionSessionDecision`, `TextCompletionWordSpan`, `TextCompletionWordLocator`),
  and the shared `TextCompletionSessionKernel` that owns provider
  invocation, current-word filtering, replacement-range validation and handling, and the open /
  filtered-empty / no-op decision (`TextCompletionSessionDecision.NoMatches` reports a word
  that filtered every candidate out), plus the host-state record `TextCompletionRequestSession`
  (the request lifetime shared by a controller pipeline and host-driven requests). The
  completion edit payload uses neutral zero-based UTF-16 offset ranges (`TextRange`) plus
  the edit's replacement text (`TextCompletionTextEdit.NewText`) rather than line and
  character coordinates.
- `Nickelony.IDEKit.IntelliSense.Diagnostics` - synchronous diagnostics
  contracts and payloads: `ITextDiagnosticsProvider`, `TextDiagnosticsRequest`,
  `TextDiagnostic` (severity, message, producer `Source`/`Code` attribution, and
  offsets, with the span probes `TextDiagnostic.ContainsOffset` and
  `TextDiagnostic.Intersects`). `TextDiagnosticSegmentProjection` maps a diagnostic list onto the
  shared renderer segments (`Nickelony.IDEKit.Core.Diagnostics.TextDiagnosticSegment`), so every
  editor binding feeds its diagnostic renderer from the same mapping. Diagnostic hover selection
  policy (for example an exact-offset hit with a containing-line fallback) is host presentation and
  is composed by hosts from those primitives. The severity vocabulary (`TextDiagnosticSeverity`)
  lives in `Nickelony.IDEKit.Core.Diagnostics`, so producers, renderers, and hosts
  share one severity enum.
- `Nickelony.IDEKit.IntelliSense.DocumentSymbols` - document-symbol
  contracts and the neutral outline carrier: `TextDocumentSymbol`,
  `TextDocumentSymbolKind`, `ITextDocumentSymbolProvider`,
  `TextDocumentSymbolRequest`, and the projection-based `DocumentSymbolOutlineBuilder`
  with its `DocumentSymbolProjection<TItem>` selector bundle that projects flat or grouped
  item data into flat or grouped `TextDocumentSymbol` outlines. The protocol kind bridge
  lives with the protocol boundary in `Nickelony.LanguageServer.Client`.
- `Nickelony.IDEKit.IntelliSense.Hover` - hover contracts, requests, and
  payloads: `ITextHoverProvider`, `TextHoverRequest`, `TextHoverInfo`, and
  `TextHoverEvaluationState` (the host's hover evaluation input for one hovered
  offset: request decision, tooltip and diagnostic permissions, and diagnostic
  info). The `TextMarkupKind` used by hover content and completion documentation
  lives in the root `Nickelony.IDEKit.IntelliSense` namespace.
- `Nickelony.IDEKit.IntelliSense.Navigation` - definition navigation
  contracts and payloads: `ITextDefinitionProvider`, `TextDefinitionRequest`,
  `TextDefinitionLocation`, `TextDefinitionDiscriminator`, plus the host-neutral
  `TextDefinitionNavigator`. The navigator owns the resolution policy: it materializes the
  snapshot text once, composes the hover-first symbol lookup over `ITextHoverProvider`
  and `ITextDefinitionProvider`, serves the symbol and offset probes (including the
  task-returning resolver bridges of a language-server-backed host), rejects a
  blank symbol, an offset outside the snapshot, and an out-of-document location, and
  returns a `TextDefinitionNavigationTarget` - the validated in-document position, or a
  cross-document location whose opening stays host-owned. `TextDefinitionLocation`
  carries an opaque provider-defined document identifier plus zero-based target and
  selection ranges in line and character units (`TextPositionRange`).
- `Nickelony.IDEKit.IntelliSense.SemanticTokens` - style-neutral semantic
  token contracts: `TextSemanticToken`, `TextSemanticTokenTypes`,
  `TextSemanticTokenModifiers`. Tokens carry range, type, and modifiers only;
  hosts map them onto their own theme model.
- `Nickelony.IDEKit.IntelliSense.Signatures` - signature-help contracts and
  payloads: `ITextSignatureHelpProvider`, `TextSignatureHelpRequest`,
  `TextSignatureHelpContext`, `TextSignatureHelpTriggerKind`,
  `TextSignatureHelp`, `TextSignatureInformation`, `TextSignatureParameterInfo`, and
  `TextSignatureActiveParameterSelection` (the not-specified / explicitly-none / index selection the
  payload and its signature options carry).

## Behavior and conventions

### Shared helpers

Besides contracts and payloads, the package ships shared behavioral helpers
(`TextCompletionSessionKernel`, `TextCompletionFilter`, `DocumentSymbolOutlineBuilder`) that editor
hosts and language packages consume instead of reimplementing session, filtering, and outline logic,
alongside the framework-neutral host-state records listed above. Diagnostic message formatting,
severity labels, message separators, and hover selection policy are presentation decisions and ship
with editor host packages, not here. The built-in completion filter matches the resolved filter text
(which falls back to the item label when no filter text was set) with an ordinal, case-insensitive
substring test; insertion and edit text are never matched. Matching algorithms and fuzzy ranking are
client-defined: a host that wants fuzzy scoring injects its own filter through
`TextCompletionItemFilter`.

### Completion model

The completion taxonomy mirrors LSP 3.17 `CompletionItemKind`: every protocol kind has a well-known
member with the same numeric identifier, so language providers map protocol kinds one-to-one instead
of collapsing them onto renderable categories (the numeric bridge lives in
`Nickelony.LanguageServer.Client`). `Generic` is the presentation fallback for items whose producer
cannot supply a category (it is not a protocol kind), and `Array`, `Section`, `Directive`,
`Parameter`, and `Namespace` are library-only extensions. The type is an open vocabulary: a host
whose editor distinguishes additional categories defines them with
`TextCompletionItemKind.CreateCustom`, and equality compares the normalized identifier ordinally.
Completion items also carry the protocol ordering data (`SortText`, `IsPreselected`, `Priority`), and
a snippet-format item keeps its insertion or edit text verbatim with raw tabstop syntax, which
`TextSnippetExpander.Expand` turns into the visible text plus a flat placeholder model. Tab
navigation, placeholder linking, and undo grouping stay host behavior; the library never rewrites
snippet text on the wire.

Completion requests carry a neutral `TextCompletionTrigger` (`Invoked` by default); a host that
distinguishes additional interactions defines custom triggers the same way. The kernel keeps no
per-call state and no completion cache: every decision invokes the provider again, so a host that
re-runs the kernel while typing re-queries a list the underlying protocol marked incomplete.
Incompleteness, caching, cancellation, and retriggering are host policy - the synchronous provider
contract carries no cancellation token, so a host that needs to supersede in-flight work schedules
the call itself and discards stale results with the request session or the item stamps. The
semantic-token type and modifier names stay plain string constants because the token vocabulary is
open-ended and hosts map unknown names onto a default presentation.

### Vocabulary and position conventions

Vocabulary policy: vocabularies that identify host- or language-defined categories are open and
accept custom identifiers (`TextCompletionItemKind`, `TextCompletionTrigger`, semantic-token type and
modifier names), while vocabularies that enumerate a protocol-defined set are closed and document
their protocol numerics as lineage (`TextMarkupKind`, `TextCompletionTag`,
`TextCompletionInsertTextFormat`, `TextDocumentSymbolKind`, `TextSignatureHelpTriggerKind`);
unrecognized values are mapped at the payload or protocol boundary. Controller option and
presentation-state records live with the editor bindings, so the neutral surface stays contracts,
payloads, and shared session helpers.

All request contracts are synchronous, stateless snapshots: each request record is immutable and
carries the document snapshot text plus the feature-specific context (an offset, a symbol name, or a
symbol filter). Providers must be safe to call from any thread and hold no document state.

Position-addressed request records carry zero-based UTF-16 offsets (`TextCompletionRequest`,
`TextHoverRequest`, `TextSignatureHelpRequest`), as does the code-action request
(`TextCodeActionRequest`) for the requested range; requests that identify content by name or filter,
and the whole-document diagnostics request, carry no position (`TextDefinitionRequest`,
`TextDocumentSymbolRequest`, `TextDiagnosticsRequest`), and the diagnostics request can additionally
name its document through the optional opaque `TextDiagnosticsRequest.DocumentId`. Every response
payload bound to the requested snapshot uses the same offsets (for example `TextHoverInfo.Range`,
document symbols, semantic tokens, and completion text-edit ranges). The single exception is
`TextDefinitionLocation`, whose target and selection ranges use zero-based line and character
positions because a definition can name a document the provider has not materialized; the host
converts when it opens the target. A request always travels with the exact text its offsets index,
and Core's `TextLineMap` (`GetOffset` and `GetPosition`) converts between the two families, so hosts
convert at their boundary in one place.

## Completion resolve lifecycle and merge rules

A language-server-backed provider attaches `TextCompletionItem.ResolveCallback`;
a host resolves lazily with `ResolveAsync` and merges the richer response with
`WithResolvedContent`. The table below is the normative per-field rule set. The resolved
item's value wins for the display and metadata fields (`Detail`, `Documentation` with its
kind, `Kind`, and `InsertTextFormat`); the commit and edit fields keep the current item's
value and fall back to the resolved item only when the current item left them unset (a
never-assigned value behaves as absent), and `Tags` is the union of both:

| Field | Merge rule |
| --- | --- |
| `Detail` | resolved when non-blank |
| `Documentation` + `DocumentationKind` | resolved together when the resolved documentation is non-blank |
| `Kind` | resolved when the resolved item explicitly set one |
| `TextEdit`, `SortText` | adopted only when the current item lacks them |
| `InsertTextFormat` | resolved when the resolved item explicitly set one |
| `Tags` | union of both |
| `CommitCharacters`, `AdditionalTextEdits` | adopted only when the current item has none |
| `InsertText` | adopted when the current item has no non-null value and no edit payload (a null assignment is the absent state; assign the label itself to pin the fallback) |
| `FilterText` | adopted when the current item never set it |
| `Label`, `Priority`, `IsPreselected`, request stamps | always from the current item |

A merged item keeps the pre-normalization documentation source alongside the normalized value, so a
later `DocumentationKind` change re-normalizes that source instead of the already-normalized text.

Two host-side lifecycle mechanisms cooperate: the request-lifetime session
(`TextCompletionRequestSession` over the core request coordinator) supersedes
in-flight pipeline runs, while item stamps (`RequestDocumentVersion`,
`RequestGeneration`) keep items that outlive the pipeline - for example entries of
an open completion session resolved asynchronously - attributable to the document
state they were produced for; `WithRequestContext` rebases the stamps and
`WithoutTextEdit` drops a resolved edit payload. A commit path chooses its text
per `TextCompletionTextEdit`: the edit's `NewText` when the item carries an edit,
the insertion text otherwise, with the edit's range used while it still fits the
current document.

Payload conventions:

- Value-like payloads are records (for example `TextCompletionRequest`, `TextHoverInfo`,
  `TextDefinitionRequest`, `TextDefinitionLocation`, `TextDefinitionNavigationTarget`,
  `TextDiagnosticsRequest`, `TextDocumentSymbolRequest`, `TextHoverRequest`,
  `TextSignatureHelpRequest`, `DocumentSymbolProjection<TItem>`) and
  record structs (for example `TextCompletionTextEdit`, `TextCompletionSessionDecision`,
  `TextCompletionWordSpan`). Payload constructors
  store supplied values as-is apart from required-argument null guards, the documented
  position checks (`TextCompletionRequest`, `TextHoverRequest`, and
  `TextSignatureHelpRequest` reject an offset outside the document text,
  `TextCodeActionRequest` rejects an unordered or out-of-range span, `TextDiagnostic`
  rejects a reversed span, `TextSignatureHelpContext`
  rejects an undefined trigger kind, and `TextSemanticToken` trims and deduplicates
  modifiers), the blank-identifier rejection on the custom completion-trigger and
  item-kind factories, the symbol-name trimming on `TextDefinitionRequest`, and the word
  normalization on `TextCompletionWordSpan`.
  Request records compare their document text in equality, so they are call data rather
  than high-volume dictionary keys.
- Payloads whose construction normalizes or validates inputs beyond that stay
  classes so the normalization is applied once (for example `TextSemanticToken`,
  `TextDocumentSymbol`, `TextSignatureInformation`). `TextCompletionItem` is also a
  class and normalizes in its accessors; create it with `new TextCompletionItem("label")`
  and set optional fields with an object initializer. Required reference arguments throw
  `ArgumentNullException`.
- Four payload types use structural equality: `TextCompletionItemKind`
  and `TextCompletionTrigger` (normalized identifier), `TextSemanticToken` (range,
  type, and modifier sequence), and `TextDiagnostic` (severity, message, source, code,
  and span); hosts that need other value comparisons project the
  relevant components. A record whose member is a collection compares that member by
  reference (for example `TextSnippetExpansion.Placeholders`).
- Payload collections are owned read-only snapshots, while the shared helpers
  (`TextCompletionFilter`, `DocumentSymbolOutlineBuilder`)
  return caller-owned lists (a filter result with no matches is a shared
  empty array that callers must not mutate).
- Blank or whitespace text values are generally tolerated and passed through unless a
  type documents otherwise (for example `TextCompletionItem` trims `Detail`, and trims
  `Documentation` only when `DocumentationKind` is `PlainText`). Every required display
  string rejects blank input: `TextDiagnostic.Message`, `TextHoverInfo.Content`,
  `TextCodeActionItem.Title`, `TextSignatureInformation.Label`, and `TextDocumentSymbol.Name`
  all throw for a null or whitespace-only value, while optional strings (`Source`, `Kind`,
  `SymbolName`, `Documentation`) treat blank as absent and trim other values.

Two deliberate asymmetries: the SemanticTokens slice ships payloads and
well-known name constants but no provider contract (hosts convert their own
token models, and `Nickelony.LanguageServer.Client` provides the shared
protocol-token conversion), and DocumentSymbols ships the neutral model plus the
synchronous provider contract only - the asynchronous counterpart lives with the
language-server packages (`ILanguageServerIntelliSenseProvider.GetDocumentSymbolsAsync`
in `Nickelony.LanguageServer.Abstractions`).

Extension points are deliberately shaped: a typed discriminator
(`TextDefinitionDiscriminator`) names a known navigation target; opaque host
payloads (`TextCodeActionItem.Payload`, `TextDocumentSymbol.Data`) carry data
the library never inspects and hands back unchanged. New seams should prefer the
typed form when the set of values is known.

The seven synchronous `IText*Provider` interfaces are the shared contract for
in-process and custom providers: a host that owns synchronous catalogs (an
editor host, or a language package that answers from local state) implements
them directly, while a language-server-backed provider wraps its asynchronous
client in a provider that projects server payloads onto the shared values. The
same split applies to the helpers above: they are the synchronous building
blocks a host composes, and a host that cannot call them synchronously (for
example a UI-bound editor) schedules the call itself.

## Adoption notes

The synchronous stack is consumed in production by an external adopter: its
scripting packages implement the `IText*Provider` contracts with
their own coordinators, compose the shared helpers (`TextCompletionSessionKernel`,
`TextCompletionFilter`, `DocumentSymbolOutlineBuilder`),
and rely on the completion item's request stamps and resolve lifecycle for their
asynchronous completion windows. The in-repo binding packages and the
language-server packages consume the same surface (the AvalonEdit binding hosts
the controllers, and the Lua language-server provider projects protocol payloads
onto the shared values), so the synchronous stack has consumers on both sides of
the repository boundary. API changes between preview versions are not accompanied
by compatibility layers; adopters migrate to the current surface.

## What stays outside this package

- **Framework-typed presentation surface** ships from editor binding packages such as
  `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`, because it is written against one editor
  framework's types: host hooks (for example a hover tooltip callback carrying framework
  coordinates), skins, presenters, host sizing policy (for example the code-action menu
  height), and message formatting (diagnostic message composition
  and severity labels). The framework-neutral state those surfaces display (the hover
  evaluation state and the code-action request records) lives in this package's feature slices;
  controller option and presentation-state records live with the editor bindings.
- The **asynchronous provider lifecycle** (document open/update/close/rename,
  request cancellation, startup/capability events, session state) lives in
  `Nickelony.LanguageServer.Abstractions`, which depends on this package for the
  shared payload values.
- **LSP protocol and transport** types live in
  `Nickelony.LanguageServer.Client` and stay at the protocol boundary as DTOs,
  together with the protocol kind bridges (`TextCompletionItemKindConversion`,
  `TextDocumentSymbolKindConversion`).
- **Text primitives** (snapshots, ranges, line maps, edit kernels) live in
  `Nickelony.IDEKit.Core`; this package depends on it for `TextRange` and
  related primitives.

## License

MIT © 2026 Kewin Kupilas.
