using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class LocationPayloadTests
{
	[TestMethod]
	public void Deserialize_FileRange_ParsesUriAndRange()
	{
		string uri = new Uri(TestPaths.Script("references.ext")).AbsoluteUri;

		LocationPayload response = JsonSerializer.Deserialize<LocationPayload>(
			$$"""
			{
			  "uri": "{{uri}}",
			  "range": {
			    "start": { "line": 2, "character": 4 },
			    "end": { "line": 3, "character": 1 }
			  }
			}
			"""
		);

		Assert.AreEqual(uri, response.Uri);
		Assert.IsNotNull(response.Range);
		Assert.AreEqual(2, response.Range.Value.Start.Line);
		Assert.AreEqual(4, response.Range.Value.Start.Character);
		Assert.AreEqual(3, response.Range.Value.End.Line);
		Assert.AreEqual(1, response.Range.Value.End.Character);
	}

	[TestMethod]
	public void Deserialize_WithoutRange_LeavesRangeNull()
	{
		LocationPayload response = JsonSerializer.Deserialize<LocationPayload>(
			"""
			{ "uri": "file:///workspace/references.ext" }
			"""
		);

		Assert.AreEqual("file:///workspace/references.ext", response.Uri);
		Assert.IsNull(response.Range);
	}

	[TestMethod]
	public void Deserialize_RangeWithNullCoordinates_ThrowsBecausePositionsAreStrict()
	{
		// Positions are required protocol values; a null coordinate is a malformed range and must not
		// degrade into a partially valid one.
		Assert.ThrowsExactly<JsonException>(() =>
			JsonSerializer.Deserialize<LocationPayload>(
				"""
				{
				  "uri": "file:///workspace/references.ext",
				  "range": {
				    "start": { "line": null, "character": null },
				    "end": { "line": 1, "character": 2 }
				  }
				}
				"""
			)
		);
	}
}
