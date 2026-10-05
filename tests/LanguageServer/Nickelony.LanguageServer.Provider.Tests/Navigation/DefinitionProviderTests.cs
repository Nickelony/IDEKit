using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DefinitionProviderTests
{
	private const string Document = "local a = 1\nprint(a)";

	[TestMethod]
	public async Task GetDefinitionAsync_WithoutASymbolOffset_SendsNothingAndReturnsNull()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		DefinitionLocation? location = await new DefinitionProvider(pipeline, LanguageServerFeatureProviderOptions.Default).GetDefinitionAsync(
			new DefinitionRequest(TestPaths.Script("a.lua"), Document, symbolName: "a"),
			CancellationToken.None
		);

		Assert.IsNull(location);
		Assert.IsEmpty(pipeline.Requests);
	}

	[TestMethod]
	public async Task GetDefinitionAsync_SendsADefinitionRequestAtTheCaretPosition()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new DefinitionProvider(pipeline, LanguageServerFeatureProviderOptions.Default).GetDefinitionAsync(
			new DefinitionRequest(documentId: TestPaths.Script("a.lua"), documentText: Document, symbolName: "a", symbolOffset: 18),
			CancellationToken.None
		);

		RecordingDocumentRequestPipeline.RecordedRequest request = pipeline.SingleRequest;

		Assert.AreEqual(LspMethodNames.Definition, request.Method);
		Assert.AreEqual(new TextPosition(1, 6), request.Position);
		Assert.IsNull(request.SupportsRequest);
	}

	[TestMethod]
	public async Task GetDefinitionAsync_WithoutAResponse_ReturnsNull()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		DefinitionLocation? location = await new DefinitionProvider(pipeline, LanguageServerFeatureProviderOptions.Default).GetDefinitionAsync(
			new DefinitionRequest(documentId: TestPaths.Script("a.lua"), documentText: Document, symbolName: "a", symbolOffset: 18),
			CancellationToken.None
		);

		Assert.IsNull(location);
	}

	[TestMethod]
	public async Task GetDefinitionAsync_ADefinitionInTheRequestedDocument_NamesNoDocument()
	{
		string path = TestPaths.Script("a.lua");

		LanguageServerFeatureProviderOptions options = LanguageServerFeatureProviderOptions.Default with
		{
			PathComparison = LocalPathComparisonPolicy.CaseInsensitive,
		};

		// The server spells the path in another case, as LuaLS writes the drive letter, which is still the requested document.
		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = PayloadTestHelpers.DeserializeDefinitionResponse(
				new
				{
					uri = new Uri(path.ToUpperInvariant()).AbsoluteUri,
					range = new { start = new { line = 0, character = 6 }, end = new { line = 0, character = 7 } },
				}
			),
		};

		DefinitionLocation? location = await new DefinitionProvider(pipeline, options).GetDefinitionAsync(
			new DefinitionRequest(documentId: path, documentText: Document, symbolName: "a", symbolOffset: 18),
			CancellationToken.None
		);

		Assert.IsNotNull(location);
		Assert.IsNull(location.DocumentId);
		Assert.AreEqual(new TextPosition(0, 6), location.NavigationStart);
	}

	[TestMethod]
	public async Task GetDefinitionAsync_WhenTheRequestNamesTheDocumentThroughAnUnnormalizedPath_StillRecognizesTheRequestedDocument()
	{
		string path = TestPaths.Script("a.lua");
		string unnormalizedPath = Path.Combine(TestPaths.Root, "Scripts", "..", "Scripts", "a.lua");

		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = PayloadTestHelpers.DeserializeDefinitionResponse(
				new
				{
					uri = new Uri(path).AbsoluteUri,
					range = new { start = new { line = 0, character = 6 }, end = new { line = 0, character = 7 } },
				}
			),
		};

		DefinitionLocation? location = await new DefinitionProvider(pipeline, LanguageServerFeatureProviderOptions.Default).GetDefinitionAsync(
			new DefinitionRequest(documentId: unnormalizedPath, documentText: Document, symbolName: "a", symbolOffset: 18),
			CancellationToken.None
		);

		// The pipeline hands the normalized path of the request, so the definition in the requested document names no document.
		Assert.IsNotNull(location);
		Assert.IsNull(location.DocumentId);
	}

	[TestMethod]
	public async Task GetDefinitionAsync_ADefinitionInAnotherDocument_NamesThatDocument()
	{
		string otherPath = TestPaths.Script("b.lua");

		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = PayloadTestHelpers.DeserializeDefinitionResponse(
				new
				{
					uri = new Uri(otherPath).AbsoluteUri,
					range = new { start = new { line = 3, character = 0 }, end = new { line = 3, character = 1 } },
				}
			),
		};

		DefinitionLocation? location = await new DefinitionProvider(pipeline, LanguageServerFeatureProviderOptions.Default).GetDefinitionAsync(
			new DefinitionRequest(documentId: TestPaths.Script("a.lua"), documentText: Document, symbolName: "a", symbolOffset: 18),
			CancellationToken.None
		);

		Assert.AreEqual(otherPath, location?.DocumentId);
	}
}
