using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Formatting;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class FormattingProviderTests
{
	private const string Document = "local a=1";

	[TestMethod]
	[DataRow(2, true)]
	[DataRow(3, false)]
	[DataRow(8, false)]
	public async Task GetFormattingEditsAsync_SendsTheFormattingOptions(int tabSize, bool insertSpaces)
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new FormattingProvider(pipeline).GetFormattingEditsAsync(
			new FormattingRequest(TestPaths.Script("a.lua"), Document, new FormattingOptions(tabSize, insertSpaces)),
			CancellationToken.None
		);

		RecordingDocumentRequestPipeline.RecordedRequest request = pipeline.SingleRequest;

		Assert.AreEqual(LspMethodNames.Formatting, request.Method);
		Assert.AreEqual(tabSize, request.Parameters.GetProperty("options").GetProperty("tabSize").GetInt32());
		Assert.AreEqual(insertSpaces, request.Parameters.GetProperty("options").GetProperty("insertSpaces").GetBoolean());
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	public async Task GetFormattingEditsAsync_NonPositiveTabSize_SendsTheDefaultTabSize(int tabSize)
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new FormattingProvider(pipeline).GetFormattingEditsAsync(
			new FormattingRequest(TestPaths.Script("a.lua"), Document, new FormattingOptions(tabSize, insertSpaces: true)),
			CancellationToken.None
		);

		JsonElement options = pipeline.SingleRequest.Parameters.GetProperty("options");

		Assert.AreEqual(FormattingOptions.DefaultTabSize, options.GetProperty("tabSize").GetInt32());
		Assert.IsTrue(options.GetProperty("insertSpaces").GetBoolean());
	}

	[TestMethod]
	public async Task GetFormattingEditsAsync_GatesTheRequestOnTheFormattingCapability()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new FormattingProvider(pipeline).GetFormattingEditsAsync(
			new FormattingRequest(TestPaths.Script("a.lua"), Document, new FormattingOptions(tabSize: 4, insertSpaces: true)),
			CancellationToken.None
		);

		Func<ILanguageServerClient, bool>? gate = pipeline.SingleRequest.SupportsRequest;

		Assert.IsNotNull(gate);

		using var unsupported = new FakeLanguageServerClient { SupportsFormatting = false };
		using var supported = new FakeLanguageServerClient { SupportsFormatting = true };

		Assert.IsFalse(gate(unsupported));
		Assert.IsTrue(gate(supported));
	}

	[TestMethod]
	public async Task GetFormattingEditsAsync_WithNoEdits_ReturnsNull()
	{
		var pipeline = new RecordingDocumentRequestPipeline { Response = Array.Empty<TextEditPayload>() };

		WorkspaceEdit? edit = await new FormattingProvider(pipeline).GetFormattingEditsAsync(
			new FormattingRequest(TestPaths.Script("a.lua"), Document, new FormattingOptions(tabSize: 4, insertSpaces: true)),
			CancellationToken.None
		);

		Assert.IsNull(edit);
	}

	[TestMethod]
	public async Task GetFormattingEditsAsync_WithEdits_ReturnsOneDocumentEditNamedByTheRequest()
	{
		string filePath = TestPaths.Script("a.lua");

		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = TestPayloads.Deserialize<TextEditPayload[]>(
				new[] { new { range = new { start = new { line = 0, character = 7 }, end = new { line = 0, character = 7 } }, newText = " " } }
			),
		};

		WorkspaceEdit? edit = await new FormattingProvider(pipeline).GetFormattingEditsAsync(
			new FormattingRequest(filePath, Document, new FormattingOptions(tabSize: 4, insertSpaces: true)),
			CancellationToken.None
		);

		Assert.IsNotNull(edit);

		TextDocumentEdit documentEdit = Assert.ContainsSingle(edit.DocumentEdits);

		Assert.AreEqual(filePath, documentEdit.DocumentId);
		Assert.AreEqual(" ", Assert.ContainsSingle(documentEdit.TextEdits).NewText);
	}
}
