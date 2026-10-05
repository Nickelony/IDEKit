using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class TolerantCollectionJsonConverterTests
{
	private static JsonSerializerOptions CreateOptions(ILogger? logger = null) => ProtocolSerialization.ForMessages(logger ?? NullLogger.Instance);

	[TestMethod]
	public void Deserialize_ReferenceArray_SkipsNullAndMalformedElements()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		LocationPayload[]? response = JsonSerializer.Deserialize<LocationPayload[]>(
			"""
			[
			  null,
			  5,
			  {
			    "uri": 7,
			    "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } }
			  },
			  {
			    "uri": "file:///workspace/test.ext",
			    "range": { "start": { "line": 1, "character": 2 }, "end": { "line": 1, "character": 5 } }
			  }
			]
			""",

			CreateOptions(logger)
		);

		Assert.IsNotNull(response);
		Assert.HasCount(1, response);
		Assert.AreEqual("file:///workspace/test.ext", response[0].Uri);
		Assert.IsNotNull(response[0].Range);
		Assert.AreEqual(1, response[0].Range!.Value.Start.Line);

		Assert.Contains(log => log.Contains("Skipping a null", StringComparison.Ordinal), logger.Logs, string.Join(Environment.NewLine, logger.Logs));

		Assert.Contains(
			log => log.Contains("Skipping a malformed", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void Deserialize_TextEditArray_SkipsNullAndMalformedElements()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		TextEditPayload[]? response = JsonSerializer.Deserialize<TextEditPayload[]>(
			"""
			[
			  null,
			  5,
			  {
			    "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } },
			    "newText": "x"
			  }
			]
			""",

			CreateOptions(logger)
		);

		Assert.IsNotNull(response);
		Assert.HasCount(1, response);
		Assert.AreEqual("x", response[0].NewText);
	}

	[TestMethod]
	public void Deserialize_TextEditArray_DropsEmptyObjectElements()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		TextEditPayload[]? response = JsonSerializer.Deserialize<TextEditPayload[]>(
			"""
			[
			  {},
			  { "range": null, "newText": null },
			  {
			    "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } },
			    "newText": "x"
			  }
			]
			""",

			CreateOptions(logger)
		);

		Assert.IsNotNull(response);
		Assert.HasCount(1, response);
		Assert.AreEqual("x", response[0].NewText);
		Assert.Contains(log => log.Contains("unusable", StringComparison.Ordinal), logger.Logs, string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Deserialize_WorkspaceEditDocumentChanges_SkipsMalformedEditElements()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		WorkspaceEditResponse? response = JsonSerializer.Deserialize<WorkspaceEditResponse>(
			"""
			{
			  "documentChanges": [
			    {
			      "textDocument": { "uri": "file:///workspace/test.ext" },
			      "edits": [
			        null,
			        {
			          "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } },
			          "newText": "x"
			        }
			      ]
			    }
			  ]
			}
			""",

			CreateOptions(logger)
		);

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.DocumentChanges);
		Assert.HasCount(1, response.Value.DocumentChanges);

		IReadOnlyList<TextEditPayload>? edits = response.Value.DocumentChanges[0].Edits;

		Assert.IsNotNull(edits);
		Assert.HasCount(1, edits);
		Assert.AreEqual("x", edits[0].NewText);
	}

	[TestMethod]
	public void Deserialize_WorkspaceEditDocumentChanges_SkipsNullAndMalformedEntries()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		WorkspaceEditResponse? response = JsonSerializer.Deserialize<WorkspaceEditResponse>(
			"""
			{
			  "documentChanges": [
			    null,
			    7,
			    {
			      "kind": "rename",
			      "oldUri": "file:///workspace/old.ext",
			      "newUri": "file:///workspace/new.ext"
			    }
			  ]
			}
			""",

			CreateOptions(logger)
		);

		// A null or malformed structured change must not fault the response; the usable rename survives.
		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.DocumentChanges);
		Assert.HasCount(1, response.Value.DocumentChanges);

		WorkspaceDocumentChangePayload change = response.Value.DocumentChanges[0];

		Assert.AreEqual("rename", change.Kind);
		Assert.AreEqual("file:///workspace/old.ext", change.OldUri);
		Assert.AreEqual("file:///workspace/new.ext", change.NewUri);
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Deserialize_NonArrayRoot_ReturnsEmptyCollectionWithWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		LocationPayload[]? response = JsonSerializer.Deserialize<LocationPayload[]>("5", CreateOptions(logger));

		Assert.IsNotNull(response);
		Assert.IsEmpty(response);
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Deserialize_NonArrayRoot_WhenArrayRootIsRequired_ThrowsJsonException()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		// Edit-bearing collections fail loudly: an empty collection is a legal outcome for them, so degrading a
		// malformed root to "nothing to apply" would turn a broken payload into a successful no-op.
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<TextEditPayload[]>("5", CreateOptions(logger)));
	}

	[TestMethod]
	public void Deserialize_NullRoot_StaysNullWithoutWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		// The serializer handles a JSON null before the converter runs, so a null collection member stays null
		// ("no items") and is not treated as a malformed payload.
		TextEditPayload[]? response = JsonSerializer.Deserialize<TextEditPayload[]>("null", CreateOptions(logger));

		Assert.IsNull(response);
		Assert.IsEmpty(logger.Logs, string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Serialize_ReferenceArray_RoundTripsTheStandardArrayShape()
	{
		LocationPayload[] response =
		[
			new LocationPayload
			{
				Uri = "file:///workspace/test.ext",
				Range = new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 1)),
			},
		];

		string json = JsonSerializer.Serialize(response, CreateOptions());

		Assert.IsTrue(json.StartsWith('['), json);

		LocationPayload[]? roundTripped = JsonSerializer.Deserialize<LocationPayload[]>(json, CreateOptions());

		Assert.IsNotNull(roundTripped);
		Assert.HasCount(1, roundTripped);
		Assert.AreEqual("file:///workspace/test.ext", roundTripped[0].Uri);
	}
}
