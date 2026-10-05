using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class SignatureHelpContextConversionTests
{
	[TestMethod]
	public void ToProtocol_WithoutAContext_ReturnsNull() => Assert.IsNull(SignatureHelpContextConversion.ToProtocol(null));

	[TestMethod]
	public void ToProtocol_ForATriggerCharacter_CarriesTheKindTheCharacterAndTheRetriggerFlag()
	{
		var context = new SignatureHelpContext(SignatureHelpTriggerKind.TriggerCharacter, triggerCharacter: ",", isRetrigger: true);

		Lsp.SignatureHelpContextPayload? payload = SignatureHelpContextConversion.ToProtocol(context);

		Assert.IsNotNull(payload);
		Assert.AreEqual(Lsp.SignatureHelpTriggerKind.TriggerCharacter, payload.Value.TriggerKind);
		Assert.AreEqual(",", payload.Value.TriggerCharacter);
		Assert.IsTrue(payload.Value.IsRetrigger);
		Assert.IsNull(payload.Value.ActiveSignatureHelp);
	}

	[TestMethod]
	public void ToProtocol_WithTheVisibleSignatureHelp_RebuildsItAsASpecShapedResponse()
	{
		var active = new SignatureHelpInformation(
			[
				new SignatureInformation("f()"),
				new SignatureInformation("f(a, b)", parameters: [new ParameterInformation("a"), new ParameterInformation("b")]),
			],

			activeSignatureIndex: 1,
			activeParameter: SignatureActiveParameterSelection.At(1)
		);

		Lsp.SignatureHelpContextPayload? payload = SignatureHelpContextConversion.ToProtocol(
			new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange, activeSignatureHelp: active)
		);

		Assert.IsNotNull(payload);

		Lsp.SignatureHelpResponse response =
			payload.Value.ActiveSignatureHelp ?? throw new AssertFailedException("The active signature help should be carried.");

		Assert.AreEqual(1, response.ActiveSignature);
		Assert.AreEqual(1, response.ActiveParameter.GetInt32());
		Assert.HasCount(2, response.Signatures ?? []);
		Assert.AreEqual("f(a, b)", response.Signatures?[1].Label);
		Assert.HasCount(2, response.Signatures?[1].Parameters ?? []);
	}

	[TestMethod]
	public void ToProtocol_WithoutAnActiveParameter_WritesAnExplicitJsonNull()
	{
		var active = new SignatureHelpInformation([new SignatureInformation("f()")], activeSignatureIndex: 0, SignatureActiveParameterSelection.None);

		Lsp.SignatureHelpContextPayload? payload = SignatureHelpContextConversion.ToProtocol(
			new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange, activeSignatureHelp: active)
		);

		Assert.IsNotNull(payload);

		Lsp.SignatureHelpResponse response =
			payload.Value.ActiveSignatureHelp ?? throw new AssertFailedException("The active signature help should be carried.");

		Assert.AreEqual(System.Text.Json.JsonValueKind.Null, response.ActiveParameter.ValueKind);
	}
}
