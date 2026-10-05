namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class MarkupKindConversionTests
{
	private static readonly (Lsp.MarkupKind Protocol, MarkupKind Model)[] s_pairs =
	[
		(Lsp.MarkupKind.PlainText, MarkupKind.PlainText),
		(Lsp.MarkupKind.Markdown, MarkupKind.Markdown),
	];

	[TestMethod]
	public void FromProtocol_MapsEveryMemberOfBothEnums()
	{
		Assert.AreSequenceEqual(
			expected: [.. s_pairs.Select(pair => pair.Protocol)],
			actual: Enum.GetValues<Lsp.MarkupKind>(),
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "A protocol member without a row is unmapped."
		);

		Assert.AreSequenceEqual(
			expected: [.. s_pairs.Select(pair => pair.Model)],
			actual: Enum.GetValues<MarkupKind>(),
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "A model member without a row is never produced."
		);

		foreach ((Lsp.MarkupKind protocol, MarkupKind model) in s_pairs)
			Assert.AreEqual(model, MarkupKindConversion.FromProtocol(protocol), $"{protocol} must map to {model}.");
	}

	[TestMethod]
	public void ToProtocol_MapsEveryMemberOfTheModel()
	{
		foreach ((Lsp.MarkupKind protocol, MarkupKind model) in s_pairs)
			Assert.AreEqual(protocol, MarkupKindConversion.ToProtocol(model), $"{model} must map to {protocol}.");
	}

	[TestMethod]
	public void ToProtocol_UndefinedValue_ReadsAsPlainText() =>
		Assert.AreEqual(Lsp.MarkupKind.PlainText, MarkupKindConversion.ToProtocol((MarkupKind)99));

	[TestMethod]
	public void FromProtocol_UndefinedValue_ReadsAsPlainText() =>
		Assert.AreEqual(MarkupKind.PlainText, MarkupKindConversion.FromProtocol((Lsp.MarkupKind)99));
}
