using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class SignatureHelpContextTests
{
	[TestMethod]
	public void Context_DefaultsToExplicitInvocation()
	{
		var context = new SignatureHelpContext();

		Assert.AreEqual(SignatureHelpTriggerKind.Invoked, context.TriggerKind);
		Assert.IsNull(context.TriggerCharacter);
		Assert.IsFalse(context.IsRetrigger);
		Assert.IsNull(context.ActiveSignatureHelp);
	}

	[TestMethod]
	public void Context_UndefinedTriggerKind_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SignatureHelpContext((SignatureHelpTriggerKind)0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SignatureHelpContext((SignatureHelpTriggerKind)9));
	}

	[TestMethod]
	public void Context_TriggerCharacterForCharacterTrigger_IsRetained()
	{
		var context = new SignatureHelpContext(SignatureHelpTriggerKind.TriggerCharacter, "(");

		Assert.AreEqual(SignatureHelpTriggerKind.TriggerCharacter, context.TriggerKind);
		Assert.AreEqual("(", context.TriggerCharacter);
	}

	[TestMethod]
	[DataRow(" ")]
	[DataRow("  (  ")]
	public void Context_TriggerCharacterWithWhitespace_IsKeptVerbatim(string triggerCharacter)
	{
		// The LSP requires the character with the trigger-character kind, and a server can name a space as one.
		var context = new SignatureHelpContext(SignatureHelpTriggerKind.TriggerCharacter, triggerCharacter);
		Assert.AreEqual(triggerCharacter, context.TriggerCharacter);
	}

	[TestMethod]
	public void Context_TriggerCharacterForOtherTriggers_IsDropped()
	{
		// The LSP contract leaves the trigger character undefined for non-character triggers.
		var context = new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange, "(");
		Assert.IsNull(context.TriggerCharacter);
	}

	[TestMethod]
	public void Context_EmptyTriggerCharacter_IsTreatedAsAbsent()
	{
		var context = new SignatureHelpContext(SignatureHelpTriggerKind.TriggerCharacter, string.Empty);
		Assert.IsNull(context.TriggerCharacter);
	}

	[TestMethod]
	public void Context_Retrigger_CarriesActiveSignatureHelp()
	{
		var activeSignatureHelp = new SignatureHelpInformation([new SignatureInformation("print(value)")]);

		var context = new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange, isRetrigger: true, activeSignatureHelp: activeSignatureHelp);

		Assert.IsTrue(context.IsRetrigger);
		Assert.AreSame(activeSignatureHelp, context.ActiveSignatureHelp);
	}

	[TestMethod]
	public void Context_NonRetrigger_CarriesActiveSignatureHelpWhenSupplied()
	{
		// The payload is carried independently of IsRetrigger: a host may restore or re-request
		// signature help without a retrigger.
		var activeSignatureHelp = new SignatureHelpInformation([new SignatureInformation("print(value)")]);
		var context = new SignatureHelpContext(activeSignatureHelp: activeSignatureHelp);

		Assert.IsFalse(context.IsRetrigger);
		Assert.AreSame(activeSignatureHelp, context.ActiveSignatureHelp);
	}
}
