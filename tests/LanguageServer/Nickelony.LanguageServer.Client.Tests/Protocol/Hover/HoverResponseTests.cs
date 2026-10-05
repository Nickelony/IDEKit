using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class HoverResponseTests
{
	[TestMethod]
	public void Deserialize_StringContents_ParsesAsMarkdown()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{ "contents": "Plain hover text" }
			"""
		);

		Assert.IsNotNull(response);
		Assert.HasCount(1, response.Contents);
		Assert.AreEqual(MarkupKind.Markdown, response.Contents[0].Kind);
		Assert.AreEqual("Plain hover text", response.Contents[0].Value);
	}

	[TestMethod]
	public void Deserialize_MissingContents_LeavesEmptyContents()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{ }
			"""
		);

		Assert.IsNotNull(response);
		Assert.IsEmpty(response.Contents);
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
			"""
		);

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Range);
		Assert.AreEqual(3, response.Range!.Value.Start.Line);
		Assert.AreEqual(2, response.Range.Value.Start.Character);
		Assert.AreEqual(9, response.Range.Value.End.Character);

		HoverResponse? withoutRange = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{ "contents": "text" }
			"""
		);

		Assert.IsNotNull(withoutRange);
		Assert.IsNull(withoutRange.Range);
	}
}
