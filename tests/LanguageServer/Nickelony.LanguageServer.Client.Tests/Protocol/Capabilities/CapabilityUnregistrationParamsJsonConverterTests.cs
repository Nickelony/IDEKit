using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class CapabilityUnregistrationParamsJsonConverterTests
{
	[TestMethod]
	public void Read_AcceptsTheHistoricalPropertyName()
	{
		CapabilityUnregistrationParams parameters = JsonSerializer.Deserialize<CapabilityUnregistrationParams>(
			"""
			{
			  "unregisterations": [
			    { "id": "1", "method": "textDocument/rename" }
			  ]
			}
			"""
		);

		Assert.IsNotNull(parameters.Unregistrations);
		Assert.HasCount(1, parameters.Unregistrations);
		Assert.AreEqual("1", parameters.Unregistrations[0].Id);
		Assert.AreEqual("textDocument/rename", parameters.Unregistrations[0].Method);
	}

	[TestMethod]
	public void Read_AcceptsTheCorrectedPropertyName()
	{
		CapabilityUnregistrationParams parameters = JsonSerializer.Deserialize<CapabilityUnregistrationParams>(
			"""
			{
			  "unregistrations": [
			    { "id": "1", "method": "textDocument/rename" }
			  ]
			}
			"""
		);

		Assert.IsNotNull(parameters.Unregistrations);
		Assert.HasCount(1, parameters.Unregistrations);
		Assert.AreEqual("1", parameters.Unregistrations[0].Id);
		Assert.AreEqual("textDocument/rename", parameters.Unregistrations[0].Method);
	}

	[TestMethod]
	public void Write_UsesTheHistoricalPropertyName()
	{
		string json = JsonSerializer.Serialize(new CapabilityUnregistrationParams([new CapabilityUnregistrationPayload("1", "textDocument/rename")]));

		using var document = JsonDocument.Parse(json);

		JsonElement root = document.RootElement;

		Assert.IsTrue(root.TryGetProperty("unregisterations", out JsonElement unregistrations));
		Assert.IsFalse(root.TryGetProperty("unregistrations", out _));
		Assert.AreEqual(JsonValueKind.Array, unregistrations.ValueKind);
		Assert.AreEqual(1, unregistrations.GetArrayLength());
	}

	[TestMethod]
	public void Read_WithANonObjectRoot_DegradesToEmptyWithAWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var options = new JsonSerializerOptions();

		options.Converters.Add(new CapabilityUnregistrationParamsJsonConverter(logger));

		CapabilityUnregistrationParams parameters = JsonSerializer.Deserialize<CapabilityUnregistrationParams>("[]", options);

		Assert.IsNull(parameters.Unregistrations);
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}
}
