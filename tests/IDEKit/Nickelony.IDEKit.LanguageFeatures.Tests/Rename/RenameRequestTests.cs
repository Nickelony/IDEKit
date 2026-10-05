using Nickelony.IDEKit.LanguageFeatures.Rename;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class RenameRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	[DataRow(-1, DisplayName = "NegativeOffset")]
	[DataRow(5, DisplayName = "BeyondTextLength")]
	public void Constructor_SymbolOffsetOutOfRange_Throws(int symbolOffset)
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new RenameRequest(DocumentId, documentText: "text", symbolOffset, newName: "newName")
		);
	}

	[TestMethod]
	public void Constructor_StoresTheRequestValues()
	{
		var request = new RenameRequest(DocumentId, documentText: "text", symbolOffset: 4, newName: "newName");

		Assert.AreEqual("text", request.DocumentText);
		Assert.AreEqual(4, request.SymbolOffset);
		Assert.AreEqual("newName", request.NewName);
	}

	[TestMethod]
	public void Constructor_BlankNewName_IsAccepted()
	{
		var request = new RenameRequest(DocumentId, documentText: "text", symbolOffset: 0, newName: "   ");
		Assert.AreEqual("   ", request.NewName);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new RenameRequest(DocumentId, documentText: null!, symbolOffset: 0, newName: "newName"));
		Assert.ThrowsExactly<ArgumentNullException>(() => new RenameRequest(DocumentId, documentText: "text", symbolOffset: 0, newName: null!));
	}
}
