# Conventions

The code rules that no compiler or formatter enforces, so a change and its review are judged against one list.
A **Kept by** cell names the guard test of [`Nickelony.Repository.Tests`](tests/Nickelony.Repository.Tests/README.md)
that fails the test run on a break, and when the guard checks only part of the
rule, the cell names that part after "for". A report test that never fails is named only as a pointer, and an analyzer
that the build turns into an error, such as CA2007 for rule C13, is named by its id. A reviewer
checks each part that no guard checks, and each rule marked *review*.

- [`REVIEWING.md`](REVIEWING.md) says when a finding blocks a change, and a rule changes only through an edit to this
  file.
- An allow-list row of a guard, which names an exception and its reason, is part of the rule, so a change that adds
  an exception adds its row.
- CSharpier owns layout, the Stanza analyzers own structure and doc-comment layout, and `.editorconfig` owns the
  casing and prefix rules of identifiers, which surface as `IDE1006` in the IDE and in `dotnet format`. Neither
  a rule here nor a code comment restates or fights them.
- [`AGENTS.md`](AGENTS.md) owns the documentation rules, and the [architecture guide](docs/ArchitectureGuide.md)
  owns the tier, slice, shared-primitive, one-home and internals rules and the reasons for the namespace rules.

## Identifiers

| Rule | Kept by |
| --- | --- |
| **I1** An identifier spells an acronym as a word (`Id`, `Lsp`, `Json`, `Uri`, `Ui`), and prose spells it in capitals. A brand keeps its own casing (`IDEKit`, `MacOS`), and a framework name keeps the framework's spelling inside a longer identifier too (`IOException`, `ToolTip`, the reflected `toolTip` field); the repository's own word is `Tooltip`. | review |
| **I2** A member does not repeat the name of its containing type. | review |

## Type names

A type's last word names its role, and the [vocabulary table](#vocabulary-table) is the one home of each role word.
`VocabularyTable` parses the table and the paragraph of retired words below it, so the header row, the
`plain agent nouns` row label, the paragraph's opening words and its sentence that starts "A type derived" keep
their wording, because the parser finds each one by those words. A bare backticked word in a row's first cell reads
as a test-double prefix, so a role word is written with its dots (`...Set`).

| Rule | Kept by |
| --- | --- |
| **N1** A type whose last word names a role or an agent takes that word from the vocabulary table, and a value or a thing is named for its concept. A role word or an agent noun that the table lacks is added to it in the same change, with its definition. | review; `VocabularyReportTests` lists, without failing, the last words of non-delegate product types that the table does not name |
| **N2** A role word keeps the one meaning that the table gives it, and roles are never interchangeable. A type whose last word `TypeNameGuardTests` restricts to one meaning, such as `Coordinator` or `Gate`, carries a row there that states its job in the table's terms. | `TypeNameGuardTests` for the row of a restricted word; review |
| **N3** No type ends in a word that the paragraph below the vocabulary table retires; a framework-derived type that keeps one carries a row in `TypeNameGuardTests`. | `TypeNameGuardTests`; `TestOrganizationGuardTests` for a test type, except `...Handler` |
| **N4** A type takes no product prefix: it is named for its concept, with the LSP or VS Code name where one exists (`CompletionItem`, `DocumentSymbol`, `ICompletionProvider`), and its namespace tells it apart from a wire or framework type of the same name. An LSP name that is too generic to read alone in its feature's namespace takes its slice (`DocumentSymbolKind` for `SymbolKind`, `CompletionInsertTextFormat` for `InsertTextFormat`). A sample type that wires a library feature into the sample host takes the `Sample` prefix (`SampleHover`, `SampleWorkbench`), and a sample's own implementation of a library contract is named for its concept (`KeywordHoverProvider`). | review |
| **N5** A type begins with `Text` only where text is the subject (`TextRange`, `TextEdit`, `ITextSnapshot`), where it keeps an LSP name (`TextDocumentEdit`), or where it wraps, extends or adapts an engine type or carries a brand (`TextDocumentSnapshot`, `TextMateTokenTheme`). A public type outside the Client package carries a row in `TypeNameGuardTests` for its stem. | `TypeNameGuardTests` for a public type outside the Client package; review |
| **N6** A type never takes its namespace's name (`HoverInformation`, not `Hover`, in `...Hover`). | `TypeNameGuardTests` |
| **N7** An enum ends in `Kind`, `Mode`, `Style`, `Outcome` or `Policy`, or in `Severity` as the LSP's `DiagnosticSeverity` does, and never in `Result`; a `[Flags]` enum takes a plural noun (`DocumentLeaseKinds`). An enum that keeps an LSP, Win32 or toolkit name (`KeyCode`), ends in another word of the vocabulary table such as `State`, or is named for the property its value sets carries a row in `TypeNameGuardTests` with its reason. `Status` is never the classification of a completed operation, which is an `...Outcome`. | `TypeNameGuardTests` for the enum words; review for `Status` |
| **N8** The payload of an `EventHandler<TEventArgs>` event is named `{EventName}EventArgs`. | `TypeNameGuardTests` |
| **N9** A wire structure takes the specification's name as its stem and one suffix: `...Params` for the parameters of a request or notification, `...Response` for what a request returns, `...Capability` for a server capability record, and `...Payload` for any other structure. An enum, an identifier structure and a root name of the specification (`ServerCapabilities`, `WorkspaceFolder`) keep the bare name, and a wire helper of the Protocol namespace carries a row in `TypeNameGuardTests`. | `TypeNameGuardTests` for the suffix; review |
| **N10** "LSP" names the protocol and "language server" names the server. An identifier about the protocol as a whole takes `Lsp` (`LspMethodNames`), and one about the server, its process, a session with it, or a library part that serves that session takes `LanguageServer` (`LanguageServerClient`, `StdioLanguageServerTransport`). In the client and provider packages a public type keeps the `LanguageServer` prefix where it names the server side (`LanguageServerProfile`), and an internal type drops it (`HoverProvider`, `DocumentSynchronizer`) unless the prefix belongs to the owner it names (`LanguageServerFeatureProviderState`) or the bare name collides with a type its file sees. `Protocol` names the wire side of a type that converts between the wire and the model or sends wire messages (`ProtocolRangeConversion`, `ProtocolSender`), and a type in `Nickelony.LanguageServer.Client.Protocol` never repeats the `Protocol` stem. | `TypeNameGuardTests` for `Lsp`; review |
| **N11** A file that sees a model type and a wire type of one name qualifies the wire side through `using Lsp = Nickelony.LanguageServer.Client.Protocol;`. | review |
| **N12** A top-level test type outside `Nickelony.Repository.Tests` that is no test class and no abstract `...TestsBase` takes its first or last word from the test rows of the vocabulary table, or ends in `Attribute`, `Exception` or `EventArgs`. Any other name carries a row in `TestOrganizationGuardTests` with its reason. | `TestOrganizationGuardTests` |

