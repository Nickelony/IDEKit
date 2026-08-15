namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class CompletionResponseTests
{
	[TestMethod]
	public void Constructor_DefensivelyClonesItemList()
	{
		CompletionItemPayload[] items =
		[
			new CompletionItemPayload
			{
				Label = "spawn",
				Kind = CompletionItemKind.Function,
				InsertText = "spawn"
			}
		];

		var response = new CompletionResponse(items);

		items[0] = new CompletionItemPayload
		{
			Label = "changed",
			Kind = CompletionItemKind.Keyword,
			InsertText = "changed"
		};

		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);
		Assert.AreEqual("spawn", response.Items[0].Label);
	}
}
