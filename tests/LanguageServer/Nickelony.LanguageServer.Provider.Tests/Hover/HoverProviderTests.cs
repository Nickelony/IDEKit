using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class HoverProviderTests
{
	private const string Document = "local a = 1\nlocal b = 2";

	[TestMethod]
	public async Task GetHoverAsync_SendsAHoverRequestAtTheHoveredPosition()
	{
		var pipeline = new RecordingDocumentRequestPipeline();
		var provider = new HoverProvider(pipeline);

		string filePath = TestPaths.Script("hover.lua");

		await provider.GetHoverAsync(new HoverRequest(filePath, Document, hoveredOffset: 18), CancellationToken.None);

		RecordingDocumentRequestPipeline.RecordedRequest request = pipeline.SingleRequest;

		Assert.AreEqual(Lsp.LspMethodNames.Hover, request.Method);
		Assert.AreEqual(filePath, request.FilePath);
		Assert.AreEqual(Document, request.DocumentText);
		Assert.AreEqual(18, request.Offset);
		Assert.AreEqual(new TextPosition(1, 6), request.Position);
		Assert.AreEqual(1, request.Parameters.GetProperty("position").GetProperty("line").GetInt32());
		Assert.AreEqual(6, request.Parameters.GetProperty("position").GetProperty("character").GetInt32());

		Assert.AreEqual(
			RecordingDocumentRequestPipeline.RecordedRequest.Uri,
			request.Parameters.GetProperty("textDocument").GetProperty("uri").GetString()
		);
	}

	[TestMethod]
	public async Task GetHoverAsync_AttemptsTheRequestWithoutACapabilityGate()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new HoverProvider(pipeline).GetHoverAsync(
			new HoverRequest(TestPaths.Script("hover.lua"), Document, hoveredOffset: 0),
			CancellationToken.None
		);

		Assert.IsNull(pipeline.SingleRequest.SupportsRequest);
	}

	[TestMethod]
	public async Task GetHoverAsync_WithoutAResponse_ReturnsNull()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		HoverInformation? hover = await new HoverProvider(pipeline).GetHoverAsync(
			new HoverRequest(TestPaths.Script("hover.lua"), Document, hoveredOffset: 0),
			CancellationToken.None
		);

		Assert.IsNull(hover);
	}

	[TestMethod]
	public async Task GetHoverAsync_ParsesTheResponseAgainstTheRequestText()
	{
		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = TestPayloads.Deserialize<Lsp.HoverResponse>(
				new
				{
					contents = new { kind = "markdown", value = "number" },
					range = new { start = new { line = 1, character = 6 }, end = new { line = 1, character = 7 } },
				}
			),
		};

		HoverInformation? hover = await new HoverProvider(pipeline).GetHoverAsync(
			new HoverRequest(TestPaths.Script("hover.lua"), Document, hoveredOffset: 18),
			CancellationToken.None
		);

		Assert.IsNotNull(hover);
		Assert.AreEqual("number", hover.Content.Value);
		Assert.AreEqual(MarkupKind.Markdown, hover.Content.Kind);
		Assert.AreEqual(new TextRange(18, 1), hover.Range);
	}
}
