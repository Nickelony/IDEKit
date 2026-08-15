using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerSignatureHelpRequestTests
{
	[TestMethod]
	public void Constructor_NegativePosition_ClampsPerComponent()
	{
		var request = new LanguageServerSignatureHelpRequest("doc.lua", "content", new TextPosition(3, -4));

		Assert.AreEqual("doc.lua", request.FilePath);
		Assert.AreEqual("content", request.DocumentText);
		Assert.AreEqual(3, request.Position.Line);
		Assert.AreEqual(0, request.Position.Character);
	}

	[TestMethod]
	public void Constructor_WithoutContext_StoresNull()
	{
		var request = new LanguageServerSignatureHelpRequest("doc.lua", "content", new TextPosition(0, 0));

		Assert.IsNull(request.Context);
	}

	[TestMethod]
	public void Constructor_Context_IsPreserved()
	{
		var context = new TextSignatureHelpContext(TextSignatureHelpTriggerKind.TriggerCharacter, "(", isRetrigger: true);
		var request = new LanguageServerSignatureHelpRequest("doc.lua", "content", new TextPosition(0, 0), context);

		Assert.AreSame(context, request.Context);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerSignatureHelpRequest(null!, "content", default));
		Assert.ThrowsExactly<ArgumentNullException>(() => new LanguageServerSignatureHelpRequest("doc.lua", null!, default));
	}

	[TestMethod]
	public void Equals_SameValues_AreEqual()
	{
		var first = new LanguageServerSignatureHelpRequest("doc.lua", "content", new TextPosition(0, 0));
		var second = new LanguageServerSignatureHelpRequest("doc.lua", "content", new TextPosition(0, 0));

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}
}
