using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerCompletionRequestTests
{
	[TestMethod]
	public void Constructor_NegativePosition_ClampsPerComponent()
	{
		var request = new LanguageServerCompletionRequest("doc.lua", "content", new TextPosition(-2, 5));

		Assert.AreEqual("doc.lua", request.FilePath);
		Assert.AreEqual("content", request.DocumentText);
		Assert.AreEqual(0, request.Position.Line);
		Assert.AreEqual(5, request.Position.Character);
	}

	[TestMethod]
	public void Constructor_WithoutTriggerCharacter_StoresNull()
	{
		var request = new LanguageServerCompletionRequest("doc.lua", "content", new TextPosition(1, 2));

		Assert.IsNull(request.TriggerCharacter);
	}

	[TestMethod]
	public void Constructor_PaddedTriggerCharacter_IsTrimmed()
	{
		var request = new LanguageServerCompletionRequest("doc.lua", "content", new TextPosition(1, 2), "  .  ");

		Assert.AreEqual(".", request.TriggerCharacter);
	}

	[TestMethod]
	public void Constructor_BlankTriggerCharacter_IsStoredAsNull()
	{
		var request = new LanguageServerCompletionRequest("doc.lua", "content", new TextPosition(1, 2), "   ");

		Assert.IsNull(request.TriggerCharacter);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerCompletionRequest(null!, "content", default));
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerCompletionRequest("doc.lua", null!, default));
	}

	[TestMethod]
	public void Equals_SameValues_AreEqual()
	{
		var first = new LanguageServerCompletionRequest("doc.lua", "content", new TextPosition(1, 2), ".");
		var second = new LanguageServerCompletionRequest("doc.lua", "content", new TextPosition(1, 2), ".");

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}
}
