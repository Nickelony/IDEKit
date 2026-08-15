using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SignatureHelpResponseJsonConverterTests
{
	[TestMethod]
	public void Deserialize_MalformedSignatureListElements_BecomePlaceholdersAndKeepIndexAlignment()
	{
		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>(
			"""
			{
			  "activeSignature": 2,
			  "signatures": [
			    42,
			    null,
			    { "label": "print(value)" },
			    true
			  ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Signatures);

		// Every server array position survives: the active-signature index must keep pointing at the
		// same element it did on the server.
		Assert.AreEqual(4, response.Signatures.Count);
		Assert.IsNull(response.Signatures[0].Label);
		Assert.IsNull(response.Signatures[1].Label);
		Assert.AreEqual("print(value)", response.Signatures[2].Label);
		Assert.IsNull(response.Signatures[3].Label);
	}

	[TestMethod]
	public void Deserialize_MalformedParameterElements_BecomePlaceholdersAndKeepIndexAlignment()
	{
		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>(
			"""
			{
			  "signatures": [
			    {
			      "label": "print(value)",
			      "parameters": [
			        7,
			        null,
			        "text",
			        { "label": "value" }
			      ]
			    }
			  ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Signatures);
		Assert.AreEqual(1, response.Signatures.Count);
		Assert.IsNotNull(response.Signatures[0].Parameters);

		// The active-parameter index must keep pointing at the same parameter the server meant.
		Assert.AreEqual(4, response.Signatures[0].Parameters!.Count);
		Assert.AreEqual(JsonValueKind.Undefined, response.Signatures[0].Parameters![0].Label.ValueKind);
		Assert.AreEqual(JsonValueKind.Undefined, response.Signatures[0].Parameters![1].Label.ValueKind);
		Assert.AreEqual(JsonValueKind.Undefined, response.Signatures[0].Parameters![2].Label.ValueKind);
		Assert.AreEqual("value", response.Signatures[0].Parameters![3].Label.GetString());
	}

	[TestMethod]
	public void Deserialize_UnsupportedSignatureListKind_LeavesSignaturesNull()
	{
		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>(
			"""
			{ "signatures": "not-an-array" }
			""");

		Assert.IsNotNull(response);
		Assert.IsNull(response.Signatures);
	}

	[TestMethod]
	public void Deserialize_NonObjectPayload_ReturnsAnEmptyResponse()
	{
		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>("42");

		Assert.IsNotNull(response);
		Assert.IsNull(response.ActiveSignature);
		Assert.AreEqual(JsonValueKind.Undefined, response.ActiveParameter.ValueKind);
		Assert.IsNull(response.Signatures);
	}

	[TestMethod]
	public void DeserializeSignaturePayload_NonObjectPayload_ProducesAnEmptySignature()
	{
		SignatureHelpSignaturePayload? payload = JsonSerializer.Deserialize<SignatureHelpSignaturePayload>("\"not-an-object\"");

		Assert.IsNotNull(payload);
		Assert.IsNull(payload.Label);
		Assert.IsNull(payload.Documentation);
		Assert.IsNull(payload.Parameters);
		Assert.AreEqual(JsonValueKind.Undefined, payload.ActiveParameter.ValueKind);
	}

	[TestMethod]
	public void Serialize_ThenDeserialize_RoundTripsTheTypedShape()
	{
		var response = new SignatureHelpResponse
		{
			ActiveSignature = 1,
			ActiveParameter = JsonSerializer.SerializeToElement(0),
			Signatures =
			[
				new SignatureHelpSignaturePayload
				{
					Label = "print(value)",
					ActiveParameter = JsonSerializer.SerializeToElement(0),
					Parameters =
					[
						new SignatureHelpParameterPayload { Label = JsonSerializer.SerializeToElement("value") }
					]
				}
			]
		};

		SignatureHelpResponse? roundTrip = JsonSerializer.Deserialize<SignatureHelpResponse>(JsonSerializer.Serialize(response));

		Assert.IsNotNull(roundTrip);
		Assert.AreEqual(1, roundTrip.ActiveSignature);
		Assert.AreEqual(JsonValueKind.Number, roundTrip.ActiveParameter.ValueKind);
		Assert.IsNotNull(roundTrip.Signatures);
		Assert.AreEqual("print(value)", roundTrip.Signatures[0].Label);
		Assert.AreEqual(JsonValueKind.Number, roundTrip.Signatures[0].ActiveParameter.ValueKind);
		Assert.IsNotNull(roundTrip.Signatures[0].Parameters);
		Assert.AreEqual("value", roundTrip.Signatures[0].Parameters![0].Label.GetString());
	}

	[TestMethod]
	public void Serialize_AfterDeserializingParameterPlaceholders_RoundTripsWithoutThrowing()
	{
		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>(
			"""
			{
			  "signatures": [
			    {
			      "label": "print(value)",
			      "parameters": [
			        7,
			        { "label": "value" }
			      ]
			    }
			  ]
			}
			""");

		Assert.IsNotNull(response);

		string serialized = JsonSerializer.Serialize(response);

		using JsonDocument document = JsonDocument.Parse(serialized);
		JsonElement parameters = document.RootElement.GetProperty("signatures")[0].GetProperty("parameters");

		// The placeholder for the malformed element serializes as an empty parameter object instead of
		// throwing on its undefined label.
		Assert.AreEqual(JsonValueKind.Object, parameters[0].ValueKind);
		Assert.IsFalse(parameters[0].TryGetProperty("label", out _));
		Assert.AreEqual("value", parameters[1].GetProperty("label").GetString());
	}

	[TestMethod]
	public void Deserialize_MalformedElements_LogWarnings()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var converter = new SignatureHelpResponseJsonConverter(logScope.CreateLogger<SignatureHelpResponseJsonConverter>());
		var options = new JsonSerializerOptions { Converters = { converter } };

		SignatureHelpResponse? response = JsonSerializer.Deserialize<SignatureHelpResponse>(
			"""
			{ "signatures": [ 42 ] }
			""", options);

		Assert.IsNotNull(response);
		Assert.AreEqual(1, response.Signatures?.Count ?? -1);
		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}
}
