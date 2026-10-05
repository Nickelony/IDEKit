using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class ReferenceProviderTests
{
	private const string Document = "local a = 1\nprint(a)";

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public async Task GetReferencesAsync_SendsTheDeclarationFlagAtTheSymbolPosition(bool includeDeclaration)
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new ReferenceProvider(pipeline, LanguageServerFeatureProviderOptions.Default).GetReferencesAsync(
			new ReferenceRequest(documentId: TestPaths.Script("a.lua"), documentText: Document, symbolOffset: 18, includeDeclaration),
			CancellationToken.None
		);

		RecordingDocumentRequestPipeline.RecordedRequest request = pipeline.SingleRequest;

		Assert.AreEqual(LspMethodNames.References, request.Method);
		Assert.AreEqual(new TextPosition(1, 6), request.Position);
		Assert.AreEqual(includeDeclaration, request.Parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean());
	}

	[TestMethod]
	public async Task GetReferencesAsync_GatesTheRequestOnTheReferencesCapability()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new ReferenceProvider(pipeline, LanguageServerFeatureProviderOptions.Default).GetReferencesAsync(
			new ReferenceRequest(TestPaths.Script("a.lua"), Document, symbolOffset: 0),
			CancellationToken.None
		);

		Func<ILanguageServerClient, bool>? gate = pipeline.SingleRequest.SupportsRequest;

		Assert.IsNotNull(gate);

		using var unsupported = new FakeLanguageServerClient { SupportsReferences = false };
		using var supported = new FakeLanguageServerClient { SupportsReferences = true };

		Assert.IsFalse(gate(unsupported));
		Assert.IsTrue(gate(supported));
	}

	[TestMethod]
	public async Task GetReferencesAsync_WithoutAResponse_ReturnsAnEmptyList()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		IReadOnlyList<ReferenceLocation> references = await new ReferenceProvider(
			pipeline,
			LanguageServerFeatureProviderOptions.Default
		).GetReferencesAsync(new ReferenceRequest(TestPaths.Script("a.lua"), Document, symbolOffset: 0), CancellationToken.None);

		Assert.IsEmpty(references);
	}

	[TestMethod]
	public async Task GetReferencesAsync_WhenTheRequestNamesTheDocumentThroughAnUnnormalizedPath_CarriesTheRequestsIdentifier()
	{
		string path = TestPaths.Script("a.lua");
		string unnormalizedPath = Path.Combine(TestPaths.Root, "Scripts", "..", "Scripts", "a.lua");

		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = PayloadTestHelpers.DeserializeReferenceLocations(
				new object[]
				{
					new
					{
						uri = new Uri(path).AbsoluteUri,
						range = new { start = new { line = 1, character = 6 }, end = new { line = 1, character = 7 } },
					},
				}
			),
		};

		IReadOnlyList<ReferenceLocation> references = await new ReferenceProvider(
			pipeline,
			LanguageServerFeatureProviderOptions.Default
		).GetReferencesAsync(new ReferenceRequest(unnormalizedPath, Document, symbolOffset: 18), CancellationToken.None);

		// The pipeline hands the normalized path of the request, so the reference in the requested document carries the identifier the host used.
		Assert.AreEqual(unnormalizedPath, Assert.ContainsSingle(references).DocumentId);
	}

	[TestMethod]
	public async Task GetReferencesAsync_AReferenceInTheRequestedDocument_CarriesTheRequestsIdentifier()
	{
		string path = TestPaths.Script("a.lua");
		string otherPath = TestPaths.Script("b.lua");

		LanguageServerFeatureProviderOptions options = LanguageServerFeatureProviderOptions.Default with
		{
			PathComparison = LocalPathComparisonPolicy.CaseInsensitive,
		};

		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = PayloadTestHelpers.DeserializeReferenceLocations(
				new object[]
				{
					new
					{
						uri = new Uri(path.ToUpperInvariant()).AbsoluteUri,
						range = new { start = new { line = 1, character = 6 }, end = new { line = 1, character = 7 } },
					},

					new
					{
						uri = new Uri(otherPath).AbsoluteUri,
						range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } },
					},
				}
			),
		};

		IReadOnlyList<ReferenceLocation> references = await new ReferenceProvider(pipeline, options).GetReferencesAsync(
			new ReferenceRequest(path, Document, symbolOffset: 18),
			CancellationToken.None
		);

		Assert.AreSequenceEqual([path, otherPath], [.. references.Select(reference => reference.DocumentId)]);
	}
}
