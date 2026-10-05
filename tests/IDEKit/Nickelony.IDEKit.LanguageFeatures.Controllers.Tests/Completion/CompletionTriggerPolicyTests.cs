using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class CompletionTriggerPolicyTests
{
	[TestMethod]
	public void Default_SchedulesWordsInCodeAndAdmitsTriggerCharactersEverywhere()
	{
		CompletionTriggerPolicy policy = CompletionTriggerPolicy.Default;

		Assert.AreEqual(CompletionScopes.Code, policy.WordScopes);
		Assert.IsTrue(policy.TriggerCharactersInEveryScope);
	}

	[TestMethod]
	public void Manual_HasNoWordScopesAndNoTriggerCharacters()
	{
		CompletionTriggerPolicy policy = CompletionTriggerPolicy.Manual;

		Assert.AreEqual(CompletionScopes.None, policy.WordScopes);
		Assert.IsFalse(policy.TriggerCharactersInEveryScope);
	}

	[TestMethod]
	[DataRow('a', true)]
	[DataRow('Z', true)]
	[DataRow('_', true)]
	[DataRow('7', false)]
	[DataRow('.', false)]
	[DataRow(' ', false)]
	public void Default_WordStart_IsALetterOrAnUnderscore(char character, bool startsAWord) =>
		Assert.AreEqual(startsAWord, CompletionTriggerPolicy.Default.WordStart(character));

	[TestMethod]
	public void WordScopes_WithAnUndefinedFlag_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CompletionTriggerPolicy.Default with
			{
				WordScopes = (CompletionScopes)8,
			}
		);

		Assert.AreEqual("WordScopes", exception.ParamName);
	}

	[TestMethod]
	public void WordScopes_EveryDefinedCombination_IsAccepted()
	{
		CompletionScopes all = CompletionScopes.Code | CompletionScopes.Comment | CompletionScopes.StringLiteral;

		Assert.AreEqual(all, (CompletionTriggerPolicy.Default with { WordScopes = all }).WordScopes);
		Assert.AreEqual(CompletionScopes.None, (CompletionTriggerPolicy.Default with { WordScopes = CompletionScopes.None }).WordScopes);
	}

	[TestMethod]
	public void WordStart_Null_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			CompletionTriggerPolicy.Default with
			{
				WordStart = null!,
			}
		);

		Assert.AreEqual("WordStart", exception.ParamName);
	}

	[TestMethod]
	public void WordStart_AHostPredicate_ReplacesTheDefault()
	{
		Func<char, bool> digits = char.IsDigit;

		CompletionTriggerPolicy policy = CompletionTriggerPolicy.Default with { WordStart = digits };

		Assert.AreSame(digits, policy.WordStart);
		Assert.IsTrue(policy.WordStart('7'));
	}
}
