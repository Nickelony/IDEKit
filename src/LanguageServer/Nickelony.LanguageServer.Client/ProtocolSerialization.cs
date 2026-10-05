using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Creates the serializer options for the two wire formats the client reads and writes.</summary>
/// <remarks>
/// The message options register the tolerant converters of every server message. Each converter reports the payload problems it recovers
/// from to the logger it receives, so those options are built per client instead of being shared statically. The payload options serialize
/// the payloads the client builds itself, with lower-camel member names.
/// </remarks>
internal static class ProtocolSerialization
{
	// The one instance that the client's own payload serialization shares (client capabilities, initialization options, the settings snapshot and
	// outgoing parameters). A caller that extends the options works on a copy, so this instance is never mutated.
	private static readonly JsonSerializerOptions s_payloads = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	/// <summary>Returns the shared options for the payloads that the client serializes itself.</summary>
	/// <remarks>
	/// The instance is shared, so a caller copies it before changing it.
	/// </remarks>
	internal static JsonSerializerOptions ForPayloads() => s_payloads;

	/// <summary>Builds the options that read server messages and write host requests.</summary>
	/// <param name="logger">The logger that receives the payload problems the tolerant converters recover from.</param>
	internal static JsonSerializerOptions ForMessages(ILogger logger)
	{
		var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

		// Normalizes an unset JsonElement member to a JSON null so a host-assigned default(JsonElement) cannot make
		// serialization throw.
		serializerOptions.Converters.Add(new JsonElementJsonConverter());
		serializerOptions.Converters.Add(new CompletionResponseJsonConverter(logger));
		serializerOptions.Converters.Add(new DefinitionResponseJsonConverter(logger));
		serializerOptions.Converters.Add(new HoverResponseJsonConverter(logger));
		serializerOptions.Converters.Add(new PublishDiagnosticsParamsJsonConverter(logger));
		serializerOptions.Converters.Add(new CapabilityUnregistrationParamsJsonConverter(logger));

		// List-valued responses whose entries are not wrapped by a feature response type: one malformed entry must
		// not discard the remaining usable entries. Edit-bearing collections require an array root: an empty
		// collection is a legal outcome for them ("nothing to apply"), so degrading a malformed root to an empty
		// collection would turn a broken payload into a successful no-op.
		serializerOptions.Converters.Add(new TolerantCollectionJsonConverter<LocationPayload>(logger));

		serializerOptions.Converters.Add(
			new TolerantCollectionJsonConverter<TextEditPayload>(
				logger,
				requireArrayRoot: true,
				isUsableElement: static edit => edit.Range is not null
			)
		);

		serializerOptions.Converters.Add(
			new TolerantCollectionJsonConverter<WorkspaceDocumentChangePayload>(
				logger,
				requireArrayRoot: true,
				isUsableElement: static change => change.TextDocument is not null || change.Kind is not null
			)
		);

		// A pull-diagnostics full report carries the same diagnostic entries as a push notification, so it applies
		// the same skip-with-a-warning policy: one malformed diagnostic must not fail the whole report. Its items
		// list keeps a strict root, because an empty full report is a legal "no diagnostics" outcome and degrading
		// a malformed root to an empty list would turn a broken payload into a silent clear.
		serializerOptions.Converters.Add(
			new TolerantCollectionJsonConverter<DiagnosticPayload>(
				logger,
				requireArrayRoot: true,
				isUsableElement: static diagnostic => diagnostic.Range is not null || diagnostic.Message is not null
			)
		);

		// Server-request arrays preserve their request positions: the configuration request/response pair is
		// positional, so a null or malformed entry keeps its slot as a placeholder instead of shifting every
		// later answer. The registration arrays are order-insensitive and keep the skip-with-a-warning policy.
		serializerOptions.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(logger));
		serializerOptions.Converters.Add(new TolerantCollectionJsonConverter<CapabilityRegistrationPayload>(logger));
		serializerOptions.Converters.Add(new TolerantCollectionJsonConverter<CapabilityUnregistrationPayload>(logger));

		// Register the tolerant feature converters with the host logger as well, so the skipped-entry warnings they
		// document are not lost when they are instantiated through their parameterless (attribute) constructors.
		serializerOptions.Converters.Add(new CodeActionResponseJsonConverter(logger));
		serializerOptions.Converters.Add(new DocumentSymbolResponseJsonConverter(logger));
		serializerOptions.Converters.Add(new WorkspaceSymbolResponseJsonConverter(logger));
		serializerOptions.Converters.Add(new SignatureHelpResponseJsonConverter(logger));
		serializerOptions.Converters.Add(new SignatureInformationPayloadJsonConverter(logger));

		// The markup payload is a member of several payloads, so its converter carries the logger for all of them.
		serializerOptions.Converters.Add(new MarkupContentPayloadJsonConverter(logger));

		return serializerOptions;
	}
}
