using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerHoverRequestTests
{
	[TestMethod]
	public void Constructor_NegativePosition_ClampsToZero()
	{
		var request = new LanguageServerHoverRequest("doc.lua", "content", new TextPosition(-1, -4));

		Assert.AreEqual("doc.lua", request.FilePath);
		Assert.AreEqual("content", request.DocumentText);
		Assert.AreEqual(new TextPosition(0, 0), request.Position);
	}

	[TestMethod]
	public void Constructor_ValidPosition_IsPreserved()
	{
		var request = new LanguageServerHoverRequest("doc.lua", "content", new TextPosition(2, 5));

		Assert.AreEqual(new TextPosition(2, 5), request.Position);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerHoverRequest(null!, "content", default));
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerHoverRequest("doc.lua", null!, default));
	}
}
