using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class DefinitionResponseTests
{
	[TestMethod]
	public void Deserialize_PreservesLocationLinkOriginSelectionRange()
	{
		string filePath = TestPaths.Script("first.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(
			new
			{
				targetUri = new Uri(filePath).AbsoluteUri,
				targetRange = new { start = new { line = 2, character = 4 }, end = new { line = 2, character = 10 } },
				targetSelectionRange = new { start = new { line = 2, character = 4 }, end = new { line = 2, character = 6 } },
				originSelectionRange = new { start = new { line = 1, character = 8 }, end = new { line = 1, character = 12 } },
			}
		);

		Assert.HasCount(1, response.Targets);
		Assert.AreEqual(Range(startLine: 1, startCharacter: 8, endLine: 1, endCharacter: 12), response.Targets[0].OriginSelectionRange);

		// The origin range must survive serialization instead of being dropped.
		string serialized = JsonSerializer.Serialize(response);

		Assert.Contains("\"originSelectionRange\"", serialized);
	}

	[TestMethod]
	public void Deserialize_PreservesAllTargetsFromMultiLocationResponse()
	{
		string firstPath = TestPaths.Script("first.ext");
		string secondPath = TestPaths.Script("second.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(
			new object[]
			{
				new
				{
					uri = new Uri(firstPath).AbsoluteUri,
					range = new { start = new { line = 2, character = 4 }, end = new { line = 2, character = 10 } },
				},

				new
				{
					uri = new Uri(secondPath).AbsoluteUri,
					range = new { start = new { line = 8, character = 1 }, end = new { line = 8, character = 5 } },
				},
			}
		);

		Assert.HasCount(2, response.Targets);
		Assert.AreEqual(new Uri(firstPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(startLine: 2, startCharacter: 4, endLine: 2, endCharacter: 10), response.Targets[0].TargetRange);
		Assert.IsNull(response.Targets[0].SelectionRange);
		Assert.AreEqual(new Uri(secondPath).AbsoluteUri, response.Targets[1].Uri);
		Assert.AreEqual(Range(startLine: 8, startCharacter: 1, endLine: 8, endCharacter: 5), response.Targets[1].TargetRange);
		Assert.IsNull(response.Targets[1].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_IgnoresMalformedTargetsAndKeepsUsableEntries()
	{
		string validPath = TestPaths.Script("valid.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(
			new object[]
			{
				new { uri = "not a uri", range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } } },

				new
				{
					uri = new Uri(validPath).AbsoluteUri,
					range = new { start = new { line = 3, character = 2 }, end = new { line = 3, character = 7 } },
				},
			}
		);

		Assert.HasCount(1, response.Targets);
		Assert.AreEqual(new Uri(validPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(startLine: 3, startCharacter: 2, endLine: 3, endCharacter: 7), response.Targets[0].TargetRange);
		Assert.IsNull(response.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_FallsBackToTargetRangeWhenSelectionRangeIsMalformed()
	{
		string targetPath = TestPaths.Script("linked.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(
			new
			{
				targetUri = new Uri(targetPath).AbsoluteUri,
				targetSelectionRange = new { start = new { line = -1, character = 2 }, end = new { line = 4, character = 9 } },
				targetRange = new { start = new { line = 6, character = 3 }, end = new { line = 6, character = 8 } },
			}
		);

		Assert.HasCount(1, response.Targets);
		Assert.AreEqual(new Uri(targetPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(startLine: 6, startCharacter: 3, endLine: 6, endCharacter: 8), response.Targets[0].TargetRange);
		Assert.IsNull(response.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_LocationLinkKeepsDistinctTargetAndSelectionRanges()
	{
		string linkedPath = TestPaths.Script("linked.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(
			new
			{
				targetUri = new Uri(linkedPath).AbsoluteUri,
				targetRange = new { start = new { line = 2, character = 0 }, end = new { line = 8, character = 3 } },
				targetSelectionRange = new { start = new { line = 2, character = 6 }, end = new { line = 2, character = 11 } },
			}
		);

		Assert.HasCount(1, response.Targets);
		Assert.AreEqual(new Uri(linkedPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(startLine: 2, startCharacter: 0, endLine: 8, endCharacter: 3), response.Targets[0].TargetRange);
		Assert.AreEqual(Range(startLine: 2, startCharacter: 6, endLine: 2, endCharacter: 11), response.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_RangeWithoutEndPosition_DegradesToEmptyRange()
	{
		string validPath = TestPaths.Script("valid.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(
			new { uri = new Uri(validPath).AbsoluteUri, range = new { start = new { line = 1, character = 2 } } }
		);

		Assert.HasCount(1, response.Targets);
		Assert.AreEqual(Range(startLine: 1, startCharacter: 2, endLine: 1, endCharacter: 2), response.Targets[0].TargetRange);
		Assert.IsNull(response.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Serialize_WritesRoundTrippableLocationArray()
	{
		string firstUri = new Uri(TestPaths.Script("first.ext")).AbsoluteUri;
		string secondUri = new Uri(TestPaths.Script("second.ext")).AbsoluteUri;

		var response = new DefinitionResponse([
			new DefinitionTargetPayload(firstUri, Range(startLine: 3, startCharacter: 5, endLine: 3, endCharacter: 11)),
			new DefinitionTargetPayload(secondUri, Range(startLine: 9, startCharacter: 2, endLine: 9, endCharacter: 6)),
		]);

		string json = JsonSerializer.Serialize(response);

		DefinitionResponse roundTripped =
			JsonSerializer.Deserialize<DefinitionResponse>(json)
			?? throw new AssertFailedException("Serialized definition response should deserialize successfully.");

		Assert.HasCount(2, roundTripped.Targets);
		Assert.AreEqual(firstUri, roundTripped.Targets[0].Uri);
		Assert.AreEqual(Range(startLine: 3, startCharacter: 5, endLine: 3, endCharacter: 11), roundTripped.Targets[0].TargetRange);
		Assert.IsNull(roundTripped.Targets[0].SelectionRange);
		Assert.AreEqual(secondUri, roundTripped.Targets[1].Uri);
		Assert.AreEqual(Range(startLine: 9, startCharacter: 2, endLine: 9, endCharacter: 6), roundTripped.Targets[1].TargetRange);
		Assert.IsNull(roundTripped.Targets[1].SelectionRange);
	}

	[TestMethod]
	public void Serialize_LocationLinkRoundTripsTargetAndSelectionRanges()
	{
		string uri = new Uri(TestPaths.Script("linked.ext")).AbsoluteUri;

		RangePayload targetRange = Range(startLine: 3, startCharacter: 1, endLine: 9, endCharacter: 4);
		RangePayload selectionRange = Range(startLine: 3, startCharacter: 7, endLine: 3, endCharacter: 12);

		var response = new DefinitionResponse([new DefinitionTargetPayload(uri, targetRange, selectionRange)]);

		string json = JsonSerializer.Serialize(response);

		DefinitionResponse roundTripped =
			JsonSerializer.Deserialize<DefinitionResponse>(json)
			?? throw new AssertFailedException("Serialized definition response should deserialize successfully.");

		Assert.HasCount(1, roundTripped.Targets);
		Assert.AreEqual(uri, roundTripped.Targets[0].Uri);
		Assert.AreEqual(targetRange, roundTripped.Targets[0].TargetRange);
		Assert.AreEqual(selectionRange, roundTripped.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_ReturnsEmptyTargetsForSingleMalformedPayload()
	{
		DefinitionResponse response = DeserializeDefinitionResponse(
			new { uri = "not a uri", range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } } }
		);

		Assert.IsEmpty(response.Targets);
		Assert.IsNull(response.FirstTarget);
	}

	[TestMethod]
	public void Deserialize_NullPayload_ProducesNullResponse()
	{
		DefinitionResponse? response = JsonSerializer.Deserialize<DefinitionResponse>("null");
		Assert.IsNull(response);
	}

	[TestMethod]
	public void Deserialize_SkipsTargetWithNonStringUriAndKeepsUsableEntries()
	{
		string validPath = TestPaths.Script("valid.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(
			new object[]
			{
				new { uri = 5, range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } } },

				new
				{
					uri = new Uri(validPath).AbsoluteUri,
					range = new { start = new { line = 1, character = 1 }, end = new { line = 1, character = 2 } },
				},
			}
		);

		Assert.HasCount(1, response.Targets);
		Assert.AreEqual(new Uri(validPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(startLine: 1, startCharacter: 1, endLine: 1, endCharacter: 2), response.Targets[0].TargetRange);
	}

	[TestMethod]
	public void Deserialize_NonObjectEntry_IsSkippedWithWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		DefinitionResponse response = DeserializeDefinitionResponseWithLogger("[ 42 ]", logger);

		Assert.IsEmpty(response.Targets);

		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Deserialize_NullEntry_IsSkippedWithoutWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		DefinitionResponse response = DeserializeDefinitionResponseWithLogger("[ null ]", logger);

		Assert.IsEmpty(response.Targets);

		Assert.DoesNotContain(
			log => log.Contains("definition", StringComparison.OrdinalIgnoreCase),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	private static RangePayload Range(int startLine, int startCharacter, int endLine, int endCharacter) =>
		new(new PositionPayload(startLine, startCharacter), new PositionPayload(endLine, endCharacter));

	private static DefinitionResponse DeserializeDefinitionResponseWithLogger(string json, RecordingLogger logger)
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new DefinitionResponseJsonConverter(logger.CreateLogger<DefinitionResponseJsonConverter>()));

		return JsonSerializer.Deserialize<DefinitionResponse>(json, options)
			?? throw new AssertFailedException("Definition response should deserialize successfully.");
	}

	private static DefinitionResponse DeserializeDefinitionResponse(object payload)
	{
		return JsonSerializer.Deserialize<DefinitionResponse>(JsonSerializer.SerializeToElement(payload))
			?? throw new AssertFailedException("Definition response should deserialize successfully.");
	}
}
