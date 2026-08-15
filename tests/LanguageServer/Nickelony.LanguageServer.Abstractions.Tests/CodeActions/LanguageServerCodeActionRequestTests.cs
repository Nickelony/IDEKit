using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerCodeActionRequestTests
{
	[TestMethod]
	public void Constructor_NegativeCoordinates_ClampToZero()
	{
		var request = new LanguageServerCodeActionRequest("a.lua", "text",
			new TextPositionRange(new TextPosition(-2, -3), new TextPosition(-4, -5)));

		Assert.AreEqual(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0)), request.Range);
	}

	[TestMethod]
	public void Constructor_MixedSignCoordinates_ClampPerComponent()
	{
		var request = new LanguageServerCodeActionRequest("a.lua", "text",
			new TextPositionRange(new TextPosition(-2, 3), new TextPosition(1, -4)));

		Assert.AreEqual(new TextPositionRange(new TextPosition(0, 3), new TextPosition(1, 0)), request.Range);
	}

	[TestMethod]
	public void Constructor_InvertedRange_IsStoredAsSupplied()
	{
		var range = new TextPositionRange(new TextPosition(2, 2), new TextPosition(1, 1));
		var request = new LanguageServerCodeActionRequest("a.lua", "text", range);

		Assert.AreEqual(range, request.Range);
	}

	[TestMethod]
	public void Constructor_StoresFilePathAndDocumentText()
	{
		var request = new LanguageServerCodeActionRequest("a.lua", "text", default);

		Assert.AreEqual("a.lua", request.FilePath);
		Assert.AreEqual("text", request.DocumentText);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerCodeActionRequest(null!, "text", default));
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerCodeActionRequest("a.lua", null!, default));
	}
}
