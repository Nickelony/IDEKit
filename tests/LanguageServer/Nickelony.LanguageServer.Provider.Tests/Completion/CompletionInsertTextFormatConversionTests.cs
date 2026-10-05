using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class CompletionInsertTextFormatConversionTests
{
	private static readonly (Lsp.InsertTextFormat Protocol, CompletionInsertTextFormat Model)[] s_pairs =
	[
		(Lsp.InsertTextFormat.PlainText, CompletionInsertTextFormat.PlainText),
		(Lsp.InsertTextFormat.Snippet, CompletionInsertTextFormat.Snippet),
	];

	[TestMethod]
	public void FromProtocol_MapsEveryMemberOfBothEnums()
	{
		Assert.AreSequenceEqual(
			expected: [.. s_pairs.Select(pair => pair.Protocol)],
			actual: Enum.GetValues<Lsp.InsertTextFormat>(),
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "A protocol member without a row is unmapped."
		);

		Assert.AreSequenceEqual(
			expected: [.. s_pairs.Select(pair => pair.Model)],
			actual: Enum.GetValues<CompletionInsertTextFormat>(),
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "A model member without a row is never produced."
		);

		foreach ((Lsp.InsertTextFormat protocol, CompletionInsertTextFormat model) in s_pairs)
			Assert.AreEqual(model, CompletionInsertTextFormatConversion.FromProtocol(protocol), $"{protocol} must map to {model}.");
	}

	[TestMethod]
	public void FromProtocol_OmittedFormat_ReadsAsPlainText() =>
		Assert.AreEqual(CompletionInsertTextFormat.PlainText, CompletionInsertTextFormatConversion.FromProtocol(null));

	[TestMethod]
	public void FromProtocol_UndefinedValue_ReadsAsPlainText() =>
		Assert.AreEqual(CompletionInsertTextFormat.PlainText, CompletionInsertTextFormatConversion.FromProtocol((Lsp.InsertTextFormat)99));
}
