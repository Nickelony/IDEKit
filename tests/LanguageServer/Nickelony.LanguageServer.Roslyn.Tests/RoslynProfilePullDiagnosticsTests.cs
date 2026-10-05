using System.Text.Json;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>Covers the Roslyn profile end to end through the fake client, with a test language in place of a shipped one.</summary>
[TestClass]
public sealed class RoslynProfilePullDiagnosticsTests
{
	private const string DocumentDiagnosticMethod = "textDocument/diagnostic";
	private const string Content = "Module M\r\nEnd Module";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-roslyn-provider-tests-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "Module.vb");

	private static readonly TimeSpan s_waitTimeout = TestPolling.DefaultTimeout;

	[TestMethod]
	public async Task PullDiagnostics_WhenAdvertised_AppliesTheFullReportToTheCacheAndRaisesTheEvent()
	{
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1, SupportsPullDiagnostics = true };
		using var provider = LanguageServerFeatureProvider.Create(TestRoslynLanguages.TestProfile, [s_workspaceRoot], client);
		var updates = new List<int>();

		provider.DiagnosticsUpdated += (_, eventArgs) => updates.Add(eventArgs.Diagnostics.Count);

		client.SendRequestHandler = static (method, _, _) =>
			Task.FromResult<object?>(string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal) ? CreateFullReport("r1") : null);

		provider.OpenDocument(s_filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DocumentDiagnosticMethod, 1, s_waitTimeout));

		// The first request carries no previousResultId: nothing has been applied for the document yet.
		JsonElement parameters = client.GetLastRequestParameters(DocumentDiagnosticMethod);

		Assert.AreEqual(new Uri(s_filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsFalse(parameters.TryGetProperty("previousResultId", out _));

		// The report flows through the same tracker and event the push path uses.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.GetDiagnostics(s_filePath).Count == 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updates.Count == 1, s_waitTimeout));
		Assert.AreEqual(1, updates[0]);
	}

	private static DocumentDiagnosticReportResponse CreateFullReport(string resultId) =>
		new(DocumentDiagnosticReportResponse.FullKind, resultId, [CreateDiagnostic()]);

	private static DiagnosticPayload CreateDiagnostic()
	{
		return new(
			Range: new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 1)),
			Severity: DiagnosticSeverity.Warning,
			Message: "BC42104: Variable is used before it has been assigned a value.",
			Source: "roslyn",
			Code: null
		);
	}
}
