namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>
/// Holds the LSP method strings that <c>ILanguageServerClient.SendRequestAsync</c> and <c>ILanguageServerClient.SendNotificationAsync</c> take,
/// so each wire string exists once instead of being re-typed at every call site.
/// </summary>
/// <remarks>
/// <para>
/// A mistyped wire string degrades silently at runtime - a request falls back to its documented default - so the names are centralized here to make
/// a typo a compile error instead.
/// </para>
/// <para>
/// Only the methods a host may send are public. The lifecycle methods and every server-to-client method stay internal, because the client sends
/// or handles them itself and a host that sent <c>initialize</c> or <c>shutdown</c> through the send members would corrupt the session.
/// </para>
/// </remarks>
public static class LspMethodNames
{
	// Lifecycle
	internal const string Initialize = "initialize";
	internal const string Initialized = "initialized";
	internal const string Shutdown = "shutdown";
	internal const string Exit = "exit";

	// Server-to-client notifications and requests
	internal const string PublishDiagnostics = "textDocument/publishDiagnostics";
	internal const string Configuration = "workspace/configuration";
	internal const string WorkspaceFolders = "workspace/workspaceFolders";
	internal const string SemanticTokensRefresh = "workspace/semanticTokens/refresh";
	internal const string DiagnosticRefresh = "workspace/diagnostic/refresh";
	internal const string RegisterCapability = "client/registerCapability";
	internal const string UnregisterCapability = "client/unregisterCapability";
	internal const string WorkDoneProgressCreate = "window/workDoneProgress/create";
	internal const string LogMessage = "window/logMessage";
	internal const string ShowMessage = "window/showMessage";
	internal const string ShowMessageRequest = "window/showMessageRequest";
	internal const string ApplyEdit = "workspace/applyEdit";
	internal const string TelemetryEvent = "telemetry/event";
	internal const string Progress = "$/progress";
	internal const string Hello = "$/hello";

	// Client-to-server notifications
	public const string DidOpen = "textDocument/didOpen";
	public const string DidChange = "textDocument/didChange";
	public const string DidClose = "textDocument/didClose";
	public const string DidChangeConfiguration = "workspace/didChangeConfiguration";
	public const string DidChangeWatchedFiles = "workspace/didChangeWatchedFiles";

	// Client-to-server requests
	public const string Completion = "textDocument/completion";
	public const string CompletionResolve = "completionItem/resolve";
	public const string CodeAction = "textDocument/codeAction";
	public const string CodeActionResolve = "codeAction/resolve";
	public const string ExecuteCommand = "workspace/executeCommand";
	public const string DocumentSymbol = "textDocument/documentSymbol";
	public const string WorkspaceSymbol = "workspace/symbol";
	public const string WorkspaceSymbolResolve = "workspaceSymbol/resolve";
	public const string Formatting = "textDocument/formatting";
	public const string Hover = "textDocument/hover";
	public const string Definition = "textDocument/definition";
	public const string References = "textDocument/references";
	public const string Rename = "textDocument/rename";
	public const string SignatureHelp = "textDocument/signatureHelp";
	public const string SemanticTokensFull = "textDocument/semanticTokens/full";
	public const string DocumentDiagnostic = "textDocument/diagnostic";
}
