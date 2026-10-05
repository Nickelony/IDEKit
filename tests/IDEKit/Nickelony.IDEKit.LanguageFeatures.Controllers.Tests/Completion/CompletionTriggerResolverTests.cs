using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class CompletionTriggerResolverTests
{
	private static readonly IReadOnlyList<string> s_dot = ["."];

	[TestMethod]
	[DataRow('a', CompletionScopes.Code, CompletionScopes.Code, true, (int)CompletionTriggerMode.Scheduled)]
	[DataRow('a', CompletionScopes.Comment, CompletionScopes.Code, true, (int)CompletionTriggerMode.None)]
	[DataRow('a', CompletionScopes.StringLiteral, CompletionScopes.Code, true, (int)CompletionTriggerMode.None)]
	[DataRow('a', CompletionScopes.None, CompletionScopes.Code, true, (int)CompletionTriggerMode.None)]
	[DataRow('a', CompletionScopes.Comment, CompletionScopes.Code | CompletionScopes.Comment, true, (int)CompletionTriggerMode.Scheduled)]
	[DataRow(
		'a',
		CompletionScopes.StringLiteral,
		CompletionScopes.Code | CompletionScopes.StringLiteral,
		false,
		(int)CompletionTriggerMode.Scheduled
	)]
	[DataRow('a', CompletionScopes.Code, CompletionScopes.None, true, (int)CompletionTriggerMode.None)]
	[DataRow('.', CompletionScopes.Code, CompletionScopes.Code, true, (int)CompletionTriggerMode.Immediate)]
	[DataRow('.', CompletionScopes.Comment, CompletionScopes.Code, true, (int)CompletionTriggerMode.Immediate)]
	[DataRow('.', CompletionScopes.StringLiteral, CompletionScopes.Code, true, (int)CompletionTriggerMode.Immediate)]
	[DataRow('.', CompletionScopes.None, CompletionScopes.Code, true, (int)CompletionTriggerMode.None)]
	[DataRow('a', CompletionScopes.None, CompletionScopes.None, true, (int)CompletionTriggerMode.None)]
	[DataRow('.', CompletionScopes.Code, CompletionScopes.Code, false, (int)CompletionTriggerMode.Immediate)]
	[DataRow('.', CompletionScopes.Comment, CompletionScopes.Code, false, (int)CompletionTriggerMode.None)]
	[DataRow('.', CompletionScopes.StringLiteral, CompletionScopes.Code, false, (int)CompletionTriggerMode.None)]
	[DataRow('.', CompletionScopes.Comment, CompletionScopes.Code | CompletionScopes.Comment, false, (int)CompletionTriggerMode.Immediate)]
	[DataRow('.', CompletionScopes.Code, CompletionScopes.None, false, (int)CompletionTriggerMode.None)]
	[DataRow('.', CompletionScopes.Code, CompletionScopes.None, true, (int)CompletionTriggerMode.Immediate)]
	[DataRow(' ', CompletionScopes.Code, CompletionScopes.Code, true, (int)CompletionTriggerMode.None)]
	[DataRow('7', CompletionScopes.Code, CompletionScopes.Code, true, (int)CompletionTriggerMode.None)]
	public void Resolve_ByScopeAndPolicy(
		char typed,
		CompletionScopes scope,
		CompletionScopes wordScopes,
		bool triggerCharactersInEveryScope,
		int expected
	)
	{
		CompletionTriggerPolicy policy = CompletionTriggerPolicy.Default with
		{
			WordScopes = wordScopes,
			TriggerCharactersInEveryScope = triggerCharactersInEveryScope,
		};

		Assert.AreEqual((CompletionTriggerMode)expected, CompletionTriggerResolver.Resolve(typed, scope, triggerCharacters: s_dot, policy));
	}

	[TestMethod]
	[DataRow('a', CompletionScopes.Code)]
	[DataRow('.', CompletionScopes.Code)]
	[DataRow('.', CompletionScopes.Comment)]
	public void Resolve_UnderTheManualPolicy_OpensNothing(char typed, CompletionScopes scope)
	{
		Assert.AreEqual(
			CompletionTriggerMode.None,
			CompletionTriggerResolver.Resolve(typed, scope, triggerCharacters: s_dot, policy: CompletionTriggerPolicy.Manual)
		);
	}

	[TestMethod]
	public void Resolve_AHostWordStart_DecidesWhichCharactersSchedule()
	{
		Assert.AreEqual(
			CompletionTriggerMode.Scheduled,

			CompletionTriggerResolver.Resolve(
				typed: '7',
				scope: CompletionScopes.Code,
				triggerCharacters: s_dot,

				policy: CompletionTriggerPolicy.Default with
				{
					WordStart = char.IsDigit,
				}
			)
		);
	}

	[TestMethod]
	public void Resolve_ATriggerCharacterThatStartsAWord_RequestsAtOnce()
	{
		Assert.AreEqual(
			CompletionTriggerMode.Immediate,

			CompletionTriggerResolver.Resolve(
				typed: 'a',
				scope: CompletionScopes.Code,
				triggerCharacters: ["a"],
				policy: CompletionTriggerPolicy.Default
			)
		);
	}

	[TestMethod]
	public void Resolve_AnEntryOfTwoCharacters_NeverMatches()
	{
		Assert.AreEqual(
			CompletionTriggerMode.None,

			CompletionTriggerResolver.Resolve(
				typed: '-',
				scope: CompletionScopes.Code,
				triggerCharacters: ["->"],
				policy: CompletionTriggerPolicy.Default
			)
		);
	}
}
