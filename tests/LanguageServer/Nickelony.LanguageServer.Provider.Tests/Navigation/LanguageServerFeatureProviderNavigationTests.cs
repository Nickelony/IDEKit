using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the definition and references requests through the framework pipeline.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderNavigationTests
{
	[TestMethod]
	public async Task GetReferencesAsync_WhenUnsupported_ReturnsEmptyAndSendsNoRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsReferences = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<ReferenceLocation> references = await provider.GetReferencesAsync(
			new ReferenceRequest(filePath, "local value = 1", 6),
			TestContext.CancellationToken
		);

		// The capability gate runs before document synchronization, so an unsupported request
		// produces no traffic at all.
		Assert.IsEmpty(references);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetDefinitionAsync_ReturnsParsedDefinitionLocationFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");
		string targetPath = TestPaths.Script("definitions.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			DefinitionResponse = JsonSerializer.SerializeToElement(
				new
				{
					uri = new Uri(targetPath).AbsoluteUri,
					range = new { start = new { line = 4, character = 2 }, end = new { line = 4, character = 7 } },
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		DefinitionLocation? definition = await provider.GetDefinitionAsync(filePath, "value", new TextPosition(0, 0));

		Assert.IsNotNull(definition);
		Assert.AreEqual(targetPath, definition.DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(4, 2), new TextPosition(4, 7)), definition.TargetRange);
		Assert.IsNull(definition.SelectionRange);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/definition"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetDefinitionAsync_NullServerResponse_ReturnsTheNullFallback()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		// A configured JSON null models a server that answers the request with null instead of a
		// location list; the provider must send the request and surface its documented null fallback.
		using var client = new FakeStatefulLanguageServerClient { DefinitionResponse = JsonSerializer.SerializeToElement<object?>(null) };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		DefinitionLocation? definition = await provider.GetDefinitionAsync(filePath, "value", new TextPosition(0, 0));

		Assert.IsNull(definition);
		Assert.AreEqual(1, client.GetSentMethodCount("textDocument/definition"));
	}

	[TestMethod]
	public async Task GetDefinitionAsync_InvalidFilePath_ReturnsNullWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		DefinitionLocation? definition = await provider.GetDefinitionAsync(TestPaths.InvalidFile, "local value = 1", new TextPosition(0, 0));

		Assert.IsNull(definition);
		Assert.IsEmpty(client.GetSentMethodNames());
		Assert.AreEqual(0, client.StartCallCount);
	}

	[TestMethod]
	public async Task GetReferencesAsync_ReturnsParsedReferenceLocationsFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");
		string targetPath = TestPaths.Script("references.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			ReferenceResponse = JsonSerializer.SerializeToElement(
				new object[]
				{
					new
					{
						uri = new Uri(targetPath).AbsoluteUri,
						range = new { start = new { line = 2, character = 4 }, end = new { line = 2, character = 9 } },
					},

					new
					{
						uri = "https://example.com/not-a-file.test",
						range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } },
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<ReferenceLocation> references = await provider.GetReferencesAsync(new ReferenceRequest(filePath, "value", 0));

		Assert.HasCount(1, references);
		Assert.AreEqual(targetPath, references[0].DocumentId);
		Assert.AreEqual(new TextPosition(2, 4), references[0].Range.Start);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/references"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetReferencesAsync_DefaultsIncludeDeclarationToTrue()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { ReferenceResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()) };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetReferencesAsync(new ReferenceRequest(filePath, "value", 0));

		JsonElement parameters = client.GetLastRequestParameters("textDocument/references");

		Assert.IsTrue(parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean());
	}

	[TestMethod]
	public async Task GetReferencesAsync_WhenIncludeDeclarationIsFalse_SendsFalseInPayload()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { ReferenceResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()) };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetReferencesAsync(
			new ReferenceRequest(documentId: filePath, documentText: "value", symbolOffset: 0, includeDeclaration: false)
		);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/references");

		Assert.IsFalse(parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean());
	}

	public TestContext? TestContext { get; set; }
}
