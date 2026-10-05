using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class ReferenceRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	[DataRow(-1, DisplayName = "NegativeOffset")]
	[DataRow(5, DisplayName = "BeyondTextLength")]
	public void Constructor_SymbolOffsetOutOfRange_Throws(int symbolOffset) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ReferenceRequest(DocumentId, "text", symbolOffset));

	[TestMethod]
	public void Constructor_SymbolOffsetAtTextLength_IsAccepted()
	{
		var request = new ReferenceRequest(DocumentId, "text", 4);

		Assert.AreEqual("text", request.DocumentText);
		Assert.AreEqual(4, request.SymbolOffset);
	}

	[TestMethod]
	public void Constructor_DefaultsIncludeDeclarationToTrue()
	{
		var request = new ReferenceRequest(DocumentId, "text", 2);
		Assert.IsTrue(request.IncludeDeclaration);
	}

	[TestMethod]
	public void Constructor_ExplicitIncludeDeclaration_IsPreserved()
	{
		var request = new ReferenceRequest(DocumentId, documentText: "text", symbolOffset: 2, includeDeclaration: false);
		Assert.IsFalse(request.IncludeDeclaration);
	}
}
