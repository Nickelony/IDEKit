using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DocumentSymbolRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	public void Constructor_StoresDocumentTextAndFilterText()
	{
		var request = new DocumentSymbolRequest(DocumentId, "document", "filter");

		Assert.AreEqual("document", request.DocumentText);
		Assert.AreEqual("filter", request.FilterText);
	}

	[TestMethod]
	public void Constructor_EmptyFilterText_IsAccepted()
	{
		var request = new DocumentSymbolRequest(DocumentId, "document", string.Empty);
		Assert.AreEqual(string.Empty, request.FilterText);
	}

	[TestMethod]
	public void Constructor_NullDocumentText_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new DocumentSymbolRequest(DocumentId, null!, "filter"));

	[TestMethod]
	public void Constructor_NullFilter_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new DocumentSymbolRequest(DocumentId, "document", null!));

	[TestMethod]
	public void Equality_ComparesDocumentTextAndFilter()
	{
		var first = new DocumentSymbolRequest(DocumentId, "document", "filter");
		var second = new DocumentSymbolRequest(DocumentId, "document", "filter");
		var different = new DocumentSymbolRequest(DocumentId, "document", "other");

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
		Assert.AreNotEqual(first, different);
	}
}
