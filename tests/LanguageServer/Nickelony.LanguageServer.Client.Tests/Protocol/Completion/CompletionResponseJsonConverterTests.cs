using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class CompletionResponseJsonConverterTests
{
	[TestMethod]
	public void DeserializeCompletionResponse_AppliesItemDefaultsToItems()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "isIncomplete": true,
			  "itemDefaults": {
			    "editRange": {
			      "insert": {
			        "start": { "line": 2, "character": 1 },
			        "end": { "line": 2, "character": 3 }
			      },
			      "replace": {
			        "start": { "line": 2, "character": 1 },
			        "end": { "line": 2, "character": 8 }
			      }
			    },
			    "insertTextFormat": 2,
			    "data": {
			      "origin": "defaults"
			    }
			  },
			  "items": [
			    {
			      "label": "call",
			      "textEditText": "call(${1:arg})"
			    },
			    {
			      "label": "warn"
			    }
			  ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsTrue(response.IsIncomplete);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(2, response.Items.Count);

		CompletionItemPayload firstItem = response.Items[0];
		Assert.AreEqual(InsertTextFormat.Snippet, firstItem.InsertTextFormat);
		Assert.IsInstanceOfType<CompletionInsertReplaceTextEditPayload>(firstItem.TextEdit);

		var firstEdit = (CompletionInsertReplaceTextEditPayload)firstItem.TextEdit;

		Assert.AreEqual("call(${1:arg})", firstEdit.NewText);
		Assert.AreEqual(2, firstEdit.Insert.Start.Line);
		Assert.AreEqual(1, firstEdit.Insert.Start.Character);
		Assert.AreEqual(8, firstEdit.Replace.End.Character);
		Assert.IsNotNull(firstItem.ExtensionData);
		Assert.IsTrue(firstItem.ExtensionData.TryGetValue("data", out JsonElement firstItemData));
		Assert.AreEqual("defaults", firstItemData.GetProperty("origin").GetString());

		CompletionItemPayload secondItem = response.Items[1];
		Assert.AreEqual(InsertTextFormat.Snippet, secondItem.InsertTextFormat);
		Assert.IsInstanceOfType<CompletionInsertReplaceTextEditPayload>(secondItem.TextEdit);
		Assert.AreEqual("warn", secondItem.TextEdit.NewText);
		Assert.IsNotNull(secondItem.ExtensionData);
		Assert.IsTrue(secondItem.ExtensionData.TryGetValue("data", out JsonElement secondItemData));
		Assert.AreEqual("defaults", secondItemData.GetProperty("origin").GetString());
	}

	[TestMethod]
	public void DeserializeCompletionResponse_DefaultEditRangeUsesLabelInsteadOfInsertText()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "itemDefaults": {
			    "editRange": {
			      "start": { "line": 0, "character": 0 },
			      "end": { "line": 0, "character": 4 }
			    }
			  },
			  "items": [
			    {
			      "label": "print",
			      "insertText": "print()"
			    }
			  ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);

		CompletionItemPayload item = response.Items[0];

		Assert.IsInstanceOfType<CompletionRangeTextEditPayload>(item.TextEdit);
		Assert.AreEqual("print", item.TextEdit!.NewText);
		Assert.AreEqual("print()", item.InsertText);
	}

	[TestMethod]
	public void DeserializeCompletionResponse_WhenRootIsNotAnArrayOrObject_LogsWarningAndReturnsEmptyResponse()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new CompletionResponseJsonConverter(logScope));

		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>("5", options);

		Assert.IsNotNull(response);
		Assert.IsNull(response.Items);
		Assert.IsFalse(response.IsIncomplete);
		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void DeserializeCompletionResponse_ParsesSortTextAndPreselectAsTypedFields()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "isIncomplete": false,
			  "items": [
			    {
			      "label": "spawn",
			      "kind": 3,
			      "sortText": "0002",
			      "preselect": true
			    }
			  ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);

		CompletionItemPayload item = response.Items[0];
		Assert.AreEqual("0002", item.SortText);
		Assert.AreEqual(CompletionItemKind.Function, item.Kind);
		Assert.IsTrue(item.Preselect);

		// The fields are modeled, so they must not fall back to extension data.
		if (item.ExtensionData is not null)
		{
			Assert.IsFalse(item.ExtensionData.ContainsKey("sortText"));
			Assert.IsFalse(item.ExtensionData.ContainsKey("preselect"));
		}
	}

	[TestMethod]
	public void DeserializeCompletionResponse_UsesConverterInstanceLogger()
	{
		using var firstLogScope = new TestLoggerScope(LogLevel.Warning);
		using var secondLogScope = new TestLoggerScope(LogLevel.Warning);

		var firstOptions = new JsonSerializerOptions();
		firstOptions.Converters.Add(new CompletionResponseJsonConverter(firstLogScope));

		var secondOptions = new JsonSerializerOptions();
		secondOptions.Converters.Add(new CompletionResponseJsonConverter(secondLogScope));

		JsonSerializer.Deserialize<CompletionResponse>("{}", firstOptions);
		JsonSerializer.Deserialize<CompletionResponse>("{}", secondOptions);
		JsonSerializer.Deserialize<CompletionResponse>("{}", firstOptions);

		Assert.AreEqual(2, firstLogScope.Logs.Count);
		Assert.AreEqual(1, secondLogScope.Logs.Count);
	}

	[TestMethod]
	public void DeserializeCompletionResponse_SkipsNonObjectItemsInArrayPayload()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""["unexpected", 5, null, { "label": "call" }]""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);
		Assert.AreEqual("call", response.Items[0].Label);
	}

	[TestMethod]
	public void DeserializeCompletionResponse_SkipsNonObjectItemsInCompletionListPayload()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "itemDefaults": { "insertTextFormat": 2 },
			  "items": [ 7, { "label": "call" } ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);
		Assert.AreEqual(InsertTextFormat.Snippet, response.Items[0].InsertTextFormat);
	}

	[TestMethod]
	public void DeserializeCompletionResponse_SkipsItemWithIllegalTextEditUnionAndKeepsValidItems()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new CompletionResponseJsonConverter(logScope));

		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "items": [
			    {
			      "label": "broken",
			      "textEdit": {
			        "newText": "broken",
			        "range": {
			          "start": { "line": 0, "character": 0 },
			          "end": { "line": 0, "character": 1 }
			        },
			        "insert": {
			          "start": { "line": 0, "character": 0 },
			          "end": { "line": 0, "character": 0 }
			        }
			      }
			    },
			    { "label": "call", "kind": 3 }
			]
			}
			""", options);

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);
		Assert.AreEqual("call", response.Items[0].Label);
		Assert.AreEqual(CompletionItemKind.Function, response.Items[0].Kind);
		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void DeserializeCompletionResponse_PreservesCompletionListMetadata()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "isIncomplete": true,
			  "items": [
			    {
			      "label": "spawn",
			      "kind": 3,
			      "insertText": "spawn",
			      "filterText": "spawn"
			    }
			  ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsTrue(response.IsIncomplete);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);
		Assert.AreEqual("spawn", response.Items[0].Label);
	}

	[TestMethod]
	public void DeserializeCompletionResponse_ParsesArrayPayload()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			[
			  {
			    "label": "spawn",
			    "kind": 3,
			    "insertText": "spawn",
			    "filterText": "spawn"
			  }
			]
			""");

		Assert.IsNotNull(response);
		Assert.IsFalse(response.IsIncomplete);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);
		Assert.AreEqual("spawn", response.Items[0].Label);
	}

	[TestMethod]
	public void DeserializeCompletionResponse_IgnoresNonBooleanIncompleteFlag()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "isIncomplete": "yes",
			  "items": [
			    {
			      "label": "spawn",
			      "kind": 3,
			      "insertText": "spawn",
			      "filterText": "spawn"
			    }
			  ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsFalse(response.IsIncomplete);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);
	}

	[TestMethod]
	public void DeserializeCompletionResponse_IgnoresMalformedCompletionListItemsShape()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new CompletionResponseJsonConverter(logScope));

		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "isIncomplete": true,
			  "items": {
			    "label": "spawn"
			  }
			}
			""", options);

		Assert.IsNotNull(response);
		Assert.IsNull(response.Items);
		Assert.IsFalse(response.IsIncomplete);

		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void DeserializeCompletionResponse_LogsWhenCompletionListItemsPropertyIsMissing()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new CompletionResponseJsonConverter(logScope));

		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "isIncomplete": true
			}
			""", options);

		Assert.IsNotNull(response);
		Assert.IsNull(response.Items);
		Assert.IsFalse(response.IsIncomplete);

		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void SerializeCompletionResponse_WritesRoundTrippableCompletionListShape()
	{
		var response = new CompletionResponse(
			[
				new CompletionItemPayload
				{
					Label = "spawn",
					Kind = CompletionItemKind.Function,
					InsertText = "spawn"
				}
			],
			isIncomplete: true);

		string json = JsonSerializer.Serialize(response);
		CompletionResponse? roundTripped = JsonSerializer.Deserialize<CompletionResponse>(json);

		Assert.AreEqual("{\"isIncomplete\":true,\"items\":[{\"label\":\"spawn\",\"kind\":3,\"insertText\":\"spawn\"}]}", json);
		Assert.IsNotNull(roundTripped);
		Assert.IsTrue(roundTripped.IsIncomplete);
		Assert.IsNotNull(roundTripped.Items);
		Assert.AreEqual(1, roundTripped.Items.Count);
		Assert.AreEqual("spawn", roundTripped.Items[0].Label);
	}

	[TestMethod]
	public void DeserializeCompletionResponse_ParsesCompletionProtocolDepthFieldsAsTypedMembers()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "isIncomplete": false,
			  "items": [
			    {
			      "label": "print",
			      "tags": [1],
			      "commitCharacters": ["(", ","],
			      "additionalTextEdits": [
			        {
			          "range": {
			            "start": { "line": 0, "character": 0 },
			            "end": { "line": 0, "character": 0 }
			          },
			          "newText": "local print = print\n"
			        }
			      ]
			    }
			  ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);

		CompletionItemPayload item = response.Items[0];

		Assert.IsNotNull(item.Tags);
		Assert.AreEqual(1, item.Tags.Count);
		Assert.AreEqual(CompletionItemTag.Deprecated, item.Tags[0]);

		Assert.IsNotNull(item.CommitCharacters);
		CollectionAssert.AreEqual(new[] { "(", "," }, item.CommitCharacters.ToArray());

		Assert.IsNotNull(item.AdditionalTextEdits);
		Assert.AreEqual(1, item.AdditionalTextEdits.Count);

		TextEditPayload additionalEdit = item.AdditionalTextEdits[0];

		Assert.IsTrue(additionalEdit.Range.HasValue);
		Assert.AreEqual(0, additionalEdit.Range.GetValueOrDefault().Start.Character);
		Assert.AreEqual("local print = print\n", additionalEdit.NewText);

		// The fields are modeled, so they must not fall back to extension data.
		if (item.ExtensionData is not null)
		{
			Assert.IsFalse(item.ExtensionData.ContainsKey("tags"));
			Assert.IsFalse(item.ExtensionData.ContainsKey("commitCharacters"));
			Assert.IsFalse(item.ExtensionData.ContainsKey("additionalTextEdits"));
		}
	}

	[TestMethod]
	public void DeserializeCompletionResponse_AppliesCommitCharacterDefaultsOnlyWhenItemLacksThem()
	{
		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "isIncomplete": false,
			  "itemDefaults": {
			    "commitCharacters": [".", ":"]
			  },
			  "items": [
			    { "label": "first" },
			    { "label": "second", "commitCharacters": ["("] }
			  ]
			}
			""");

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);

		CompletionItemPayload first = response.Items[0];
		CompletionItemPayload second = response.Items[1];

		Assert.IsNotNull(first.CommitCharacters);
		CollectionAssert.AreEqual(new[] { ".", ":" }, first.CommitCharacters.ToArray());
		Assert.IsNotNull(second.CommitCharacters);
		CollectionAssert.AreEqual(new[] { "(" }, second.CommitCharacters.ToArray());
	}

	[TestMethod]
	public void DeserializeCompletionResponse_CasingVariantItemMemberWinsOverTheDefault()
	{
		// The transport binds members case-insensitively, so the default merge must detect a casing variant;
		// otherwise it appends the canonical default after the item member and the default wins.
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};

		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "itemDefaults": {
			    "commitCharacters": [".", ":"]
			  },
			  "items": [
			    { "Label": "variant", "CommitCharacters": ["("] }
			  ]
			}
			""",
			options);

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);

		CompletionItemPayload item = response.Items[0];

		Assert.AreEqual("variant", item.Label);
		Assert.IsNotNull(item.CommitCharacters);
		CollectionAssert.AreEqual(new[] { "(" }, item.CommitCharacters.ToArray());
	}

	[TestMethod]
	public void DeserializeCompletionResponse_CasingVariantLabelSynthesizesTheDefaultTextEdit()
	{
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};

		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "itemDefaults": {
			    "editRange": {
			      "start": { "line": 0, "character": 0 },
			      "end": { "line": 0, "character": 6 }
			    }
			  },
			  "items": [
			    { "Label": "casing" }
			  ]
			}
			""",
			options);

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);
		Assert.AreEqual(1, response.Items.Count);

		CompletionItemPayload item = response.Items[0];

		Assert.AreEqual("casing", item.Label);
		Assert.IsInstanceOfType<CompletionRangeTextEditPayload>(item.TextEdit);
		Assert.AreEqual("casing", item.TextEdit!.NewText);
	}

	[TestMethod]
	public void SerializeCompletionItemPayload_RoundTripsCompletionProtocolDepthFields()
	{
		CompletionItemPayload? payload = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""
			{
			  "label": "print",
			  "tags": [1],
			  "commitCharacters": ["("],
			  "additionalTextEdits": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 0 }
			      },
			      "newText": "local print = print"
			    }
			  ]
			}
			""");

		Assert.IsNotNull(payload);

		string json = JsonSerializer.Serialize(payload);

		Assert.IsTrue(json.Contains("\"tags\":[1]", StringComparison.Ordinal), json);
		Assert.IsTrue(json.Contains("\"commitCharacters\":[\"(\"]", StringComparison.Ordinal), json);
		Assert.IsTrue(json.Contains("\"additionalTextEdits\":", StringComparison.Ordinal), json);
		Assert.IsTrue(json.Contains("\"newText\":\"local print = print\"", StringComparison.Ordinal), json);
	}
}
