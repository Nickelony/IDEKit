using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Verifies the commit-character decision: which combinations of option, event state, typed text, and selected item commit.</summary>
[TestClass]
public sealed class CompletionCommitPolicyTests
{
	[TestMethod]
	public void ShouldCommit_TypedCharacterListedBySelectedItem_ReturnsTrue()
	{
		StubCommitData selected = new(".", "(");
		Assert.IsTrue(CompletionCommitPolicy.ShouldCommit(acceptOnCommitCharacters: true, isHandled: false, typedText: ".", selected));
	}

	[TestMethod]
	public void ShouldCommit_AnyListedCharacter_ReturnsTrue()
	{
		StubCommitData selected = new(".", "(");
		Assert.IsTrue(CompletionCommitPolicy.ShouldCommit(acceptOnCommitCharacters: true, isHandled: false, typedText: "(", selected));
	}

	[TestMethod]
	public void ShouldCommit_OptionOff_ReturnsFalse()
	{
		StubCommitData selected = new(".");
		Assert.IsFalse(CompletionCommitPolicy.ShouldCommit(acceptOnCommitCharacters: false, isHandled: false, typedText: ".", selected));
	}

	[TestMethod]
	public void ShouldCommit_HandledEvent_ReturnsFalse()
	{
		StubCommitData selected = new(".");
		Assert.IsFalse(CompletionCommitPolicy.ShouldCommit(acceptOnCommitCharacters: true, isHandled: true, typedText: ".", selected));
	}

	[TestMethod]
	[DataRow(null, DisplayName = "NoText")]
	[DataRow("", DisplayName = "EmptyText")]
	[DataRow("..", DisplayName = "TwoCharacters")]
	public void ShouldCommit_TextThatIsNotOneCharacter_ReturnsFalse(string? typedText)
	{
		// Commit characters are single characters, so a composition result of several characters never matches an entry that happens to equal it.
		StubCommitData selected = new(".", "..");
		Assert.IsFalse(CompletionCommitPolicy.ShouldCommit(acceptOnCommitCharacters: true, isHandled: false, typedText, selected));
	}

	[TestMethod]
	public void ShouldCommit_NoCommitData_ReturnsFalse() =>
		Assert.IsFalse(CompletionCommitPolicy.ShouldCommit(acceptOnCommitCharacters: true, isHandled: false, typedText: ".", selected: null));

	[TestMethod]
	public void ShouldCommit_EmptyCommitCharacterList_ReturnsFalse()
	{
		StubCommitData selected = new();
		Assert.IsFalse(CompletionCommitPolicy.ShouldCommit(acceptOnCommitCharacters: true, isHandled: false, typedText: ".", selected));
	}

	[TestMethod]
	public void ShouldCommit_CharacterThatIsNotListed_ReturnsFalse()
	{
		StubCommitData selected = new(".", "(");
		Assert.IsFalse(CompletionCommitPolicy.ShouldCommit(acceptOnCommitCharacters: true, isHandled: false, typedText: ")", selected));
	}

	[TestMethod]
	public void ShouldCommit_ComparesTheCharacterOrdinally()
	{
		StubCommitData selected = new("a");
		Assert.IsFalse(CompletionCommitPolicy.ShouldCommit(acceptOnCommitCharacters: true, isHandled: false, typedText: "A", selected));
	}

	private sealed class StubCommitData(params string[] commitCharacters) : ICompletionCommitCharacters
	{
		public IReadOnlyList<string> CommitCharacters { get; } = commitCharacters;
	}
}
