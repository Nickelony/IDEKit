using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SignatureHelpResponseTests
{
	[TestMethod]
	public void Deserialize_FullPayload_ParsesSignaturesAndParameters()
	{
		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>(
			"""
			{
			  "activeSignature": 0,
			  "activeParameter": 1,
			  "signatures": [
			    {
			      "label": "print(value)",
			      "documentation": { "kind": "markdown", "value": "Prints a value." },
			      "activeParameter": 1,
			      "parameters": [
			        { "label": [6, 11] },
			        { "label": "value", "documentation": "The value to print." }
			      ]
			    }
			  ]
			}
			"""
		);

		Assert.IsNotNull(response);
		Assert.AreEqual(0, response.ActiveSignature);
		Assert.AreEqual(JsonValueKind.Number, response.ActiveParameter.ValueKind);
		Assert.AreEqual(1, response.ActiveParameter.GetInt32());
		Assert.IsNotNull(response.Signatures);

		SignatureInformationPayload signature = response.Signatures![0];

		Assert.HasCount(1, response.Signatures);
		Assert.AreEqual("print(value)", signature.Label);
		Assert.AreEqual(JsonValueKind.Number, signature.ActiveParameter.ValueKind);
		Assert.AreEqual(1, signature.ActiveParameter.GetInt32());
		Assert.AreEqual(new MarkupContentPayload { Kind = MarkupKind.Markdown, Value = "Prints a value." }, signature.Documentation);
		Assert.IsNotNull(signature.Parameters);

		IReadOnlyList<ParameterInformationPayload> parameters = signature.Parameters!;

		Assert.HasCount(2, parameters);
		Assert.AreEqual(SignatureHelpParameterLabelPayload.FromOffsets(6, 11), parameters[0].Label);
		Assert.AreEqual(SignatureHelpParameterLabelPayload.FromText("value"), parameters[1].Label);
		Assert.AreEqual(new MarkupContentPayload { Kind = MarkupKind.PlainText, Value = "The value to print." }, parameters[1].Documentation);
	}

	[TestMethod]
	public void Deserialize_MinimalPayload_LeavesOptionalMembersNull()
	{
		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>(
			"""
			{ }
			"""
		);

		Assert.IsNotNull(response);
		Assert.IsNull(response.ActiveSignature);
		Assert.AreEqual(JsonValueKind.Undefined, response.ActiveParameter.ValueKind);
		Assert.IsNull(response.Signatures);
	}

	[TestMethod]
	public void Deserialize_ExplicitNullActiveParameter_ReportsTheNullState()
	{
		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>(
			"""
			{
			  "activeParameter": null,
			  "signatures": [
			    {
			      "label": "print(value)",
			      "activeParameter": null
			    }
			  ]
			}
			"""
		);

		// An explicit null is the LSP 3.18 "no active parameter" state and must stay distinguishable
		// from an absent property, which the JsonElement value kind preserves.
		Assert.IsNotNull(response);
		Assert.AreEqual(JsonValueKind.Null, response.ActiveParameter.ValueKind);
		Assert.IsNotNull(response.Signatures);
		Assert.AreEqual(JsonValueKind.Null, response.Signatures![0].ActiveParameter.ValueKind);
	}
}
