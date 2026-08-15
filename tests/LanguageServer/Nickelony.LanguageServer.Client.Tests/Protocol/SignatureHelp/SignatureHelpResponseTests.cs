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
			""");

		Assert.IsNotNull(response);
		Assert.AreEqual(0, response.ActiveSignature);
		Assert.AreEqual(JsonValueKind.Number, response.ActiveParameter.ValueKind);
		Assert.AreEqual(1, response.ActiveParameter.GetInt32());
		Assert.IsNotNull(response.Signatures);

		SignatureHelpSignaturePayload signature = response.Signatures![0];

		Assert.AreEqual(1, response.Signatures.Count);
		Assert.AreEqual("print(value)", signature.Label);
		Assert.AreEqual(JsonValueKind.Number, signature.ActiveParameter.ValueKind);
		Assert.AreEqual(1, signature.ActiveParameter.GetInt32());
		Assert.IsNotNull(signature.Documentation);
		Assert.IsNotNull(signature.Parameters);

		IReadOnlyList<SignatureHelpParameterPayload> parameters = signature.Parameters!;

		Assert.AreEqual(2, parameters.Count);
		Assert.AreEqual(JsonValueKind.Array, parameters[0].Label.ValueKind);
		Assert.AreEqual(JsonValueKind.String, parameters[1].Label.ValueKind);
		Assert.AreEqual("The value to print.", parameters[1].Documentation?.GetString());
	}

	[TestMethod]
	public void Deserialize_MinimalPayload_LeavesOptionalMembersNull()
	{
		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>(
			"""
			{ }
			""");

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
			""");

		// An explicit null is the LSP 3.17 "no active parameter" state and must stay distinguishable
		// from an absent property, which the JsonElement value kind preserves.
		Assert.IsNotNull(response);
		Assert.AreEqual(JsonValueKind.Null, response.ActiveParameter.ValueKind);
		Assert.IsNotNull(response.Signatures);
		Assert.AreEqual(JsonValueKind.Null, response.Signatures![0].ActiveParameter.ValueKind);
	}
}
