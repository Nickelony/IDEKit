using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DiagnosticsRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	public void Constructor_StoresDocumentIdAndText()
	{
		var request = new DiagnosticsRequest(DocumentId, "document");

		Assert.AreEqual(DocumentId, request.DocumentId);
		Assert.AreEqual("document", request.DocumentText);
	}

	[TestMethod]
	public void Constructor_NullDocumentText_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => new DiagnosticsRequest(DocumentId, null!));
		Assert.AreEqual("documentText", exception.ParamName);
	}

	[TestMethod]
	public void Equality_SameDocumentText_AreEqual()
	{
		var first = new DiagnosticsRequest(DocumentId, "document");
		var second = new DiagnosticsRequest(DocumentId, "document");

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equality_DifferentDocumentText_AreNotEqual() =>
		Assert.AreNotEqual(new DiagnosticsRequest(DocumentId, "document"), new DiagnosticsRequest(DocumentId, "other"));

	[TestMethod]
	public void Equality_DifferentDocumentId_AreNotEqual() =>
		Assert.AreNotEqual(new DiagnosticsRequest("a.lua", "document"), new DiagnosticsRequest("b.lua", "document"));
}
