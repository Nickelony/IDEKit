using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Builds and deserializes the wire payloads that the response-parser tests give to the parsers.</summary>
internal static class PayloadTestHelpers
{
	internal static CompletionItemPayload CreateCompletionItem(
		string label,
		int kind,
		string? detail,
		string? documentation,
		string? insertText = null
	)
	{
		return DeserializeCompletionItemPayload(
			new Dictionary<string, object?>
			{
				["label"] = label,
				["kind"] = kind,
				["detail"] = detail,
				["documentation"] = documentation,
				["insertText"] = insertText ?? label,
				["filterText"] = label,
			}
		);
	}

	internal static CompletionItemPayload DeserializeCompletionItemPayload(object payload)
	{
		return JsonSerializer.Deserialize<CompletionItemPayload>(JsonSerializer.Serialize(payload))
			?? throw new InvalidOperationException("Failed to deserialize the completion-item test payload.");
	}

	internal static HoverResponse DeserializeHoverResponse(object payload)
	{
		return JsonSerializer.Deserialize<HoverResponse>(JsonSerializer.Serialize(payload))
			?? throw new InvalidOperationException("Failed to deserialize the hover response test payload.");
	}

	internal static SignatureHelpResponse? DeserializeSignatureHelpResponse(object payload) =>
		JsonSerializer.Deserialize<SignatureHelpResponse>(JsonSerializer.Serialize(payload));

	internal static TextEditPayload[]? DeserializeTextEdits(object payload) =>
		JsonSerializer.Deserialize<TextEditPayload[]>(JsonSerializer.Serialize(payload));

	internal static DefinitionResponse DeserializeDefinitionResponse(object payload)
	{
		return JsonSerializer.Deserialize<DefinitionResponse>(JsonSerializer.Serialize(payload))
			?? throw new InvalidOperationException("Failed to deserialize the definition response test payload.");
	}

	internal static DocumentSymbolResponse? DeserializeDocumentSymbolResponse(object payload) =>
		JsonSerializer.Deserialize<DocumentSymbolResponse>(JsonSerializer.Serialize(payload));

	internal static CodeActionResponse? DeserializeCodeActionResponse(object payload) =>
		JsonSerializer.Deserialize<CodeActionResponse>(JsonSerializer.Serialize(payload));

	internal static LocationPayload[]? DeserializeReferenceLocations(object payload) =>
		JsonSerializer.Deserialize<LocationPayload[]>(JsonSerializer.Serialize(payload));
}
