namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DiagnosticSeverityConversionTests
{
	private static readonly (Lsp.DiagnosticSeverity Protocol, DiagnosticSeverity Model)[] s_pairs =
	[
		(Lsp.DiagnosticSeverity.Error, DiagnosticSeverity.Error),
		(Lsp.DiagnosticSeverity.Warning, DiagnosticSeverity.Warning),
		(Lsp.DiagnosticSeverity.Information, DiagnosticSeverity.Information),
		(Lsp.DiagnosticSeverity.Hint, DiagnosticSeverity.Hint),
	];

	[TestMethod]
	public void FromProtocol_MapsEveryMemberOfBothEnums()
	{
		Assert.AreSequenceEqual(
			expected: [.. s_pairs.Select(pair => pair.Protocol)],
			actual: Enum.GetValues<Lsp.DiagnosticSeverity>(),
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "A protocol member without a row is unmapped."
		);

		// DiagnosticSeverity.None is the model's "no severity" value, which no protocol severity produces.
		Assert.AreSequenceEqual(
			expected: [.. s_pairs.Select(pair => pair.Model), DiagnosticSeverity.None],
			actual: Enum.GetValues<DiagnosticSeverity>(),
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "A model member without a row is never produced."
		);

		foreach ((Lsp.DiagnosticSeverity protocol, DiagnosticSeverity model) in s_pairs)
			Assert.AreEqual(model, DiagnosticSeverityConversion.FromProtocol(protocol), $"{protocol} must map to {model}.");
	}

	[TestMethod]
	public void FromProtocol_OmittedSeverity_IsAnError() =>
		Assert.AreEqual(DiagnosticSeverity.Error, DiagnosticSeverityConversion.FromProtocol(null));

	[TestMethod]
	public void FromProtocol_UndefinedValue_IsAWarning() =>
		Assert.AreEqual(DiagnosticSeverity.Warning, DiagnosticSeverityConversion.FromProtocol((Lsp.DiagnosticSeverity)99));
}
