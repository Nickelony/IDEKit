using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the pull side of the diagnostics source directly: result-id threading, the mapping hand-off and the capability gate.</summary>
[TestClass]
public sealed class DiagnosticsPullCoordinatorTests
{
	private const string DocumentDiagnosticMethod = "textDocument/diagnostic";

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	private sealed class TestHost : IDisposable
	{
		internal TestHost(bool supportsPullDiagnostics = true, Func<IReadOnlyList<Diagnostic>?>? mapped = null)
		{
			Client = new FakeLanguageServerClient { SupportsPullDiagnostics = supportsPullDiagnostics };
			Client.SendRequestHandler = static (_, _, _) => Task.FromResult<object?>(null);

			(ProviderContext context, RequestRunner runner) = TestProviderContext.CreateWithRunner(Client);

			Context = context;
			Path = TestPaths.Script("pull.lua");

			Context.Documents.Synchronize(Path, "local a = 1", leases: DocumentLeaseKinds.Open);
			Document = Context.Documents.GetDocumentSnapshot(Path) ?? throw new InvalidOperationException("The document should be tracked.");

			Coordinator = new DiagnosticsPullCoordinator(
				Context,
				requestRunner: runner,
				scheduler: new DocumentOperationScheduler(),
				mapAndStore: (_, _, _) => (mapped ?? (static () => []))(),
				raiseUpdated: (path, _) => Raised.Add(path)
			);
		}

		internal FakeLanguageServerClient Client { get; }

		internal ProviderContext Context { get; }

		internal string Path { get; }

		internal DocumentSnapshot Document { get; }

		internal DiagnosticsPullCoordinator Coordinator { get; }

		internal List<string> Raised { get; } = [];

		internal async Task<JsonElement> PullAsync(int expectedRequestCount)
		{
			Coordinator.Trigger(Document);

			Assert.IsTrue(await Client.WaitForMethodCountAsync(DocumentDiagnosticMethod, expectedRequestCount, s_waitTimeout));

			return Client.GetLastRequestParameters(DocumentDiagnosticMethod);
		}

		public void Dispose() => Client.Dispose();
	}

	private static DocumentDiagnosticReportResponse FullReport(string? resultId) => new(DocumentDiagnosticReportResponse.FullKind, resultId, []);

	[TestMethod]
	public async Task Trigger_WithoutAPriorReport_SendsNoPreviousResultId()
	{
		using var fixture = new TestHost();

		JsonElement parameters = await fixture.PullAsync(expectedRequestCount: 1);

		Assert.IsFalse(parameters.TryGetProperty("previousResultId", out _));
	}

	[TestMethod]
	public async Task ApplyReportCore_FullReport_ThreadsItsResultIdIntoTheNextPullAndRaisesTheUpdate()
	{
		using var fixture = new TestHost();

		fixture.Coordinator.ApplyReportCore(fixture.Document, FullReport("r1"));

		Assert.AreSequenceEqual([fixture.Path], [.. fixture.Raised]);

		JsonElement parameters = await fixture.PullAsync(expectedRequestCount: 1);

		Assert.AreEqual("r1", parameters.GetProperty("previousResultId").GetString());
	}

	[TestMethod]
	public void ApplyReportCore_ForADocumentWithAHostId_RaisesTheUpdateUnderThatId()
	{
		using var fixture = new TestHost();

		var document = new DocumentSnapshot(
			filePath: fixture.Document.FilePath,
			uri: fixture.Document.Uri,
			content: fixture.Document.Content,
			version: fixture.Document.Version,
			documentId: "host-id"
		);

		fixture.Coordinator.ApplyReportCore(document, FullReport("r1"));

		Assert.AreSequenceEqual(["host-id"], [.. fixture.Raised]);
	}

	[TestMethod]
	public async Task ApplyReportCore_AReportWithoutAResultId_KeepsTheLastThreadedId()
	{
		using var fixture = new TestHost();

		fixture.Coordinator.ApplyReportCore(fixture.Document, FullReport("r1"));
		fixture.Coordinator.ApplyReportCore(fixture.Document, FullReport(resultId: null));

		JsonElement parameters = await fixture.PullAsync(expectedRequestCount: 1);

		Assert.AreEqual("r1", parameters.GetProperty("previousResultId").GetString());
	}

	[TestMethod]
	public async Task ApplyReportCore_WhenTheMappingDropsThePayload_ThreadsNoIdAndRaisesNothing()
	{
		using var fixture = new TestHost(mapped: static () => null);

		fixture.Coordinator.ApplyReportCore(fixture.Document, FullReport("r-dropped"));

		Assert.IsEmpty(fixture.Raised);

		JsonElement parameters = await fixture.PullAsync(expectedRequestCount: 1);

		Assert.IsFalse(parameters.TryGetProperty("previousResultId", out _));
	}

	[TestMethod]
	public async Task Cancel_DropsTheResultIdOfThatDocumentOnly()
	{
		using var fixture = new TestHost();

		string otherPath = TestPaths.Script("other.lua");
		fixture.Context.Documents.Synchronize(otherPath, "local b = 1", leases: DocumentLeaseKinds.Open);

		DocumentSnapshot other =
			fixture.Context.Documents.GetDocumentSnapshot(otherPath) ?? throw new InvalidOperationException("The document should be tracked.");

		fixture.Coordinator.ApplyReportCore(fixture.Document, FullReport("r1"));
		fixture.Coordinator.ApplyReportCore(other, FullReport("r-other"));

		fixture.Coordinator.Cancel(fixture.Path);

		Assert.IsFalse((await fixture.PullAsync(expectedRequestCount: 1)).TryGetProperty("previousResultId", out _));

		fixture.Coordinator.Trigger(other);
		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 2, s_waitTimeout));

		Assert.AreEqual("r-other", fixture.Client.GetLastRequestParameters(DocumentDiagnosticMethod).GetProperty("previousResultId").GetString());
	}

	[TestMethod]
	public async Task CancelAll_ClosesAdmission_SoNoLaterPullIsSent()
	{
		using var fixture = new TestHost();

		fixture.Coordinator.CancelAll();
		fixture.Coordinator.Trigger(fixture.Document);

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken);

		Assert.AreEqual(0, fixture.Client.GetSentMethodCount(DocumentDiagnosticMethod));
	}

	[TestMethod]
	public async Task Trigger_WithAServerThatDoesNotPull_SendsNothing()
	{
		using var fixture = new TestHost(supportsPullDiagnostics: false);

		fixture.Coordinator.Trigger(fixture.Document);
		fixture.Coordinator.RequestRefresh();

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken);

		Assert.AreEqual(0, fixture.Client.GetSentMethodCount(DocumentDiagnosticMethod));
	}

	public TestContext TestContext { get; set; } = null!;
}
