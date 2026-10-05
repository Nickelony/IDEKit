using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text.Json;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Formatting;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Rename;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>Live tests against a developer-supplied LuaLS archive.</summary>
/// <remarks>
/// The archive is supplied by the developer, so the tests are inconclusive when <c>NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE</c> is unset or the expected
/// executable is missing after extraction. A malformed archive fails during setup.
/// </remarks>
[TestClass]
[TestCategory(TestCategories.Integration)]
public sealed class LuaLanguageServerRealIntegrationTests
{
	// A script whose first statement is a syntax error, and the script that completes it and adds a local to complete.
	private const string InitialContent = "local stable_local =\r\nreturn stable_local\r\n";
	private const string UpdatedContent = "local stable_local = 1\r\nlocal updated_local = stable_local + 1\r\nreturn updated_local\r\nupd";

	private static readonly TimeSpan s_integrationTimeout = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(150);

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_ReportsASyntaxErrorOfTheOpenedDocumentAsAnError()
	{
		using var script = new LiveScript("syntax.lua", InitialContent);

		IReadOnlyList<Diagnostic> diagnostics = await TestPolling.WaitForAsync(
			action: () => Task.FromResult(script.Provider.GetDiagnostics(script.FilePath)),
			isSatisfied: reported => reported.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
			timeout: s_integrationTimeout,
			failureMessage: "Expected LuaLS to publish an error for the syntax error in the opened document.",
			describeLastResult: reported => "Last diagnostic codes: " + string.Join(", ", reported.Select(diagnostic => diagnostic.Code))
		);

		Assert.Contains(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error, diagnostics);
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_ReturnsSemanticTokensThatReflectAnUpdatedDocument()
	{
		using var script = new LiveScript("tokens.lua", InitialContent);

		await script.WaitUntilAvailableAsync();

		script.Provider.UpdateDocument(script.FilePath, UpdatedContent);

		var updatedLineMap = TextLineMap.Build(UpdatedContent);

		await WaitForConditionAsync(
			() => script.Provider.GetSemanticTokens(script.FilePath).Any(token => token.Range.Offset >= updatedLineMap.GetLineStartOffset(2)),
			s_integrationTimeout,
			"Expected semantic tokens to reflect the updated live document content."
		);
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_CompletesALocalOfTheOpenedDocument()
	{
		using var script = new LiveScript("completion.lua", UpdatedContent);

		IReadOnlyList<CompletionItem> completionItems = await TestPolling.WaitForAsync(
			action: () =>
				script.Provider.GetCompletionItemsAsync(
					new CompletionRequest(script.FilePath, UpdatedContent, UpdatedContent.OffsetAt(new TextPosition(3, 3))),
					TestContext.CancellationToken
				),

			isSatisfied: items => items.Any(item => string.Equals(item.Label, "updated_local", StringComparison.Ordinal)),
			timeout: s_integrationTimeout,
			failureMessage: "Expected LuaLS to return completion items for the updated local variable.",
			describeLastResult: items => "Last completion labels: " + string.Join(", ", items.Select(item => item.Label))
		);

		Assert.Contains(item => string.Equals(item.Label, "updated_local", StringComparison.Ordinal), completionItems);
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_ReturnsHoverInformationForALocalOfTheOpenedDocument()
	{
		using var script = new LiveScript("hover.lua", UpdatedContent);

		HoverInformation? hover = await TestPolling.WaitForAsync(
			action: () =>
				script.Provider.GetHoverAsync(
					filePath: script.FilePath,
					documentText: UpdatedContent,
					position: new TextPosition(2, 8),
					cancellationToken: TestContext.CancellationToken
				),

			isSatisfied: hoverInfo => hoverInfo is not null,
			timeout: s_integrationTimeout,
			failureMessage: "Expected LuaLS to return hover information for the opened document.",
			describeLastResult: _ => "No hover information was returned."
		);

		Assert.IsNotNull(hover);
		Assert.IsFalse(string.IsNullOrWhiteSpace(hover.Content.Value));
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_ReturnsDocumentSymbolsForALocalOfTheOpenedDocument()
	{
		using var script = new LiveScript("symbols.lua", UpdatedContent);

		IReadOnlyList<DocumentSymbol> documentSymbols = await TestPolling.WaitForAsync(
			action: () => script.Provider.GetDocumentSymbolsAsync(script.FilePath, UpdatedContent, TestContext.CancellationToken),
			isSatisfied: symbols => symbols.Any(symbol => string.Equals(symbol.Name, "updated_local", StringComparison.Ordinal)),
			timeout: s_integrationTimeout,
			failureMessage: "Expected LuaLS to return document symbols for the opened document.",
			describeLastResult: symbols => "Last symbol names: " + string.Join(", ", symbols.Select(symbol => symbol.Name))
		);

		DocumentSymbol localSymbol = documentSymbols.First(symbol => string.Equals(symbol.Name, "updated_local", StringComparison.Ordinal));

		Assert.AreEqual(DocumentSymbolKind.Variable, localSymbol.Kind);
		Assert.IsNotNull(localSymbol.SelectionRange);
		Assert.IsLessThanOrEqualTo(UpdatedContent.Length, localSymbol.SelectionRange.Value.EndOffset);
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_CompletesASymbolOfALibraryFolderAddedWhileRunning()
	{
		const string initialContent = "local seed = 1\r\n";
		const string libraryAwareContent = initialContent + "gen";

		using var script = new LiveScript("library.lua", initialContent);

		await script.WaitUntilAvailableAsync();

		// The host generates Lua stubs outside the workspace and declares their folder as a LuaLS library in .luarc.json,
		// so the server sees the stubs only through that declaration.
		using var library = new TemporaryDirectory("LuaLibrary_");

		File.WriteAllText(library.GetFilePath("Generated.lua"), "---@meta\r\n" + "function generated_function() end\r\n");

		File.WriteAllText(
			Path.Combine(script.WorkspaceRoot, ".luarc.json"),
			JsonSerializer.Serialize(new Dictionary<string, string[]> { ["workspace.library"] = [library.Path] })
		);

		script.Provider.UpdateDocument(script.FilePath, libraryAwareContent);

		IReadOnlyList<CompletionItem> libraryItems = await TestPolling.WaitForAsync(
			action: () =>
				script.Provider.GetCompletionItemsAsync(
					new CompletionRequest(script.FilePath, libraryAwareContent, libraryAwareContent.OffsetAt(new TextPosition(1, 3))),
					TestContext.CancellationToken
				),

			isSatisfied: items => items.Any(item => item.Label.StartsWith("generated_function", StringComparison.Ordinal)),
			timeout: s_integrationTimeout,
			failureMessage: "Expected a newly forwarded symbol from the generated library folder to appear in completions after the live workspace change.",
			describeLastResult: items => "Last completion labels: " + string.Join(", ", items.Select(item => item.Label))
		);

		Assert.Contains(item => item.Label.StartsWith("generated_function", StringComparison.Ordinal), libraryItems);
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_StopsTheServerProcessWhenTheProviderIsDisposed()
	{
		using var script = new LiveScript("shutdown.lua", InitialContent);

		await script.WaitUntilAvailableAsync();

		int processId = GetRequiredServerProcessId(script.ExecutablePath);

		script.Provider.Dispose();

		await WaitForProcessExitAsync(
			processId,
			s_integrationTimeout,
			"Expected disposing the provider to stop the live Lua language-server process."
		);
	}

	[TestMethod]
	[DataRow(2, true, "  ")]
	[DataRow(4, false, "\t")]
	public async Task Provider_WithRealLuaLanguageServer_FormatsAScriptWithTheRequestedIndentation(int tabSize, bool insertSpaces, string indentation)
	{
		const string content = "local function f()\r\nlocal x=1\r\nif x then\r\nreturn   x\r\nend\r\nend\r\nreturn f\r\n";

		using var script = new LiveScript("format.lua", content);

		WorkspaceEdit? formatting = await TestPolling.WaitForAsync(
			action: () =>
				script.Provider.GetFormattingEditsAsync(
					new FormattingRequest(script.FilePath, content, new FormattingOptions(tabSize, insertSpaces)),
					TestContext.CancellationToken
				),

			isSatisfied: edit => edit?.HasChanges == true,
			timeout: s_integrationTimeout,
			failureMessage: "Expected LuaLS to return formatting edits for the unformatted script.",
			describeLastResult: _ => "No formatting edit was returned."
		);

		Assert.IsNotNull(formatting);

		// The edit names the document as the host did, and applying it indents each nesting level by the requested unit
		// and normalizes the spacing of the seeded statements.
		TextDocumentEdit documentEdit = Assert.ContainsSingle(formatting.DocumentEdits);

		Assert.AreEqual(script.FilePath, documentEdit.DocumentId);

		string[] expectedLines =
		[
			"local function f()",
			indentation + "local x = 1",
			indentation + "if x then",
			indentation + indentation + "return x",
			indentation + "end",
			"end",
			"return f",
			"",
		];

		Assert.AreEqual(string.Join("\n", expectedLines), ApplyEdits(content, documentEdit.TextEdits).ReplaceLineEndings("\n"));
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_OffersQuickFixesForAReportedDiagnostic()
	{
		const string content = "local unused_value = 1\r\nreturn 1\r\n";

		using var script = new LiveScript("actions.lua", content);

		IReadOnlyList<Diagnostic> diagnostics = await TestPolling.WaitForAsync(
			action: () => Task.FromResult(script.Provider.GetDiagnostics(script.FilePath)),
			isSatisfied: reported => reported.Any(diagnostic => diagnostic.Code == "unused-local"),
			timeout: s_integrationTimeout,
			failureMessage: "Expected LuaLS to report the unused local variable.",
			describeLastResult: reported => "Last diagnostic codes: " + string.Join(", ", reported.Select(diagnostic => diagnostic.Code))
		);

		Diagnostic unusedLocal = diagnostics.First(diagnostic => diagnostic.Code == "unused-local");

		// The request names no diagnostics, so the provider reports those cached for the requested range, from which LuaLS derives its quick fixes.
		IReadOnlyList<CodeAction> actions = await TestPolling.WaitForAsync(
			action: () =>
				script.Provider.GetCodeActionsAsync(
					new CodeActionRequest(script.FilePath, content, unusedLocal.Range),
					TestContext.CancellationToken
				),

			isSatisfied: offered => offered.Count > 0,
			timeout: s_integrationTimeout,
			failureMessage: "Expected LuaLS to offer code actions for the unused local variable.",
			describeLastResult: offered => "Last action titles: " + string.Join(", ", offered.Select(action => action.Title))
		);

		Assert.IsTrue(actions.All(action => action.Kind == "quickfix"));

		// The fixes that carry an edit annotate the script for the reported code and leave the rest of it as it was.
		CodeAction[] editingActions = [.. actions.Where(action => action.Edit is not null)];

		Assert.IsNotEmpty(editingActions);

		foreach (CodeAction action in editingActions)
		{
			TextDocumentEdit documentEdit = Assert.ContainsSingle(action.Edit!.DocumentEdits);

			Assert.AreEqual(script.FilePath, documentEdit.DocumentId);

			string fixedContent = ApplyEdits(content, documentEdit.TextEdits);

			Assert.Contains("---@diagnostic", fixedContent);
			Assert.Contains("unused-local", fixedContent);
			Assert.Contains(content, fixedContent);
		}

		// The workspace-wide fix changes the LuaLS configuration, so it is a command and carries no edit.
		Assert.Contains(action => action.Edit is null && action.Command?.Name == "lua.setConfig", actions);
	}

	[TestMethod]
	[DataRow("add(", 0)]
	[DataRow("add(1, ", 1)]
	public async Task Provider_WithRealLuaLanguageServer_ReportsTheSignatureAndActiveParameterOfACall(
		string calleeAndArguments,
		int expectedActiveParameter
	)
	{
		const string content = "local function add(left, right)\r\n  return left + right\r\nend\r\nadd(1, 2)\r\n";

		using var script = new LiveScript("signature.lua", content);

		int caretOffset = content.IndexOf("add(1, 2)", StringComparison.Ordinal) + calleeAndArguments.Length;

		SignatureHelpInformation? help = await TestPolling.WaitForAsync(
			action: () =>
				script.Provider.GetSignatureHelpAsync(new SignatureHelpRequest(script.FilePath, content, caretOffset), TestContext.CancellationToken),

			isSatisfied: signatureHelp => signatureHelp is not null,
			timeout: s_integrationTimeout,
			failureMessage: "Expected LuaLS to return signature help inside the call.",
			describeLastResult: _ => "No signature help was returned."
		);

		Assert.IsNotNull(help);

		SignatureInformation signature = Assert.ContainsSingle(help.Signatures);

		Assert.AreEqual(0, help.ActiveSignatureIndex);
		Assert.AreEqual(expectedActiveParameter, help.ActiveParameterIndex);
		Assert.HasCount(2, signature.Parameters);

		// LuaLS labels each parameter with an offset pair into the signature label, which the client advertises.
		string[] parameterNames = ["left", "right"];

		for (int i = 0; i < parameterNames.Length; i++)
		{
			ParameterInformation parameter = signature.Parameters[i];

			Assert.StartsWith(parameterNames[i], parameter.Label, StringComparison.Ordinal);
			Assert.IsNotNull(parameter.Range);
			Assert.AreEqual(parameter.Label, parameter.Range.Value.GetText(signature.Label));
		}
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_RestartsAfterLiveServerCrashAndResumesRequests()
	{
		using var session = new RealLuaLanguageServerTestSession();

		string filePath = Path.Combine(session.WorkspaceRoot, "Scripts", "restart.lua");

		const string initialContent = "local restart_probe = 1\r\nres";
		const string restartedContent = "local restart_probe = 1\r\nlocal after_restart = restart_probe + 1\r\naft";

		Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? session.WorkspaceRoot);
		File.WriteAllText(filePath, initialContent);

		using var provider = new LanguageServerFeatureProvider(
			LuaLanguageServerProfileFactory.Create(),
			[session.WorkspaceRoot],
			session.ExecutablePath
		);

		provider.OpenDocument(filePath, initialContent);

		IReadOnlyList<CompletionItem> initialItems = await TestPolling.WaitForAsync(
			action: () =>
				provider.GetCompletionItemsAsync(
					new CompletionRequest(filePath, initialContent, initialContent.OffsetAt(new TextPosition(1, 3))),
					TestContext.CancellationToken
				),

			isSatisfied: items => items.Any(item => string.Equals(item.Label, "restart_probe", StringComparison.Ordinal)),
			timeout: s_integrationTimeout,
			failureMessage: "Expected LuaLS to return the initial completion before restart.",
			describeLastResult: items => "Last completion labels: " + string.Join(", ", items.Select(item => item.Label))
		);

		Assert.Contains(item => string.Equals(item.Label, "restart_probe", StringComparison.Ordinal), initialItems);

		int initialProcessId = GetRequiredServerProcessId(session.ExecutablePath);

		using (var initialProcess = Process.GetProcessById(initialProcessId))
			initialProcess.Kill(entireProcessTree: true);

		await WaitForProcessExitAsync(
			initialProcessId,
			s_integrationTimeout,
			"Expected the live Lua language-server process to exit after the simulated crash."
		);

		await WaitForConditionAsync(
			() => !provider.IsAvailable,
			s_integrationTimeout,
			"Expected the provider to observe the live LuaLS connection disconnect."
		);

		provider.UpdateDocument(filePath, restartedContent);

		IReadOnlyList<CompletionItem> restartedItems = await TestPolling.WaitForAsync(
			action: () =>
				provider.GetCompletionItemsAsync(
					new CompletionRequest(filePath, restartedContent, restartedContent.OffsetAt(new TextPosition(2, 3))),
					TestContext.CancellationToken
				),

			isSatisfied: items => items.Any(item => string.Equals(item.Label, "after_restart", StringComparison.Ordinal)),
			timeout: s_integrationTimeout,
			failureMessage: "Expected the provider to restart the live server and resume completions after the crash.",
			describeLastResult: items => "Last completion labels: " + string.Join(", ", items.Select(item => item.Label))
		);

		Assert.Contains(item => string.Equals(item.Label, "after_restart", StringComparison.Ordinal), restartedItems);

		int restartedProcessId = GetRequiredServerProcessId(session.ExecutablePath);

		Assert.AreNotEqual(initialProcessId, restartedProcessId);

		provider.Dispose();

		await WaitForProcessExitAsync(
			restartedProcessId,
			s_integrationTimeout,
			"Expected the restarted live Lua language-server process to stop on provider disposal."
		);
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_ResolvesDefinitionAndReferencesForLocalSymbol()
	{
		using var session = new RealLuaLanguageServerTestSession();

		string filePath = Path.Combine(session.WorkspaceRoot, "Scripts", "navigation.lua");
		const string content = "local tracked_value = 1\r\n" + "local combined = tracked_value + tracked_value\r\n" + "return combined\r\n";

		Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? session.WorkspaceRoot);
		File.WriteAllText(filePath, content);

		using var provider = new LanguageServerFeatureProvider(
			LuaLanguageServerProfileFactory.Create(),
			[session.WorkspaceRoot],
			session.ExecutablePath
		);

		provider.OpenDocument(filePath, content);

		await WaitForConditionAsync(
			() => provider.Capabilities.SupportsReferences,
			s_integrationTimeout,
			"Expected the Lua language server to advertise reference support."
		);

		DefinitionLocation? definition = await TestPolling.WaitForAsync(
			action: () =>
				provider.GetDefinitionAsync(
					filePath,
					documentText: content,
					position: new TextPosition(1, 19),
					cancellationToken: TestContext.CancellationToken
				),

			isSatisfied: definitionLocation => definitionLocation is not null,
			timeout: s_integrationTimeout,
			failureMessage: "Expected the Lua language server to resolve the local symbol definition.",
			describeLastResult: _ => "No definition location was returned."
		);

		Assert.IsNotNull(definition);

		// A definition in the requested document names no document, so the host navigates within the one it asked about.
		Assert.IsNull(definition.DocumentId);
		Assert.AreEqual(new TextPosition(0, 6), definition.TargetRange.Start);

		IReadOnlyList<ReferenceLocation> references = await TestPolling.WaitForAsync(
			action: () =>
				provider.GetReferencesAsync(
					new ReferenceRequest(filePath, content, content.OffsetAt(new TextPosition(1, 19))),
					TestContext.CancellationToken
				),

			isSatisfied: referenceLocations => referenceLocations.Count >= 3,
			timeout: s_integrationTimeout,
			failureMessage: "Expected the Lua language server to return declaration and usage references for the local symbol.",
			describeLastResult: locations => "Last reference count: " + locations.Count
		);

		Assert.AreEqual(3, references.Count(location => LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(location.DocumentId, filePath)));
		Assert.Contains(location => location.Range.Start.Line == 0 && location.Range.Start.Character == 6, references);
		Assert.AreEqual(2, references.Count(location => location.Range.Start.Line == 1));
	}

	[TestMethod]
	public async Task Provider_WithRealLuaLanguageServer_ReturnsWorkspaceEditForRename()
	{
		using var session = new RealLuaLanguageServerTestSession();

		string filePath = Path.Combine(session.WorkspaceRoot, "Scripts", "rename.lua");
		const string content = "local tracked_value = 1\r\n" + "local result = tracked_value + 2\r\n" + "return tracked_value, result\r\n";

		Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? session.WorkspaceRoot);
		File.WriteAllText(filePath, content);

		using var provider = new LanguageServerFeatureProvider(
			LuaLanguageServerProfileFactory.Create(),
			[session.WorkspaceRoot],
			session.ExecutablePath
		);

		provider.OpenDocument(filePath, content);

		await WaitForConditionAsync(
			() => provider.Capabilities.SupportsRename,
			s_integrationTimeout,
			"Expected the Lua language server to advertise rename support."
		);

		WorkspaceEdit? workspaceEdit = await TestPolling.WaitForAsync(
			action: () =>
				provider.GetRenameEditsAsync(
					new RenameRequest(
						documentId: filePath,
						documentText: content,
						symbolOffset: content.OffsetAt(new TextPosition(0, 8)),
						newName: "renamed_value"
					),

					TestContext.CancellationToken
				),

			isSatisfied: edit => edit?.HasChanges == true,
			timeout: s_integrationTimeout,
			failureMessage: "Expected the Lua language server to return a rename workspace edit for the local symbol.",
			describeLastResult: _ => "No rename workspace edit was returned."
		);

		Assert.IsNotNull(workspaceEdit);
		Assert.IsTrue(workspaceEdit.HasChanges);
		Assert.HasCount(1, workspaceEdit.DocumentEdits);

		TextDocumentEdit documentEdit = workspaceEdit.DocumentEdits[0];

		Assert.IsTrue(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(filePath, documentEdit.DocumentId));
		Assert.IsGreaterThanOrEqualTo(3, documentEdit.TextEdits.Count);
		Assert.IsTrue(documentEdit.TextEdits.All(edit => edit.NewText == "renamed_value"));
	}

	/// <summary>Returns the id of the live process that was launched from the executable of the test session.</summary>
	/// <param name="executablePath">The path of the extracted language-server executable.</param>
	/// <remarks>
	/// The provider exposes no process identity, but each session extracts the archive into a folder of its own, so the executable path names
	/// the process the provider launched.
	/// </remarks>
	private static int GetRequiredServerProcessId(string executablePath)
	{
		foreach (Process candidate in Process.GetProcesses())
		{
			using (candidate)
			{
				if (
					TryGetExecutablePath(candidate) is { } candidatePath
					&& LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(candidatePath, executablePath)
				)
				{
					return candidate.Id;
				}
			}
		}

		throw new InvalidOperationException("No live process was launched from the language-server executable of this test session.");
	}

	private static string? TryGetExecutablePath(Process process)
	{
		try
		{
			return process.MainModule?.FileName;
		}
		catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			// A process that exited or that this user may not inspect cannot be the child the provider launched.
			return null;
		}
	}

	private static async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout, string failureMessage)
	{
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < timeout)
		{
			if (predicate())
				return;

			await Task.Delay(s_pollInterval).ConfigureAwait(false);
		}

		Assert.Fail(failureMessage);
	}

	private static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout, string failureMessage)
	{
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < timeout)
		{
			if (!TryGetProcess(processId, out Process? process))
				return;

			using (process)
			{
				if (process.HasExited)
					return;
			}

			await Task.Delay(s_pollInterval).ConfigureAwait(false);
		}

		Assert.Fail(failureMessage);
	}

	private static bool TryGetProcess(int processId, [NotNullWhen(true)] out Process? process)
	{
		try
		{
			process = Process.GetProcessById(processId);
			return true;
		}
		catch (ArgumentException)
		{
			process = null;
			return false;
		}
	}

	/// <summary>Applies <paramref name="edits"/> to <paramref name="text"/> and returns the result.</summary>
	/// <remarks>
	/// The edits address the original text, so they are applied from the end backwards and each keeps the offsets of those before it.
	/// </remarks>
	private static string ApplyEdits(string text, IReadOnlyList<TextEdit> edits)
	{
		var lineMap = TextLineMap.Build(text);
		string result = text;

		foreach (TextEdit edit in edits.OrderByDescending(edit => lineMap.GetOffset(edit.Range.Start)))
		{
			int start = lineMap.GetOffset(edit.Range.Start);
			int end = lineMap.GetOffset(edit.Range.End);

			result = result[..start] + edit.NewText + result[end..];
		}

		return result;
	}

	/// <summary>A scratch workspace with one script, a provider over the real server, and the script opened in it.</summary>
	private sealed class LiveScript : IDisposable
	{
		private readonly RealLuaLanguageServerTestSession _session = new();

		public LiveScript(string fileName, string content)
		{
			try
			{
				FilePath = Path.Combine(_session.WorkspaceRoot, "Scripts", fileName);

				Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? _session.WorkspaceRoot);
				File.WriteAllText(FilePath, content);

				Provider = new LanguageServerFeatureProvider(
					LuaLanguageServerProfileFactory.Create(),
					[_session.WorkspaceRoot],
					_session.ExecutablePath
				);

				Provider.OpenDocument(FilePath, content);
			}
			catch
			{
				// A constructor that throws never reaches Dispose, so the session is released here.
				_session.Dispose();
				throw;
			}
		}

		public string ExecutablePath => _session.ExecutablePath;
		public string WorkspaceRoot => _session.WorkspaceRoot;
		public string FilePath { get; }
		public LanguageServerFeatureProvider Provider { get; }

		/// <summary>Waits until the server has started and answers requests.</summary>
		public Task WaitUntilAvailableAsync() =>
			WaitForConditionAsync(() => Provider.IsAvailable, s_integrationTimeout, "Expected the live Lua language server to start.");

		public void Dispose()
		{
			Provider.Dispose();
			_session.Dispose();
		}
	}

