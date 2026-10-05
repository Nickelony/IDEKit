using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Nickelony.IDEKit.LanguageFeatures.Rename;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the formatting request through the framework pipeline.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderFormattingTests
{
	[TestMethod]
	public async Task FormatDocumentAsync_ReturnsFormattingEditsAndPassesEditorOptions()
	{
		string workspaceRoot = TestPaths.Root;
		string requestedFilePath = Path.Combine(TestPaths.Root, "Scripts", "..", "Scripts", "test.test");

		const string content = "local value=1";

		string normalizedFilePath = LocalPath.Normalize(requestedFilePath);

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsFormatting = true,

			FormattingResponse = JsonSerializer.SerializeToElement(
				new object[]
				{
					new
					{
						range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } },
						newText = "local value = 1\r\n",
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		WorkspaceEdit? workspaceEdit = await provider.GetFormattingEditsAsync(
			new FormattingRequest(requestedFilePath, content, new FormattingOptions(tabSize: 3, insertSpaces: false)),
			TestContext.CancellationToken
		);

		Assert.IsNotNull(workspaceEdit);
		Assert.HasCount(1, workspaceEdit.DocumentEdits);

		// The edit names the document as the request did, so a host that matches its own identifier ordinally finds it.
		Assert.AreEqual(requestedFilePath, workspaceEdit.DocumentEdits[0].DocumentId);
		Assert.HasCount(1, workspaceEdit.DocumentEdits[0].TextEdits);
		Assert.AreEqual("local value = 1\r\n", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/formatting");

		Assert.AreEqual(new Uri(normalizedFilePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.AreEqual(3, parameters.GetProperty("options").GetProperty("tabSize").GetInt32());
		Assert.IsFalse(parameters.GetProperty("options").GetProperty("insertSpaces").GetBoolean());

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/formatting"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task FormatDocumentAsync_WithNonPositiveTabSize_SendsTheDefaultTabSize()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsFormatting = true,
			FormattingResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetFormattingEditsAsync(
			new FormattingRequest(filePath, "local value=1", new FormattingOptions(tabSize: 0, insertSpaces: true)),
			TestContext.CancellationToken
		);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/formatting");

		// The options type clamps a non-positive tab size to its documented default before the request is built.
		Assert.AreEqual(FormattingOptions.DefaultTabSize, parameters.GetProperty("options").GetProperty("tabSize").GetInt32());
		Assert.IsTrue(parameters.GetProperty("options").GetProperty("insertSpaces").GetBoolean());
	}

	[TestMethod]
	public async Task FormatDocumentAsync_InvalidFilePath_ReturnsNullWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		WorkspaceEdit? workspaceEdit = await provider.GetFormattingEditsAsync(
			new FormattingRequest(TestPaths.InvalidFile, "local value = 1", new FormattingOptions(tabSize: 4, insertSpaces: true)),
			TestContext.CancellationToken
		);

		Assert.IsNull(workspaceEdit);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task FormatDocumentAsync_EmptyEditList_ReturnsNullAfterTheRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsFormatting = true,
			FormattingResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		WorkspaceEdit? workspaceEdit = await provider.GetFormattingEditsAsync(
			new FormattingRequest(filePath, content, new FormattingOptions(tabSize: 4, insertSpaces: true)),
			TestContext.CancellationToken
		);

		// An empty edit list means the document is already formatted; the provider reports null
		// instead of an empty workspace edit.
		Assert.IsNull(workspaceEdit);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/formatting"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task FormatDocumentAsync_ReturnsNullWhenFormattingIsUnsupported()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsFormatting = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		WorkspaceEdit? workspaceEdit = await provider.GetFormattingEditsAsync(
			new FormattingRequest(filePath, "local value=1", new FormattingOptions(tabSize: 4, insertSpaces: true)),
			TestContext.CancellationToken
		);

		// The capability gate runs before document synchronization, so an unsupported request
		// produces no traffic at all.
		Assert.IsNull(workspaceEdit);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	public TestContext? TestContext { get; set; }
}
