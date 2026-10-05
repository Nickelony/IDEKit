using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the diagnostics source's publish, pull, read and cancel paths directly, without a provider.</summary>
[TestClass]
public sealed class DiagnosticsSourceTests
{
	private const string Content = "local a = 1";

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static DiagnosticsSource CreateSource(SynchronizerTestHost fixture) => CreateSource(fixture, DiagnosticMappingPolicy.Default);

	private static DiagnosticsSource CreateSource(SynchronizerTestHost fixture, DiagnosticMappingPolicy? diagnosticMappingPolicy)
	{
		return new DiagnosticsSource(
			context: fixture.Context,
			requestRunner: fixture.Runner,
			diagnosticMappingPolicy,
			scheduler: fixture.Scheduler,
			events: fixture.Events
		);
	}

	private static DiagnosticPayload CreatePayload(string message = "message")
	{
		return new(
			Range: new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 5)),
			Severity: Lsp.DiagnosticSeverity.Warning,
			message,
			Source: "test",
			Code: null
		);
	}

	private static DiagnosticsPublishedEventArgs Publish(string filePath, int version = 1, string message = "message") =>
		new(new PublishDiagnosticsParams(new Uri(filePath).AbsoluteUri, version, [CreatePayload(message)]));

	private static async Task<DocumentSnapshot> OpenAsync(SynchronizerTestHost fixture, string filePath)
	{
		fixture.Synchronizer.Open(filePath, Content);

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		return fixture.Documents.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The opened document should be tracked.");
	}

	private static void StoreDiagnostic(SynchronizerTestHost fixture, string filePath)
	{
		fixture.Documents.Synchronize(filePath, Content, leases: DocumentLeaseKinds.Open);

		DocumentSnapshot document =
			fixture.Documents.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The document should be tracked.");

		Assert.IsTrue(
			DiagnosticsParser.TryParse(
				parameters: Publish(filePath).Parameters,
				normalizedFilePath: document.FilePath,
				Content,
				documentVersion: document.Version,
				policy: DiagnosticMappingPolicy.Default,
				publishedDiagnostics: out ParsedDiagnostics? published
			)
		);

		Assert.IsTrue(fixture.Documents.TryStoreDiagnostics(published, document.Version, Content));
	}

	[TestMethod]
	public async Task HandlePublished_ForATrackedDocument_RaisesAndCachesTheMappedDiagnostics()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = TestPaths.Script("a.lua");
		DiagnosticsSource source = CreateSource(fixture);

		var raised = new List<DiagnosticsUpdatedEventArgs>();

		source.DiagnosticsUpdated += (_, eventArgs) => raised.Add(eventArgs);

		await OpenAsync(fixture, filePath);

		source.HandlePublished(sender: null, Publish(filePath));

		DiagnosticsUpdatedEventArgs eventArguments = Assert.ContainsSingle(raised);
		Diagnostic raisedDiagnostic = Assert.ContainsSingle(eventArguments.Diagnostics);

		Assert.AreEqual(filePath, eventArguments.DocumentId);
		Assert.AreEqual("message", raisedDiagnostic.Message);
		Assert.AreEqual(TextRange.FromBounds(0, 5), raisedDiagnostic.Range);
		Assert.AreSame(raisedDiagnostic, Assert.ContainsSingle(fixture.Documents.GetDiagnostics(filePath)));
	}

	[TestMethod]
	public void HandlePublished_ForAnUntrackedDocument_RaisesNothing()
	{
		using var fixture = new SynchronizerTestHost();
		DiagnosticsSource source = CreateSource(fixture);

		var raised = new List<DiagnosticsUpdatedEventArgs>();

		source.DiagnosticsUpdated += (_, eventArgs) => raised.Add(eventArgs);

		source.HandlePublished(sender: null, Publish(TestPaths.Script("untracked.lua")));

		Assert.IsEmpty(raised);
	}

	[TestMethod]
	public async Task HandlePublished_WhenTheProfileSuppliesNoPolicy_RaisesNothing()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = TestPaths.Script("a.lua");
		DiagnosticsSource source = CreateSource(fixture, diagnosticMappingPolicy: null);

		var raised = new List<DiagnosticsUpdatedEventArgs>();

		source.DiagnosticsUpdated += (_, eventArgs) => raised.Add(eventArgs);

		await OpenAsync(fixture, filePath);

		source.HandlePublished(sender: null, Publish(filePath));

		Assert.IsEmpty(raised);
		Assert.IsEmpty(fixture.Documents.GetDiagnostics(filePath));
	}

	[TestMethod]
	public async Task HandlePublished_WhenTheWordRuleOfThePolicyThrows_ContainsTheFaultAndRaisesNothing()
	{
		using var fixture = new SynchronizerTestHost();
		string filePath = TestPaths.Script("a.lua");

		int wordRuleCalls = 0;

		DiagnosticsSource source = CreateSource(
			fixture,

			new DiagnosticMappingPolicy
			{
				WordCharacters = IdentifierCharacterPolicy.Create(_ =>
				{
					wordRuleCalls++;
					throw new InvalidOperationException("word rule failed");
				}),
			}
		);

		var raised = new List<DiagnosticsUpdatedEventArgs>();

		source.DiagnosticsUpdated += (_, eventArgs) => raised.Add(eventArgs);

		await OpenAsync(fixture, filePath);

		// An inverted range falls back to the word anchor of its start, which is the only path that runs the word rule.
		DiagnosticPayload invertedRangePayload = CreatePayload() with
		{
			Range = new RangePayload(new PositionPayload(0, 6), new PositionPayload(0, 2)),
		};

		source.HandlePublished(
			sender: null,
			new DiagnosticsPublishedEventArgs(new PublishDiagnosticsParams(new Uri(filePath).AbsoluteUri, 1, [invertedRangePayload]))
		);

		Assert.IsGreaterThan(0, wordRuleCalls);
		Assert.IsEmpty(raised);
		Assert.IsEmpty(fixture.Documents.GetDiagnostics(filePath));
	}

	[TestMethod]
	public async Task HandlePublished_ForAStalePayloadVersion_RaisesNothingAndKeepsTheCache()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = TestPaths.Script("a.lua");
		DiagnosticsSource source = CreateSource(fixture);

		var raised = new List<DiagnosticsUpdatedEventArgs>();

		source.DiagnosticsUpdated += (_, eventArgs) => raised.Add(eventArgs);

		await OpenAsync(fixture, filePath);

		source.HandlePublished(sender: null, Publish(filePath));

		Assert.HasCount(1, raised);

		// The tracked document is at version 1, so a payload for version 7 describes a document this provider never sent.
		source.HandlePublished(sender: null, Publish(filePath, version: 7, message: "stale"));

		Assert.HasCount(1, raised);
		Assert.AreEqual("message", Assert.ContainsSingle(fixture.Documents.GetDiagnostics(filePath)).Message);
	}

	[TestMethod]
	public async Task HandlePublished_AfterDisposalStarted_IsIgnored()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = TestPaths.Script("a.lua");
		DiagnosticsSource source = CreateSource(fixture);

		await OpenAsync(fixture, filePath);

		fixture.Disposal.Signal();

		var raised = new List<DiagnosticsUpdatedEventArgs>();

		source.DiagnosticsUpdated += (_, eventArgs) => raised.Add(eventArgs);

		source.HandlePublished(sender: null, Publish(filePath));

		Assert.IsEmpty(raised);
		Assert.IsEmpty(fixture.Documents.GetDiagnostics(filePath));
	}

	[TestMethod]
	public void HandlePublished_ForAUriThatIsNotAFile_IsIgnored()
	{
		using var fixture = new SynchronizerTestHost();
		DiagnosticsSource source = CreateSource(fixture);

		var raised = new List<DiagnosticsUpdatedEventArgs>();

		source.DiagnosticsUpdated += (_, eventArgs) => raised.Add(eventArgs);

		source.HandlePublished(sender: null, new DiagnosticsPublishedEventArgs(new PublishDiagnosticsParams("untitled:Untitled-1", 1, [])));

		Assert.IsEmpty(raised);
	}

	[TestMethod]
	public void GetDiagnostics_ReadsTheTrackedDiagnosticsOfTheNormalizedPath()
	{
		using var fixture = new SynchronizerTestHost();

		string filePath = TestPaths.Script("a.lua");
		DiagnosticsSource source = CreateSource(fixture);

		StoreDiagnostic(fixture, filePath);

		// A raw path that normalizes to the tracked one reads the same cache.
		Assert.AreEqual(
			"message",
			Assert.ContainsSingle(source.GetDiagnostics(Path.Combine(Path.GetDirectoryName(filePath)!, ".", "a.lua"))).Message
		);
	}

	[TestMethod]
	public void GetDiagnostics_ForAnUnusablePathOrAClosedProvider_ReturnsAnEmptyList()
	{
		using var fixture = new SynchronizerTestHost();
		DiagnosticsSource source = CreateSource(fixture);

		StoreDiagnostic(fixture, TestPaths.Script("a.lua"));

		Assert.IsEmpty(source.GetDiagnostics("relative.lua"));

		fixture.Disposal.Signal();

		Assert.IsEmpty(source.GetDiagnostics(TestPaths.Script("a.lua")));
	}

	[TestMethod]
	public void AnnounceRenamed_RaisesTheTrackedDiagnosticsOfTheNewPath()
	{
		using var fixture = new SynchronizerTestHost();

		string newPath = TestPaths.Script("new.lua");
		DiagnosticsSource source = CreateSource(fixture);

		StoreDiagnostic(fixture, newPath);

		var raised = new List<DiagnosticsUpdatedEventArgs>();

		source.DiagnosticsUpdated += (_, eventArgs) => raised.Add(eventArgs);

		source.AnnounceRenamed(newPath);

		DiagnosticsUpdatedEventArgs eventArguments = Assert.ContainsSingle(raised);

		Assert.AreEqual(newPath, eventArguments.DocumentId);
		Assert.AreEqual("message", Assert.ContainsSingle(eventArguments.Diagnostics).Message);
	}

	[TestMethod]
	public async Task Trigger_OnAServerWithoutPullDiagnostics_SendsNoRequest()
	{
		using var fixture = new SynchronizerTestHost();

		DiagnosticsSource source = CreateSource(fixture);
		DocumentSnapshot document = await OpenAsync(fixture, TestPaths.Script("a.lua"));

		// Trigger decides on the negotiated capability before it schedules anything.
		source.Trigger(document);

		Assert.DoesNotContain(LspMethodNames.DocumentDiagnostic, fixture.Client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task Trigger_ThreadsTheLastResultIdUntilTheDocumentIsCanceled()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsPullDiagnostics = true };
		using var fixture = new SynchronizerTestHost(client);

		string filePath = TestPaths.Script("a.lua");

		client.SendRequestHandler = (method, _, _) =>
			Task.FromResult<object?>(
				string.Equals(method, LspMethodNames.DocumentDiagnostic, StringComparison.Ordinal)
					? new DocumentDiagnosticReportResponse(DocumentDiagnosticReportResponse.FullKind, "r1", [])
					: null
			);

		DiagnosticsSource source = CreateSource(fixture);
		DocumentSnapshot document = await OpenAsync(fixture, filePath);

		source.Trigger(document);

		Assert.IsTrue(await client.WaitForMethodCountAsync(LspMethodNames.DocumentDiagnostic, 1, s_waitTimeout));
		Assert.IsFalse(client.GetLastRequestParameters(LspMethodNames.DocumentDiagnostic).TryGetProperty("previousResultId", out _));

		// The report is applied after the request settles, so wait for the id to be threaded.
		Assert.IsTrue(
			await TestPolling.ForConditionAsync(
				() =>
				{
					source.Trigger(document);

					return client
							.GetLastRequestParameters(LspMethodNames.DocumentDiagnostic)
							.TryGetProperty("previousResultId", out System.Text.Json.JsonElement previous)
						&& previous.GetString() == "r1";
				},

				s_waitTimeout
			)
		);

		source.Cancel(filePath);

		int requestsBeforeCancel = client.GetSentMethodNames().Count(name => name == LspMethodNames.DocumentDiagnostic);

		source.Trigger(document);

		Assert.IsTrue(await client.WaitForMethodCountAsync(LspMethodNames.DocumentDiagnostic, requestsBeforeCancel + 1, s_waitTimeout));
		Assert.IsFalse(client.GetLastRequestParameters(LspMethodNames.DocumentDiagnostic).TryGetProperty("previousResultId", out _));
	}
}