	private sealed class RealLuaLanguageServerTestSession : IDisposable
	{
		private readonly string _extractionRoot;

		public RealLuaLanguageServerTestSession()
		{
			string? archivePath = Environment.GetEnvironmentVariable("NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE");

			// The repository ships no LuaLS archive, so the developer downloads one and points this
			// variable at it; the tier stays opt-in rather than silently passing.
			if (archivePath is null || !File.Exists(archivePath))
			{
				throw new AssertInconclusiveException(
					"LuaLS integration fixture is not configured. Set NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE to "
						+ "the LuaLS archive you downloaded; this repository does not provide one."
				);
			}

			_extractionRoot = Path.Combine(Path.GetTempPath(), "LuaLsExtract_" + Guid.NewGuid().ToString("N"));
			WorkspaceRoot = Path.Combine(Path.GetTempPath(), "LuaLsWorkspace_" + Guid.NewGuid().ToString("N"));

			try
			{
				ZipFile.ExtractToDirectory(archivePath, _extractionRoot);
				Directory.CreateDirectory(WorkspaceRoot);

				string executableName = OperatingSystem.IsWindows() ? "lua-language-server.exe" : "lua-language-server";
				string executablePath = Path.Combine(_extractionRoot, "bin", executableName);

				if (!File.Exists(executablePath))
				{
					throw new AssertInconclusiveException(
						$"The configured LuaLS archive was found, but bin/{executableName} was missing after extraction."
					);
				}

				ExecutablePath = executablePath;
			}
			catch
			{
				// A constructor that throws never reaches Dispose, so the temp roots are cleaned up here:
				// a malformed archive or a missing binary cannot leak them.
				TemporaryDirectory.DeleteBestEffort(_extractionRoot);
				TemporaryDirectory.DeleteBestEffort(WorkspaceRoot);

				throw;
			}
		}

		public string ExecutablePath { get; }
		public string WorkspaceRoot { get; }

		public void Dispose()
		{
			TemporaryDirectory.DeleteBestEffort(WorkspaceRoot);
			TemporaryDirectory.DeleteBestEffort(_extractionRoot);
		}
	}

	public required TestContext TestContext { get; set; }
}
