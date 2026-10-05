namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Pins every centralized LSP method name to its exact wire spelling.</summary>
[TestClass]
public sealed class LspMethodNamesTests
{
	[TestMethod]
	public void MethodNames_MatchTheLspWireStrings()
	{
		// Each pair is read back through the loop variable rather than comparing the const expressions directly, so
		// the compiler-visible constness does not turn the assertion into a condition the analyzers fold away.
		var expectations = new (string Constant, string Expected)[]
		{
			// Lifecycle
			(LspMethodNames.Initialize, "initialize"),
			(LspMethodNames.Initialized, "initialized"),
			(LspMethodNames.Shutdown, "shutdown"),
			(LspMethodNames.Exit, "exit"),

			// Server-to-client notifications and requests
			(LspMethodNames.PublishDiagnostics, "textDocument/publishDiagnostics"),
			(LspMethodNames.Configuration, "workspace/configuration"),
			(LspMethodNames.WorkspaceFolders, "workspace/workspaceFolders"),
			(LspMethodNames.SemanticTokensRefresh, "workspace/semanticTokens/refresh"),
			(LspMethodNames.DiagnosticRefresh, "workspace/diagnostic/refresh"),
			(LspMethodNames.RegisterCapability, "client/registerCapability"),
			(LspMethodNames.UnregisterCapability, "client/unregisterCapability"),
			(LspMethodNames.WorkDoneProgressCreate, "window/workDoneProgress/create"),
			(LspMethodNames.LogMessage, "window/logMessage"),
			(LspMethodNames.ShowMessage, "window/showMessage"),
			(LspMethodNames.ShowMessageRequest, "window/showMessageRequest"),
			(LspMethodNames.ApplyEdit, "workspace/applyEdit"),
			(LspMethodNames.TelemetryEvent, "telemetry/event"),
			(LspMethodNames.Progress, "$/progress"),
			(LspMethodNames.Hello, "$/hello"),

			// Client-to-server notifications
			(LspMethodNames.DidOpen, "textDocument/didOpen"),
			(LspMethodNames.DidChange, "textDocument/didChange"),
			(LspMethodNames.DidClose, "textDocument/didClose"),
			(LspMethodNames.DidChangeConfiguration, "workspace/didChangeConfiguration"),
			(LspMethodNames.DidChangeWatchedFiles, "workspace/didChangeWatchedFiles"),

			// Client-to-server requests
			(LspMethodNames.Completion, "textDocument/completion"),
			(LspMethodNames.CompletionResolve, "completionItem/resolve"),
			(LspMethodNames.CodeAction, "textDocument/codeAction"),
			(LspMethodNames.CodeActionResolve, "codeAction/resolve"),
			(LspMethodNames.ExecuteCommand, "workspace/executeCommand"),
			(LspMethodNames.DocumentSymbol, "textDocument/documentSymbol"),
			(LspMethodNames.WorkspaceSymbol, "workspace/symbol"),
			(LspMethodNames.WorkspaceSymbolResolve, "workspaceSymbol/resolve"),
			(LspMethodNames.Formatting, "textDocument/formatting"),
			(LspMethodNames.Hover, "textDocument/hover"),
			(LspMethodNames.Definition, "textDocument/definition"),
			(LspMethodNames.References, "textDocument/references"),
			(LspMethodNames.Rename, "textDocument/rename"),
			(LspMethodNames.SignatureHelp, "textDocument/signatureHelp"),
			(LspMethodNames.SemanticTokensFull, "textDocument/semanticTokens/full"),
			(LspMethodNames.DocumentDiagnostic, "textDocument/diagnostic"),
		};

		foreach ((string constant, string expected) in expectations)
			Assert.AreEqual(expected, constant);
	}
}
