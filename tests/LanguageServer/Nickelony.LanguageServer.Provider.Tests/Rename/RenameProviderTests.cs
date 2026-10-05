using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class RenameProviderTests
{
	private const string Document = "local a = 1\nprint(a)";

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public async Task GetRenameEditsAsync_WithABlankNewName_SendsNothingAndReturnsNull(string newName)
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		WorkspaceEdit? edit = await new RenameProvider(
			pipeline,
			LanguageServerFeatureProviderOptions.Default,
			NullLogger.Instance
		).GetRenameEditsAsync(
			new RenameRequest(documentId: TestPaths.Script("a.lua"), documentText: Document, symbolOffset: 18, newName),
			CancellationToken.None
		);

		Assert.IsNull(edit);
		Assert.IsEmpty(pipeline.Requests);
	}

	[TestMethod]
	public async Task GetRenameEditsAsync_SendsTheNewNameAtTheSymbolPosition()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new RenameProvider(pipeline, LanguageServerFeatureProviderOptions.Default, NullLogger.Instance).GetRenameEditsAsync(
			new RenameRequest(documentId: TestPaths.Script("a.lua"), documentText: Document, symbolOffset: 18, newName: "renamed"),
			CancellationToken.None
		);

		RecordingDocumentRequestPipeline.RecordedRequest request = pipeline.SingleRequest;

		Assert.AreEqual(LspMethodNames.Rename, request.Method);
		Assert.AreEqual(new TextPosition(1, 6), request.Position);
		Assert.AreEqual("renamed", request.Parameters.GetProperty("newName").GetString());
	}

	[TestMethod]
	public async Task GetRenameEditsAsync_GatesTheRequestOnTheRenameCapability()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new RenameProvider(pipeline, LanguageServerFeatureProviderOptions.Default, NullLogger.Instance).GetRenameEditsAsync(
			new RenameRequest(documentId: TestPaths.Script("a.lua"), documentText: Document, symbolOffset: 0, newName: "renamed"),
			CancellationToken.None
		);

		Func<ILanguageServerClient, bool>? gate = pipeline.SingleRequest.SupportsRequest;

		Assert.IsNotNull(gate);

		using var unsupported = new FakeLanguageServerClient { SupportsRename = false };
		using var supported = new FakeLanguageServerClient { SupportsRename = true };

		Assert.IsFalse(gate(unsupported));
		Assert.IsTrue(gate(supported));
	}

	[TestMethod]
	public async Task GetRenameEditsAsync_WhenTheRequestNamesTheDocumentThroughAnUnnormalizedPath_CarriesTheRequestsIdentifier()
	{
		string path = TestPaths.Script("a.lua");
		string unnormalizedPath = Path.Combine(TestPaths.Root, "Scripts", "..", "Scripts", "a.lua");

		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = TestPayloads.Deserialize<WorkspaceEditResponse>(
				new
				{
					changes = new Dictionary<string, object[]>
					{
						[new Uri(path).AbsoluteUri] =
						[
							new
							{
								range = new { start = new { line = 0, character = 6 }, end = new { line = 0, character = 7 } },
								newText = "renamed",
							},
						],
					},
				}
			),
		};

		WorkspaceEdit? edit = await new RenameProvider(
			pipeline,
			LanguageServerFeatureProviderOptions.Default,
			NullLogger.Instance
		).GetRenameEditsAsync(
			new RenameRequest(documentId: unnormalizedPath, documentText: Document, symbolOffset: 6, newName: "renamed"),
			CancellationToken.None
		);

		// The pipeline hands the normalized path of the request, so the edit of the requested document carries the identifier the host used.
		Assert.IsNotNull(edit);
		Assert.AreEqual(unnormalizedPath, Assert.ContainsSingle(edit.DocumentEdits).DocumentId);
	}

	[TestMethod]
	public async Task GetRenameEditsAsync_WithoutAResponse_ReturnsNull()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		WorkspaceEdit? edit = await new RenameProvider(
			pipeline,
			LanguageServerFeatureProviderOptions.Default,
			NullLogger.Instance
		).GetRenameEditsAsync(
			new RenameRequest(documentId: TestPaths.Script("a.lua"), documentText: Document, symbolOffset: 0, newName: "renamed"),
			CancellationToken.None
		);

		Assert.IsNull(edit);
	}
}
