using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	public void CaptureServerCapabilities_WithDiagnosticProvider_EnablesPullDiagnostics()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
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
				"""
			)
		);

		Assert.IsTrue(client.NegotiatedCapabilities.SupportsPullDiagnostics);
	}

	[TestMethod]
	public void DocumentDiagnosticParams_SerializesPreviousResultIdOnlyWhenPresent()
	{
		JsonSerializerOptions options = LanguageServerClient.CreateProtocolSerializerOptions();

		JsonElement withoutPreviousResultId = JsonSerializer.SerializeToElement(
			new DocumentDiagnosticParams(new TextDocumentIdentifier("file:///workspace/Test.vb")),
			options
		);

		Assert.AreEqual("file:///workspace/Test.vb", withoutPreviousResultId.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsFalse(withoutPreviousResultId.TryGetProperty("previousResultId", out _));

		JsonElement withPreviousResultId = JsonSerializer.SerializeToElement(
			new DocumentDiagnosticParams(new TextDocumentIdentifier("file:///workspace/Test.vb"), "result-1"),
			options
		);

		Assert.AreEqual("result-1", withPreviousResultId.GetProperty("previousResultId").GetString());
	}

	[TestMethod]
	public void DocumentDiagnosticReportResponse_DeserializesFullUnchangedAndRelatedReports()
	{
		JsonSerializerOptions options = LanguageServerClient.CreateProtocolSerializerOptions();

		DocumentDiagnosticReportResponse full = JsonSerializer.Deserialize<DocumentDiagnosticReportResponse>(
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
			""",

			options
		)!;

		Assert.IsTrue(full.IsFull);
		Assert.IsFalse(full.IsUnchanged);
		Assert.AreEqual("result-1", full.ResultId);
		Assert.IsNotNull(full.Items);
		Assert.HasCount(1, full.Items);
		Assert.AreEqual(DiagnosticSeverity.Warning, full.Items[0].Severity);
		Assert.AreEqual("BC0001", full.Items[0].Code?.GetString());
		Assert.IsNull(full.RelatedDocuments);

		DocumentDiagnosticReportResponse unchanged = JsonSerializer.Deserialize<DocumentDiagnosticReportResponse>(
			"""{ "kind": "unchanged", "resultId": "result-2" }""",
			options
		)!;

		Assert.IsTrue(unchanged.IsUnchanged);
		Assert.IsFalse(unchanged.IsFull);
		Assert.AreEqual("result-2", unchanged.ResultId);
		Assert.IsNull(unchanged.Items);

		DocumentDiagnosticReportResponse related = JsonSerializer.Deserialize<DocumentDiagnosticReportResponse>(
			"""
			{
			  "kind": "full",
			  "items": [],
			  "relatedDocuments": {
			    "file:///workspace/Other.vb": { "kind": "unchanged", "resultId": "result-3" }
			  }
			}
			""",

			options
		)!;

		Assert.IsNotNull(related.RelatedDocuments);
		Assert.HasCount(1, related.RelatedDocuments);
		Assert.IsTrue(related.RelatedDocuments["file:///workspace/Other.vb"].IsUnchanged);
	}

	[TestMethod]
	public void DocumentDiagnosticReportResponse_SkipsMalformedItemAndKeepsValidOnes()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		JsonSerializerOptions options = ProtocolSerialization.ForMessages(logger);

		DocumentDiagnosticReportResponse report = JsonSerializer.Deserialize<DocumentDiagnosticReportResponse>(
			"""
			{
			  "kind": "full",
			  "items": [
			    { "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } }, "message": "kept" },
			    { "range": "not-a-range", "message": "malformed" },
			    { "range": { "start": { "line": 1, "character": 0 }, "end": { "line": 1, "character": 1 } }, "message": "also kept" }
			  ]
			}
			""",

			options
		)!;

		Assert.IsNotNull(report.Items);
		Assert.HasCount(2, report.Items);
		Assert.AreEqual("kept", report.Items[0].Message);
		Assert.AreEqual("also kept", report.Items[1].Message);
	}

	[TestMethod]
	public void DocumentDiagnosticReportResponse_WithNonArrayItemsRoot_FailsDeserialization()
	{
		JsonSerializerOptions options = ProtocolSerialization.ForMessages(NullLogger.Instance);

		Assert.ThrowsExactly<JsonException>(() =>
			JsonSerializer.Deserialize<DocumentDiagnosticReportResponse>("""{ "kind": "full", "items": { "not": "an array" } }""", options)
		);
	}

	[TestMethod]
	public async Task RefreshDiagnosticsAsync_RaisesEventAndReturnsNull()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		StartServerMessageDispatcher(client);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var refreshRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		client.DiagnosticRefreshRequested += (_, _) => refreshRequested.TrySetResult(true);

		object? result = await rpcTarget.RefreshDiagnosticsAsync().ConfigureAwait(false);
		Assert.IsNull(result);

		await refreshRequested.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);
	}
}