### Vocabulary table

| Word | Meaning | Examples |
| --- | --- | --- |
| `...Result` | an envelope that carries an operation's value or failure | `ProcessRunResult` |
| `...Outcome` | an enum that classifies how an operation ended | `ProcessRunOutcome` |
| `...Issue`, `...IssueKind` | one problem found in an input, and its classification | `TextEditPreparationIssue` |
| `...Failure`, `...FailureCodes` | a failure payload with a string `Code`, and a static class in the raising package that holds its codes, because one failure record can cross packages that each add codes | `WorkspaceOperationFailure`, `WorkspaceOperationFailureCodes` |
| `...Hooks` | a host delegate bundle: a positional `readonly record struct` for up to three delegates, at least one of them required, otherwise a sealed class with init-only properties, `required` for each required delegate | `FileReloadHooks<T>`, `HoverControllerHooks` |
| `...Primitives` | the raw operations of a platform API, held as a record of delegates that a component calls, so a test substitutes them as T1 allows | `WorkspaceFileSystemPrimitives` |
| `...TestHooks` | the internal sealed observer of an interleaving point that T1 allows | `WorkspaceDocumentStoreTestHooks` |
| `...State` | the condition of one owner: live and mutable inside a stateful type, or the immutable value a type publishes about itself; an input a host supplies is not a state | `ConnectionState`, `CompletionPresentationState` |
| `...Snapshot` | an immutable copy of an owner's data | `ITextSnapshot`, `WorkspaceDocumentSnapshot` |
| `...Options` | an options bag, shaped as K1 says | `AutoClosingOptions` |
| `...Context` | what an operation needs from its surroundings: request data in LanguageFeatures, as the LSP's `SignatureHelpContext` does, or a collaborator bundle in an internal type | `SignatureHelpContext` |
| `...Request` | the complete input of one call | `ProcessRunRequest`, `AutoClosingRequest` |
| `...Information` | a feature's answer whose LSP name is the slice name; an LSP or Win32 structure whose own name ends in the word keeps that name (`ParameterInformation`) | `HoverInformation`, `SignatureHelpInformation` |
| `...Decision` | a host's answer to a question the library asks | `HoverDecision` |
| `...Directive` | an instruction one collaborator produces for another | `DocumentSynchronizationDirective`, `CompletionSessionDirective` |
| `...Source` | produces its product without a request: current state a consumer pulls, change events, or ids, the way the BCL's `CancellationTokenSource` produces tokens; a push-model feature such as diagnostics or semantic tokens is a source too | `ILineStatusSource`, `IChangeNotificationSource`, `IDiagnosticsSource` |
| `...Provider` | answers a request record, and its asynchronous contract method is `Get{Answer}Async`, named for what it answers (`GetHoverAsync`, `GetFormattingEditsAsync`); an aggregate provider implements each feature contract of one language over one session | `ICompletionProvider`, `ILanguageServerFeatureProvider` |
| `...Service` | the host-facing facade of one feature: an editor service applies a Core operation to an engine editor, and `KeyBindingService` is the host-facing facade of the key bindings | `AutoClosingService`, `KeyBindingService` |
| `...Controller` | drives one editor feature for one editor: it owns the request state and the debounce, publishes presentation state, takes host decisions as hooks and is disposable; a language-feature controller whose request logic names no engine type lives in `LanguageFeatures.Controllers`, and a binding supplies the engine through a per-engine factory, whereas the quick-pick controller drives no language-feature contract and lives in the QuickPick family | `SignatureHelpController`, `QuickPickController` |
| `...Kernel` | the engine-free decision core of one operation: it validates and orders work, or hosts injected strategies | `TextEditKernel`, `CompletionSessionKernel` |
| `...Planner` | an engine-free, stateless core that turns one request into the edits or actions to apply; its product is a `...Plan`, or a type named for its content when one value is the whole decision (`AutoClosingAction`, `LineCommentEdit`) | `LineCommentPlanner` |
| `...Plan` | the immutable result of a Planner: the decisions, with no engine type, that an emitter or an applier carries out | `WorkspaceEditPlan`, `MarkdownBlockPlan` |
| `...Resolver` | looks a value up from its inputs, with no side effect | `DefinitionResolver` |
| `...Coordinator` | sequences two or more collaborators that it does not own through a flow or a lifecycle and decides which result applies; it may own the lock or queue of that flow, and a type whose whole job is the state it guards is a Gate or a Tracker | `FileReloadCoordinator`, `DeleteLeaseCoordinator` |
| `...Supervisor` | owns child lifetimes and replaces failed ones | `ConnectionSessionSupervisor` |
| `...Store` | owns records and their persisted form, or is the persistence contract a host implements for them | `IBookmarkStore`, `IKeyBindingOverrideStore` |
| `...Tracker` | holds the state of a set of keyed things, answers queries about it and may raise change notifications | `WorkspaceDocumentTracker`, `UnsavedChangesTracker` |
| `...Set` | a collection of like items that its owner holds and acts on as one unit | `RegexHighlightingRuleSet`, `DocumentReplacementChangeSet` |
| `...Key` | a value type that identifies one item in a lookup or an order | `WorkspaceDocumentKey`, `TextEditOrderKey` |
| `...Dispatcher` | delivers work or input to where it is handled | `IUiDispatcher`, `KeyBindingDispatcher` |
| `...Runner` | runs a unit of work to an outcome under timeouts, retries or a multi-step plan | `ProcessRunner` |
| `...Gate` | decides whether work proceeds now: it admits, supersedes, coalesces or holds it back | `StopGate`, `KeyedLatestRequestGate`, `LineStatusInvalidationGate` |
| `...Scheduler` | orders work per key | `DocumentOperationScheduler` |
| `...Lane` | the serialized run of one key's work, in which each item waits for the one queued before it | `OperationLane` |
| `...Chain` | an ordered list of collaborators of one contract that each get a turn at an input, where the first to handle it ends the turn | `KeyBindingDispatcherChain` |
| `...Coalescer`, `...Debouncer` | merges pending entries per key; holds work until its input settles | `FileChangeCoalescer`, `DispatcherDebouncer` |
| `...Forwarder`, `...Sender`, `...Router` | passes batches, calls or events on to an owner, and may hold back a batch that failed recoverably; sends protocol messages over one connection; routes incoming server messages to their dispatchers | `WorkspaceChangeForwarder`, `ProtocolSender`, `ServerMessageRouter` |
| `...Target` | the object that a framework or an operation directs its calls at: the one an RPC library invokes for the calls it receives, the one an edit applies to, or the one a synchronizer mirrors documents to; or the place an operation leads to, as the LSP's `LocationLink.targetUri` does (`DefinitionNavigationTarget`) | `ClientRpcTarget`, `ITextEditTarget`, `IDocumentSynchronizationTarget` |
| `...Synchronizer` | brings a mirror up to date with its source | `ViewSynchronizer` |
| `...Reporter` | turns a failure into its log entries and events | `StartupFailureReporter` |
| `...Factory` | creates a configured object through `Create` or `Create{Product}`, or, as a delegate, when invoked, such as a live controller or the profile of a language | `CodeActionControllerFactory` |
| `...Builder` | turns one input structure, an options record included, into a derived structure through `Build` | `MarkdownDocumentModelBuilder` |
| `...Parser`, `...Decoder` | a mapping type's suffix follows its entry verb: `Parse` makes a Parser and `Decode` a Decoder | `WorkspaceEditParser`, `SemanticTokensDecoder` |
| `...Conversion` | a conversion between two models; a same-meaning enum pair maps through an explicit `switch` in one Conversion, never a cast | `ProtocolRangeConversion` |
| `...Codec` | converts one model to and from its serialized form in both directions | `WorkspaceTextCodec`, `KeyBindingOverrideCodec` |
| `...Converter` | a serializer converter; the word is reserved for that | `CompletionResponseJsonConverter` |
| `...Normalizer` | returns the canonical form of a value | `LineTerminatorNormalizer` |
| `...Mapper` | maps positions or offsets through a change | `EditorSelectionMapper` |
| `...Probe` | answers one question about a text or a document without changing it | `TextMatchProbe` |
| `...Validation` | a static group of argument checks, named for what it checks | `NumericValidation` |
| `...Operations` | a public static function group of several related entry points over engine-free data; a type with one entry point takes the agent noun (`LineDiffer`), a static noun that mirrors a BCL static class (`LocalPath`) keeps that noun, and an internal static function group keeps a plain descriptive name | `IdentifierOperations`, `CommentOperations` |
| `...Extensions` | a static holder of extension methods, named `{ExtendedType}Extensions`, or `{ExtendedType}{Slice}Extensions` when one type's extensions span several slices, so each holder stays in its slice's namespace; a holder whose name would collide with an adapter's holder takes its concept's name | `TextEditorEditingExtensions` |
| `...FileSystem` | a contract over file I/O that a host can replace | `IWorkspaceFileSystem` |
| `...Catalog` | names a set of entries and looks them up by name or alias | `ThemeCatalog`, `CommandCatalog` |
| `...Theme` | a look: an options-bag record of brushes or colors with a `Default`; `TextMateTokenTheme` keeps TextMate's own term | `MarkdownRenderTheme`, `CompletionWindowTheme` |
| `...Policy` | the choice of a rule: an enum for a closed set of behaviors a caller picks, an options record or struct for a tunable rule, or an interface or class for a rule implemented in code | `KeyBindingConflictPolicy`, `DiagnosticMappingPolicy`, `IIndentationPolicy` |
| `...Presenter` | maps neutral state onto toolkit surfaces and owns their lifetime, at two levels: a feature presenter serves one feature through every surface it needs, and a surface presenter drives one toolkit surface | feature: `CompletionPresenter`, `CodeActionPresenter`, `HoverPresenter`; surface: `CompletionWindowPresenter`, `CompletionTooltipPresenter`, `CodeActionMenuPresenter`, `FeaturePopupPresenter` |
| `...Renderer`, `...Colorizer`, `...Emitter`, `...Margin`, `...Strategy` | the engine's terms: draws a layer; a document colorizing transformer; writes a toolkit tree from a model; a gutter element derived from the engine's margin base; the engine's strategy class that adapts a neutral policy | `DiagnosticsRenderer`, `TextMateColorizer`, `MarkdownPanelEmitter`, `BookmarkMargin`, `PolicyIndentationStrategy` |
| `...Applier`, `...Adapter` | applies a prepared change or style, or one it prepares through a Planner, to a target; adapts a repository model to an engine contract | `TextRunStyleApplier`, `WorkspaceEditApplier`, `CompletionItemAdapter` |
| `...Decorator` | the base of wrappers that forward each member of one interface | `WorkspaceFileSystemDecorator` |
| `...Base` | an abstract class that holds the behavior its derived types share, named for the concept it generalizes, and not an interface, a wrapper base or a framework base | `LineStatusMarginBase` |
| `...Seam`, `...Interop` | a seam is a per-binding internal static class that has the same members in each binding and that only shared code calls (`docs/EditorSharingGuide.md` defines it, and `EngineParityGuardTests` checks the members); a type that does real platform interop (a P/Invoke or a native handle) keeps `...Interop` | `CompletionSeam` |
| `...Lifetime` | owns its owner's cancellation token and disposed state | `StoreLifetime`, `RequestLifetime` |
| `...Teardown` | runs an ordered shutdown protocol | `SessionTeardown` |
| `...Lease`, `...Reservation`, `...Subscription`, `...Entry` | holds a claim until released; holds a key for one operation; delivers to one subscriber until disposed, or removes its registration when disposed; one record of a table, an index or a document that its owner keys or orders | `DocumentRequestLease`, `DeleteReservation`, `ActionSubscription`, `KeyBindingOverrideEntry` |
| `...Guard` | the operating-system guard that ends child processes with their parent, the no-op guard of a platform that has none, and the selector between them, and nothing else | `IChildProcessLifetimeGuard`, `PlatformChildProcessGuard` |
| `...Profile` | the data and rules that vary by platform or language, bundled for one generic component | `KeyBindingPlatformProfile` |
| `...Conventions` | the fixed patterns, paths and labels that one language or platform follows, held as data with the few lookups that read them | `WorkspaceConventions` |
| `...Containment` | runs a host callback so that its fault is reported and never escapes | `CallbackContainment` |
| `...Cache` | holds computed values for reuse and drops them when they go stale | `IncrementalLineStateCache`, `SettingsSnapshotCache` |
| `...Pipeline` | runs one request through fixed, ordered steps | `DocumentRequestPipeline`, `MarkdownRenderPipeline` |
| `...Composition` | the wired collaborator graph of one owner, built once for it | `ProviderComposition`, `ClientComposition` |
| `...Session` | the state and resources of one live run, from its start to its disposal: a connection to an external process, or a feature attached to one view | `ConnectionSession`, `TextMateHighlightingSession` |
| `...Handle` | a value a caller holds to refer to one running operation | `IProcessHandle`, `RequestHandle` |
| `...Descriptor`, `...Specification` | a type that describes one thing for the component that acts on it, such as a command to bind, a document to track or an operation to run; a Specification describes what a watch covers | `CommandDescriptor`, `TrackedDocumentDescriptor`, `FileWatchSpecification` |
| `...Params`, `...Response`, `...Capability`, `...Payload` | wire structures of the LSP, named as N9 says | `CompletionParams`, `HoverResponse` |
| `...Launcher` | starts an operating-system process and returns the handle that drives it; a type that starts a session or a run is a Coordinator, a Runner or a Supervisor | `IProcessLauncher` |
| plain agent nouns | a concrete noun for what a type does when no role word fits; never a word of this table with another meaning | `Scanner`, `Walker`, `Reader`, `Comparer`, `Enumerator`, `Filter`, `Formatter`, `Opener`, `Closer`, `Writer`, `Mover`, `Deleter`, `Persister`, `Relocator`, `Reloader`, `Refresher`, `Committer`, `Watcher`, `Replacer`, `Detector`, `Differ`, `Activator`, `Expander`, `Calculator`, `Splitter`, `Attacher`, `Installer`, `Locator`, `Serializer`, `Cloner`, `Counter`, `Timer`, `Buffer`, `Observer`, `Discriminator`, `Highlighter`, `Workbench`, `Worker`, `Merger`, `Matcher`, `Ranker` |
| `...Kind`, `...Mode`, `...Style` | classifies, as an enum or, when a host may add kinds, a sealed class with static members; selects a behavior, or, as `QuickPickMode`, pairs a provider with the prefix that picks its mode; selects a visual or formatting variant, or holds resolved formatting values in a record | `CompletionItemKind`, `AutoClosingTrackingMode`, `QuickPickMode`, `LineTerminatorStyle`, `TextRunStyle` |
| `Fake`, `Recording`, `Scripted`, `Gated`, `Throwing`, `Stub`, `Test` | a test double named for what it does: a Fake is a working stand-in, a Recording captures calls, a Scripted plays back a script, a Gated blocks until released, a Throwing fails on purpose and a Stub answers a fixed value; Test marks a subclass written for a test, and a static class named for the one kind of thing it provides or runs | `FakeUiDispatcher`, `RecordingBookmarkStore`, `TestPolling`, `TestThreads` |
| `...TestHost`, `...TestHelpers`, `...TestExtensions` | owns the object graph, or the toolkit host, that a test drives; a static group of helpers for one feature area; extension methods for tests | `CodeActionTestHost`, `WpfTestHost`, `EditingTestHelpers` |

