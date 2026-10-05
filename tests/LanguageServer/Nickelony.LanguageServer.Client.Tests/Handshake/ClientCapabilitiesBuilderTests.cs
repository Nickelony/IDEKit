using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ClientCapabilitiesBuilderTests
{
	private const string Utf16Pin = """ "general": { "positionEncodings": [ "utf-16" ] } """;

	private static void AssertBuilt(string inputJson, string expectedJson)
	{
		JsonObject actual = ClientCapabilitiesBuilder.Build(JsonNode.Parse(inputJson), new LanguageServerClientHooks(), NullLogger.Instance);
		Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), actual), actual.ToJsonString());
	}

	[TestMethod]
	[DataRow(
		"""
			{
			  "workspace": {
			    "didChangeWatchedFiles": { "dynamicRegistration": true },
			    "fileOperations": { "didCreate": { "dynamicRegistration": true } }
			  },
			  "textDocument": {
			    "rename": { "dynamicRegistration": true, "prepareSupport": true },
			    "hover": { "contentFormat": [ "markdown" ] }
			  }
			}
			""",

		"""
			{
			  "workspace": {
			    "didChangeWatchedFiles": { "dynamicRegistration": false },
			    "fileOperations": { "didCreate": { "dynamicRegistration": false } }
			  },
			  "textDocument": {
			    "rename": { "dynamicRegistration": false, "prepareSupport": true },
			    "hover": { "contentFormat": [ "markdown" ] }
			  },
			  "general": { "positionEncodings": [ "utf-16" ] }
			}
			""",

		DisplayName = "Flags under workspace and textDocument go off, nested objects too, and a capability without the member gains none"
	)]
	[DataRow(
		"""{ "window": { "showMessage": { "dynamicRegistration": true } } }""",
		"""{ "window": { "showMessage": { "dynamicRegistration": true } }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "Other parents keep the flag the host declared"
	)]
	public void Build_DynamicRegistration_IsForcedOffUnderWorkspaceAndTextDocument(string inputJson, string expectedJson) =>
		AssertBuilt(inputJson, expectedJson);

	[TestMethod]
	[DataRow(
		"""{ "window": { "workDoneProgress": true, "showMessage": {} } }""",
		"""{ "window": { "workDoneProgress": false, "showMessage": {} }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "An advertised flag goes off"
	)]
	[DataRow(
		"""{ "window": { "showMessage": {} } }""",
		"""{ "window": { "showMessage": {} }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "An absent flag is not added"
	)]
	[DataRow(
		"""{ "window": 5 }""",
		"""{ "window": 5, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "A window member that is not an object stays as it is"
	)]
	public void Build_WorkDoneProgress_IsForcedOffWhenAdvertised(string inputJson, string expectedJson) => AssertBuilt(inputJson, expectedJson);

	[TestMethod]
	[DataRow(
		"""{ "workspace": { "applyEdit": true, "configuration": true } }""",
		"""{ "workspace": { "applyEdit": false, "configuration": true }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "An advertised flag goes off and its siblings stay"
	)]
	[DataRow(
		"""{ "workspace": { "configuration": true } }""",
		"""{ "workspace": { "configuration": true }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "An absent flag is not added"
	)]
	public void Build_ApplyEdit_IsForcedOffWhenAdvertised(string inputJson, string expectedJson) => AssertBuilt(inputJson, expectedJson);

	private static void AssertBuiltWith(LanguageServerClientHooks hooks, string inputJson, string expectedJson)
	{
		JsonObject actual = ClientCapabilitiesBuilder.Build(JsonNode.Parse(inputJson), hooks, NullLogger.Instance);
		Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), actual), actual.ToJsonString());
	}

	[TestMethod]
	[DataRow(
		"{}",
		"""{ "workspace": { "applyEdit": true }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "An absent flag is added, with its group"
	)]
	[DataRow(
		"""{ "workspace": { "applyEdit": false, "configuration": true } }""",
		"""{ "workspace": { "applyEdit": true, "configuration": true }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "A flag the host declared off goes on, and its siblings stay"
	)]
	public void Build_ApplyEdit_IsAdvertisedOnlyWhenItsHookIsSet(string inputJson, string expectedJson)
	{
		AssertBuiltWith(
			new LanguageServerClientHooks { ApplyWorkspaceEdit = static (_, _) => Task.FromResult(new ApplyWorkspaceEditResponse(true)) },
			inputJson,
			expectedJson
		);

		// The other hooks do not enable it.
		AssertBuiltWith(
			new LanguageServerClientHooks
			{
				ShowMessage = static _ => { },
				ShowMessageRequest = static (_, _) => Task.FromResult<MessageActionItemPayload?>(null),
			},

			"""{ "workspace": { "applyEdit": true } }""",
			"""{ "workspace": { "applyEdit": false }, "general": { "positionEncodings": [ "utf-16" ] } }"""
		);
	}

	[TestMethod]
	[DataRow(
		"{}",
		"""{ "window": { "workDoneProgress": true }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "An absent flag is added, with its group"
	)]
	[DataRow(
		"""{ "window": { "workDoneProgress": false, "showMessage": {} } }""",
		"""{ "window": { "workDoneProgress": true, "showMessage": {} }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "A flag the host declared off goes on, and its siblings stay"
	)]
	public void Build_WorkDoneProgress_IsAdvertisedOnlyWhenItsHookIsSet(string inputJson, string expectedJson)
	{
		AssertBuiltWith(new LanguageServerClientHooks { Progress = static _ => { } }, inputJson, expectedJson);

		// The other hooks do not enable it.
		AssertBuiltWith(
			new LanguageServerClientHooks
			{
				ShowMessage = static _ => { },
				ShowMessageRequest = static (_, _) => Task.FromResult<MessageActionItemPayload?>(null),
			},

			"""{ "window": { "workDoneProgress": true } }""",
			"""{ "window": { "workDoneProgress": false }, "general": { "positionEncodings": [ "utf-16" ] } }"""
		);
	}

	[TestMethod]
	[DataRow(
		"""{ "general": { "positionEncodings": [ "utf-8", "utf-32" ], "markdown": { "parser": "marked" } } }""",
		"""{ "general": { "positionEncodings": [ "utf-16" ], "markdown": { "parser": "marked" } } }""",
		DisplayName = "Advertised encodings are replaced by UTF-16 and the other general members stay"
	)]
	[DataRow("""{ "general": 5 }""", "{ " + Utf16Pin + " }", DisplayName = "A general member that is not an object is replaced")]
	[DataRow("{}", "{ " + Utf16Pin + " }", DisplayName = "An absent general member is added")]
	public void Build_PositionEncodings_ArePinnedToUtf16(string inputJson, string expectedJson) => AssertBuilt(inputJson, expectedJson);

	[TestMethod]
	[DataRow(
		"""
			{
			  "textDocument": {
			    "semanticTokens": { "multilineTokenSupport": true, "overlappingTokenSupport": true, "requests": { "full": true } }
			  }
			}
			""",

		"""
			{
			  "textDocument": {
			    "semanticTokens": { "multilineTokenSupport": false, "overlappingTokenSupport": false, "requests": { "full": true } }
			  },
			  "general": { "positionEncodings": [ "utf-16" ] }
			}
			""",

		DisplayName = "Advertised multiline and overlapping support go off"
	)]
	[DataRow(
		"""{ "textDocument": { "semanticTokens": { "requests": {} } } }""",
		"""{ "textDocument": { "semanticTokens": { "requests": {} } }, "general": { "positionEncodings": [ "utf-16" ] } }""",
		DisplayName = "Absent support flags are not added"
	)]
	public void Build_SemanticTokenSupport_IsForcedOffWhenAdvertised(string inputJson, string expectedJson) => AssertBuilt(inputJson, expectedJson);

	[TestMethod]
	public void Build_NullCapabilities_YieldsOnlyThePositionEncodingPin()
	{
		JsonObject actual = ClientCapabilitiesBuilder.Build(clientCapabilities: null, new LanguageServerClientHooks(), NullLogger.Instance);
		Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("{ " + Utf16Pin + " }"), actual), actual.ToJsonString());
	}

	[TestMethod]
	public void Build_SerializesHostObjectsWithLowerCamelMemberNames()
	{
		JsonObject actual = ClientCapabilitiesBuilder.Build(
			new { TextDocument = new { Hover = new { DynamicRegistration = true } } },
			new LanguageServerClientHooks(),
			NullLogger.Instance
		);

		var expected = JsonNode.Parse("""{ "textDocument": { "hover": { "dynamicRegistration": false } }, """ + Utf16Pin + " }");

		Assert.IsTrue(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
	}

	[TestMethod]
	[DataRow(42, DisplayName = "A number")]
	[DataRow("text", DisplayName = "A string")]
	[DataRow(new[] { 1, 2 }, DisplayName = "An array")]
	public void Build_PayloadThatIsNotAnObject_YieldsEmptyCapabilitiesAndWarns(object payload)
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		JsonObject actual = ClientCapabilitiesBuilder.Build(payload, new LanguageServerClientHooks(), logger);

		Assert.IsEmpty(actual);
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Build_ObjectPayload_DoesNotWarn()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		_ = ClientCapabilitiesBuilder.Build(new { }, new LanguageServerClientHooks(), logger);

		Assert.IsFalse(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}
}
