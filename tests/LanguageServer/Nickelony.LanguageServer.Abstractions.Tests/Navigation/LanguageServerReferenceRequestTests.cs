using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerReferenceRequestTests
{
	[TestMethod]
	public void Constructor_NegativePosition_ClampsToZero()
	{
		var request = new LanguageServerReferenceRequest("doc.lua", "content", new TextPosition(-1, -4));

		Assert.AreEqual("doc.lua", request.FilePath);
		Assert.AreEqual("content", request.DocumentText);
		Assert.AreEqual(new TextPosition(0, 0), request.Position);
	}

	[TestMethod]
	public void Constructor_ValidPosition_IsPreserved()
	{
		var request = new LanguageServerReferenceRequest("doc.lua", "content", new TextPosition(2, 5));

		Assert.AreEqual(new TextPosition(2, 5), request.Position);
	}

	[TestMethod]
	public void Constructor_DefaultsIncludeDeclarationToTrue()
	{
		var request = new LanguageServerReferenceRequest("doc.lua", "content", new TextPosition(2, 5));

		Assert.IsTrue(request.IncludeDeclaration);
	}

	[TestMethod]
	public void Constructor_ExplicitIncludeDeclaration_IsPreserved()
	{
		var request = new LanguageServerReferenceRequest("doc.lua", "content", new TextPosition(2, 5), includeDeclaration: false);

		Assert.IsFalse(request.IncludeDeclaration);
	}
}
