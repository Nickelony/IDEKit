using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class HoverResponseJsonConverterTests
{
	[TestMethod]
	public void Deserialize_MalformedRange_KeepsContentsAndDropsRange()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		HoverResponse? response = DeserializeHoverResponse(
			"""
			{
			  "contents": "Plain hover text",
			  "range": { "start": { "line": 3 } }
			}
			""",
			logScope.CreateLogger<HoverResponseJsonConverter>());

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Contents);
		Assert.AreEqual("Plain hover text", response.Contents!.Value.GetString());
		Assert.IsNull(response.Range);

		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_RangeWithMissingEnd_DegradesToEmptyRange()
	{
		HoverResponse? response = DeserializeHoverResponse(
			"""
			{
			  "contents": "Plain hover text",
			  "range": { "start": { "line": 2, "character": 4 } }
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Range);
		Assert.AreEqual(new ProtocolPosition(2, 4), response.Range!.Value.Start);
		Assert.AreEqual(new ProtocolPosition(2, 4), response.Range.Value.End);
	}

	[TestMethod]
	public void Deserialize_NonObjectRange_DropsRangeWithoutFailing()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		HoverResponse? response = DeserializeHoverResponse(
			"""
			{
			  "contents": "Plain hover text",
			  "range": 5
			}
			""",
			logScope.CreateLogger<HoverResponseJsonConverter>());

		Assert.IsNotNull(response);
		Assert.AreEqual("Plain hover text", response.Contents!.Value.GetString());
		Assert.IsNull(response.Range);
	}

	[TestMethod]
	public void Deserialize_ExplicitNullRange_LeavesNullRange()
	{
		HoverResponse? response = DeserializeHoverResponse(
			"""
			{
			  "contents": "Plain hover text",
			  "range": null
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNull(response.Range);
	}

	[TestMethod]
	public void Deserialize_NonObjectPayload_ProducesEmptyResponse()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		HoverResponse? response = DeserializeHoverResponse("[ 1 ]", logScope.CreateLogger<HoverResponseJsonConverter>());

		Assert.IsNotNull(response);
		Assert.IsNull(response.Contents);
		Assert.IsNull(response.Range);

		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_NullPayload_ProducesNullResponse()
	{
		HoverResponse? response = DeserializeHoverResponse("null");

		Assert.IsNull(response);
	}

	[TestMethod]
	public void Serialize_AbsentMembers_OmitsThemInsteadOfWritingNull()
	{
		string json = JsonSerializer.Serialize(new HoverResponse());

		Assert.AreEqual("{}", json);
	}

	[TestMethod]
	public void Serialize_PresentMembers_RoundTrip()
	{
		var response = new HoverResponse
		{
			Contents = JsonSerializer.SerializeToElement("Plain hover text"),
			Range = new ProtocolRangePayload(new ProtocolPosition(1, 2), new ProtocolPosition(1, 5))
		};

		string json = JsonSerializer.Serialize(response);

		Assert.AreEqual(
			"""{"contents":"Plain hover text","range":{"start":{"line":1,"character":2},"end":{"line":1,"character":5}}}""",
			json);
	}

	private static HoverResponse? DeserializeHoverResponse(string json, ILogger? logger = null)
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new HoverResponseJsonConverter(logger));
		return JsonSerializer.Deserialize<HoverResponse>(json, options);
	}
}
