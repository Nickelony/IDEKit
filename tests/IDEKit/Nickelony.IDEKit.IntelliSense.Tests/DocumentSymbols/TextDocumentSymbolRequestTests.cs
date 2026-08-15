using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.IDEKit.IntelliSense.Tests.DocumentSymbols;

[TestClass]
public sealed class TextDocumentSymbolRequestTests
{
	[TestMethod]
	public void Constructor_StoresDocumentTextAndFilterText()
	{
		var request = new TextDocumentSymbolRequest("document", "filter");

		Assert.AreEqual("document", request.DocumentText);
		Assert.AreEqual("filter", request.FilterText);
	}

	[TestMethod]
	public void Constructor_EmptyFilterText_IsAccepted()
	{
		var request = new TextDocumentSymbolRequest("document", string.Empty);

		Assert.AreEqual(string.Empty, request.FilterText);
	}

	[TestMethod]
	public void Constructor_NullDocumentText_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextDocumentSymbolRequest(null!, "filter"));
	}

	[TestMethod]
	public void Constructor_NullFilter_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextDocumentSymbolRequest("document", null!));
	}

	[TestMethod]
	public void Equality_ComparesDocumentTextAndFilter()
	{
		var first = new TextDocumentSymbolRequest("document", "filter");
		var second = new TextDocumentSymbolRequest("document", "filter");
		var different = new TextDocumentSymbolRequest("document", "other");

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
		Assert.AreNotEqual(first, different);
	}
}
