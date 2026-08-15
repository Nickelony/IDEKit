namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerFormattingRequestTests
{
	[TestMethod]
	public void Constructor_StoresFileDocumentAndOptions()
	{
		var options = new TextFormattingOptions(8, insertSpaces: true);
		var request = new LanguageServerFormattingRequest("doc.lua", "content", options);

		Assert.AreEqual("doc.lua", request.FilePath);
		Assert.AreEqual("content", request.DocumentText);
		Assert.AreSame(options, request.Options);
	}
}
