using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public void CaptureServerCapabilities_WithDiagnosticProvider_EnablesPullDiagnostics()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "diagnosticProvider": {
			      "interFileDependencies": true,
			      "workspaceDiagnostics": false
			    }
			  }
			}
			"""));

		Assert.IsTrue(client.SupportsPullDiagnostics);
	}

	[TestMethod]
	public void DocumentDiagnosticParams_SerializesPreviousResultIdOnlyWhenPresent()
	{
		JsonSerializerOptions options = LanguageServerClient.CreateProtocolSerializerOptions();

		JsonElement withoutPreviousResultId = JsonSerializer.SerializeToElement(
			new DocumentDiagnosticParams(new TextDocumentIdentifier("file:///workspace/Test.vb")), options);

		Assert.AreEqual("file:///workspace/Test.vb", withoutPreviousResultId.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsFalse(withoutPreviousResultId.TryGetProperty("previousResultId", out _));

		JsonElement withPreviousResultId = JsonSerializer.SerializeToElement(
			new DocumentDiagnosticParams(new TextDocumentIdentifier("file:///workspace/Test.vb"), "result-1"), options);

		Assert.AreEqual("result-1", withPreviousResultId.GetProperty("previousResultId").GetString());
	}

	[TestMethod]
	public void DocumentDiagnosticReportPayload_DeserializesFullUnchangedAndRelatedReports()
	{
		JsonSerializerOptions options = LanguageServerClient.CreateProtocolSerializerOptions();

		DocumentDiagnosticReportPayload full = JsonSerializer.Deserialize<DocumentDiagnosticReportPayload>(
			"""
			{
			  "kind": "full",
			  "resultId": "result-1",
			  "items": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 3 }
			      },
			      "severity": 2,
			      "code": "BC0001",
			      "message": "message",
			      "source": "vb"
			    }
			  ]
			}
			""", options)!;

		Assert.IsTrue(full.IsFull);
		Assert.IsFalse(full.IsUnchanged);
		Assert.AreEqual("result-1", full.ResultId);
		Assert.IsNotNull(full.Items);
		Assert.AreEqual(1, full.Items.Count);
		Assert.AreEqual(DiagnosticSeverity.Warning, full.Items[0].Severity);
		Assert.AreEqual("BC0001", full.Items[0].Code?.GetString());
		Assert.IsNull(full.RelatedDocuments);

		DocumentDiagnosticReportPayload unchanged = JsonSerializer.Deserialize<DocumentDiagnosticReportPayload>(
			"""{ "kind": "unchanged", "resultId": "result-2" }""", options)!;

		Assert.IsTrue(unchanged.IsUnchanged);
		Assert.IsFalse(unchanged.IsFull);
		Assert.AreEqual("result-2", unchanged.ResultId);
		Assert.IsNull(unchanged.Items);

		DocumentDiagnosticReportPayload related = JsonSerializer.Deserialize<DocumentDiagnosticReportPayload>(
			"""
			{
			  "kind": "full",
			  "items": [],
			  "relatedDocuments": {
			    "file:///workspace/Other.vb": { "kind": "unchanged", "resultId": "result-3" }
			  }
			}
			""", options)!;

		Assert.IsNotNull(related.RelatedDocuments);
		Assert.AreEqual(1, related.RelatedDocuments.Count);
		Assert.IsTrue(related.RelatedDocuments["file:///workspace/Other.vb"].IsUnchanged);
	}

	[TestMethod]
	public async Task RefreshDiagnosticsAsync_RaisesEventAndReturnsNull()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		StartCallbackPump(client);
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		var refreshRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		client.DiagnosticRefreshRequested += (_, _) => refreshRequested.TrySetResult(true);

		object? result = await rpcTarget.RefreshDiagnosticsAsync().ConfigureAwait(false);
		Assert.IsNull(result);

		await refreshRequested.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
	}
}
