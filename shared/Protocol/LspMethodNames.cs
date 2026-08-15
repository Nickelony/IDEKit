namespace Nickelony.LanguageServer.Protocol;

/// <summary>
/// Carries the LSP method names the language-server family sends or dispatches, so each wire string exists once
/// across the client, provider, and language packages instead of being re-typed at every call site.
/// </summary>
/// <remarks>
/// <para>
/// This file is compiled into every package that needs it through a <c>&lt;Compile Include&gt;</c> link. The
/// <c>Nickelony.LanguageServer.Protocol</c> namespace is intentionally shared by those linked copies instead of
/// following one package's folder-to-namespace convention, so the constants resolve the same way in each assembly.
/// </para>
/// <para>
/// A package that consumes the provider framework's internals through <c>InternalsVisibleTo</c> must not also link
/// this file: two visible copies of the same internal type make every use of it ambiguous (CS0436). Such a package
/// resolves the type from the framework assembly instead.
/// </para>
/// <para>
/// A mistyped wire string degrades silently at runtime - a request falls back to its documented default - so the
/// names are centralized here to make a typo a compile error instead.
/// </para>
/// </remarks>
internal static class LspMethodNames
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
	internal const string TelemetryEvent = "telemetry/event";
	internal const string Progress = "$/progress";
	internal const string Hello = "$/hello";

	// Client-to-server notifications
	internal const string DidOpen = "textDocument/didOpen";
	internal const string DidChange = "textDocument/didChange";
	internal const string DidClose = "textDocument/didClose";
	internal const string DidChangeConfiguration = "workspace/didChangeConfiguration";
	internal const string DidChangeWatchedFiles = "workspace/didChangeWatchedFiles";

	// Client-to-server requests
	internal const string Completion = "textDocument/completion";
	internal const string CompletionResolve = "completionItem/resolve";
	internal const string CodeAction = "textDocument/codeAction";
	internal const string DocumentSymbol = "textDocument/documentSymbol";
	internal const string Formatting = "textDocument/formatting";
	internal const string Hover = "textDocument/hover";
	internal const string Definition = "textDocument/definition";
	internal const string References = "textDocument/references";
	internal const string Rename = "textDocument/rename";
	internal const string SignatureHelp = "textDocument/signatureHelp";
	internal const string SemanticTokensFull = "textDocument/semanticTokens/full";
	internal const string DocumentDiagnostic = "textDocument/diagnostic";
}
