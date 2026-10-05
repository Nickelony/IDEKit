using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class SignatureHelpRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	public void Request_StoresSnapshotCaretOffsetAndContext()
	{
		var context = new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange, isRetrigger: true);
		var request = new SignatureHelpRequest(DocumentId, documentText: "print(value", caretOffset: 6, context);

		Assert.AreEqual("print(value", request.DocumentText);
		Assert.AreEqual(6, request.CaretOffset);
		Assert.AreSame(context, request.Context);
	}

	[TestMethod]
	public void Request_WithoutContext_LeavesContextAbsent()
	{
		var request = new SignatureHelpRequest(DocumentId, "print(value", 6);
		Assert.IsNull(request.Context);
	}

	[TestMethod]
	public void Request_CaretOffsetAtTextLength_IsAccepted()
	{
		var request = new SignatureHelpRequest(DocumentId, "abc", 3);
		Assert.AreEqual(3, request.CaretOffset);
	}

	[TestMethod]
	public void Request_NegativeCaretOffset_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SignatureHelpRequest(DocumentId, "abc", -1));

	[TestMethod]
	public void Request_CaretOffsetBeyondTextLength_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SignatureHelpRequest(DocumentId, "abc", 4));

	[TestMethod]
	public void Request_NullDocumentText_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new SignatureHelpRequest(DocumentId, null!, 0));

	[TestMethod]
	public void Request_Equality_ComparesAllComponents()
	{
		var context = new SignatureHelpContext(SignatureHelpTriggerKind.TriggerCharacter, "(");
		var request = new SignatureHelpRequest(DocumentId, documentText: "print(", caretOffset: 6, context);

		Assert.AreEqual(request, new SignatureHelpRequest(DocumentId, documentText: "print(", caretOffset: 6, context));
		Assert.AreNotEqual(request, new SignatureHelpRequest(DocumentId, documentText: "other(", caretOffset: 6, context));
		Assert.AreNotEqual(request, new SignatureHelpRequest(DocumentId, "print(", 6));
		Assert.AreNotEqual(request, new SignatureHelpRequest(DocumentId, documentText: "print(", caretOffset: 5, context));
	}
}
