using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class SignatureHelpTriggerKindTests
{
	[TestMethod]
	[DataRow(SignatureHelpTriggerKind.Invoked, 1)]
	[DataRow(SignatureHelpTriggerKind.TriggerCharacter, 2)]
	[DataRow(SignatureHelpTriggerKind.ContentChange, 3)]
	public void TriggerKind_MatchesLspNumericContract(SignatureHelpTriggerKind kind, int expectedValue) => Assert.AreEqual(expectedValue, (int)kind);
}
