using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class CompletionItemTagConversionTests
{
	[TestMethod]
	public void FromProtocol_ForAnItemWithoutDeprecation_ReturnsNoTags() =>
		Assert.IsEmpty(CompletionItemTagConversion.FromProtocol(new Lsp.CompletionItemPayload { Label = "item" }));

	[TestMethod]
	public void FromProtocol_ForTheDeprecatedTag_ReturnsTheDeprecatedTag()
	{
		var payload = new Lsp.CompletionItemPayload { Label = "item", Tags = [Lsp.CompletionItemTag.Deprecated] };
		Assert.AreSequenceEqual([CompletionItemTag.Deprecated], CompletionItemTagConversion.FromProtocol(payload));
	}

	[TestMethod]
	public void FromProtocol_ForTheDeprecatedBooleanAlone_ReturnsTheDeprecatedTag()
	{
		var payload = new Lsp.CompletionItemPayload
		{
			Label = "item",
			ExtensionData = new Dictionary<string, JsonElement> { ["deprecated"] = JsonSerializer.SerializeToElement(true) },
		};

		Assert.AreSequenceEqual([CompletionItemTag.Deprecated], CompletionItemTagConversion.FromProtocol(payload));
	}
}
