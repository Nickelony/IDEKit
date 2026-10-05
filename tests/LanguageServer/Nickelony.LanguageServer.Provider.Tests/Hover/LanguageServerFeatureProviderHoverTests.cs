using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the hover request through the framework pipeline.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderHoverTests
{
	[TestMethod]
	public async Task GetHoverAsync_ReturnsParsedHoverInfoFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		HoverInformation? hover = await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

		Assert.IsNotNull(hover);
		Assert.AreEqual("Hover docs.", hover.Content.Value);
		Assert.AreEqual(MarkupKind.Markdown, hover.Content.Kind);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/hover"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetHoverAsync_WhenTheServerAnswersNothing_ReturnsNullAfterTheRequest()
	{
		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		HoverInformation? hover = await provider.GetHoverAsync(TestPaths.Script("test.test"), "local value = 1", new TextPosition(0, 0));

		Assert.IsNull(hover);
		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/hover"], client.GetSentMethodNames());
	}
}
