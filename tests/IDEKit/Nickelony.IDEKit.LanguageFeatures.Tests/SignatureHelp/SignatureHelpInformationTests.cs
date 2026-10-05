using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class SignatureHelpInformationTests
{
	[TestMethod]
	public void Info_NullSignatures_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new SignatureHelpInformation(null!));

	[TestMethod]
	public void Info_EmptySignatureList_Throws() => Assert.ThrowsExactly<ArgumentException>(() => new SignatureHelpInformation([]));

	[TestMethod]
	public void Info_SignatureSnapshot_IsOwned()
	{
		var signatures = new List<SignatureInformation> { new("f(a)") };
		var info = new SignatureHelpInformation(signatures);

		signatures.Add(new SignatureInformation("g(b)"));

		Assert.HasCount(1, info.Signatures);
		Assert.AreEqual("f(a)", info.Signatures[0].Label);
	}

	[TestMethod]
	public void Info_NegativeActiveSignatureIndex_ClampsToFirstSignature()
	{
		var info = new SignatureHelpInformation([new SignatureInformation("f(a)"), new SignatureInformation("g(b)")], activeSignatureIndex: -3);

		Assert.AreEqual(0, info.ActiveSignatureIndex);
		Assert.AreEqual("f(a)", info.ActiveSignature.Label);
	}

	[TestMethod]
	public void Info_ActiveSignatureIndexAboveRange_FallsBackToFirstSignature()
	{
		var info = new SignatureHelpInformation([new SignatureInformation("f(a)"), new SignatureInformation("g(b)")], activeSignatureIndex: 5);

		Assert.AreEqual(0, info.ActiveSignatureIndex);
		Assert.AreEqual("f(a)", info.ActiveSignature.Label);
	}

	[TestMethod]
	public void Info_DefaultParameterIndexWithoutParameters_IsNull()
	{
		var info = new SignatureHelpInformation([new SignatureInformation("print()")]);

		Assert.IsEmpty(info.ActiveSignature.Parameters);
		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_DefaultParameterIndexWithParameters_ClampsToFirstParameter()
	{
		// A signature with parameters always has an active parameter, so the not-specified default selects the first.
		var info = new SignatureHelpInformation([new SignatureInformation("print(value)", parameters: [new ParameterInformation("value")])]);
		Assert.AreEqual(0, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_NoneSelection_IsPreserved()
	{
		// An explicit None selection represents the LSP 3.18 "no active parameter" state.
		var info = new SignatureHelpInformation(
			[new SignatureInformation("print(value)", parameters: [new ParameterInformation("value")])],
			activeParameter: SignatureActiveParameterSelection.None
		);

		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_NoneSelectionWithoutParameters_IsPreserved()
	{
		var info = new SignatureHelpInformation([new SignatureInformation("print()")], activeParameter: SignatureActiveParameterSelection.None);
		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_PayloadIndexAboveRange_FallsBackToFirstParameter()
	{
		var info = new SignatureHelpInformation(
			[
				new SignatureInformation(
					"f(a, b, c)",
					parameters: [new ParameterInformation("a"), new ParameterInformation("b"), new ParameterInformation("c")]
				),
			],

			activeParameter: SignatureActiveParameterSelection.At(5)
		);

		Assert.AreEqual(0, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_ValidPayloadIndex_IsPreserved()
	{
		var info = new SignatureHelpInformation(
			[new SignatureInformation("f(a, b)", parameters: [new ParameterInformation("a"), new ParameterInformation("b")])],
			activeParameter: SignatureActiveParameterSelection.At(1)
		);

		Assert.AreEqual(1, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_PayloadIndexWithEmptyParameterList_IsNull()
	{
		var info = new SignatureHelpInformation([new SignatureInformation("f()")], activeParameter: SignatureActiveParameterSelection.At(3));
		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_SignatureIndex_OverridesPayloadIndex()
	{
		// LSP 3.16+ resolves the signature-level value in place of the payload-level value.
		var info = new SignatureHelpInformation(
			[
				new SignatureInformation(
					"f(a, b)",
					activeParameter: SignatureActiveParameterSelection.At(1),
					parameters: [new ParameterInformation("a"), new ParameterInformation("b")]
				),
			],

			activeParameter: SignatureActiveParameterSelection.At(2)
		);

		Assert.AreEqual(1, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_SignatureNoneSelection_OverridesPayloadSelection()
	{
		// A signature-level None selection represents the LSP 3.18 "no active parameter" state and
		// suppresses the payload-level selection, mirroring the precedence of a signature-level value.
		var info = new SignatureHelpInformation(
			[
				new SignatureInformation(
					"f(a, b)",
					activeParameter: SignatureActiveParameterSelection.None,
					parameters: [new ParameterInformation("a"), new ParameterInformation("b")]
				),
			],

			activeParameter: SignatureActiveParameterSelection.At(1)
		);

		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_SignatureIndexAboveRange_FallsBackToFirstParameter()
	{
		var info = new SignatureHelpInformation([
			new SignatureInformation(
				"f(a, b, c)",
				activeParameter: SignatureActiveParameterSelection.At(5),
				parameters: [new ParameterInformation("a"), new ParameterInformation("b"), new ParameterInformation("c")]
			),
		]);

		Assert.AreEqual(0, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_SignatureIndexWithoutParameters_IsNull()
	{
		var info = new SignatureHelpInformation([new SignatureInformation("f()", activeParameter: SignatureActiveParameterSelection.At(3))]);
		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_NullSignatureElement_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => new SignatureHelpInformation([new SignatureInformation("f(a)"), null!]));

	[TestMethod]
	public void WithActiveSignature_ResolvesParameterForSelectedSignature()
	{
		var singleParameter = new SignatureInformation("f(a)", parameters: [new ParameterInformation("a")]);

		var twoParameters = new SignatureInformation("f(a, b)", parameters: [new ParameterInformation("a"), new ParameterInformation("b")]);

		var info = new SignatureHelpInformation([singleParameter, twoParameters], activeParameter: SignatureActiveParameterSelection.At(1));

		// The payload index is out of range for the single-parameter signature (first parameter) and
		// in range for the two-parameter signature, so the effective index is resolved per signature.
		Assert.AreEqual(0, info.ActiveParameterIndex);

		SignatureHelpInformation secondSignature = info.WithActiveSignature(1);

		Assert.AreEqual(1, secondSignature.ActiveSignatureIndex);
		Assert.AreEqual(1, secondSignature.ActiveParameterIndex);
		Assert.AreSame(twoParameters, secondSignature.ActiveSignature);
	}

	[TestMethod]
	public void WithActiveSignature_DoesNotMutateTheOriginalPayload()
	{
		var first = new SignatureInformation("f(a, b)", parameters: [new ParameterInformation("a"), new ParameterInformation("b")]);
		var second = new SignatureInformation("g(c)", parameters: [new ParameterInformation("c")]);
		var info = new SignatureHelpInformation([first, second], activeSignatureIndex: 0, activeParameter: SignatureActiveParameterSelection.At(1));

		SignatureHelpInformation updated = info.WithActiveSignature(1);

		Assert.AreNotSame(info, updated);
		Assert.AreEqual(0, info.ActiveSignatureIndex);
		Assert.AreSame(first, info.ActiveSignature);
		Assert.AreEqual(1, info.ActiveParameterIndex);
		Assert.AreEqual(1, updated.ActiveSignatureIndex);
		Assert.AreSame(second, updated.ActiveSignature);
	}

	[TestMethod]
	public void WithActiveSignature_FallsBackToFirstForOutOfRangeIndex()
	{
		var info = new SignatureHelpInformation([new SignatureInformation("f(a)"), new SignatureInformation("g(b)")]);

		Assert.AreEqual(0, info.WithActiveSignature(9).ActiveSignatureIndex);
		Assert.AreEqual(0, info.WithActiveSignature(-2).ActiveSignatureIndex);
		Assert.AreEqual(1, info.WithActiveSignature(1).ActiveSignatureIndex);
	}
}
