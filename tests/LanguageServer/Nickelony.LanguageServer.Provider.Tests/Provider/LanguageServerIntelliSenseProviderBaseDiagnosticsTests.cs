using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the framework pull-diagnostics loop: the capability gate, the request payload's <c>previousResultId</c>
/// threading, applying full and unchanged reports to the same cache and event the push path uses, related-document
/// reports, and the server-driven refresh fan-out.
/// </summary>
[TestClass]
public sealed class LanguageServerIntelliSenseProviderBaseDiagnosticsTests
{
	private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-diagnostics-tests-" + Guid.NewGuid().ToString("N"));
	private static readonly string FilePath = Path.Combine(WorkspaceRoot, "Scripts", "test.test");
	private static readonly string RelatedFilePath = Path.Combine(WorkspaceRoot, "Scripts", "related.test");
	private const string Content = "line one";
	private const string UpdatedContent = "line two";
	private const string DocumentDiagnosticMethod = "textDocument/diagnostic";
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	[TestMethod]
	public async Task PullDiagnostics_WhenSupported_SendsRequestWithoutPreviousResultIdAndAppliesTheFullReport()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using var provider = CreateProvider(client);
		var updates = new List<int>();

		provider.DiagnosticsFactory = _ => CreateDiagnostics(3);
		provider.DiagnosticsUpdated += (_, eventArgs) => updates.Add(eventArgs.Diagnostics.Count);
		client.SendRequestHandler = static (method, _, _) => Task.FromResult<object?>(
			string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal)
				? CreateFullReport("r1")
				: null);

		provider.OpenDocument(FilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		// The first request carries no previousResultId: nothing has been applied for the document yet.
		JsonElement parameters = client.GetLastRequestParameters(DocumentDiagnosticMethod);
		Assert.AreEqual(new Uri(FilePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsFalse(parameters.TryGetProperty("previousResultId", out _));

		// The report flows through the same hook and cache the push path uses.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.GetDiagnostics(FilePath).Count == 3, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updates.Count == 1, s_waitTimeout));
		Assert.AreEqual(3, updates[0]);
	}

	[TestMethod]
	public async Task PullDiagnostics_UnchangedReport_KeepsTheCacheAndThreadsTheResultId()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using var provider = CreateProvider(client);
		int updateCount = 0;

		provider.DiagnosticsFactory = _ => CreateDiagnostics(2);
		provider.DiagnosticsUpdated += (_, _) => updateCount++;

		client.SendRequestHandler = static (method, parameters, _) =>
		{
			if (!string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal))
				return Task.FromResult<object?>(null);

			string? previousResultId = parameters is DocumentDiagnosticParams request ? request.PreviousResultId : null;

			return Task.FromResult<object?>(previousResultId switch
			{
				null => CreateFullReport("r1"),
				"r1" => CreateUnchangedReport("r2"),
				_ => CreateFullReport("r3")
			});
		};

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updateCount == 1, s_waitTimeout));

		// A change re-triggers the pull; the fake answers with an unchanged report because the request carries the
		// first report's resultId.
		provider.UpdateDocument(FilePath, UpdatedContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => client.GetLastRequestParameters(DocumentDiagnosticMethod).TryGetProperty("previousResultId", out JsonElement previous) && previous.GetString() == "r1",
			s_waitTimeout));

		// The unchanged report applied nothing and raised nothing, so the cached diagnostics remain.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		Assert.AreEqual(1, updateCount);
		Assert.AreEqual(2, provider.GetDiagnostics(FilePath).Count);
	}

	[TestMethod]
	public async Task PullDiagnostics_WhenTheServerDoesNotAdvertiseTheCapability_SendsNoRequest()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = CreateProvider(client);

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// The push-only default: the capability is not negotiated, so no pull is ever issued.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		Assert.AreEqual(0, client.GetSentMethodCount(DocumentDiagnosticMethod));
		Assert.AreEqual(0, provider.GetDiagnostics(FilePath).Count);
	}

	[TestMethod]
	public async Task PullDiagnostics_UnrecognizedReportKind_IsDroppedAndKeepsTheCache()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using var provider = CreateProvider(client);
		int updateCount = 0;

		provider.DiagnosticsFactory = _ => CreateDiagnostics(2);
		provider.DiagnosticsUpdated += (_, _) => updateCount++;
		client.SendRequestHandler = static (method, _, _) => Task.FromResult<object?>(
			string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal)
				? new DocumentDiagnosticReportPayload("bogus", "r1", [])
				: null);

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		Assert.AreEqual(0, updateCount);
		Assert.AreEqual(0, provider.GetDiagnostics(FilePath).Count);
	}

	[TestMethod]
	public async Task PullDiagnostics_RelatedReport_IsAppliedToTheTrackedRelatedDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using var provider = CreateProvider(client);

		// The related document reports a different count so the assertion proves which document received which report.
		provider.DiagnosticsFactory = path => CreateDiagnostics(LanguageServerPaths.AreLocalPathsEqual(path, RelatedFilePath) ? 4 : 1);

		client.SendRequestHandler = static (method, parameters, _) =>
		{
			if (!string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal)
				|| parameters is not DocumentDiagnosticParams request
				|| !LanguageServerPaths.AreLocalPathsEqual(FilePath, new Uri(request.TextDocument.Uri).LocalPath))
			{
				return Task.FromResult<object?>(null);
			}

			var relatedDocuments = new Dictionary<string, DocumentDiagnosticReportPayload>
			{
				[new Uri(RelatedFilePath).AbsoluteUri] = new(DocumentDiagnosticReportPayload.FullKind, "r-related", [CreateDiagnostic()])
			};

			return Task.FromResult<object?>(new DocumentDiagnosticReportPayload(
				DocumentDiagnosticReportPayload.FullKind, "r-primary", [CreateDiagnostic()], relatedDocuments));
		};

		provider.OpenDocument(RelatedFilePath, Content);

		// The related document's own pull proves it is tracked before the primary request can report for it.
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		provider.OpenDocument(FilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.GetDiagnostics(RelatedFilePath).Count == 4, s_waitTimeout));
		Assert.AreEqual(1, provider.GetDiagnostics(FilePath).Count);
	}

	[TestMethod]
	public async Task DiagnosticRefreshRequested_RepullsTheOpenDocuments()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using var provider = CreateProvider(client);

		provider.DiagnosticsFactory = _ => CreateDiagnostics(1);
		client.SendRequestHandler = static (method, _, _) => Task.FromResult<object?>(
			string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal)
				? CreateFullReport("r1")
				: null);

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		// The server asks the client to re-pull; the fan-out issues a second request that threads the first resultId.
		client.RaiseDiagnosticRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => client.GetLastRequestParameters(DocumentDiagnosticMethod).TryGetProperty("previousResultId", out JsonElement previous) && previous.GetString() == "r1",
			s_waitTimeout));
	}

	[TestMethod]
	public async Task PullDiagnostics_AfterRestartReopen_PullsForTheReopenedDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using var provider = CreateProvider(client);

		provider.DiagnosticsFactory = _ => CreateDiagnostics(1);
		client.SendRequestHandler = static (method, _, _) => Task.FromResult<object?>(
			string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal)
				? CreateFullReport("r1")
				: null);

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		// A transport loss invalidates the session; the next request restarts and replays the tracked document.
		client.RaiseTransportUnavailable(client.TransportGeneration);

		// The real client re-advertises its capabilities after the re-handshake; the fake applies a static
		// loss snapshot, so restore the pull capability before the restart completes.
		client.SupportsPullDiagnostics = true;

		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout));

		// The restart reopen pulls diagnostics for the reopened document, exactly like an ordinary open; without
		// the replay pull a pull-capable server would report nothing for reopened documents until the next change.
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));
	}

	private static TestLanguageServerProvider CreateProvider(FakeLanguageServerClient client)
		=> new([WorkspaceRoot], client);

	private static IReadOnlyList<TextDiagnostic> CreateDiagnostics(int count)
	{
		var diagnostics = new TextDiagnostic[count];

		for (int i = 0; i < count; i++)
			diagnostics[i] = new TextDiagnostic(TextDiagnosticSeverity.Warning, $"diagnostic-{i}", i, i);

		return diagnostics;
	}

	private static DiagnosticPayload CreateDiagnostic()
		=> new(
			new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 1)),
			DiagnosticSeverity.Warning,
			"message",
			"test",
			null);

	private static DocumentDiagnosticReportPayload CreateFullReport(string resultId)
		=> new(DocumentDiagnosticReportPayload.FullKind, resultId, [CreateDiagnostic()]);

	private static DocumentDiagnosticReportPayload CreateUnchangedReport(string resultId)
		=> new(DocumentDiagnosticReportPayload.UnchangedKind, resultId, null);
}
