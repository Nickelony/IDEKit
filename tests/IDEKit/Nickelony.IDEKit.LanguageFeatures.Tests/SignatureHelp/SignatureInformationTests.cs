using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class SignatureInformationTests
{
	[TestMethod]
	public void Signature_NotSpecifiedSelection_ReportsNull()
	{
		// The not-specified construction default means "this signature does not override the
		// selection"; the public property reports an actual index or null rather than a state.
		var signature = new SignatureInformation("f(a)", parameters: [new ParameterInformation("a")]);
		Assert.IsNull(signature.ActiveParameterIndex);
	}

	[TestMethod]
	public void Signature_SpecifiedIndexWithoutParameters_ReportsNull()
	{
		var signature = new SignatureInformation("f()", activeParameter: SignatureActiveParameterSelection.At(3));
		Assert.IsNull(signature.ActiveParameterIndex);
	}

	[TestMethod]
	public void Signature_SpecifiedIndexAboveRange_FallsBackToFirstParameter()
	{
		var signature = new SignatureInformation(
			"f(a, b)",
			activeParameter: SignatureActiveParameterSelection.At(5),
			parameters: [new ParameterInformation("a"), new ParameterInformation("b")]
		);

		Assert.AreEqual(0, signature.ActiveParameterIndex);
	}

	[TestMethod]
	public void Signature_Label_IsStoredAsSupplied()
	{
		// Labels are primary display text: editors render them verbatim and match parameter labels
		// into the signature label, so they are deliberately not normalized.
		var signature = new SignatureInformation("  print()  ");
		Assert.AreEqual("  print()  ", signature.Label);
	}

	[TestMethod]
	public void Signature_BlankLabel_Throws() => Assert.ThrowsExactly<ArgumentException>(() => new SignatureInformation("   "));

	[TestMethod]
	public void Signature_WhitespaceDocumentation_IsNormalizedToAbsent()
	{
		var signature = new SignatureInformation("print()", documentation: "   ");
		Assert.IsNull(signature.Documentation);
	}

	[TestMethod]
	public void Signature_PaddedDocumentation_IsTrimmed()
	{
		var signature = new SignatureInformation("print()", documentation: "  Prints a value.  ");
		Assert.AreEqual("Prints a value.", signature.Documentation);
	}

	[TestMethod]
	public void Signature_ParameterSnapshot_IsOwned()
	{
		var parameters = new List<ParameterInformation> { new("a") };
		var signature = new SignatureInformation("f(a)", parameters: parameters);

		parameters.Add(new ParameterInformation("b"));

		Assert.HasCount(1, signature.Parameters);
		Assert.AreEqual("a", signature.Parameters[0].Label);
	}

	[TestMethod]
	public void Signature_NullParameterElement_Throws()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			new SignatureInformation("f(a, b)", parameters: [new ParameterInformation("a"), null!])
		);

		Assert.AreEqual("parameters", exception.ParamName);
		Assert.Contains("index 1", exception.Message, "The error should identify the offending element.");
	}
}
