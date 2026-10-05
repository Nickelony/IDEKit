using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class CompletionContextConversionTests
{
	[TestMethod]
	public void ToProtocol_ForATypedCharacter_ReportsATriggerCharacterContext()
	{
		Lsp.CompletionContextPayload context = CompletionContextConversion.ToProtocol(CompletionContext.ForCharacter('.'));

		Assert.AreEqual(Lsp.CompletionTriggerKind.TriggerCharacter, context.TriggerKind);
		Assert.AreEqual(".", context.TriggerCharacter);
	}

	[TestMethod]
	public void ToProtocol_ForAnInvokedRequest_ReportsAnInvokedContextWithoutACharacter()
	{
		Lsp.CompletionContextPayload context = CompletionContextConversion.ToProtocol(CompletionContext.Invoked);

		Assert.AreEqual(Lsp.CompletionTriggerKind.Invoked, context.TriggerKind);
		Assert.IsNull(context.TriggerCharacter);
	}

	[TestMethod]
	public void ToProtocol_ForAHostDefinedTrigger_ReadsAsInvoked()
	{
		Lsp.CompletionContextPayload context = CompletionContextConversion.ToProtocol(CompletionContext.CreateCustom("Snippet"));

		Assert.AreEqual(Lsp.CompletionTriggerKind.Invoked, context.TriggerKind);
		Assert.IsNull(context.TriggerCharacter);
	}
}