Retired words are not used in a type name: `Manager`, `Helpers` in product code, `Pump`, `Registry`,
`Notifier`, `Driver`, `Machinery`, `Admission`, `Registration`, `Choice`, `Projection`, `Fixture`,
`Capture`, `Starter`, `Access`, and a `...Handler` that is not a delegate. A type derived from a framework type keeps the
framework's word (`...Exception`, `...EventArgs`, `...Attribute`).

### One word per concept

Each concept below has one word in every package.

| Rule | Kept by |
| --- | --- |
| **W1** An IDEKit type that runs a provider or plans from its answer takes its feature's name, not `Provider` (`FormattingService`, `FormattingPlanner`). | review |
| **W2** A watch failure takes the `FileWatch` stem: `FileWatchFailure` in FileWatching, and `WorkspaceFileWatchFailure`, its `WorkspaceFileWatchFailed` event and the `FileWatch...` codes of `ProviderFailureCodes` in the provider contract; `FileWatcher` names only the watcher. | review |
| **W3** `Callback` names a host delegate (`CallbackContainment`), and a request or notification that the server sends is a server message (`ServerMessageRouter`, `ServerMessageDispatcher`), after the LSP's server-to-client messages. | review |
| **W4** The key-binding override document is plural (`KeyBindingOverrides`), and a type named for what it does to overrides takes the singular stem (`KeyBindingOverrideEntry`, `IKeyBindingOverrideStore`, `KeyBindingOverrideValidation`). | review |
| **W5** A file-system identity change is a move (`IWorkspaceFileSystem.MoveAsync`, `WorkspaceFileMoveOutcome`, as the BCL's `File.Move`), and a document identity change is a rename (`RenameAsync`, `WorkspaceDocumentRenameOutcome`, `IDocumentSynchronizationTarget.RenameDocument`, as the LSP's `workspace/didRenameFiles`); `DocumentRelocator` keeps its agent noun because it retargets the store's records for save-as, rename and delete alike. | review |
| **W6** The sibling LanguageServer packages file one role under one folder name: `Session/` holds the owner's lifetime and the state its collaborators share (`ClientLifetime`, `ProviderLifetime`), and `Lifecycle/` holds the start and restart flow (`StartupCoordinator`, `ProviderLifecycleCoordinator`). | review |
| **W7** A Provider folder never spells an IDEKit slice name for something else (`Mirroring/`, not `Synchronization/`, holds the mirroring of documents to the server). | review |
| **W8** A feature has one name in every tier: a namespace directly below an editor binding root, a top-level folder of the provider and language packages, and a folder of the Client's `Protocol/` name a slice of Core or LanguageFeatures. A role folder that serves every feature (`Session/`, `Lifecycle/`, `Tracking/`) and a binding-only namespace (`Rendering`) carry a row in `SliceGuardTests` with the reason, and so does a slice name that Core and LanguageFeatures both declare. | `SliceGuardTests` |
| **W9** Prose and identifiers say "line terminator", not "newline", except in an LSP wire name such as `insertFinalNewline`. | review |
| **W10** "Stroke" names one key press and "chord" a sequence of them. | review |
| **W11** The id of a request is a "request id"; "request token" names only a request's `CancellationToken`. | review |

