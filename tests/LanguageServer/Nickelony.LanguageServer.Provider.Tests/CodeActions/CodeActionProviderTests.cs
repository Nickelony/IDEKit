using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class CodeActionProviderTests
{
	private const string Document = "local a = 1\nprint(a)";

	private static CodeActionProvider CreateProvider(RecordingDocumentRequestPipeline pipeline)
	{
		(ProviderContext context, RequestRunner runner) = TestProviderContext.CreateWithRunner(client: null);
		return new CodeActionProvider(pipeline, context, runner);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_SendsTheRequestedRangeAsProtocolPositions()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await CreateProvider(pipeline)
			.GetCodeActionsAsync(new CodeActionRequest(TestPaths.Script("a.lua"), Document, new TextRange(12, 5)), CancellationToken.None);

		RecordingDocumentRequestPipeline.RecordedRequest request = pipeline.SingleRequest;
		JsonElement range = request.Parameters.GetProperty("range");

		Assert.AreEqual(LspMethodNames.CodeAction, request.Method);
		Assert.AreEqual(1, range.GetProperty("start").GetProperty("line").GetInt32());
		Assert.AreEqual(0, range.GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(1, range.GetProperty("end").GetProperty("line").GetInt32());
		Assert.AreEqual(5, range.GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_WithNoCachedDiagnostics_SendsAnEmptyContext()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await CreateProvider(pipeline)
			.GetCodeActionsAsync(new CodeActionRequest(TestPaths.Script("a.lua"), Document, new TextRange(0, 5)), CancellationToken.None);

		Assert.AreEqual(0, pipeline.SingleRequest.Parameters.GetProperty("context").GetProperty("diagnostics").GetArrayLength());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_GatesTheRequestOnTheCodeActionCapability()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await CreateProvider(pipeline)
			.GetCodeActionsAsync(new CodeActionRequest(TestPaths.Script("a.lua"), Document, new TextRange(0, 5)), CancellationToken.None);

		Func<ILanguageServerClient, bool>? gate = pipeline.SingleRequest.SupportsRequest;

		Assert.IsNotNull(gate);

		using var unsupported = new FakeLanguageServerClient { SupportsCodeActions = false };
		using var supported = new FakeLanguageServerClient { SupportsCodeActions = true };

		Assert.IsFalse(gate(unsupported));
		Assert.IsTrue(gate(supported));
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_WhenTheRequestNamesTheDocumentThroughAnUnnormalizedPath_CarriesTheRequestsIdentifier()
	{
		string path = TestPaths.Script("a.lua");
		string unnormalizedPath = Path.Combine(TestPaths.Root, "Scripts", "..", "Scripts", "a.lua");

		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = PayloadTestHelpers.DeserializeCodeActionResponse(
				new object[]
				{
					new
					{
						title = "Fix it",

						edit = new
						{
							changes = new Dictionary<string, object[]>
							{
								[new Uri(path).AbsoluteUri] =
								[
									new
									{
										range = new { start = new { line = 0, character = 6 }, end = new { line = 0, character = 7 } },
										newText = "b",
									},
								],
							},
						},
					},
				}
			),
		};

		IReadOnlyList<CodeAction> actions = await CreateProvider(pipeline)
			.GetCodeActionsAsync(new CodeActionRequest(unnormalizedPath, Document, new TextRange(0, 1)), CancellationToken.None);

		// The pipeline hands the normalized path of the request, so the edit of the requested document carries the identifier the host used.
		CodeAction action = Assert.ContainsSingle(actions);

		Assert.IsNotNull(action.Edit);
		Assert.AreEqual(unnormalizedPath, Assert.ContainsSingle(action.Edit.DocumentEdits).DocumentId);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_WithoutAResponse_ReturnsAnEmptyList()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		IReadOnlyList<CodeAction> actions = await CreateProvider(pipeline)
			.GetCodeActionsAsync(new CodeActionRequest(TestPaths.Script("a.lua"), Document, new TextRange(0, 5)), CancellationToken.None);

		Assert.IsEmpty(actions);
	}
}
