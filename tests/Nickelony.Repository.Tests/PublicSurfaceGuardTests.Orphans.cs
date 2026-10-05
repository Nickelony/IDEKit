namespace Nickelony.Repository.Tests;

public sealed partial class PublicSurfaceGuardTests
{
	// A public type that no library or sample of the repository names in code, keyed "{project below Nickelony}: {type}".
	// Each reason opens with one of the keys in s_reasonKeys.
	private static readonly AllowList s_unconsumedTypes = new(
		"unconsumed public types",

		(
			"IDEKit.FileWatching: FileSnapshotDiffer",
			"scenario: A host that substitutes IFileSnapshotTracker computes the reconciliation delta of its snapshots with it (FileWatchingGuide)."
		),

		(
			"IDEKit.FileWatching: FileSnapshotEntry",
			"scenario: The value of the snapshots that IFileSnapshotTracker exchanges, which a host that substitutes the tracker reads and writes (FileWatchingGuide)."
		),

		(
			"IDEKit.FileWatching: FileWatcherStartOutcome",
			"scenario: A host that implements IFileWatcher classifies its start with it (FileWatchingGuide)."
		),

		(
			"LanguageServer.Provider: LanguageServerClientCapabilitiesBuilder",
			"scenario: A host that adopts a client it built itself with LanguageServerFeatureProvider.Create builds the capability payload with it (ProviderAuthoring)."
		),

		(
			"LanguageServer.Provider: LanguageServerFeatureProviderOptions",
			"scenario: A host passes it to the LanguageServerFeatureProvider constructor to tune the framework (ProviderGuide)."
		),

		(
			"IDEKit.JsonSchema: JsonSchemaVocabularyIndexFailureCodes",
			"scenario: A host that branches on a failed vocabulary index compares JsonSchemaVocabularyIndexFailure.Code with these values."
		),

		(
			"IDEKit.Markdown: MarkdownDocumentModelBuilder",
			"scenario: A host that renders the content model with its own emitter parses with it (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownBlock",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownCodeSpan",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownEmphasis",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownHeading",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownImagePlaceholder",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownInline",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownLineBreak",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownLink",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownList",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownListItem",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownParagraph",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownQuote",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownSpan",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownTable",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownTableCell",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownTableRow",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownText",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.Markdown: MarkdownThematicBreak",
			"scenario: A node of the content model that MarkdownRenderPlanner plans from and a host with its own emitter walks (MarkdownGuide)."
		),

		(
			"IDEKit.LanguageFeatures.Controllers: GoToLineQuickPickOptions",
			"scenario: A host that localizes the texts of the go to line mode passes it to GoToLineQuickPickProvider (QuickPickGuide)."
		),

		(
			"IDEKit.AvalonEdit: IAutoClosingService",
			"scenario: A host that composes services per editor substitutes its own implementation (EditorUsageGuide)."
		),

		(
			"IDEKit.AvalonEdit: ILineCommentService",
			"scenario: A host that composes services per editor substitutes its own implementation (EditorUsageGuide)."
		),

		(
			"IDEKit.AvaloniaEdit: IAutoClosingService",
			"scenario: A host that composes services per editor substitutes its own implementation (EditorUsageGuide)."
		),

		(
			"IDEKit.AvaloniaEdit: ILineCommentService",
			"scenario: A host that composes services per editor substitutes its own implementation (EditorUsageGuide)."
		),

		(
			"IDEKit.AvalonEdit.LanguageFeatures: CodeActionMargin",
			"scenario: A host that owns the left-press gesture sets OpensOnLeftClick on the margin of the presenter."
		),

		(
			"IDEKit.AvalonEdit.LanguageFeatures: CodeActionMenuHooks",
			"scenario: A host that restyles the menu or its items passes the hooks to the presenter."
		),

		(
			"IDEKit.AvalonEdit.LanguageFeatures: CompletionItemAdapter",
			"scenario: A host that maps its own items wraps the shared item with the adapter in CompletionItemFactory."
		),

		(
			"IDEKit.AvalonEdit.LanguageFeatures: CompletionItemAdapterHooks",
			"scenario: A host that draws item icons, checks staleness or adjusts the insertion passes it."
		),

		(
			"IDEKit.AvalonEdit.LanguageFeatures: CompletionWindowPresenter",
			"adopter: tomb-gate4 TextEditorBase holds one to show the completion window."
		),

		(
			"IDEKit.AvalonEdit.LanguageFeatures: TextEditorWorkspaceEditExtensions",
			"extension: A host that applies the document edits of a language-server workspace edit to an editor calls ApplyDocumentEdits."
		),

		("IDEKit.AvalonEdit: LineStatusMarginBase", "scenario: A host that draws its own margin derives from it."),

		(
			"IDEKit.AvalonEdit: TextEditorEditTarget",
			"scenario: The default edit target of the editing extensions; a host with document authority passes its own ITextEditTarget."
		),

		(
			"IDEKit.AvaloniaEdit.LanguageFeatures: CodeActionMargin",
			"scenario: A host that owns the left-press gesture sets OpensOnLeftClick on the margin of the presenter."
		),

		(
			"IDEKit.AvaloniaEdit.LanguageFeatures: CodeActionMenuHooks",
			"scenario: A host that restyles the menu or its items passes the hooks to the presenter."
		),

		(
			"IDEKit.AvaloniaEdit.LanguageFeatures: CompletionItemAdapter",
			"scenario: A host that maps its own items wraps the shared item with the adapter in CompletionItemFactory."
		),

		(
			"IDEKit.AvaloniaEdit.LanguageFeatures: CompletionItemAdapterHooks",
			"scenario: A host that draws item icons, checks staleness or adjusts the insertion passes it."
		),

		(
			"IDEKit.AvaloniaEdit.LanguageFeatures: CompletionWindowPresenter",
			"scenario: An Avalonia host shows the completion window with it as the WPF twin does for tomb-gate4, and the binding parity guard keeps the surfaces equal."
		),

		(
			"IDEKit.AvaloniaEdit.LanguageFeatures: TextEditorWorkspaceEditExtensions",
			"extension: A host that applies the document edits of a language-server workspace edit to an editor calls ApplyDocumentEdits."
		),

		("IDEKit.AvaloniaEdit: LineStatusMarginBase", "scenario: A host that draws its own margin derives from it."),

		(
			"IDEKit.AvaloniaEdit: TextEditorEditTarget",
			"scenario: The default edit target of the editing extensions; a host with document authority passes its own ITextEditTarget."
		),

		("IDEKit.Core: AutoClosingPairKind", "adopter: tomb-gate4 TextEditorBase sets it on the quote pairs it configures."),
		("IDEKit.Core: CommentKind", "scenario: A host that scans comments reads it from a CommentSpan."),

		(
			"IDEKit.Core: CommentSpanEnumerator",
			"scenario: CommentOperations.EnumerateComments returns it to a host that scans comments in one forward pass."
		),

		("IDEKit.Core: ContinuationMarkerProbe", "adopter: tomb-gate4 ErrorDetector calls EndsWithContinuationMarker."),

		(
			"IDEKit.Core: ITextEditVersionSource",
			"adopter: tomb-gate4 TextEditorWorkspaceView implements it so a prepared batch can detect a stale document."
		),

		("IDEKit.Core: LineDiffResult", "scenario: LineDiffer returns it to a host that marks the lines changed against a baseline."),
		("IDEKit.Core: TextEditPreparationIssueKind", "scenario: A host reads it from an issue that TextEditKernel reports for an invalid edit."),

		(
			"IDEKit.Core: TextEditSkipResult",
			"scenario: TextEditKernel returns it to a host that applies the valid edits of a batch and reports the rest."
		),

		(
			"IDEKit.Core: WhitespaceOperations",
			"scenario: A host that converts the indentation of a text no editor shows, or expands every tab of it, calls it (CoreGuide)."
		),

		("IDEKit.JsonSchema: JsonSchemaPropertyType", "adopter: tomb-gate4 TRXGameFlowSchemaService maps it to its own property types."),

		(
			"IDEKit.LanguageFeatures.Controllers: CodeActionExecutionOutcome",
			"scenario: A host with its own menu or key command reads the outcome of ExecuteActionAsync to tell the user why nothing changed."
		),

		(
			"IDEKit.LanguageFeatures.Controllers: CompletionFilter",
			"adopter: tomb-gate4 TRXCompletionManagerTests call CompletionFilter.FilterByWord."
		),

		(
			"IDEKit.LanguageFeatures.Controllers: CompletionPresentationState",
			"scenario: A host reads it from the controller to learn whether a list or a tooltip is showing."
		),

		(
			"IDEKit.LanguageFeatures.Controllers: CompletionTriggerPolicy",
			"scenario: A host with its own typing rules sets one on CompletionControllerOptions.Triggers."
		),

		(
			"IDEKit.LanguageFeatures.Controllers: CompletionWordSpan",
			"adopter: tomb-gate4 completion session coordinators create it for the word at the caret."
		),

		(
			"IDEKit.LanguageFeatures.Controllers: DiagnosticsSourceExtensions",
			"extension: A host that shows the diagnostics of one document calls Subscribe on its IDiagnosticsSource."
		),

		(
			"IDEKit.LanguageFeatures.Controllers: SignatureHelpPresentationState",
			"scenario: A host reads it from the controller to learn whether a popup is showing."
		),

		(
			"IDEKit.LanguageFeatures.Controllers: SnippetExpander",
			"scenario: A host that commits its own completion data instead of the default adapter expands a snippet with it."
		),

		("IDEKit.Processes: IProcessHandle", "adopter: tomb-gate4 tests return it from their fake process launcher."),

		(
			"IDEKit.Processes: IProcessLauncher",
			"scenario: A host that drives a started process itself takes this seam, and ProcessRunner implements it beside IProcessRunner."
		),

		(
			"IDEKit.Workspace.Views: AttachedDocumentChangeKind",
			"scenario: A host that handles AttachedDocumentChanged itself, instead of through WorkspaceDocumentSynchronizer, switches on it."
		),

		(
			"IDEKit.Workspace.Views: AttachedDocumentChangedEventArgs",
			"scenario: A host that handles AttachedDocumentChanged itself, instead of through WorkspaceDocumentSynchronizer, reads it."
		),

		(
			"IDEKit.Workspace.Views: IWorkspaceDocumentDeleteLeaseView",
			"adopter: tomb-gate4 TextEditorWorkspaceView and StringEditorWorkspaceView implement it."
		),

		(
			"IDEKit.Workspace.Views: IWorkspaceDocumentPublishingView",
			"scenario: WorkspaceDocumentTextViewBase implements it, and a host view of its own that holds typed text back implements it too."
		),

		("IDEKit.Workspace.Views: IWorkspaceViewCoordinator", "adopter: tomb-gate4 composes its shell and document controllers over it."),

		(
			"IDEKit.Workspace.Views: WorkspaceDocumentViewDeleteLeaseOutcome",
			"adopter: tomb-gate4 TextEditorWorkspaceView reports its delete lease with it."
		),

		(
			"IDEKit.Workspace.Views: WorkspaceDocumentViewDeleteLeaseResult",
			"adopter: tomb-gate4 TextEditorWorkspaceView returns it from ApplyDeleteLease."
		),

		(
			"IDEKit.Workspace.Views: WorkspaceDocumentViewSynchronizationIssue",
			"scenario: A host adapter for IWorkspaceDocumentView implements or reads the type."
		),

		(
			"IDEKit.Workspace.Views: WorkspaceEditPlan",
			"scenario: WorkspaceEditApplyResult carries it to a host that reads why a language-server edit did not apply."
		),

		(
			"IDEKit.Workspace.Views: WorkspaceEditPlanIssueKind",
			"scenario: A host that reports why a language-server edit cannot be applied reads the type."
		),

		(
			"IDEKit.Workspace.Views: WorkspaceViewCoordinatorResult",
			"scenario: The coordinator returns it to a host that reads the common part of its operation results."
		),

		(
			"IDEKit.Workspace.Views: WorkspaceViewOperationFailureCodes",
			"adopter: tomb-gate4 EditorDocumentController reports ViewRefreshFailed with it."
		),

		(
			"IDEKit.Workspace: DocumentReplacementChangeSet",
			"scenario: A host that applies a computed change to several tracked documents uses the change-set applier (WorkspaceGuide)."
		),

		(
			"IDEKit.Workspace: DocumentReplacementFailureCodes",
			"scenario: A host that applies a computed change to several tracked documents uses the change-set applier (WorkspaceGuide)."
		),

		(
			"IDEKit.Workspace: DocumentReplacementTargetOutcome",
			"scenario: A host that applies a computed change to several tracked documents uses the change-set applier (WorkspaceGuide)."
		),

		(
			"IDEKit.Workspace: DocumentReplacementTargetResult",
			"scenario: A host that applies a computed change to several tracked documents uses the change-set applier (WorkspaceGuide)."
		),

		("IDEKit.Workspace: DocumentReplacementChange", "adopter: tomb-gate4 TextWorkspaceEditApplier builds a list of them."),
		("IDEKit.Workspace: WorkspaceFileReadResult", "adopter: tomb-gate4 RecycleBinWorkspaceFileSystem returns it from ReadAsync."),
		("IDEKit.Workspace: WorkspaceFileReplacementOutcome", "adopter: tomb-gate4 WorkspaceDocumentManagerTests build replacement results with it."),
		("IDEKit.Workspace: WorkspaceFileReplacementResult", "adopter: tomb-gate4 RecycleBinWorkspaceFileSystem returns it from WriteFileAsync."),

		(
			"IDEKit.Workspace: WorkspaceOperationFailureCodes",
			"adopter: tomb-gate4 RecycleBinWorkspaceFileSystemTests compare a failure code with DeleteFailed."
		),

		("IDEKit.Workspace: WorkspaceTextCodec", "adopter: tomb-gate4 EditorDocumentController decodes file bytes with it."),

		(
			"KeyBindings.Avalonia: KeyCodeConversion",
			"scenario: A host that converts a toolkit key itself calls it, as the extension methods of the adapter do."
		),

		(
			"KeyBindings.Wpf: KeyCodeConversion",
			"scenario: A host that converts a toolkit key itself calls it, as the extension methods of the adapter do."
		),

		("KeyBindings.Wpf: KeyDownHandlingOptions", "scenario: A host passes it to HandleKeyDown to dispatch auto-repeat or AltGr strokes."),

		(
			"KeyBindings.Wpf: KeyGestureDisplayTextFormatter",
			"scenario: A WPF host whose menus show the text of WPF's own KeyGesture assigns it, so the two texts match."
		),

		("KeyBindings: IKeyBindingResolver", "scenario: A host that dispatches against its own binding source implements it alone."),
		("KeyBindings: IKeyBindingService", "adopter: tomb-gate4 MenuService and the studio lifecycle coordinator take it."),

		(
			"KeyBindings: IKeyDisplayTextFormatter",
			"scenario: KeyBindingServiceOptions takes it, the adapters implement it, and a host with its own display format implements it too."
		),

		("KeyBindings: KeyBindingOverrideChord", "scenario: A host reads or edits it in the override document that its store round-trips."),
		("KeyBindings: KeyBindingOverrideEntry", "scenario: A host reads or edits it in the override document that its store round-trips."),
		("KeyBindings: KeyBindingOverrideStroke", "scenario: A host reads or edits it in the override document that its store round-trips."),
		("LanguageServer.Client: CodeActionDisabledPayload", "wire: A host reads the wire record through a Client event or a request result."),
		("LanguageServer.Client: CodeDescriptionPayload", "wire: A host reads the wire record through a Client event or a request result."),
		("LanguageServer.Client: CommandPayload", "wire: A host reads the wire record through a Client event or a request result."),

		(
			"LanguageServer.Client: DiagnosticRelatedInformationPayload",
			"wire: A host reads the wire record through a Client event or a request result."
		),

		("LanguageServer.Client: DiagnosticTag", "wire: A host reads the wire record through a Client event or a request result."),
		("LanguageServer.Client: DidChangeConfigurationParams", "wire: A host reads the wire record through a Client event or a request result."),

		(
			"LanguageServer.Client: IChildProcessLifetimeGuard",
			"scenario: A host replaces the platform guard through the guard factory of StdioLanguageServerTransportOptions, as ClientGuide documents."
		),

		(
			"LanguageServer.Client: ILanguageServerConnection",
			"scenario: A host with its own transport or connection uses the seam that ClientGuide documents."
		),

		(
			"LanguageServer.Client: ILanguageServerTransport",
			"scenario: A host with its own transport or connection uses the seam that ClientGuide documents."
		),

		(
			"LanguageServer.Client: LanguageServerConnectRequest",
			"scenario: A host with its own transport or connection uses the seam that ClientGuide documents."
		),

		(
			"LanguageServer.Client: LanguageServerConnectionException",
			"scenario: A host with its own transport or connection uses the seam that ClientGuide documents."
		),

		(
			"LanguageServer.Client: OptionalVersionedTextDocumentIdentifier",
			"wire: A host reads the wire record through a Client event or a request result."
		),

		("LanguageServer.Client: SymbolLocationPayload", "wire: A host reads the wire record through a Client event or a request result."),
		("LanguageServer.Client: SymbolTag", "wire: A host reads the wire record through a Client event or a request result."),

		(
			"LanguageServer.Client: WorkspaceEditChangeAnnotationPayload",
			"wire: A host reads the wire record through a Client event or a request result."
		),

		(
			"LanguageServer.Client: WorkspaceResourceOperationOptionsPayload",
			"wire: A host reads the wire record through a Client event or a request result."
		),

		("LanguageServer.Lua: LuaLanguageServerOptions", "adopter: tomb-gate4 creates it to configure the Lua language server profile."),

		(
			"LanguageServer.Provider.Abstractions: ILanguageServerCapabilitiesSource",
			"scenario: A host reads the capabilities of a provider through it after CapabilitiesChanged."
		)
	);
}
