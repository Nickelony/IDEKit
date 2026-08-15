using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a single text edit returned by the language server.
/// </summary>
/// <param name="Range">The replaced document range.</param>
/// <param name="NewText">The replacement text.</param>
/// <param name="AnnotationId">The change-annotation identifier when the edit is an annotated text edit, or <see langword="null"/>.</param>
public readonly record struct TextEditPayload(
	[property: JsonPropertyName("range")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	ProtocolRangePayload? Range,
	[property: JsonPropertyName("newText")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? NewText,
	[property: JsonPropertyName("annotationId")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? AnnotationId = null);