## Member names

A method's first word says what it does. The table fixes the meaning and the return shape of the verbs this
code shares, and any other verb keeps its plain English meaning.

| Verb | Means | Returns |
| --- | --- | --- |
| `Get` | a value that the owner holds or computes from its inputs; a provider's contract method follows the `...Provider` row of the vocabulary table | the value, or `null` where the answer can be absent |
| `Find` | searches a set or a text | the match; `null`, `-1`, an empty list, `false` or a `None` value when nothing matches |
| `Read` | reads a serialized form or an outside input, such as a JSON reader, a stream, a file or a process | the value read, `null` when the input holds none, or nothing when a read loop hands each read on |
| `Resolve` | settles a value from its inputs, as a `...Resolver` does; a completion item's `ResolveAsync` is the LSP's `completionItem/resolve`, a code action's `ResolveAsync` is the LSP's `codeAction/resolve`, a workspace symbol's `ResolveAsync` is the LSP's `workspaceSymbol/resolve`, and `ResolveExternalConflictAsync` settles a reload conflict | the settled value |
| `Capture` | copies the current state at one moment, such as a stamp, a version or a snapshot | the copy, or nothing when the owner keeps it |
| `Create` | makes a new object, as a `...Factory` does | the new object, or `null` when the inputs make none |
| `Build` | turns one input structure into a derived one, as a `...Builder` does | the built value, or `null` when the inputs make none |
| `Plan` | turns one request into the decisions to carry out, as a `...Planner` does | a `...Plan`, the one value that is the whole decision, or `null` when nothing is carried out |
| `Parse`, `Decode` | maps a serialized or encoded form to a model, as a `...Parser` or a `...Decoder` does | the model, or `null` when the input holds none; a step that writes the model to an `out` parameter or to the caller's collection returns nothing, whether the input was well formed, or the failure when it was not |
| `Apply` | carries out a prepared change, plan, style or directive on a target | nothing, a result, whether it took effect, or the changed copy when the target is an immutable value (`PreparedTextEdits.ApplyTo`) |
| `Normalize` | returns the canonical form of a value, as a `...Normalizer` does | the normalized value |
| `Validate` | checks an argument and throws when it is invalid; a `Validate` beside an `Apply` dry-runs it and returns the outcome that `Apply` would report (`IKeyBindingService.Validate`) | the checked value, nothing, or that outcome; a WPF or Avalonia property validation callback returns `bool` |
| `Classify` | maps a failure or an input to its code or its kind | the code or the kind |
| `Describe` | writes the text of a message about its input | a string, or `null` when there is nothing to describe |
| `Report` | reports a failure or a status to its log, event or status surface, as a `...Reporter` does | nothing, or the task that completes once it is reported |
| `Ensure` | makes a condition hold when it does not yet, or throws when it does not hold, as the BCL's `EnsureSuccessStatusCode` does, and is safe to call again; `Ensure...StartedAsync` starts a run if needed and is safe when it is running | nothing, the ensured value, or whether the condition holds |
| `ThrowIf` | throws when the condition holds, as the BCL's `ObjectDisposedException.ThrowIf` does | nothing |
| `Start` | starts a run once; a caller that may find it running calls `Ensure...StartedAsync` instead | nothing, or what it started: a handle, a session, a result or the task of its background work |
| `Try` | does work that can decline, and says whether it did | `bool`, or `Task<bool>` or `ValueTask<bool>` when it is asynchronous; a synchronous one passes any value through `out` |
| `Is`, `Has`, `Can`, `Should`, `Supports`, or a third-person verb such as `Contains`, `Matches` or `Conflicts` | answers a question and changes nothing | `bool` |
| `To`, `From`, `With` | converts a value, or returns a copy with one member changed (`With`) | the value |
| `Raise` | raises an event of its owner | nothing, or the task that completes once the event is raised |
| `Handle` | reacts to an event, a notification or an input that is routed to it, including the call by which a host tells its owner that something happened (`DiagnosticsController.HandleTextChanged`) | nothing, or what the routing asks for, such as whether it handled the input |
| `On` | a framework override, a hook that a base type invokes, or a hook that an interface declares for its owner to invoke, which each implementer keeps | what the framework, the base or the owner asks for |
| `Log` | writes a log event | nothing, or the reply to the server message it logs |

