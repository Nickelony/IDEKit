using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class CompletionRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	[DataRow(-1, DisplayName = "NegativeOffset")]
	[DataRow(5, DisplayName = "BeyondTextLength")]
	public void Constructor_CaretOffsetOutOfRange_Throws(int caretOffset) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompletionRequest(DocumentId, "text", caretOffset));

	[TestMethod]
	public void Constructor_CaretOffsetAtTextLength_IsAccepted()
	{
		var request = new CompletionRequest(DocumentId, "text", 4);
		Assert.AreEqual(4, request.CaretOffset);
	}

	[TestMethod]
	public void Constructor_DefaultArguments_UseExpectedValues()
	{
		var request = new CompletionRequest(DocumentId, "text", 2);

		Assert.AreEqual("text", request.DocumentText);
		Assert.AreEqual(2, request.CaretOffset);
		Assert.AreSame(CompletionContext.Invoked, request.Context);
	}

	[TestMethod]
	public void Constructor_ExplicitCustomTrigger_IsPreserved()
	{
		var emptyLineTrigger = CompletionContext.CreateCustom("EmptyLine");
		var request = new CompletionRequest(DocumentId, documentText: "text", caretOffset: 0, context: emptyLineTrigger);

		Assert.AreSame(emptyLineTrigger, request.Context);
	}

	[TestMethod]
	public void Equality_ComparesAllComponents()
	{
		var wordTrigger = CompletionContext.CreateCustom("Word");
		var contextualTrigger = CompletionContext.CreateCustom("Contextual");
		var request = new CompletionRequest(DocumentId, documentText: "text", caretOffset: 2, context: wordTrigger);

		Assert.AreEqual(
			request,
			new CompletionRequest(DocumentId, documentText: "text", caretOffset: 2, context: CompletionContext.CreateCustom("Word"))
		);

		Assert.AreNotEqual(request, new CompletionRequest(DocumentId, documentText: "text", caretOffset: 2, context: contextualTrigger));
		Assert.AreNotEqual(request, new CompletionRequest(DocumentId, documentText: "text", caretOffset: 1, context: wordTrigger));
	}
}
