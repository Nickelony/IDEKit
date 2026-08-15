using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class DefinitionResponseTests
{
	[TestMethod]
	public void Deserialize_PreservesLocationLinkOriginSelectionRange()
	{
		string filePath = Path.GetFullPath(@"C:\Workspace\Scripts\first.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(new
		{
			targetUri = new Uri(filePath).AbsoluteUri,
			targetRange = new
			{
				start = new { line = 2, character = 4 },
				end = new { line = 2, character = 10 }
			},
			targetSelectionRange = new
			{
				start = new { line = 2, character = 4 },
				end = new { line = 2, character = 6 }
			},
			originSelectionRange = new
			{
				start = new { line = 1, character = 8 },
				end = new { line = 1, character = 12 }
			}
		});

		Assert.AreEqual(1, response.Targets.Count);
		Assert.AreEqual(Range(1, 8, 1, 12), response.Targets[0].OriginSelectionRange);

		// The origin range must survive serialization instead of being dropped.
		string serialized = JsonSerializer.Serialize(response);

		StringAssert.Contains(serialized, "\"originSelectionRange\"");
	}

	[TestMethod]
	public void Deserialize_PreservesAllTargetsFromMultiLocationResponse()
	{
		string firstPath = Path.GetFullPath(@"C:\Workspace\Scripts\first.ext");
		string secondPath = Path.GetFullPath(@"C:\Workspace\Scripts\second.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(new object[]
		{
			new
			{
				uri = new Uri(firstPath).AbsoluteUri,
				range = new
				{
					start = new { line = 2, character = 4 },
					end = new { line = 2, character = 10 }
				}
			},
			new
			{
				uri = new Uri(secondPath).AbsoluteUri,
				range = new
				{
					start = new { line = 8, character = 1 },
					end = new { line = 8, character = 5 }
				}
			}
		});

		Assert.AreEqual(2, response.Targets.Count);
		Assert.AreEqual(new Uri(firstPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(2, 4, 2, 10), response.Targets[0].TargetRange);
		Assert.IsNull(response.Targets[0].SelectionRange);
		Assert.AreEqual(new Uri(secondPath).AbsoluteUri, response.Targets[1].Uri);
		Assert.AreEqual(Range(8, 1, 8, 5), response.Targets[1].TargetRange);
		Assert.IsNull(response.Targets[1].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_IgnoresMalformedTargetsAndKeepsUsableEntries()
	{
		string validPath = Path.GetFullPath(@"C:\Workspace\Scripts\valid.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(new object[]
		{
			new
			{
				uri = "not a uri",
				range = new
				{
					start = new { line = 0, character = 0 },
					end = new { line = 0, character = 1 }
				}
			},
			new
			{
				uri = new Uri(validPath).AbsoluteUri,
				range = new
				{
					start = new { line = 3, character = 2 },
					end = new { line = 3, character = 7 }
				}
			}
		});

		Assert.AreEqual(1, response.Targets.Count);
		Assert.AreEqual(new Uri(validPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(3, 2, 3, 7), response.Targets[0].TargetRange);
		Assert.IsNull(response.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_FallsBackToTargetRangeWhenSelectionRangeIsMalformed()
	{
		string targetPath = Path.GetFullPath(@"C:\Workspace\Scripts\linked.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(new
		{
			targetUri = new Uri(targetPath).AbsoluteUri,
			targetSelectionRange = new
			{
				start = new { line = -1, character = 2 },
				end = new { line = 4, character = 9 }
			},
			targetRange = new
			{
				start = new { line = 6, character = 3 },
				end = new { line = 6, character = 8 }
			}
		});

		Assert.AreEqual(1, response.Targets.Count);
		Assert.AreEqual(new Uri(targetPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(6, 3, 6, 8), response.Targets[0].TargetRange);
		Assert.IsNull(response.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_LocationLinkKeepsDistinctTargetAndSelectionRanges()
	{
		string linkedPath = Path.GetFullPath(@"C:\Workspace\Scripts\linked.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(new
		{
			targetUri = new Uri(linkedPath).AbsoluteUri,
			targetRange = new
			{
				start = new { line = 2, character = 0 },
				end = new { line = 8, character = 3 }
			},
			targetSelectionRange = new
			{
				start = new { line = 2, character = 6 },
				end = new { line = 2, character = 11 }
			}
		});

		Assert.AreEqual(1, response.Targets.Count);
		Assert.AreEqual(new Uri(linkedPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(2, 0, 8, 3), response.Targets[0].TargetRange);
		Assert.AreEqual(Range(2, 6, 2, 11), response.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_RangeWithoutEndPosition_DegradesToEmptyRange()
	{
		string validPath = Path.GetFullPath(@"C:\Workspace\Scripts\valid.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(new
		{
			uri = new Uri(validPath).AbsoluteUri,
			range = new
			{
				start = new { line = 1, character = 2 }
			}
		});

		Assert.AreEqual(1, response.Targets.Count);
		Assert.AreEqual(Range(1, 2, 1, 2), response.Targets[0].TargetRange);
		Assert.IsNull(response.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Serialize_WritesRoundTrippableLocationArray()
	{
		string firstUri = new Uri(Path.GetFullPath(@"C:\Workspace\Scripts\first.ext")).AbsoluteUri;
		string secondUri = new Uri(Path.GetFullPath(@"C:\Workspace\Scripts\second.ext")).AbsoluteUri;

		var response = new DefinitionResponse(
		[
			new DefinitionTargetPayload(firstUri, Range(3, 5, 3, 11)),
			new DefinitionTargetPayload(secondUri, Range(9, 2, 9, 6))
		]);

		string json = JsonSerializer.Serialize(response);

		DefinitionResponse roundTripped = JsonSerializer.Deserialize<DefinitionResponse>(json)
			?? throw new AssertFailedException("Serialized definition response should deserialize successfully.");

		Assert.AreEqual(2, roundTripped.Targets.Count);
		Assert.AreEqual(firstUri, roundTripped.Targets[0].Uri);
		Assert.AreEqual(Range(3, 5, 3, 11), roundTripped.Targets[0].TargetRange);
		Assert.IsNull(roundTripped.Targets[0].SelectionRange);
		Assert.AreEqual(secondUri, roundTripped.Targets[1].Uri);
		Assert.AreEqual(Range(9, 2, 9, 6), roundTripped.Targets[1].TargetRange);
		Assert.IsNull(roundTripped.Targets[1].SelectionRange);
	}

	[TestMethod]
	public void Serialize_LocationLinkRoundTripsTargetAndSelectionRanges()
	{
		string uri = new Uri(Path.GetFullPath(@"C:\Workspace\Scripts\linked.ext")).AbsoluteUri;
		var targetRange = Range(3, 1, 9, 4);
		var selectionRange = Range(3, 7, 3, 12);

		var response = new DefinitionResponse(
		[
			new DefinitionTargetPayload(uri, targetRange, selectionRange)
		]);

		string json = JsonSerializer.Serialize(response);

		DefinitionResponse roundTripped = JsonSerializer.Deserialize<DefinitionResponse>(json)
			?? throw new AssertFailedException("Serialized definition response should deserialize successfully.");

		Assert.AreEqual(1, roundTripped.Targets.Count);
		Assert.AreEqual(uri, roundTripped.Targets[0].Uri);
		Assert.AreEqual(targetRange, roundTripped.Targets[0].TargetRange);
		Assert.AreEqual(selectionRange, roundTripped.Targets[0].SelectionRange);
	}

	[TestMethod]
	public void Deserialize_ReturnsEmptyTargetsForSingleMalformedPayload()
	{
		DefinitionResponse response = DeserializeDefinitionResponse(new
		{
			uri = "not a uri",
			range = new
			{
				start = new { line = 0, character = 0 },
				end = new { line = 0, character = 1 }
			}
		});

		Assert.AreEqual(0, response.Targets.Count);
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
		string validPath = Path.GetFullPath(@"C:\Workspace\Scripts\valid.ext");

		DefinitionResponse response = DeserializeDefinitionResponse(new object[]
		{
			new
			{
				uri = 5,
				range = new
				{
					start = new { line = 0, character = 0 },
					end = new { line = 0, character = 1 }
				}
			},
			new
			{
				uri = new Uri(validPath).AbsoluteUri,
				range = new
				{
					start = new { line = 1, character = 1 },
					end = new { line = 1, character = 2 }
				}
			}
		});

		Assert.AreEqual(1, response.Targets.Count);
		Assert.AreEqual(new Uri(validPath).AbsoluteUri, response.Targets[0].Uri);
		Assert.AreEqual(Range(1, 1, 1, 2), response.Targets[0].TargetRange);
	}

	[TestMethod]
	public void Deserialize_NonObjectEntry_IsSkippedWithWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		DefinitionResponse response = DeserializeDefinitionResponseWithLogger("[ 42 ]", logScope);

		Assert.AreEqual(0, response.Targets.Count);

		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_NullEntry_IsSkippedWithoutWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		DefinitionResponse response = DeserializeDefinitionResponseWithLogger("[ null ]", logScope);

		Assert.AreEqual(0, response.Targets.Count);

		Assert.IsFalse(logScope.Logs.Any(log => log.Contains("definition", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	private static ProtocolRangePayload Range(int startLine, int startCharacter, int endLine, int endCharacter)
		=> new(new ProtocolPosition(startLine, startCharacter), new ProtocolPosition(endLine, endCharacter));

	private static DefinitionResponse DeserializeDefinitionResponseWithLogger(string json, TestLoggerScope logScope)
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new DefinitionResponseJsonConverter(logScope.CreateLogger<DefinitionResponseJsonConverter>()));

		return JsonSerializer.Deserialize<DefinitionResponse>(json, options)
			?? throw new AssertFailedException("Definition response should deserialize successfully.");
	}

	private static DefinitionResponse DeserializeDefinitionResponse(object payload)
		=> JsonSerializer.Deserialize<DefinitionResponse>(JsonSerializer.SerializeToElement(payload))
			?? throw new AssertFailedException("Definition response should deserialize successfully.");
}