| Rule | Kept by |
| --- | --- |
| **M1** An action method begins with a verb, and a verb of the table keeps its row; a question reads as the question row says, or as a clause with a third-person verb (`FileExists`, `ItemNeedsResolve`). A method that returns a value and changes nothing may be named for that value, as a static factory or lookup is (`SignatureActiveParameterSelection.At`, `RoslynLanguageDescriptor.For`, as the BCL's `Array.Empty`); a JSON-RPC target method keeps the name of its LSP method (`ClientRpcTarget.WorkspaceConfiguration`); and a sample's command method takes the name of the editor command it runs (`FindAll`, `CreateFolder`). | review |
| **M2** A `Try...` member returns what the `Try` row says, and a member that returns a result object, or nothing, takes no `Try` prefix. | `TypeNameGuardTests` for the return type |
| **M3** A method that runs asynchronous work for its caller to await ends in `Async`, and the inner body behind it ends in `CoreAsync` (`StopCoreAsync`); a synchronous inner body ends in `Core` (`DisposeCore`). A method that starts background work and hands back its task for the owner to keep and observe takes no suffix (`StandardErrorReader.Start`, `BackgroundTaskObserver.StartObservedLoop`). | `MethodNameGuardTests` for the `Async` suffix of a method that returns a task and for `AsyncCore`; review for the inner body |
| **M4** A method that reacts to an event or a notification is `Handle{Event}` (`HandleContextChanged`), `On{Event}` is kept for the cases of the `On` row, and no method is named `{Source}_{Event}`. | `MethodNameGuardTests` for `{Source}_{Event}`, for a method that a `+=` or an `AddHandler` subscribes without the `Handle` prefix, and for an `On` method that is no override, no virtual or abstract method, and no member of an interface, where each implementer of an interface hook carries a row; review for a reaction that is wired another way |
| **M5** A `bool` property names an affirmative fact. A state reads as a question or as a clause with a third-person verb (`IsDirty`, `HasConflict`, `SupportsHover`, `OpensOnLeftClick`); an outcome reads as a past participle or a past-tense clause (`Succeeded`, `StandardOutputTruncated`, `AlreadyRegistered`, `DidWrapSelection`); and an option or a directive names the behavior it turns on (`AllowScrolling`, `IgnoreAutoRepeat`, `ReopenServerDocument`), as the BCL's `ProcessStartInfo.UseShellExecute` does. A name that mirrors the LSP, the BCL or an engine keeps its form (`Preselect`, `InsertSpaces`, `Deprecated`). | review |

## Parameters and values

| Rule | Kept by |
| --- | --- |
| **P1** A document's identity in a neutral model or a host-facing provider contract is its `DocumentId`, never a `FilePath`, and a provider states what its ids are (the language-server provider's are local file paths). A value the code treats as a file-system path, because it compares it under a path policy, opens it or maps it to a file URI, is a `FilePath`. | review |
| **P2** An `offset` is a zero-based index into a document's text in UTF-16 code units. A `lineNumber` is one-based, as `ITextLine` and the engines count lines, and a `lineIndex` is zero-based, as `TextPosition.Line` and the LSP count them; a `character` counts UTF-16 code units within its line. | review |
| **P3** The token parameter that cancels a member's own work is `cancellationToken`, and a token with another job is named for that job, such as `lifetimeToken` for the lifetime of its owner. A member's `ILogger` parameter is `logger`. | `ParameterNameGuardTests`, where a row states the job of a token with another name |
| **P4** `Normalized...` names the normalized form of a key. A rename of an identity key names the old key `old...` and the new one `new...` (`RenameDocument(oldDocumentId, newDocumentId, ...)`). An operation over file-system paths, whether a move, a document rename or a directory operation, names them `source...` and `destination...`, as the BCL's `File.Move` and `Directory.Move` do. | review |

