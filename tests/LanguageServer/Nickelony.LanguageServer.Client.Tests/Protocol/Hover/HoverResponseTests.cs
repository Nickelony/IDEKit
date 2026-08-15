using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class HoverResponseTests
{
	[TestMethod]
	public void Deserialize_StringContents_ParsesPlainText()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{ "contents": "Plain hover text" }
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Contents);
		Assert.AreEqual(JsonValueKind.String, response.Contents!.Value.ValueKind);
		Assert.AreEqual("Plain hover text", response.Contents.Value.GetString());
	}

	[TestMethod]
	public void Deserialize_MissingContents_LeavesNullContents()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{ }
			""");

		Assert.IsNotNull(response);
		Assert.IsNull(response.Contents);
	}

	[TestMethod]
	public void Deserialize_Range_RoundTripsAndAbsentRangeStaysNull()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{
			  "contents": "text",
			  "range": {
			    "start": { "line": 3, "character": 2 },
			    "end": { "line": 3, "character": 9 }
			  }
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Range);
		Assert.AreEqual(3, response.Range!.Value.Start.Line);
		Assert.AreEqual(2, response.Range.Value.Start.Character);
		Assert.AreEqual(9, response.Range.Value.End.Character);

		HoverResponse? withoutRange = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{ "contents": "text" }
			""");

		Assert.IsNotNull(withoutRange);
		Assert.IsNull(withoutRange.Range);
	}
}
