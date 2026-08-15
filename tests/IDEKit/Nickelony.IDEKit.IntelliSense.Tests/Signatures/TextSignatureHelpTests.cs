using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.IDEKit.IntelliSense.Tests.Signatures;

[TestClass]
public sealed class TextSignatureHelpTests
{
	[TestMethod]
	public void Request_StoresSnapshotCaretOffsetAndContext()
	{
		var context = new TextSignatureHelpContext(TextSignatureHelpTriggerKind.ContentChange, isRetrigger: true);
		var request = new TextSignatureHelpRequest("print(value", 6, context);

		Assert.AreEqual("print(value", request.DocumentText);
		Assert.AreEqual(6, request.CaretOffset);
		Assert.AreSame(context, request.Context);
	}

	[TestMethod]
	public void Request_WithoutContext_LeavesContextAbsent()
	{
		var request = new TextSignatureHelpRequest("print(value", 6);

		Assert.IsNull(request.Context);
	}

	[TestMethod]
	public void Request_CaretOffsetAtTextLength_IsAccepted()
	{
		var request = new TextSignatureHelpRequest("abc", 3);

		Assert.AreEqual(3, request.CaretOffset);
	}

	[TestMethod]
	public void Request_NegativeCaretOffset_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextSignatureHelpRequest("abc", -1));
	}

	[TestMethod]
	public void Request_CaretOffsetBeyondTextLength_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextSignatureHelpRequest("abc", 4));
	}

	[TestMethod]
	public void Context_DefaultsToExplicitInvocation()
	{
		var context = new TextSignatureHelpContext();

		Assert.AreEqual(TextSignatureHelpTriggerKind.Invoked, context.TriggerKind);
		Assert.IsNull(context.TriggerCharacter);
		Assert.IsFalse(context.IsRetrigger);
		Assert.IsNull(context.ActiveSignatureHelp);
	}

	[TestMethod]
	public void Context_UndefinedTriggerKind_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextSignatureHelpContext((TextSignatureHelpTriggerKind)0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextSignatureHelpContext((TextSignatureHelpTriggerKind)9));
	}

	[TestMethod]
	public void Context_TriggerCharacterForCharacterTrigger_IsRetained()
	{
		var context = new TextSignatureHelpContext(TextSignatureHelpTriggerKind.TriggerCharacter, "(");

		Assert.AreEqual(TextSignatureHelpTriggerKind.TriggerCharacter, context.TriggerKind);
		Assert.AreEqual("(", context.TriggerCharacter);
	}

	[TestMethod]
	public void Context_PaddedTriggerCharacter_IsTrimmed()
	{
		var context = new TextSignatureHelpContext(TextSignatureHelpTriggerKind.TriggerCharacter, "  (  ");

		Assert.AreEqual("(", context.TriggerCharacter);
	}

	[TestMethod]
	public void Context_TriggerCharacterForOtherTriggers_IsDropped()
	{
		// The LSP contract leaves the trigger character undefined for non-character triggers.
		var context = new TextSignatureHelpContext(TextSignatureHelpTriggerKind.ContentChange, "(");

		Assert.IsNull(context.TriggerCharacter);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow(" ")]
	public void Context_BlankTriggerCharacter_IsTreatedAsAbsent(string triggerCharacter)
	{
		var context = new TextSignatureHelpContext(TextSignatureHelpTriggerKind.TriggerCharacter, triggerCharacter);

		Assert.IsNull(context.TriggerCharacter);
	}

	[TestMethod]
	public void Context_Retrigger_CarriesActiveSignatureHelp()
	{
		var activeSignatureHelp = new TextSignatureHelp([new TextSignatureInformation("print(value)")]);
		var context = new TextSignatureHelpContext(
			TextSignatureHelpTriggerKind.ContentChange,
			isRetrigger: true,
			activeSignatureHelp: activeSignatureHelp);

		Assert.IsTrue(context.IsRetrigger);
		Assert.AreSame(activeSignatureHelp, context.ActiveSignatureHelp);
	}

	[TestMethod]
	public void Context_NonRetrigger_CarriesActiveSignatureHelpWhenSupplied()
	{
		// The payload is carried independently of IsRetrigger: a host may restore or re-request
		// signature help without a retrigger.
		var activeSignatureHelp = new TextSignatureHelp([new TextSignatureInformation("print(value)")]);
		var context = new TextSignatureHelpContext(activeSignatureHelp: activeSignatureHelp);

		Assert.IsFalse(context.IsRetrigger);
		Assert.AreSame(activeSignatureHelp, context.ActiveSignatureHelp);
	}

	[TestMethod]
	public void Info_NullSignatures_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextSignatureHelp(null!));
	}

	[TestMethod]
	public void Info_EmptySignatureList_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new TextSignatureHelp([]));
	}

	[TestMethod]
	public void Info_SignatureSnapshot_IsOwned()
	{
		var signatures = new List<TextSignatureInformation> { new("f(a)") };
		var info = new TextSignatureHelp(signatures);

		signatures.Add(new TextSignatureInformation("g(b)"));

		Assert.AreEqual(1, info.Signatures.Count);
		Assert.AreEqual("f(a)", info.Signatures[0].Label);
	}

	[TestMethod]
	public void Info_NegativeActiveSignatureIndex_ClampsToFirstSignature()
	{
		var info = new TextSignatureHelp(
			[new TextSignatureInformation("f(a)"), new TextSignatureInformation("g(b)")],
			activeSignatureIndex: -3);

		Assert.AreEqual(0, info.ActiveSignatureIndex);
		Assert.AreEqual("f(a)", info.ActiveSignature.Label);
	}

	[TestMethod]
	public void Info_ActiveSignatureIndexAboveRange_FallsBackToFirstSignature()
	{
		var info = new TextSignatureHelp(
			[new TextSignatureInformation("f(a)"), new TextSignatureInformation("g(b)")],
			activeSignatureIndex: 5);

		Assert.AreEqual(0, info.ActiveSignatureIndex);
		Assert.AreEqual("f(a)", info.ActiveSignature.Label);
	}

	[TestMethod]
	public void Info_DefaultParameterIndexWithoutParameters_IsNull()
	{
		var info = new TextSignatureHelp([new TextSignatureInformation("print()")]);

		Assert.AreEqual(0, info.ActiveSignature.Parameters.Count);
		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_DefaultParameterIndexWithParameters_ClampsToFirstParameter()
	{
		// A signature with parameters always has an active parameter, so the not-specified default selects the first.
		var info = new TextSignatureHelp(
			[new TextSignatureInformation("print(value)", parameters: [new TextSignatureParameterInfo("value")])]);

		Assert.AreEqual(0, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_NoneSelection_IsPreserved()
	{
		// An explicit None selection represents the LSP 3.18 "no active parameter" state.
		var info = new TextSignatureHelp(
			[new TextSignatureInformation("print(value)", parameters: [new TextSignatureParameterInfo("value")])],
			activeParameter: TextSignatureActiveParameterSelection.None);

		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_NoneSelectionWithoutParameters_IsPreserved()
	{
		var info = new TextSignatureHelp([new TextSignatureInformation("print()")], activeParameter: TextSignatureActiveParameterSelection.None);

		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_PayloadIndexAboveRange_FallsBackToFirstParameter()
	{
		var info = new TextSignatureHelp(
			[
				new TextSignatureInformation(
					"f(a, b, c)",
					parameters: [new TextSignatureParameterInfo("a"), new TextSignatureParameterInfo("b"), new TextSignatureParameterInfo("c")])
			],
			activeParameter: TextSignatureActiveParameterSelection.At(5));

		Assert.AreEqual(0, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_ValidPayloadIndex_IsPreserved()
	{
		var info = new TextSignatureHelp(
			[new TextSignatureInformation("f(a, b)", parameters: [new TextSignatureParameterInfo("a"), new TextSignatureParameterInfo("b")])],
			activeParameter: TextSignatureActiveParameterSelection.At(1));

		Assert.AreEqual(1, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_PayloadIndexWithEmptyParameterList_IsNull()
	{
		var info = new TextSignatureHelp([new TextSignatureInformation("f()")], activeParameter: TextSignatureActiveParameterSelection.At(3));

		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_SignatureIndex_OverridesPayloadIndex()
	{
		// LSP 3.16+ resolves the signature-level value in place of the payload-level value.
		var info = new TextSignatureHelp(
			[new TextSignatureInformation("f(a, b)", activeParameter: TextSignatureActiveParameterSelection.At(1), parameters: [new TextSignatureParameterInfo("a"), new TextSignatureParameterInfo("b")])],
			activeParameter: TextSignatureActiveParameterSelection.At(2));

		Assert.AreEqual(1, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_SignatureNoneSelection_OverridesPayloadSelection()
	{
		// A signature-level None selection represents the LSP 3.18 "no active parameter" state and
		// suppresses the payload-level selection, mirroring the precedence of a signature-level value.
		var info = new TextSignatureHelp(
			[new TextSignatureInformation("f(a, b)", activeParameter: TextSignatureActiveParameterSelection.None, parameters: [new TextSignatureParameterInfo("a"), new TextSignatureParameterInfo("b")])],
			activeParameter: TextSignatureActiveParameterSelection.At(1));

		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_SignatureIndexAboveRange_FallsBackToFirstParameter()
	{
		var info = new TextSignatureHelp(
			[
				new TextSignatureInformation(
					"f(a, b, c)",
					activeParameter: TextSignatureActiveParameterSelection.At(5),
					parameters: [new TextSignatureParameterInfo("a"), new TextSignatureParameterInfo("b"), new TextSignatureParameterInfo("c")])
			]);

		Assert.AreEqual(0, info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_SignatureIndexWithoutParameters_IsNull()
	{
		var info = new TextSignatureHelp([new TextSignatureInformation("f()", activeParameter: TextSignatureActiveParameterSelection.At(3))]);

		Assert.IsNull(info.ActiveParameterIndex);
	}

	[TestMethod]
	public void Info_NullSignatureElement_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new TextSignatureHelp([new TextSignatureInformation("f(a)"), null!]));
	}

	[TestMethod]
	public void Signature_NotSpecifiedSelection_ReportsNull()
	{
		// The not-specified construction default means "this signature does not override the
		// selection"; the public property reports an actual index or null rather than a state.
		var signature = new TextSignatureInformation("f(a)", parameters: [new TextSignatureParameterInfo("a")]);

		Assert.IsNull(signature.ActiveParameterIndex);
	}

	[TestMethod]
	public void Signature_SpecifiedIndexWithoutParameters_ReportsNull()
	{
		var signature = new TextSignatureInformation("f()", activeParameter: TextSignatureActiveParameterSelection.At(3));

		Assert.IsNull(signature.ActiveParameterIndex);
	}

	[TestMethod]
	public void Signature_SpecifiedIndexAboveRange_FallsBackToFirstParameter()
	{
		var signature = new TextSignatureInformation(
			"f(a, b)",
			activeParameter: TextSignatureActiveParameterSelection.At(5),
			parameters: [new TextSignatureParameterInfo("a"), new TextSignatureParameterInfo("b")]);

		Assert.AreEqual(0, signature.ActiveParameterIndex);
	}

	[TestMethod]
	public void WithActiveSignature_ResolvesParameterForSelectedSignature()
	{
		var singleParameter = new TextSignatureInformation("f(a)", parameters: [new TextSignatureParameterInfo("a")]);
		var twoParameters = new TextSignatureInformation(
			"f(a, b)",
			parameters: [new TextSignatureParameterInfo("a"), new TextSignatureParameterInfo("b")]);
		var info = new TextSignatureHelp([singleParameter, twoParameters], activeParameter: TextSignatureActiveParameterSelection.At(1));

		// The payload index is out of range for the single-parameter signature (first parameter) and
		// in range for the two-parameter signature, so the effective index is resolved per signature.
		Assert.AreEqual(0, info.ActiveParameterIndex);

		TextSignatureHelp secondSignature = info.WithActiveSignature(1);

		Assert.AreEqual(1, secondSignature.ActiveSignatureIndex);
		Assert.AreEqual(1, secondSignature.ActiveParameterIndex);
		Assert.AreSame(twoParameters, secondSignature.ActiveSignature);
	}

	[TestMethod]
	public void WithActiveSignature_DoesNotMutateTheOriginalPayload()
	{
		var first = new TextSignatureInformation(
			"f(a, b)",
			parameters: [new TextSignatureParameterInfo("a"), new TextSignatureParameterInfo("b")]);
		var second = new TextSignatureInformation("g(c)", parameters: [new TextSignatureParameterInfo("c")]);
		var info = new TextSignatureHelp([first, second], activeSignatureIndex: 0, activeParameter: TextSignatureActiveParameterSelection.At(1));

		TextSignatureHelp updated = info.WithActiveSignature(1);

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
		var info = new TextSignatureHelp(
			[new TextSignatureInformation("f(a)"), new TextSignatureInformation("g(b)")]);

		Assert.AreEqual(0, info.WithActiveSignature(9).ActiveSignatureIndex);
		Assert.AreEqual(0, info.WithActiveSignature(-2).ActiveSignatureIndex);
		Assert.AreEqual(1, info.WithActiveSignature(1).ActiveSignatureIndex);
	}

	[TestMethod]
	public void Signature_Label_IsStoredAsSupplied()
	{
		// Labels are primary display text: editors render them verbatim and match parameter labels
		// into the signature label, so they are deliberately not normalized.
		var signature = new TextSignatureInformation("  print()  ");

		Assert.AreEqual("  print()  ", signature.Label);
	}

	[TestMethod]
	public void Signature_BlankLabel_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextSignatureInformation("   "));

	[TestMethod]
	public void Signature_WhitespaceDocumentation_IsNormalizedToAbsent()
	{
		var signature = new TextSignatureInformation("print()", documentation: "   ");

		Assert.IsNull(signature.Documentation);
	}

	[TestMethod]
	public void Signature_PaddedDocumentation_IsTrimmed()
	{
		var signature = new TextSignatureInformation("print()", documentation: "  Prints a value.  ");

		Assert.AreEqual("Prints a value.", signature.Documentation);
	}

	[TestMethod]
	public void Signature_ParameterSnapshot_IsOwned()
	{
		var parameters = new List<TextSignatureParameterInfo> { new("a") };
		var signature = new TextSignatureInformation("f(a)", parameters: parameters);

		parameters.Add(new TextSignatureParameterInfo("b"));

		Assert.AreEqual(1, signature.Parameters.Count);
		Assert.AreEqual("a", signature.Parameters[0].Label);
	}

	[TestMethod]
	public void Signature_NullParameterElement_Throws()
	{
		var exception = Assert.ThrowsExactly<ArgumentException>(
			() => new TextSignatureInformation(
				"f(a, b)",
				parameters: [new TextSignatureParameterInfo("a"), null!]));

		Assert.AreEqual("parameters", exception.ParamName);
		StringAssert.Contains(exception.Message, "index 1", "The error should identify the offending element.");
	}

	[TestMethod]
	public void Request_Equality_ComparesAllComponents()
	{
		var context = new TextSignatureHelpContext(TextSignatureHelpTriggerKind.TriggerCharacter, "(");
		var request = new TextSignatureHelpRequest("print(", 6, context);

		Assert.AreEqual(request, new TextSignatureHelpRequest("print(", 6, context));
		Assert.AreNotEqual(request, new TextSignatureHelpRequest("other(", 6, context));
		Assert.AreNotEqual(request, new TextSignatureHelpRequest("print(", 6));
		Assert.AreNotEqual(request, new TextSignatureHelpRequest("print(", 5, context));
	}

	[TestMethod]
	[DataRow(TextSignatureHelpTriggerKind.Invoked, 1)]
	[DataRow(TextSignatureHelpTriggerKind.TriggerCharacter, 2)]
	[DataRow(TextSignatureHelpTriggerKind.ContentChange, 3)]
	public void TriggerKind_MatchesLspNumericContract(TextSignatureHelpTriggerKind kind, int expectedValue)
	{
		Assert.AreEqual(expectedValue, (int)kind);
	}

	[TestMethod]
	public void ParameterInfo_StoresLabelAndOptionalDocumentation()
	{
		var plain = new TextSignatureParameterInfo("value");

		Assert.AreEqual("value", plain.Label);
		Assert.IsNull(plain.Documentation);

		var documented = new TextSignatureParameterInfo("value", "The value to print.");

		Assert.AreEqual("The value to print.", documented.Documentation);
	}

	[TestMethod]
	public void ParameterInfo_WhitespaceDocumentation_IsNormalizedToAbsent()
	{
		var parameter = new TextSignatureParameterInfo("value", "   ");

		Assert.IsNull(parameter.Documentation);
	}

	[TestMethod]
	public void ParameterInfo_PaddedDocumentation_IsTrimmed()
	{
		var parameter = new TextSignatureParameterInfo("value", "  The value to print.  ");

		Assert.AreEqual("The value to print.", parameter.Documentation);
	}
}
