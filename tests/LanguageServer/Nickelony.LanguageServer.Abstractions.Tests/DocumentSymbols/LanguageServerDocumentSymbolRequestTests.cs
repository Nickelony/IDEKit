namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerDocumentSymbolRequestTests
{
	[TestMethod]
	public void Constructor_StoresFileAndDocument()
	{
		var request = new LanguageServerDocumentSymbolRequest("doc.lua", "content");

		Assert.AreEqual("doc.lua", request.FilePath);
		Assert.AreEqual("content", request.DocumentText);
	}

	[TestMethod]
	public void Equality_SameValues_AreEqual()
	{
		var left = new LanguageServerDocumentSymbolRequest("doc.lua", "content");
		var right = new LanguageServerDocumentSymbolRequest("doc.lua", "content");

		Assert.AreEqual(left, right);
		Assert.IsTrue(left == right);
		Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
	}

	[TestMethod]
	public void Equality_DifferentDocumentText_AreNotEqual()
	{
		var left = new LanguageServerDocumentSymbolRequest("doc.lua", "content");
		var right = new LanguageServerDocumentSymbolRequest("doc.lua", "other");

		Assert.AreNotEqual(left, right);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerDocumentSymbolRequest(null!, "content"));
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerDocumentSymbolRequest("doc.lua", null!));
	}
}
