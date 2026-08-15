using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.IDEKit.IntelliSense.Tests.Diagnostics;

[TestClass]
public sealed class TextDiagnosticsRequestTests
{
	[TestMethod]
	public void Constructor_StoresDocumentText()
	{
		var request = new TextDiagnosticsRequest("document");

		Assert.AreEqual("document", request.DocumentText);
	}

	[TestMethod]
	public void Constructor_NullDocumentText_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => new TextDiagnosticsRequest(null!));

		Assert.AreEqual("documentText", exception.ParamName);
	}

	[TestMethod]
	public void Equality_SameDocumentText_AreEqual()
	{
		var first = new TextDiagnosticsRequest("document");
		var second = new TextDiagnosticsRequest("document");

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equality_DifferentDocumentText_AreNotEqual()
	{
		Assert.AreNotEqual(new TextDiagnosticsRequest("document"), new TextDiagnosticsRequest("other"));
	}

	[TestMethod]
	public void Constructor_DocumentId_IsTrimmedAndBlankBecomesNull()
	{
		Assert.AreEqual("scripts/test.lua", new TextDiagnosticsRequest("document", "  scripts/test.lua  ").DocumentId);
		Assert.IsNull(new TextDiagnosticsRequest("document", "   ").DocumentId);
		Assert.IsNull(new TextDiagnosticsRequest("document").DocumentId);
	}
}
