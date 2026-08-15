using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class TolerantCollectionJsonConverterTests
{
	private static JsonSerializerOptions CreateOptions(ILogger? logger = null)
	{
		// Mirrors the transport registrations for the collections whose entries are not wrapped by a feature
		// response type. Bare result lists stay tolerant; edit-bearing collections require an array root so a
		// malformed payload cannot degrade to a successful no-op.
		var options = new JsonSerializerOptions();
		options.Converters.Add(new TolerantCollectionJsonConverter<ReferenceLocationPayload>(logger));
		options.Converters.Add(new TolerantCollectionJsonConverter<TextEditPayload>(logger, requireArrayRoot: true));
		options.Converters.Add(new TolerantCollectionJsonConverter<WorkspaceDocumentChangePayload>(logger, requireArrayRoot: true));
		return options;
	}

	[TestMethod]
	public void Deserialize_ReferenceArray_SkipsNullAndMalformedElements()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		ReferenceLocationPayload[]? response = JsonSerializer.Deserialize<ReferenceLocationPayload[]>(
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
			CreateOptions(logScope));

		Assert.IsNotNull(response);
		Assert.AreEqual(1, response.Length);
		Assert.AreEqual("file:///workspace/test.ext", response[0].Uri);
		Assert.IsNotNull(response[0].Range);
		Assert.AreEqual(1, response[0].Range!.Value.Start.Line);
		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("Skipping a null", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("Skipping a malformed", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_TextEditArray_SkipsNullAndMalformedElements()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

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
			CreateOptions(logScope));

		Assert.IsNotNull(response);
		Assert.AreEqual(1, response.Length);
		Assert.AreEqual("x", response[0].NewText);
	}

	[TestMethod]
	public void Deserialize_WorkspaceEditDocumentChanges_SkipsMalformedEditElements()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

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
			CreateOptions(logScope));

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.DocumentChanges);
		Assert.AreEqual(1, response.Value.DocumentChanges.Count);

		IReadOnlyList<TextEditPayload>? edits = response.Value.DocumentChanges[0].Edits;

		Assert.IsNotNull(edits);
		Assert.AreEqual(1, edits.Count);
		Assert.AreEqual("x", edits[0].NewText);
	}

	[TestMethod]
	public void Deserialize_WorkspaceEditDocumentChanges_SkipsNullAndMalformedEntries()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

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
			CreateOptions(logScope));

		// A null or malformed structured change must not fault the response; the usable rename survives.
		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.DocumentChanges);
		Assert.AreEqual(1, response.Value.DocumentChanges.Count);

		WorkspaceDocumentChangePayload change = response.Value.DocumentChanges[0];

		Assert.AreEqual("rename", change.Kind);
		Assert.AreEqual("file:///workspace/old.ext", change.OldUri);
		Assert.AreEqual("file:///workspace/new.ext", change.NewUri);
		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_NonArrayRoot_ReturnsEmptyCollectionWithWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		ReferenceLocationPayload[]? response = JsonSerializer.Deserialize<ReferenceLocationPayload[]>("5", CreateOptions(logScope));

		Assert.IsNotNull(response);
		Assert.AreEqual(0, response.Length);
		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_NonArrayRoot_WhenArrayRootIsRequired_ThrowsJsonException()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		// Edit-bearing collections fail loudly: an empty collection is a legal outcome for them, so degrading a
		// malformed root to "nothing to apply" would turn a broken payload into a successful no-op.
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<TextEditPayload[]>("5", CreateOptions(logScope)));
	}

	[TestMethod]
	public void Deserialize_NullRoot_StaysNullWithoutWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		// The serializer handles a JSON null before the converter runs, so a null collection member stays null
		// ("no items") and is not treated as a malformed payload.
		TextEditPayload[]? response = JsonSerializer.Deserialize<TextEditPayload[]>("null", CreateOptions(logScope));

		Assert.IsNull(response);
		Assert.AreEqual(0, logScope.Logs.Count, string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Serialize_ReferenceArray_RoundTripsTheStandardArrayShape()
	{
		ReferenceLocationPayload[] response =
		[
			new ReferenceLocationPayload
			{
				Uri = "file:///workspace/test.ext",
				Range = new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 1))
			}
		];

		string json = JsonSerializer.Serialize(response, CreateOptions());

		Assert.IsTrue(json.StartsWith('['), json);

		ReferenceLocationPayload[]? roundTripped = JsonSerializer.Deserialize<ReferenceLocationPayload[]>(json, CreateOptions());

		Assert.IsNotNull(roundTripped);
		Assert.AreEqual(1, roundTripped.Length);
		Assert.AreEqual("file:///workspace/test.ext", roundTripped[0].Uri);
	}
}
