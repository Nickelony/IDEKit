namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class SignatureHelpTriggerKindConversionTests
{
	private static readonly (SignatureHelpTriggerKind Model, Lsp.SignatureHelpTriggerKind Protocol)[] s_pairs =
	[
		(SignatureHelpTriggerKind.Invoked, Lsp.SignatureHelpTriggerKind.Invoked),
		(SignatureHelpTriggerKind.TriggerCharacter, Lsp.SignatureHelpTriggerKind.TriggerCharacter),
		(SignatureHelpTriggerKind.ContentChange, Lsp.SignatureHelpTriggerKind.ContentChange),
	];

	[TestMethod]
	public void ToProtocol_MapsEveryMemberOfBothEnums()
	{
		Assert.AreSequenceEqual(
			expected: [.. s_pairs.Select(pair => pair.Model)],
			actual: Enum.GetValues<SignatureHelpTriggerKind>(),
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "A model member without a row is unmapped."
		);

		Assert.AreSequenceEqual(
			expected: [.. s_pairs.Select(pair => pair.Protocol)],
			actual: Enum.GetValues<Lsp.SignatureHelpTriggerKind>(),
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "A protocol member without a row is never produced."
		);

		foreach ((SignatureHelpTriggerKind model, Lsp.SignatureHelpTriggerKind protocol) in s_pairs)
			Assert.AreEqual(protocol, SignatureHelpTriggerKindConversion.ToProtocol(model), $"{model} must map to {protocol}.");
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(4)]
	[DataRow(-1)]
	public void ToProtocol_UndefinedValue_IsSentAsInvoked(int value) =>
		Assert.AreEqual(Lsp.SignatureHelpTriggerKind.Invoked, SignatureHelpTriggerKindConversion.ToProtocol((SignatureHelpTriggerKind)value));
}
