using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class CompletionTextEditPayloadJsonConverterTests
{
	[TestMethod]
	public void Deserialize_ClassicRangeShape_YieldsRangeTextEdit()
	{
		CompletionTextEditPayload? payload = JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "range": {
			    "start": { "line": 2, "character": 1 },
			    "end": { "line": 2, "character": 5 }
			  }
			}
			""");

		Assert.IsInstanceOfType<CompletionRangeTextEditPayload>(payload);

		var rangeEdit = (CompletionRangeTextEditPayload)payload;

		Assert.AreEqual("name", rangeEdit.NewText);
		Assert.AreEqual(2, rangeEdit.Range.Start.Line);
		Assert.AreEqual(5, rangeEdit.Range.End.Character);
	}

	[TestMethod]
	public void Deserialize_InsertReplaceShape_YieldsInsertReplaceTextEdit()
	{
		CompletionTextEditPayload? payload = JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "insert": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 0 }
			  },
			  "replace": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 4 }
			  }
			}
			""");

		Assert.IsInstanceOfType<CompletionInsertReplaceTextEditPayload>(payload);

		var insertReplaceEdit = (CompletionInsertReplaceTextEditPayload)payload;

		Assert.AreEqual("name", insertReplaceEdit.NewText);
		Assert.AreEqual(0, insertReplaceEdit.Insert.End.Character);
		Assert.AreEqual(4, insertReplaceEdit.Replace.End.Character);
	}

	[TestMethod]
	public void Deserialize_MissingNewText_LeavesNewTextNull()
	{
		CompletionTextEditPayload? payload = JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "range": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 1 }
			  }
			}
			""");

		Assert.IsInstanceOfType<CompletionRangeTextEditPayload>(payload);
		Assert.IsNull(payload.NewText);

		string json = JsonSerializer.Serialize(payload);

		Assert.IsFalse(json.Contains("newText", StringComparison.Ordinal));
	}

	[TestMethod]
	public void Deserialize_RangeCombinedWithInsert_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "range": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 1 }
			  },
			  "insert": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 0 }
			  }
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_RangeCombinedWithReplace_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "range": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 1 }
			  },
			  "replace": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 4 }
			  }
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_InsertWithoutReplace_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "insert": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 0 }
			  }
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_ReplaceWithoutInsert_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "replace": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 4 }
			  }
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_WithoutAnyShape_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""{ "newText": "name" }"""));
	}

	[TestMethod]
	public void Deserialize_NonObjectShape_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CompletionTextEditPayload>("5"));
	}

	[TestMethod]
	public void Deserialize_NullRange_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "range": null
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_NonStringNewText_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": 5,
			  "range": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 1 }
			  }
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_UnknownProperties_AreIgnored()
	{
		CompletionTextEditPayload? payload = JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "insertTextMode": 2,
			  "range": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 1 }
			  }
			}
			""");

		Assert.IsInstanceOfType<CompletionRangeTextEditPayload>(payload);
	}

	[TestMethod]
	public void Serialize_RangeTextEdit_WritesClassicShape()
	{
		string json = JsonSerializer.Serialize<CompletionTextEditPayload>(new CompletionRangeTextEditPayload
		{
			NewText = "name",
			Range = new ProtocolRangePayload(new ProtocolPosition(2, 1), new ProtocolPosition(2, 5))
		});

		Assert.AreEqual(
			"""{"newText":"name","range":{"start":{"line":2,"character":1},"end":{"line":2,"character":5}}}""",
			json);
	}

	[TestMethod]
	public void Serialize_InsertReplaceTextEdit_WritesInsertReplaceShape()
	{
		string json = JsonSerializer.Serialize<CompletionTextEditPayload>(new CompletionInsertReplaceTextEditPayload
		{
			NewText = "name",
			Insert = new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 0)),
			Replace = new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 4))
		});

		Assert.AreEqual(
			"""{"newText":"name","insert":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}},"replace":{"start":{"line":0,"character":0},"end":{"line":0,"character":4}}}""",
			json);
	}

	[TestMethod]
	public void Serialize_StaticDerivedType_RoundTripsThroughTheUnionConverter()
	{
		// A statically derived-typed value serializes with the default derived contract (same members,
		// different property order); the wire shape must still bind back to the same payload.
		var rangeEdit = new CompletionRangeTextEditPayload
		{
			NewText = "name",
			Range = new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 1))
		};

		string derivedJson = JsonSerializer.Serialize(rangeEdit);

		CompletionTextEditPayload? roundTripped = JsonSerializer.Deserialize<CompletionTextEditPayload>(derivedJson);

		Assert.IsInstanceOfType<CompletionRangeTextEditPayload>(roundTripped);
		Assert.AreEqual(rangeEdit, roundTripped);
	}
}
