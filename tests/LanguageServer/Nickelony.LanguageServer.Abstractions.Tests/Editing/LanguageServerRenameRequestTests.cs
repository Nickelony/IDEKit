using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerRenameRequestTests
{
	[TestMethod]
	public void Constructor_NegativePosition_ClampsToZero()
	{
		var request = new LanguageServerRenameRequest("doc.lua", "content", new TextPosition(-3, -7), "newName");

		Assert.AreEqual(new TextPosition(0, 0), request.Position);
		Assert.AreEqual("newName", request.NewName);
	}

	[TestMethod]
	public void Constructor_MixedSignPosition_ClampsPerComponent()
	{
		var request = new LanguageServerRenameRequest("doc.lua", "content", new TextPosition(4, -1), "newName");

		Assert.AreEqual(4, request.Position.Line);
		Assert.AreEqual(0, request.Position.Character);
	}

	[TestMethod]
	public void Constructor_ValidPosition_IsPreserved()
	{
		var request = new LanguageServerRenameRequest("doc.lua", "content", new TextPosition(4, 9), "newName");

		Assert.AreEqual("doc.lua", request.FilePath);
		Assert.AreEqual("content", request.DocumentText);
		Assert.AreEqual(new TextPosition(4, 9), request.Position);
	}

	[TestMethod]
	public void Constructor_BlankNewName_IsAccepted()
	{
		var request = new LanguageServerRenameRequest("doc.lua", "content", new TextPosition(0, 0), "   ");

		Assert.AreEqual("   ", request.NewName);
	}
}
