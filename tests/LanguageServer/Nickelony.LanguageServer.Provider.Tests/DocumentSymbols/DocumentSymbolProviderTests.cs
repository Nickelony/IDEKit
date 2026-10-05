using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DocumentSymbolProviderTests
{
	private const string Document = "local alpha = 1\nlocal beta = 2";

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_SendsADocumentScopedRequest()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new DocumentSymbolProvider(pipeline, LanguageServerFeatureProviderOptions.Default, NullLogger.Instance).GetDocumentSymbolsAsync(
			new DocumentSymbolRequest(TestPaths.Script("a.lua"), Document, filterText: string.Empty),
			CancellationToken.None
		);

		RecordingDocumentRequestPipeline.RecordedRequest request = pipeline.SingleRequest;

		Assert.AreEqual(LspMethodNames.DocumentSymbol, request.Method);
		Assert.IsNull(request.Offset);
		Assert.AreEqual(default, request.Position);

		Assert.AreEqual(
			RecordingDocumentRequestPipeline.RecordedRequest.Uri,
			request.Parameters.GetProperty("textDocument").GetProperty("uri").GetString()
		);
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_GatesTheRequestOnTheDocumentSymbolCapability()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new DocumentSymbolProvider(pipeline, LanguageServerFeatureProviderOptions.Default, NullLogger.Instance).GetDocumentSymbolsAsync(
			new DocumentSymbolRequest(TestPaths.Script("a.lua"), Document, filterText: string.Empty),
			CancellationToken.None
		);

		Func<ILanguageServerClient, bool>? gate = pipeline.SingleRequest.SupportsRequest;

		Assert.IsNotNull(gate);

		using var unsupported = new FakeLanguageServerClient { SupportsDocumentSymbols = false };
		using var supported = new FakeLanguageServerClient { SupportsDocumentSymbols = true };

		Assert.IsFalse(gate(unsupported));
		Assert.IsTrue(gate(supported));
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_WithoutAResponse_ReturnsAnEmptyList()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		IReadOnlyList<DocumentSymbol> symbols = await new DocumentSymbolProvider(
			pipeline,
			LanguageServerFeatureProviderOptions.Default,
			NullLogger.Instance
		).GetDocumentSymbolsAsync(new DocumentSymbolRequest(TestPaths.Script("a.lua"), Document, filterText: string.Empty), CancellationToken.None);

		Assert.IsEmpty(symbols);
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_KeepsOnlyTheSymbolsMatchingTheFilterText()
	{
		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = TestPayloads.Deserialize<DocumentSymbolResponse>(
				new[]
				{
					new
					{
						name = "alpha",
						kind = 13,
						range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 15 } },
						selectionRange = new { start = new { line = 0, character = 6 }, end = new { line = 0, character = 11 } },
					},

					new
					{
						name = "beta",
						kind = 13,
						range = new { start = new { line = 1, character = 0 }, end = new { line = 1, character = 14 } },
						selectionRange = new { start = new { line = 1, character = 6 }, end = new { line = 1, character = 10 } },
					},
				}
			),
		};

		IReadOnlyList<DocumentSymbol> symbols = await new DocumentSymbolProvider(
			pipeline,
			LanguageServerFeatureProviderOptions.Default,
			NullLogger.Instance
		).GetDocumentSymbolsAsync(new DocumentSymbolRequest(TestPaths.Script("a.lua"), Document, filterText: "alp"), CancellationToken.None);

		Assert.AreEqual("alpha", Assert.ContainsSingle(symbols).Name);
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_KeepsFlatEntriesOfTheRequestedDocumentOnly()
	{
		string filePath = TestPaths.Script("a.lua");

		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = TestPayloads.Deserialize<DocumentSymbolResponse>(
				new[]
				{
					new
					{
						name = "alpha",
						kind = 13,

						location = new
						{
							uri = DocumentUriConversion.CreateFileUri(filePath),
							range = new { start = new { line = 0, character = 6 }, end = new { line = 0, character = 11 } },
						},
					},

					new
					{
						name = "foreign",
						kind = 13,

						location = new
						{
							uri = DocumentUriConversion.CreateFileUri(TestPaths.Script("other.lua")),
							range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 5 } },
						},
					},
				}
			),
		};

		IReadOnlyList<DocumentSymbol> symbols = await new DocumentSymbolProvider(
			pipeline,
			LanguageServerFeatureProviderOptions.Default,
			NullLogger.Instance
		).GetDocumentSymbolsAsync(new DocumentSymbolRequest(filePath, Document, filterText: string.Empty), CancellationToken.None);

		// The pipeline hands the normalized path of the requested document, which decides which flat entry belongs to it.
		Assert.AreEqual("alpha", Assert.ContainsSingle(symbols).Name);
	}
}
