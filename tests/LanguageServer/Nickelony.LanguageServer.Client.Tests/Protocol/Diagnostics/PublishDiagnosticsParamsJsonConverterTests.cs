using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class PublishDiagnosticsParamsJsonConverterTests
{
	[TestMethod]
	public void Deserialize_WellFormedPayload_ParsesTypedEntries()
	{
		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""
			{
			  "uri": "file:///workspace/test.ext",
			  "version": 3,
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 5 }
			      },
			      "severity": 1,
			      "message": "broken",
			      "source": "example-language-server",
			      "code": "E1"
			    }
			  ]
			}
			""");

		Assert.AreEqual("file:///workspace/test.ext", parameters.Uri);
		Assert.AreEqual(3, parameters.Version);
		Assert.IsNotNull(parameters.Diagnostics);
		Assert.AreEqual(1, parameters.Diagnostics.Count);
		Assert.AreEqual(DiagnosticSeverity.Error, parameters.Diagnostics[0].Severity);
		Assert.AreEqual(5, parameters.Diagnostics[0].Range?.End.Character);
		Assert.AreEqual("broken", parameters.Diagnostics[0].Message);
		Assert.AreEqual("example-language-server", parameters.Diagnostics[0].Source);
	}

	[TestMethod]
	public void Deserialize_MalformedEntry_IsSkippedWithWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new PublishDiagnosticsParamsJsonConverter(logScope));

		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""
			{
			  "uri": "file:///workspace/test.ext",
			  "version": 3,
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 5 }
			      },
			      "severity": 1,
			      "message": "kept"
			    },
			    {
			      "range": { "start": { "line": 0, "character": 0 } },
			      "severity": 1,
			      "message": "dropped"
			    }
			  ]
			}
			""", options);

		Assert.IsNotNull(parameters.Diagnostics);
		Assert.AreEqual(1, parameters.Diagnostics.Count);
		Assert.AreEqual("kept", parameters.Diagnostics[0].Message);
		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_RootNotObject_LogsAndYieldsEmptyPayload()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new PublishDiagnosticsParamsJsonConverter(logScope));

		PublishDiagnosticsParams parameters = DeserializeDiagnostics("[]", options);

		Assert.IsNull(parameters.Uri);
		Assert.IsNull(parameters.Version);
		Assert.IsNull(parameters.Diagnostics);
		Assert.IsTrue(parameters.IsDegraded);
		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_UnknownSeverity_StaysRepresentable()
	{
		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""
			{
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 1 }
			      },
			      "severity": 99
			    }
			  ]
			}
			""");

		Assert.IsNotNull(parameters.Diagnostics);
		Assert.AreEqual(1, parameters.Diagnostics.Count);
		Assert.AreEqual((DiagnosticSeverity)99, parameters.Diagnostics[0].Severity);
	}

	[TestMethod]
	public void Deserialize_MalformedRootMembers_AreIgnoredWithWarnings()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new PublishDiagnosticsParamsJsonConverter(logScope));

		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""{ "uri": 7, "version": "newest", "diagnostics": null }""", options);

		Assert.IsNull(parameters.Uri);
		Assert.IsNull(parameters.Version);
		Assert.IsNull(parameters.Diagnostics);
		Assert.IsTrue(parameters.IsDegraded);
		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("'uri' property", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("'version' property", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_AbsentDiagnostics_LeavesNullAndMarksPayloadDegraded()
	{
		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""{ "uri": "file:///workspace/test.ext" }""");

		Assert.AreEqual("file:///workspace/test.ext", parameters.Uri);
		Assert.IsNull(parameters.Version);
		Assert.IsNull(parameters.Diagnostics);
		Assert.IsTrue(parameters.IsDegraded);
	}

	[TestMethod]
	public void Serialize_WritesStandardShape()
	{
		var parameters = new PublishDiagnosticsParams(
			"file:///workspace/test.ext",
			3,
			[
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
					DiagnosticSeverity.Error,
					"broken",
					"example-language-server",
					JsonSerializer.SerializeToElement("E1"))
			]);

		string json = JsonSerializer.Serialize(parameters);

		Assert.AreEqual(
			"""{"uri":"file:///workspace/test.ext","version":3,"diagnostics":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":5}},"severity":1,"message":"broken","source":"example-language-server","code":"E1"}]}""",
			json);
	}

	[TestMethod]
	public void Deserialize_RichDiagnosticMembers_RoundTrip()
	{
		const string payload =
			"""
			{
			  "uri": "file:///workspace/test.ext",
			  "version": 4,
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 1, "character": 2 },
			        "end": { "line": 1, "character": 6 }
			      },
			      "severity": 2,
			      "message": "deprecated",
			      "tags": [2],
			      "codeDescription": { "href": "https://example.invalid/E1" },
			      "relatedInformation": [
			        {
			          "location": {
			            "uri": "file:///workspace/other.ext",
			            "range": {
			              "start": { "line": 4, "character": 0 },
			              "end": { "line": 4, "character": 3 }
			            }
			          },
			          "message": "related"
			        }
			      ],
			      "data": { "origin": "example" }
			    }
			  ]
			}
			""";

		PublishDiagnosticsParams parameters = DeserializeDiagnostics(payload);

		Assert.IsFalse(parameters.IsDegraded);
		Assert.IsNotNull(parameters.Diagnostics);

		DiagnosticPayload diagnostic = parameters.Diagnostics[0];

		Assert.IsNotNull(diagnostic.Tags);
		Assert.AreEqual(DiagnosticTag.Deprecated, diagnostic.Tags![0]);
		Assert.AreEqual("https://example.invalid/E1", diagnostic.CodeDescription?.Href);
		Assert.IsNotNull(diagnostic.RelatedInformation);
		Assert.AreEqual("related", diagnostic.RelatedInformation![0].Message);
		Assert.AreEqual("file:///workspace/other.ext", diagnostic.RelatedInformation[0].Location?.Uri);
		Assert.AreEqual("example", diagnostic.Data?.GetProperty("origin").GetString());

		string serialized = JsonSerializer.Serialize(parameters);
		PublishDiagnosticsParams roundTrip = DeserializeDiagnostics(serialized);

		Assert.IsFalse(roundTrip.IsDegraded);
		Assert.AreEqual("https://example.invalid/E1", roundTrip.Diagnostics![0].CodeDescription?.Href);
		Assert.AreEqual(DiagnosticTag.Deprecated, roundTrip.Diagnostics[0].Tags![0]);
	}

	[TestMethod]
	public void Deserialize_MalformedDiagnosticsMember_MarksPayloadDegraded()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new PublishDiagnosticsParamsJsonConverter(logScope));

		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""{ "uri": "file:///workspace/test.ext", "diagnostics": { "oops": true } }""", options);

		Assert.IsTrue(parameters.IsDegraded);
		Assert.IsNull(parameters.Diagnostics);
		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_BlankUri_MarksPayloadDegraded()
	{
		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""{ "uri": "   ", "diagnostics": [] }""");

		Assert.IsTrue(parameters.IsDegraded);
		Assert.AreEqual("   ", parameters.Uri);
	}

	[TestMethod]
	public void CreateSnapshot_DetachesDiagnosticsAndPreservesTheDegradedFlag()
	{
		var diagnostics = new List<DiagnosticPayload>
		{
			new(null, DiagnosticSeverity.Warning, "kept", null, null)
		};

		var parameters = new PublishDiagnosticsParams("file:///workspace/test.ext", 7, diagnostics) { IsDegraded = true };
		PublishDiagnosticsParams snapshot = parameters.CreateSnapshot();

		diagnostics.Clear();

		Assert.AreEqual(7, snapshot.Version);
		Assert.IsNotNull(snapshot.Diagnostics);
		Assert.AreEqual(1, snapshot.Diagnostics.Count);
		Assert.IsTrue(snapshot.IsDegraded);
	}

	[TestMethod]
	public void Deserialize_MalformedVersion_KeepsTheDiagnosticsAndIgnoresTheVersion()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var options = new JsonSerializerOptions();
		options.Converters.Add(new PublishDiagnosticsParamsJsonConverter(logScope));

		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""{ "uri": "file:///workspace/test.ext", "version": "newest", "diagnostics": [] }""", options);

		// The version is optional staleness information: a malformed value is ignored like an absent one
		// instead of dropping the valid diagnostics with a degraded payload.
		Assert.IsFalse(parameters.IsDegraded);
		Assert.IsNull(parameters.Version);
		Assert.IsNotNull(parameters.Diagnostics);
		Assert.IsTrue(logScope.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Deserialize_MalformedNestedCollectionElements_AreSkippedWithoutDroppingTheEntry()
	{
		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""
			{
			  "uri": "file:///workspace/test.ext",
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 5 }
			      },
			      "severity": 1,
			      "message": "kept",
			      "tags": [2, "not-a-number"],
			      "relatedInformation": [
			        {
			          "location": {
			            "uri": "file:///workspace/other.ext",
			            "range": {
			              "start": { "line": 0, "character": 0 },
			              "end": { "line": 0, "character": 1 }
			            }
			          },
			          "message": "related"
			        },
			        { "message": 7 }
			      ]
			    }
			  ]
			}
			""");

		Assert.IsNotNull(parameters.Diagnostics);
		Assert.AreEqual(1, parameters.Diagnostics.Count);

		DiagnosticPayload diagnostic = parameters.Diagnostics[0];

		Assert.AreEqual("kept", diagnostic.Message);
		Assert.IsNotNull(diagnostic.Tags);
		Assert.AreEqual(1, diagnostic.Tags.Count);
		Assert.AreEqual(DiagnosticTag.Deprecated, diagnostic.Tags[0]);
		Assert.IsNotNull(diagnostic.RelatedInformation);
		Assert.AreEqual(1, diagnostic.RelatedInformation.Count);
		Assert.AreEqual("related", diagnostic.RelatedInformation[0].Message);
	}

	[TestMethod]
	public void Deserialize_MalformedNestedCollectionRoot_DegradesToEmptyWithoutDroppingTheEntry()
	{
		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""
			{
			  "uri": "file:///workspace/test.ext",
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 5 }
			      },
			      "severity": 1,
			      "message": "kept",
			      "tags": { "not": "an array" }
			    }
			  ]
			}
			""");

		Assert.IsNotNull(parameters.Diagnostics);
		Assert.AreEqual(1, parameters.Diagnostics.Count);

		DiagnosticPayload diagnostic = parameters.Diagnostics[0];

		Assert.AreEqual("kept", diagnostic.Message);
		Assert.IsNotNull(diagnostic.Tags);
		Assert.IsEmpty(diagnostic.Tags);
	}

	[TestMethod]
	public void Serialize_AbsentRequiredMembers_AreOmittedInsteadOfWrittenAsNull()
	{
		var parameters = new PublishDiagnosticsParams(null, null, null);

		string json = JsonSerializer.Serialize(parameters);

		Assert.AreEqual("{}", json);
	}

	[TestMethod]
	public void Serialize_PayloadWithAnEmptyDiagnosticsList_StillWritesTheArray()
	{
		var parameters = new PublishDiagnosticsParams(null, null, []);

		string json = JsonSerializer.Serialize(parameters);

		Assert.AreEqual("""{"diagnostics":[]}""", json);
	}

	private static PublishDiagnosticsParams DeserializeDiagnostics(string json, JsonSerializerOptions? options = null)
		=> JsonSerializer.Deserialize<PublishDiagnosticsParams>(json, options)!;
}
