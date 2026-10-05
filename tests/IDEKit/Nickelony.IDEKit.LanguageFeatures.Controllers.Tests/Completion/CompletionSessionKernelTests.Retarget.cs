using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Verifies how the kernel carries a decision over to the caret of a later snapshot: onto the grown word, or not at all once the caret left it.
/// </summary>
public sealed partial class CompletionSessionKernelTests
{
	private static readonly IReadOnlyList<CompletionItem> s_retargetItems = [new CompletionItem("byte"), new CompletionItem("rep")];

	[TestMethod]
	public void TryRetarget_WordGrewByTyping_MovesTheDecisionToTheGrownWord()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 0));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string."),
			snapshot: new StringTextSnapshot("string.re"),
			caretOffset: 9,
			retargeted: out CompletionSessionDirective result
		);

		Assert.IsTrue(retargeted);
		Assert.AreEqual(new TextRange(7, 2), result.ReplacementRange);
		Assert.AreSame(decision.Items, result.Items);
	}

	[TestMethod]
	public void TryRetarget_CaretMovedBackInsideTheTypedText_MovesTheDecisionToTheShorterWord()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 0));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string."),
			snapshot: new StringTextSnapshot("string.re"),
			caretOffset: 8,
			retargeted: out CompletionSessionDirective result
		);

		// The caret still sits in the part typed since the request, so the answer holds for the word up to it.
		Assert.IsTrue(retargeted);
		Assert.AreEqual(new TextRange(7, 1), result.ReplacementRange);
	}

	[TestMethod]
	public void TryRetarget_WordWithTextGrewByTyping_KeepsTheStartAndExtendsTheEnd()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 1));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string.r"),
			snapshot: new StringTextSnapshot("string.rep"),
			caretOffset: 10,
			retargeted: out CompletionSessionDirective result
		);

		Assert.IsTrue(retargeted);
		Assert.AreEqual(new TextRange(7, 3), result.ReplacementRange);
	}

	[TestMethod]
	public void TryRetarget_NothingChanged_ReturnsTheDecisionUnchanged()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 0));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string."),
			snapshot: new StringTextSnapshot("string."),
			caretOffset: 7,
			retargeted: out CompletionSessionDirective result
		);

		Assert.IsTrue(retargeted);
		Assert.AreEqual(decision, result);
	}

	[TestMethod]
	public void TryRetarget_SpaceTypedAfterTheWord_Declines()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 0));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string."),
			snapshot: new StringTextSnapshot("string. "),
			caretOffset: 8,
			retargeted: out CompletionSessionDirective result
		);

		Assert.IsFalse(retargeted);
		Assert.AreEqual(default, result);
	}

	[TestMethod]
	public void TryRetarget_SpaceTypedAfterAGrownWord_Declines()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 0));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string."),
			snapshot: new StringTextSnapshot("string.re "),
			caretOffset: 10,
			retargeted: out _
		);

		Assert.IsFalse(retargeted);
	}

	[TestMethod]
	public void TryRetarget_CaretMovedBeforeTheWord_Declines()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 0));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string."),
			snapshot: new StringTextSnapshot("string."),
			caretOffset: 0,
			retargeted: out _
		);

		Assert.IsFalse(retargeted);
	}

	[TestMethod]
	public void TryRetarget_CaretMovedBackInsideTheRequestWord_Declines()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 2));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string.re"),
			snapshot: new StringTextSnapshot("string.re"),
			caretOffset: 8,
			retargeted: out _
		);

		// The items were filtered by the longer word, so a shorter one would show an incomplete list.
		Assert.IsFalse(retargeted);
	}

	[TestMethod]
	public void TryRetarget_RequestWordEditedSinceTheRequest_Declines()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 2));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string.re"),
			snapshot: new StringTextSnapshot("string.rxp"),
			caretOffset: 10,
			retargeted: out _
		);

		Assert.IsFalse(retargeted);
	}

	[TestMethod]
	public void TryRetarget_TextInsertedBeforeTheWord_Declines()
	{
		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 0));

		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("string."),
			snapshot: new StringTextSnapshot("--string.re"),
			caretOffset: 11,
			retargeted: out _
		);

		// The word at the caret starts two characters later than the decision's range, so the range names another word.
		Assert.IsFalse(retargeted);
	}

	[TestMethod]
	public void TryRetarget_ConfiguredLocator_DecidesWhatTheWordIs()
	{
		// A locator whose word runs from the last space to the caret, so a hyphen belongs to the word.
		var kernel = new CompletionSessionKernel(
			wordLocator: (snapshot, caretOffset) =>
			{
				string text = snapshot.GetText(0, caretOffset);
				int start = text.LastIndexOf(' ') + 1;

				return new CompletionWordSpan(text[start..], new TextRange(start, caretOffset - start));
			}
		);

		var decision = CompletionSessionDirective.Open(s_retargetItems, new TextRange(4, 3));

		bool retargeted = kernel.TryRetarget(
			directive: decision,
			requestSnapshot: new StringTextSnapshot("say hel"),
			snapshot: new StringTextSnapshot("say hel-lo"),
			caretOffset: 10,
			retargeted: out CompletionSessionDirective result
		);

		Assert.IsTrue(retargeted);
		Assert.AreEqual(new TextRange(4, 6), result.ReplacementRange);
	}

	[TestMethod]
	public void TryRetarget_DecisionWithoutAReplacementRange_ReturnsItUnchanged()
	{
		bool retargeted = new CompletionSessionKernel().TryRetarget(
			directive: CompletionSessionDirective.NoMatches,
			requestSnapshot: new StringTextSnapshot("string."),
			snapshot: new StringTextSnapshot("string. "),
			caretOffset: 8,
			retargeted: out CompletionSessionDirective result
		);

		Assert.IsTrue(retargeted);
		Assert.AreEqual(CompletionSessionDirective.NoMatches, result);
	}

	[TestMethod]
	public void TryRetarget_CaretBeyondTheSnapshot_Throws()
	{
		var kernel = new CompletionSessionKernel();
		var snapshot = new StringTextSnapshot("string.");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			kernel.TryRetarget(directive: CompletionSessionDirective.None, requestSnapshot: snapshot, snapshot, caretOffset: 8, retargeted: out _)
		);
	}

	[TestMethod]
	public void TryRetarget_LocatorRangeOutsideTheSnapshot_Throws()
	{
		var kernel = new CompletionSessionKernel(wordLocator: (_, _) => new CompletionWordSpan("x", new TextRange(5, 4)));
		var snapshot = new StringTextSnapshot("string.");

		Assert.ThrowsExactly<InvalidOperationException>(() =>
			kernel.TryRetarget(
				directive: CompletionSessionDirective.Open(s_retargetItems, new TextRange(7, 0)),
				requestSnapshot: snapshot,
				snapshot,
				caretOffset: 7,
				retargeted: out _
			)
		);
	}
}
