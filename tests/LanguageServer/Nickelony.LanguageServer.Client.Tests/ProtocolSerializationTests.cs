using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ProtocolSerializationTests
{
	private const string Range = """{ "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } }""";

	[TestMethod]
	[DataRow(typeof(JsonElement), """{ "member": 1 }""", "\"member\":1", DisplayName = nameof(JsonElement))]
	[DataRow(typeof(CompletionResponse), """[ { "label": "print" } ]""", "print", DisplayName = nameof(CompletionResponse))]
	[DataRow(
		typeof(DefinitionResponse),
		"""[ { "uri": "file:///workspace/a.lua", "range": """ + Range + " } ]",
		"a.lua",
		DisplayName = nameof(DefinitionResponse)
	)]
	[DataRow(typeof(HoverResponse), """{ "contents": "hover text" }""", "hover text", DisplayName = nameof(HoverResponse))]
	[DataRow(
		typeof(PublishDiagnosticsParams),
		"""{ "uri": "file:///workspace/a.lua", "diagnostics": [ { "range": """ + Range + """, "message": "diagnostic text" } ] }""",
		"diagnostic text",
		DisplayName = nameof(PublishDiagnosticsParams)
	)]
	[DataRow(
		typeof(CapabilityUnregistrationParams),
		"""{ "unregisterations": [ { "id": "1", "method": "workspace/didChangeWatchedFiles" } ] }""",
		"workspace/didChangeWatchedFiles",
		DisplayName = nameof(CapabilityUnregistrationParams)
	)]
	[DataRow(
		typeof(LocationPayload[]),
		"""[ { "uri": "file:///workspace/a.lua", "range": """ + Range + " } ]",
		"a.lua",
		DisplayName = nameof(LocationPayload) + "[]"
	)]
	[DataRow(
		typeof(TextEditPayload[]),
		"""[ { "range": """ + Range + """, "newText": "replacement" } ]""",
		"replacement",
		DisplayName = nameof(TextEditPayload) + "[]"
	)]
	[DataRow(
		typeof(WorkspaceDocumentChangePayload[]),
		"""[ { "kind": "create", "uri": "file:///workspace/new.lua" } ]""",
		"new.lua",
		DisplayName = nameof(WorkspaceDocumentChangePayload) + "[]"
	)]
	[DataRow(
		typeof(DiagnosticPayload[]),
		"""[ { "range": """ + Range + """, "message": "diagnostic text" } ]""",
		"diagnostic text",
		DisplayName = nameof(DiagnosticPayload) + "[]"
	)]
	[DataRow(
		typeof(WorkspaceConfigurationItemPayload[]),
		"""[ { "section": "lua.settings" } ]""",
		"lua.settings",
		DisplayName = nameof(WorkspaceConfigurationItemPayload) + "[]"
	)]
	[DataRow(
		typeof(CapabilityRegistrationPayload[]),
		"""[ { "id": "1", "method": "workspace/didChangeWatchedFiles" } ]""",
		"workspace/didChangeWatchedFiles",
		DisplayName = nameof(CapabilityRegistrationPayload) + "[]"
	)]
	[DataRow(
		typeof(CapabilityUnregistrationPayload[]),
		"""[ { "id": "1", "method": "workspace/didChangeWatchedFiles" } ]""",
		"workspace/didChangeWatchedFiles",
		DisplayName = nameof(CapabilityUnregistrationPayload) + "[]"
	)]
	[DataRow(typeof(CodeActionResponse), """[ { "title": "Fix it" } ]""", "Fix it", DisplayName = nameof(CodeActionResponse))]
	[DataRow(
		typeof(DocumentSymbolResponse),
		"""[ { "name": "symbolName", "kind": 12, "range": """ + Range + """, "selectionRange": """ + Range + " } ]",
		"symbolName",
		DisplayName = nameof(DocumentSymbolResponse)
	)]
	[DataRow(
		typeof(SignatureHelpResponse),
		"""{ "signatures": [ { "label": "print(value)" } ] }""",
		"print(value)",
		DisplayName = nameof(SignatureHelpResponse)
	)]
	[DataRow(
		typeof(SignatureInformationPayload),
		"""{ "label": "print(value)" }""",
		"print(value)",
		DisplayName = nameof(SignatureInformationPayload)
	)]
	[DataRow(typeof(MarkupContentPayload), """{ "kind": "markdown", "value": "**bold**" }""", "**bold**", DisplayName = nameof(MarkupContentPayload))]
	public void ForMessages_RoundTripsAValidPayload(Type payloadType, string json, string expectedFragment)
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		JsonSerializerOptions options = ProtocolSerialization.ForMessages(logger);

		object? first = JsonSerializer.Deserialize(json, payloadType, options);

		string firstJson = JsonSerializer.Serialize(first, payloadType, options);
		string secondJson = JsonSerializer.Serialize(JsonSerializer.Deserialize(firstJson, payloadType, options), payloadType, options);

		Assert.IsNotNull(first);
		Assert.Contains(expectedFragment, firstJson);
		Assert.AreEqual(firstJson, secondJson);
		Assert.IsFalse(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	[DataRow(typeof(CompletionResponse), "5", DisplayName = nameof(CompletionResponse))]
	[DataRow(typeof(DefinitionResponse), "[ 42 ]", DisplayName = nameof(DefinitionResponse))]
	[DataRow(typeof(HoverResponse), "[ 1 ]", DisplayName = nameof(HoverResponse))]
	[DataRow(typeof(PublishDiagnosticsParams), "[]", DisplayName = nameof(PublishDiagnosticsParams))]
	[DataRow(typeof(CapabilityUnregistrationParams), "[]", DisplayName = nameof(CapabilityUnregistrationParams))]
	[DataRow(typeof(LocationPayload[]), "5", DisplayName = nameof(LocationPayload) + "[]")]
	[DataRow(typeof(TextEditPayload[]), "[ null ]", DisplayName = nameof(TextEditPayload) + "[]")]
	[DataRow(typeof(WorkspaceDocumentChangePayload[]), "[ null ]", DisplayName = nameof(WorkspaceDocumentChangePayload) + "[]")]
	[DataRow(typeof(DiagnosticPayload[]), "[ null ]", DisplayName = nameof(DiagnosticPayload) + "[]")]
	[DataRow(typeof(WorkspaceConfigurationItemPayload[]), "[ 42 ]", DisplayName = nameof(WorkspaceConfigurationItemPayload) + "[]")]
	[DataRow(typeof(CapabilityRegistrationPayload[]), "5", DisplayName = nameof(CapabilityRegistrationPayload) + "[]")]
	[DataRow(typeof(CapabilityUnregistrationPayload[]), "5", DisplayName = nameof(CapabilityUnregistrationPayload) + "[]")]
	[DataRow(typeof(CodeActionResponse), "[ 42 ]", DisplayName = nameof(CodeActionResponse))]
	[DataRow(typeof(DocumentSymbolResponse), "[ 42 ]", DisplayName = nameof(DocumentSymbolResponse))]
	[DataRow(typeof(SignatureHelpResponse), """{ "signatures": [ 42 ] }""", DisplayName = nameof(SignatureHelpResponse))]
	[DataRow(typeof(SignatureInformationPayload), "42", DisplayName = nameof(SignatureInformationPayload))]
	[DataRow(typeof(MarkupContentPayload), """{ "kind": "markdown" }""", DisplayName = nameof(MarkupContentPayload))]
	public void ForMessages_ReportsAMalformedPayloadToTheGivenLogger(Type payloadType, string malformedJson)
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		JsonSerializerOptions options = ProtocolSerialization.ForMessages(logger);

		_ = JsonSerializer.Deserialize(malformedJson, payloadType, options);

		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void ForMessages_ReadsMemberNamesCaseInsensitively()
	{
		JsonSerializerOptions options = ProtocolSerialization.ForMessages(new RecordingLogger());

		CapabilityUnregistrationPayload[]? payloads = JsonSerializer.Deserialize<CapabilityUnregistrationPayload[]>(
			""" [ { "ID": "1", "METHOD": "workspace/didChangeWatchedFiles" } ] """,
			options
		);

		Assert.IsNotNull(payloads);
		Assert.HasCount(1, payloads);
		Assert.AreEqual("1", payloads[0].Id);
		Assert.AreEqual("workspace/didChangeWatchedFiles", payloads[0].Method);
	}

	[TestMethod]
	public void ForMessages_ReturnsANewInstancePerCall() =>
		Assert.AreNotSame(ProtocolSerialization.ForMessages(new RecordingLogger()), ProtocolSerialization.ForMessages(new RecordingLogger()));

	[TestMethod]
	public void ForPayloads_UsesLowerCamelMemberNames()
	{
		JsonSerializerOptions options = ProtocolSerialization.ForPayloads();

		Assert.AreEqual(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
		Assert.AreEqual("{\"triggerCharacter\":\"x\"}", JsonSerializer.Serialize(new { TriggerCharacter = "x" }, options));
	}

	[TestMethod]
	public void ForPayloads_ReturnsTheSharedInstance() => Assert.AreSame(ProtocolSerialization.ForPayloads(), ProtocolSerialization.ForPayloads());
}
