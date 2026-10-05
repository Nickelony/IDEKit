using System.Collections.Concurrent;
using System.Text.Json;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Exercises the pull-diagnostics loop: capability gating, result-id threading, report application, and refresh.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderDiagnosticsTests
{
	private static readonly string s_workspaceRoot = Path.Combine(
		Path.GetTempPath(),
		"ls-provider-diagnostics-tests-" + Guid.NewGuid().ToString("N")
	);

	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");
	private static readonly string s_relatedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "related.test");

	private const string Content = "line one";
	private const string UpdatedContent = "line two";
	private const string DocumentDiagnosticMethod = "textDocument/diagnostic";

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	[TestMethod]
	public async Task PullDiagnostics_WhenSupported_SendsRequestWithoutPreviousResultIdAndAppliesTheFullReport()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using LanguageServerFeatureProvider provider = CreateProvider(client);
		var updates = new List<int>();

		provider.DiagnosticsUpdated += (_, eventArgs) => updates.Add(eventArgs.Diagnostics.Count);

		client.SendRequestHandler = static (method, _, _) =>
			Task.FromResult<object?>(
				string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal) ? CreateFullReport("r1", diagnosticCount: 3) : null
			);

		provider.OpenDocument(s_filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		// The first request carries no previousResultId: nothing has been applied for the document yet.
		JsonElement parameters = client.GetLastRequestParameters(DocumentDiagnosticMethod);

		Assert.AreEqual(new Uri(s_filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsFalse(parameters.TryGetProperty("previousResultId", out _));

		// The report flows through the same mapping and cache the push path uses.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.GetDiagnostics(s_filePath).Count == 3, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updates.Count == 1, s_waitTimeout));
		Assert.AreEqual(3, updates[0]);
	}

	[TestMethod]
	public async Task PullDiagnostics_UnchangedReport_KeepsTheCacheAndThreadsTheResultId()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };

		using LanguageServerFeatureProvider provider = CreateProvider(client);
		int updateCount = 0;

		provider.DiagnosticsUpdated += (_, _) => updateCount++;

		client.SendRequestHandler = static (method, parameters, _) =>
		{
			if (!string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal))
				return Task.FromResult<object?>(null);

			string? previousResultId = parameters is DocumentDiagnosticParams request ? request.PreviousResultId : null;

			return Task.FromResult<object?>(
				previousResultId switch
				{
					null => CreateFullReport("r1", diagnosticCount: 2),
					"r1" => CreateUnchangedReport("r2"),
					_ => CreateFullReport("r3"),
				}
			);
		};

		provider.OpenDocument(s_filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updateCount == 1, s_waitTimeout));

		// A change re-triggers the pull; the fake answers with an unchanged report because the request carries the
		// first report's resultId.
		provider.UpdateDocument(s_filePath, UpdatedContent);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(
				() =>
					client.GetLastRequestParameters(DocumentDiagnosticMethod).TryGetProperty("previousResultId", out JsonElement previous)
					&& previous.GetString() == "r1",

				s_waitTimeout
			)
		);

		// The unchanged report applied nothing and raised nothing, so the cached diagnostics remain.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, updateCount);
		Assert.HasCount(2, provider.GetDiagnostics(s_filePath));
	}

	[TestMethod]
	public async Task PullDiagnostics_UnchangedReportForSupersededSnapshot_DoesNotThreadTheStaleResultId()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };

		using LanguageServerFeatureProvider provider = CreateProvider(client);

		int reportIndex = 0;
		int updateCount = 0;

		provider.DiagnosticsUpdated += (_, _) => Interlocked.Increment(ref updateCount);

		client.SendRequestHandler = (method, _, _) =>
		{
			if (!string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal))
				return Task.FromResult<object?>(null);

			int index = Interlocked.Increment(ref reportIndex);
			return Task.FromResult<object?>(CreateFullReport($"r{index}"));
		};

		provider.OpenDocument(s_filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updateCount == 1, s_waitTimeout));

		// Capture the request-time snapshot, then supersede it by editing the document. The next full report
		// threads "r2".
		DocumentSnapshot staleSnapshot =
			provider.GetTrackedSnapshot(s_filePath) ?? throw new InvalidOperationException("The document should be tracked after opening it.");

		provider.UpdateDocument(s_filePath, UpdatedContent);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updateCount == 2, s_waitTimeout));

		// Apply an unchanged report carrying a stale id against the superseded snapshot. The version fence must
		// reject it, so the next pull keeps advertising the current report's id.
		provider.Composition.DiagnosticsSource.Pulls.ApplyReportCore(staleSnapshot, CreateUnchangedReport("r-stale"));

		provider.UpdateDocument(s_filePath, "line three");

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 3, s_waitTimeout));

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(
				() =>
					client.GetLastRequestParameters(DocumentDiagnosticMethod).TryGetProperty("previousResultId", out JsonElement previous)
					&& previous.GetString() == "r2",

				s_waitTimeout
			)
		);
	}

	[TestMethod]
	public async Task PullDiagnostics_AfterTheDocumentWasClosed_SendsNoPreviousResultId()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };

		using LanguageServerFeatureProvider provider = CreateProvider(client);
		int updateCount = 0;

		provider.DiagnosticsUpdated += (_, _) => Interlocked.Increment(ref updateCount);

		client.SendRequestHandler = static (method, _, _) =>
			Task.FromResult<object?>(string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal) ? CreateFullReport("r1") : null);

		provider.OpenDocument(s_filePath, Content);

		// The update is raised after the report threads its result id.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => Volatile.Read(ref updateCount) == 1, s_waitTimeout));

		provider.CloseDocument(s_filePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout));

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));

		// A closed document has no server copy, so the id the server issued for it must not be advertised again.
		Assert.IsFalse(client.GetLastRequestParameters(DocumentDiagnosticMethod).TryGetProperty("previousResultId", out _));
	}

	[TestMethod]
	public async Task PullDiagnostics_WhenTheServerDoesNotAdvertiseTheCapability_SendsNoRequest()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = CreateProvider(client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// The push-only default: the capability is not negotiated, so no pull is ever issued.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodCount(DocumentDiagnosticMethod));
		Assert.IsEmpty(provider.GetDiagnostics(s_filePath));
	}

	[TestMethod]
	public async Task PullDiagnostics_UnrecognizedReportKind_IsDroppedAndKeepsTheCache()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };

		using LanguageServerFeatureProvider provider = CreateProvider(client);
		int updateCount = 0;

		provider.DiagnosticsUpdated += (_, _) => updateCount++;

		client.SendRequestHandler = static (method, _, _) =>
			Task.FromResult<object?>(
				string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal)
					? new DocumentDiagnosticReportResponse("bogus", "r1", [])
					: null
			);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, updateCount);
		Assert.IsEmpty(provider.GetDiagnostics(s_filePath));
	}

	[TestMethod]
	public async Task PullDiagnostics_RelatedReport_IsAppliedToTheTrackedRelatedDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using LanguageServerFeatureProvider provider = CreateProvider(client);

		client.SendRequestHandler = static (method, parameters, _) =>
		{
			if (
				!string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal)
				|| parameters is not DocumentDiagnosticParams request
				|| !LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(s_filePath, new Uri(request.TextDocument.Uri).LocalPath)
			)
			{
				return Task.FromResult<object?>(null);
			}

			var relatedDocuments = new Dictionary<string, DocumentDiagnosticReportResponse>
			{
				// The related document reports a different count so the assertion proves which document received which report.
				[new Uri(s_relatedFilePath).AbsoluteUri] = new(DocumentDiagnosticReportResponse.FullKind, "r-related", CreateDiagnostics(4)),
			};

			return Task.FromResult<object?>(
				new DocumentDiagnosticReportResponse(
					Kind: DocumentDiagnosticReportResponse.FullKind,
					ResultId: "r-primary",
					Items: [CreateDiagnostic()],
					relatedDocuments
				)
			);
		};

		provider.OpenDocument(s_relatedFilePath, Content);

		// The related document's own pull proves it is tracked before the primary request can report for it.
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		provider.OpenDocument(s_filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.GetDiagnostics(s_relatedFilePath).Count == 4, s_waitTimeout));
		Assert.HasCount(1, provider.GetDiagnostics(s_filePath));
	}

	[TestMethod]
	public async Task DiagnosticRefreshRequested_RepullsTheOpenDocuments()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using LanguageServerFeatureProvider provider = CreateProvider(client);

		client.SendRequestHandler = static (method, _, _) =>
			Task.FromResult<object?>(string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal) ? CreateFullReport("r1") : null);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		// The server asks the client to re-pull; the fan-out issues a second request that threads the first resultId.
		client.RaiseDiagnosticRefreshRequested();

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(
				() =>
					client.GetLastRequestParameters(DocumentDiagnosticMethod).TryGetProperty("previousResultId", out JsonElement previous)
					&& previous.GetString() == "r1",

				s_waitTimeout
			)
		);
	}

	[TestMethod]
	public async Task PullDiagnostics_AfterRestartReopen_PullsForTheReopenedDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using LanguageServerFeatureProvider provider = CreateProvider(client);

		client.SendRequestHandler = static (method, _, _) =>
			Task.FromResult<object?>(string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal) ? CreateFullReport("r1") : null);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		// A connection loss invalidates the session; the next request restarts and replays the tracked document.
		client.RaiseConnectionUnavailable(client.ConnectionGeneration);

		// The real client re-advertises its capabilities after the re-handshake; the fake applies a static
		// loss snapshot, so restore the pull capability before the restart completes.
		client.SupportsPullDiagnostics = true;

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout));

		// The restart reopen pulls diagnostics for the reopened document, exactly like an ordinary open; without
		// the replay pull a pull-capable server would report nothing for reopened documents until the next change.
		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));
	}

	[TestMethod]
	public async Task RenameDocument_ForRecordsTheRestartReplayHasNotReopened_PullsNothingForTheRenamedPaths()
	{
		string[] sourcePaths = [s_filePath, s_relatedFilePath, Path.Combine(s_workspaceRoot, "Scripts", "third.test")];
		string[] renamedPaths = [.. sourcePaths.Select(static path => Path.ChangeExtension(path, ".renamed"))];

		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using var host = new LanguageServerFeatureProviderTestHost([s_workspaceRoot], client);

		LanguageServerFeatureProvider provider = host.Provider;
		var pulledUris = new ConcurrentQueue<string>();

		client.SendRequestHandler = (method, parameters, _) =>
		{
			if (!string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal))
				return Task.FromResult<object?>(null);

			if (parameters is DocumentDiagnosticParams request)
				pulledUris.Enqueue(request.TextDocument.Uri);

			return Task.FromResult<object?>(CreateFullReport("r1"));
		};

		foreach (string sourcePath in sourcePaths)
			provider.OpenDocument(sourcePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", sourcePaths.Length, s_waitTimeout));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);

		// The fake applies a static loss snapshot, so restore the pull capability the real client re-advertises.
		client.SupportsPullDiagnostics = true;

		var replayGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int reopenAttempts = 0;

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			// Hold the first replayed reopen: the restart has marked every tracked record closed, and the records it
			// has not reached yet keep no server copy.
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal) && Interlocked.Increment(ref reopenAttempts) == 1)
				await replayGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		};

		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath: Path.Combine(s_workspaceRoot, "Scripts", "trigger.test"),
			documentText: Content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => Volatile.Read(ref reopenAttempts) == 1, s_waitTimeout));

		for (int i = 0; i < sourcePaths.Length; i++)
			provider.RenameDocument(sourcePaths[i], renamedPaths[i], Content);

		replayGate.TrySetResult(true);
		Assert.IsNull(await hoverTask.ConfigureAwait(false));

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(() => renamedPaths.All(renamedPath => host.RenamedPaths.Contains(renamedPath)), s_waitTimeout)
		);

		// A renamed record the server never held sends no open, so a pull for its renamed path would ask the server about a
		// document it does not know.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		string[] renamedUris = [.. renamedPaths.Select(static path => new Uri(path).AbsoluteUri)];

		Assert.AreSequenceEqual([], pulledUris.Where(uri => renamedUris.Contains(uri, StringComparer.Ordinal)).ToArray());
	}

	private static LanguageServerFeatureProvider CreateProvider(FakeLanguageServerClient client) => TestProviders.Create([s_workspaceRoot], client);

	private static DiagnosticPayload[] CreateDiagnostics(int count) => [.. Enumerable.Range(0, count).Select(static _ => CreateDiagnostic())];

	private static DiagnosticPayload CreateDiagnostic()
	{
		return new(
			Range: new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 1)),
			Severity: Lsp.DiagnosticSeverity.Warning,
			Message: "message",
			Source: "test",
			Code: null
		);
	}

	private static DocumentDiagnosticReportResponse CreateFullReport(string resultId, int diagnosticCount = 1) =>
		new(DocumentDiagnosticReportResponse.FullKind, resultId, CreateDiagnostics(diagnosticCount));

	private static DocumentDiagnosticReportResponse CreateUnchangedReport(string resultId) =>
		new(DocumentDiagnosticReportResponse.UnchangedKind, resultId, null);

	public TestContext? TestContext { get; set; }
}
