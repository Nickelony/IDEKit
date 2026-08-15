using System.Text.Json;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// Covers the shared Roslyn provider end to end through the fake client: the pull-diagnostics delivery path the
/// Roslyn language server uses, the semantic-token cache, the document-open language identifier, and the
/// unavailable-client contract.
/// </summary>
[TestClass]
public sealed class RoslynLanguageServerIntelliSenseProviderTests
{
	private const string DocumentDiagnosticMethod = "textDocument/diagnostic";
	private const string SemanticTokensFullMethod = "textDocument/semanticTokens/full";
	private const string DidOpenMethod = "textDocument/didOpen";
	private const string Content = "Module M\r\nEnd Module";

	private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), "ls-roslyn-provider-tests-" + Guid.NewGuid().ToString("N"));
	private static readonly string FilePath = Path.Combine(WorkspaceRoot, "Scripts", "Module.vb");
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	[TestMethod]
	public async Task PullDiagnostics_WhenAdvertised_AppliesTheFullReportToTheCacheAndRaisesTheEvent()
	{
		using var client = new FakeLanguageServerClient { SupportsPullDiagnostics = true };
		using var provider = new TestRoslynLanguageServerIntelliSenseProvider([WorkspaceRoot], client);
		var updates = new List<int>();

		provider.DiagnosticsUpdated += (_, eventArgs) => updates.Add(eventArgs.Diagnostics.Count);
		client.SendRequestHandler = static (method, _, _) => Task.FromResult<object?>(
			string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal) ? CreateFullReport("r1") : null);

		provider.OpenDocument(FilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		// The first request carries no previousResultId: nothing has been applied for the document yet.
		JsonElement parameters = client.GetLastRequestParameters(DocumentDiagnosticMethod);
		Assert.AreEqual(new Uri(FilePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsFalse(parameters.TryGetProperty("previousResultId", out _));

		// The report flows through the same store and event the push path uses.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.GetDiagnostics(FilePath).Count == 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updates.Count == 1, s_waitTimeout));
		Assert.AreEqual(1, updates[0]);
	}

	[TestMethod]
	public async Task PullDiagnostics_WhenNotAdvertised_SendsNoRequest()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new TestRoslynLanguageServerIntelliSenseProvider([WorkspaceRoot], client);

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DidOpenMethod, 1, s_waitTimeout));

		// Without the pull capability no request is ever issued, so the cache stays empty.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		Assert.AreEqual(0, client.GetSentMethodCount(DocumentDiagnosticMethod));
		Assert.AreEqual(0, provider.GetDiagnostics(FilePath).Count);
	}

	[TestMethod]
	public async Task DiagnosticRefreshRequested_RepullsTheOpenDocumentAndThreadsTheResultId()
	{
		using var client = new FakeLanguageServerClient { SupportsPullDiagnostics = true };
		using var provider = new TestRoslynLanguageServerIntelliSenseProvider([WorkspaceRoot], client);

		client.SendRequestHandler = static (method, parameters, _) =>
		{
			if (!string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal))
				return Task.FromResult<object?>(null);

			string? previousResultId = parameters is DocumentDiagnosticParams request ? request.PreviousResultId : null;

			return Task.FromResult<object?>(CreateFullReport(previousResultId is null ? "r1" : "r2"));
		};

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		// workspace/diagnostic/refresh re-queries the open documents and threads the previous report's resultId.
		client.RaiseDiagnosticRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => client.GetLastRequestParameters(DocumentDiagnosticMethod).TryGetProperty("previousResultId", out JsonElement previous)
				&& previous.GetString() == "r1",
			s_waitTimeout));
	}

	[TestMethod]
	public async Task SemanticTokens_WhenTheLegendIsAdvertised_AreDecodedAndStored()
	{
		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"],
			SemanticTokenModifiers = []
		};

		using var provider = new TestRoslynLanguageServerIntelliSenseProvider([WorkspaceRoot], client);

		client.SendRequestHandler = static (method, _, _) => Task.FromResult<object?>(
			string.Equals(method, SemanticTokensFullMethod, StringComparison.Ordinal)
				? new SemanticTokensResponsePayload([0, 0, 6, 0, 0])
				: null);

		provider.OpenDocument(FilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(SemanticTokensFullMethod, 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.GetSemanticTokens(FilePath).Count == 1, s_waitTimeout));

		SemanticToken token = provider.GetSemanticTokens(FilePath)[0];

		Assert.AreEqual("variable", token.Type);
		Assert.AreEqual(0, token.Line);
		Assert.AreEqual(0, token.Character);
		Assert.AreEqual(6, token.Length);
	}

	[TestMethod]
	public async Task OpenDocument_ReportsTheLanguagePackageLanguageId()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new TestRoslynLanguageServerIntelliSenseProvider([WorkspaceRoot], client);

		provider.OpenDocument(FilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DidOpenMethod, 1, s_waitTimeout));

		JsonElement didOpen = client.GetLastNotificationParameters(DidOpenMethod);

		Assert.AreEqual(TestRoslynLanguageServerIntelliSenseProvider.TestLanguageId,
			didOpen.GetProperty("textDocument").GetProperty("languageId").GetString());
	}

	[TestMethod]
	public void MissingClient_DisablesIntelliSenseWithoutThrowing()
	{
		using var provider = new TestRoslynLanguageServerIntelliSenseProvider([WorkspaceRoot], client: null);

		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(0, provider.GetDiagnostics(FilePath).Count);
		Assert.AreEqual(0, provider.GetSemanticTokens(FilePath).Count);
	}

	private static DocumentDiagnosticReportPayload CreateFullReport(string resultId)
		=> new(DocumentDiagnosticReportPayload.FullKind, resultId, [CreateDiagnostic()]);

	private static DiagnosticPayload CreateDiagnostic()
		=> new(
			new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 1)),
			DiagnosticSeverity.Warning,
			"BC42104: Variable is used before it has been assigned a value.",
			"roslyn",
			null);
}