## Namespaces

The Namespaces section of the [architecture guide](docs/ArchitectureGuide.md#namespaces) gives the reason for the
folder-derived and the flat outcome below.

| Rule | Kept by |
| --- | --- |
| **S1** A `Nickelony.IDEKit.*` file's namespace mirrors its folder path below the project root. A folder that holds only internal machinery, below a slice or directly below a binding root or a shared editor home, keeps the namespace of its parent folder, so the machinery groups into folders without adding namespaces; it is listed in `RepositoryModel.InternalOnlyFolders`. A binding's slice folder keeps its slice namespace even when its own files are internal, because linked `shared/` source adds the slice's public types. | `NamespaceGuardTests`; `MemberModifierGuardTests` for a listed folder |
| **S2** `Nickelony.LanguageServer.*`, `Nickelony.KeyBindings.*`, `Nickelony.QuickPick.*` and `Nickelony.Matching` keep one flat namespace per package; folders express slice ownership only, and a file keeps its namespace when it moves. The exceptions are the Client package's wire model, where every file under its `Protocol/` folder declares `Nickelony.LanguageServer.Client.Protocol`, and the entries of the KeyBindings override document, where every file under its `Overrides/Entries/` folder declares `Nickelony.KeyBindings.OverrideEntries`. | `NamespaceGuardTests` |
| **S3** A file under an editor home of `shared/` declares the binding namespace that its folder path below the home spells, and the `#if AVALONIAEDIT` header selects the engine's root; a file of `shared/KeyBindings` declares `Nickelony.KeyBindings.Wpf` or `Nickelony.KeyBindings.Avalonia`, which `AVALONIAKEYBINDINGS` selects, and a file of `shared/QuickPick` declares `Nickelony.QuickPick.Wpf` or `Nickelony.QuickPick.Avalonia`, which `AVALONIAQUICKPICK` selects. A file of `shared/Primitives` declares the Core slice namespace that its folder spells, or the flat `Nickelony.QuickPick` namespace when the family compiles it with `QUICKPICKFAMILY`, and the same symbol makes the type `internal` there instead of `public`. An `internal` helper of `shared/Infrastructure`, which Core and Client both compile, declares the one flat `Nickelony.Infrastructure` namespace in every package that compiles it; the [editor sharing guide](docs/EditorSharingGuide.md) owns the rules. | `NamespaceGuardTests`; `InternalsVisibleToGuardTests` for the internal helpers |
| **S4** No public type lives in an `Infrastructure` namespace. | `TypeNameGuardTests` |
| **S5** A namespace holds at most 30 public types, so a package with more takes feature namespaces, and `PublicTypeBoundGuardTests` lists each namespace that stays larger by design. | `PublicTypeBoundGuardTests` |
| **S6** A public type sits in the root namespace of an IDEKit package with feature namespaces only when the whole package uses it and its name carries the package concept, or when it belongs to the host-facing surface that the package's main audience imports, as the architecture guide's Namespaces section sets out. Each such type carries a row in `RootNamespaceGuardTests` with its reason, and a row that ends in `*` covers the types of one family by their common name prefix. | `RootNamespaceGuardTests` |
| **S7** An editor binding is named for the control it wraps (`AvalonEdit`, `AvaloniaEdit`), and a key-binding adapter, a quick-pick window or a sample for the toolkit (`Wpf`, `Avalonia`). | review |
| **S8** A product namespace that equals a third-party name (`Nickelony.IDEKit.Markdown` against `Markdig.Markdown`) hides the third-party type from every file below it, so such a file aliases the type it needs, as `MarkdownDocumentModelBuilder.cs` does with `using MarkdigMarkdown = Markdig.Markdown;`. | review |
| **S9** A test project declares one flat namespace, its project name, whatever its folders, and a shared test tree (`tests/IDEKit/Shared/Editor*.Tests/`, `tests/QuickPick/Shared/Presenter.Tests/`, `tests/Samples/Shared/Samples.Tests/`) declares the flat namespace of the test project that compiles it; the `#if AVALONIAEDIT` or `#if AVALONIAQUICKPICK` header selects the toolkit's root ([editor sharing guide](docs/EditorSharingGuide.md)). | `NamespaceGuardTests` |
| **S10** A shared `TestSupport` folder declares one flat namespace: `Nickelony.Testing` for `tests/TestSupport` and `Nickelony.{Family}.Testing` for a family's folder. | `NamespaceGuardTests` |
| **S11** A sample project under `samples/` declares one flat namespace, its project name, like a test project. The body that its heads compile from `shared/Samples` declares the flat namespace `Nickelony.Samples`. | `NamespaceGuardTests` |
| **S12** A product project's `GlobalUsings.cs` holds the logging namespaces and the namespaces of the packages it references, and never a namespace of its own package, so a file's `using` lines show which of its own package's namespaces it reaches. Test projects are free. | `NamespaceGuardTests` |

## Contracts and shapes

| Rule | Kept by |
| --- | --- |
| **C1** A public or protected member guards each non-nullable reference argument before any other statement: with `ArgumentNullException.ThrowIfNull`, or, for a generic parameter, with `is null` and a thrown `ArgumentNullException`, so a struct argument does not box (`TextMatchProbe.MatchesAt`). An init accessor and a chained constructor argument throw through `?? throw new ArgumentNullException(nameof(...))`, and so does a positional record parameter of a record whose doc promises that the member is never null (`WorkspaceDocumentViewSynchronizationIssue.ViewId`). Any other positional record parameter takes no guard of its own, because a `default` struct, a `with` expression and a deserializer skip the constructor. The public member that takes a record the host fills in, a `...Request`, `...Query`, `...Specification` or `...Hooks` record, rejects each non-nullable reference member it reads, as `AutoClosingPlanner.TryPlanAction` does for `request.Snapshot`, and the doc of a record struct whose `default` leaves such a member null says so (`SearchQuery.Pattern`). The library builds every other record (a result, plan, model, edit, snapshot, failure or payload), so no host passes one in. An override and an explicit interface implementation keep the contract of the member they implement, and a member whose first statement hands the argument unchanged to a call that rejects it needs no guard of its own. An internal or private member takes no guard, because in-assembly callers are trusted, unless it checks what no public member can check first: the arguments of a constructor that a public one chains to, the argument of a forwarding core that names the caller's expression, the arguments of an asynchronous core whose guard faults the returned task, or the result of a host delegate. `MemberModifierGuardTests` lists each such `ThrowIfNull` guard with its reason. | `PublicArgumentGuardTests` for the method and constructor arguments of a public or protected member and for the members of an input record that such a member reads; `MemberModifierGuardTests` for a non-public `ThrowIfNull`; review |
| **C2** Inside an internal type, a member is `internal` or `private` unless it implements an interface, overrides a member, is an operator, or is reached by reflection: a JSON-RPC target, a member with a `[Json...]` attribute that a serializer reads, or the constructor of a converter. A type of `shared/Primitives` is public where Core publishes it and internal in the quick-pick family, so it keeps its public members and the guard reads its public variant only. | `MemberModifierGuardTests` |
| **C3** An event is an `EventHandler` when it carries no payload and an `EventHandler<TEventArgs>` otherwise, never an `Action` or a custom delegate. | `TypeNameGuardTests` |
| **C4** A public payload collection is typed `IReadOnlyList<T>`, a membership set `IReadOnlySet<T>` (`IKeyBindingService.Contexts`), and a keyed map `IReadOnlyDictionary<TKey, TValue>` (`ProcessRunRequest.EnvironmentVariables`). A few serializer DTOs keep an array member, as the [client guide](docs/ClientGuide.md) describes, a `[JsonExtensionData]` member keeps `IDictionary`, the KeyBindings override document keeps mutable `List<T>` members for its serializer, an abstract hook that a host implements with `yield` returns `IEnumerable<T>` (`RegexHighlightingDefinition.BuildRules`), a method that hands its caller a copy the caller owns may return the mutable type (`IFileSnapshotTracker.CloneTrackedSnapshot`), and a member that an engine interface declares keeps the engine's type. | `PublicCollectionGuardTests` for the property, field and return types of a public member; review |
| **C5** A public Core or LanguageFeatures model expresses an offset span as `TextRange` and a line and character span as `TextPositionRange`, never as a bare pair of ints. A lone offset is a position and stays an `int`, and a type that describes one document line keeps the engine line's `Offset` and `EndOffset` (`ITextLine`). | review |
| **C6** An expected failure is reported through the operation's result, not an exception, and the caught exception is attached to the failure so the evidence survives. | review |
| **C7** An operation that reports an outcome reports `Canceled` when its owner is disposed while it runs, and only a member invoked after disposal throws `ObjectDisposedException`; a member with no outcome, such as one of `ProcessHandle`, also throws it when disposal completes while it runs. The editor-facing controllers and the render-path caches are the exception: the editor raises their callbacks during teardown, so they return a safe default and throw only for an invalid argument. `UnsavedChangesTracker.SetBaseline` keeps the throw, because a write to a disposed tracker is a host error. | review |
| **C8** An exception is caught only where the code recovers from it, translates it, adds context, or contains a fault that must not escape a background loop, a host callback or a best-effort cleanup. A contained fault is logged, and a catch that logs nothing states in a comment why the exception carries no information; a `catch (OperationCanceledException)` or `catch (ObjectDisposedException)` that ends a loop, a cancel or a dispose needs no comment, because its type says why. | review |
| **C9** One top-level type per file is the default. A wire record may share its file with the records of its nested JSON objects that only it uses (`DiagnosticPayload.cs`). A group of types that change together and are read together may share one file, such as a closed record hierarchy (`MarkdownBlock.cs`), or a type with the enum or issue record that describes one of its members (`WorkspaceEditPlan.cs`). The request, result and outcome of one operation share one file named for the operation (`WorkspaceDocumentViewApply.cs`). Unrelated types do not share a file. | review |
| **C10** A type splits into partial files by implemented contract (`.Features.cs`, `.Sources.cs`, `.Disposal.cs`) or by engine half (`.Common.cs`), and the file name says which. A type that outgrows one sentence splits into collaborators, never into partial files by operation. | `PartialFileGuardTests` for the file name of each part; review |
| **C11** A public member ships with a consumer and a test. A public type's consumer is another library or a sample host that names it, or a row in `PublicSurfaceGuardTests` whose reason opens with a key: `adopter:` names the external adopter that uses the type, `extension:` the extension call that a host makes, `wire:` the wire record that a host reads from the Client, and `scenario:` the host scenario that needs it. | `PublicSurfaceGuardTests` for a public type; review |
| **C12** The null-forgiving operator `!` marks only an invariant that is proven but out of the compiler's reach; flow analysis, an explicit check or a nullability attribute comes first, because `!` hides a real null until far from its cause. | review |
| **C13** A library await ends in `.ConfigureAwait(false)`, because a host may block on the task from a UI thread; a binding or a controller type awaits on the captured context and says so with `.ConfigureAwait(true)`, because it touches the editor after the await. | CA2007 for a library, which a project turns on with `RequireConfigureAwait`; `AwaitContextGuardTests` for `ConfigureAwait(true)` in a binding, a stated choice in a controller package, and no `ConfigureAwait(true)` in a library |
| **C14** A request that can change state on the server is sent once and never timed out like a read: the provider framework sends it with `RequestRunner.SendOnceAsync`, so no attempt has the request timeout, a wait never counts toward `RequestTimeoutRestartThreshold`, and a connection boundary ends the request with its fallback instead of a second send. A request that only reads or computes goes through `SendAsync` or `SendOutcomeAsync`. | review |
| **C15** An operation that applies several edits to a document raises one change notification: it replaces the span the edits cover in one document change, with an offset change map that holds one entry per edit, so subscribers that re-read the document on each notification (a line diff, a tokenizer, a line-state cache) run once per operation and anchors still move per edit. | review |
| **C16** A host hook that the library may call again before an earlier call returns says so in its doc, and the library serializes such calls unless the hook says it is reentrant, so a hook that ignores its cancellation token never runs twice at once (`ProjectDiagnosticsSourceHooks`). | review |
| **C17** A string that a user reads in a host window is never a public `const`: it is an option of the type that shows it, with its default stated on the option, so a host localizes it or hides it with `null` (`OpenEditorsQuickPickOptions.UnsavedText`). A key that a host maps to an image is a different thing and may be a public `const` (`OpenEditorDescriptor.UnsavedIconKey`). | review |
| **C18** A host hook states the thread it runs on in the doc of its parameter, and the library calls a hook that may touch the UI on the dispatcher thread: before its first `await`, or after an `await` that resumes on the captured context (`.ConfigureAwait(true)`), never after one that resumes on a pool thread. A hook that only records a failure may run on another thread when its doc says which (`WorkspaceFileQuickPickProvider`'s `reportFault`). | review |
| **C19** A `catch` that writes the owner's state checks first that the caller still owns the transition it would undo, because a caller that only waited, or whose own step already completed, would overwrite the state another caller left: a startup waiter canceled at the lock leaves a completed start ready (`ProviderLifecycleCoordinator.EnsureConnectionStartedAsync`). | review |

## Composition

| Rule | Kept by |
| --- | --- |
| **K1** An options bag is a `sealed record` with an init-only, self-validating property per member and a `static X Default` when the all-defaults instance is valid; a consumer substitutes `Default` rather than re-deriving member defaults. | `OptionsShapeGuardTests` for the sealed record, the init-only members and the `Default`; review for the validation of a member |
| **K2** A directly constructed component takes its arguments in one order: the owner it serves and the id of the document it serves (`textArea, documentId`), then the contract it calls (a provider), then presentation and hooks, then the options bag, then rarely supplied infrastructure, then the logger last. The options bag and the logger stay optional and trailing, and a constructed component takes its logger as a constructor parameter, never through its options bag; a static entry point whose only configuration is its options bag keeps the logger there (`MarkdownRenderOptions.Logger`). Infrastructure that the component cannot run without, such as the dispatcher of a neutral controller, is a required parameter and goes before the options bag. | review |
| **K3** A rule that would otherwise be duplicated or negated at several call sites has one home - a named predicate, probe, or shared internal core - and each call site keeps its own reaction. | review |
| **K4** No member only forwards to another member without adding validation, abstraction, or meaningful domain terminology. | review |
| **K5** A positional constructor on an options bag covers every member, so one optional member never gets a convenience constructor while the others take an initializer. Collaborators that share one policy receive one options value, not copies of its members. | review |
| **K6** A type can be described in one sentence. When that sentence needs "and", the second responsibility moves to a collaborator behind the facade, and state with its own timer, token source, disposal or loop belongs to a collaborator. | review |
| **K7** A request record built on a hot path, such as the per-stroke path, is a value type passed by `in`. | review |

## Logging

| Rule | Kept by |
| --- | --- |
| **L1** A package picks one of two logging styles and keeps it. A package that emits direct diagnostics logs through the `ILogger` extension methods and suppresses CA1848 in its project file with a rationale. A package whose log events are a host-facing contract logs through `LoggerMessage` source-generated methods with named `EventId`s, stable within the package and pinned by a test where the contract is relied on. | `LoggingGuardTests` for the CA1848 suppression of a direct package, a mixed package and the event id and name of a `LoggerMessage` method; review for the choice of style |
| **L2** A log event id lies in the numeric block of its package, which the [architecture guide](docs/ArchitectureGuide.md#log-event-ids) lists, and no two events share an id. | `EventIdGuardTests` |
| **L3** A component accepts the host's logger as a plain, non-generic `ILogger` and keeps the host's category. It never wraps the logger in an adapter that re-categorizes its messages, because a host that filters by category would lose the library's messages. | `LoggingGuardTests` for `ILogger<T>` and `ILoggerFactory`; review for a wrapping adapter |
| **L4** A `LoggerMessage` method in a package that logs direct diagnostics cites in a comment the measurement that justifies it, and the package keeps its CA1848 suppression. | review |

## Tests

| Rule | Kept by |
| --- | --- |
| **T1** A test seam in product code is one of three kinds: a constructor-injected internal collaborator when it replaces a dependency, an `{Owner}TestHooks` observer when it marks an interleaving point, or an internal read-only state property that a test reads and production code never branches on. Production code calls a hook only through `?.Invoke()`, or, for an asynchronous hook, a null check and an `await`; a settable factory on a hooks type is not a seam, and no product type keeps static mutable state for tests. | `TestSeamGuardTests` for the hook calls, the settable factory and the static mutable state; review |
| **T2** One type's large test suite is one partial class over `{Type}Tests.{Aspect}.cs`, where each partial holds tests of one aspect and support code goes to `TestSupport`; a suite that drives one facade through one feature is `{Facade}{Feature}Tests` in that feature's test folder; a suite across several types ends in `ScenarioTests`. | review |
| **T3** A test class is sealed or abstract, or it is the base of another class. | `TestClassGuardTests` |
| **T4** A test helper lives in the nearest `TestSupport/` folder that every compiling project shares: `tests/TestSupport` for several families, `tests/{Family}/TestSupport` for several projects of one family, and the project's own `TestSupport/` otherwise. A test project never links a file from another project's own folder. | `TestOrganizationGuardTests`, `TestLayoutGuardTests` for the folder and the link; review for the nearest folder |
