using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionRequestTests
{
	[TestMethod]
	[DataRow(-1, DisplayName = "NegativeOffset")]
	[DataRow(5, DisplayName = "BeyondTextLength")]
	public void Constructor_CaretOffsetOutOfRange_Throws(int caretOffset)
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionRequest("text", caretOffset));
	}

	[TestMethod]
	public void Constructor_CaretOffsetAtTextLength_IsAccepted()
	{
		var request = new TextCompletionRequest("text", 4);

		Assert.AreEqual(4, request.CaretOffset);
	}

	[TestMethod]
	public void Constructor_DefaultArguments_UseExpectedValues()
	{
		var request = new TextCompletionRequest("text", 2);

		Assert.AreEqual("text", request.DocumentText);
		Assert.AreEqual(2, request.CaretOffset);
		Assert.AreSame(TextCompletionTrigger.Invoked, request.Trigger);
	}

	[TestMethod]
	public void Constructor_ExplicitCustomTrigger_IsPreserved()
	{
		TextCompletionTrigger emptyLineTrigger = TextCompletionTrigger.CreateCustom("EmptyLine");
		var request = new TextCompletionRequest("text", 0, emptyLineTrigger);

		Assert.AreSame(emptyLineTrigger, request.Trigger);
	}

	[TestMethod]
	public void Equality_ComparesAllComponents()
	{
		TextCompletionTrigger wordTrigger = TextCompletionTrigger.CreateCustom("Word");
		TextCompletionTrigger contextualTrigger = TextCompletionTrigger.CreateCustom("Contextual");
		var request = new TextCompletionRequest("text", 2, wordTrigger);

		Assert.AreEqual(request, new TextCompletionRequest("text", 2, TextCompletionTrigger.CreateCustom("Word")));
		Assert.AreNotEqual(request, new TextCompletionRequest("text", 2, contextualTrigger));
		Assert.AreNotEqual(request, new TextCompletionRequest("text", 1, wordTrigger));
	}
}
