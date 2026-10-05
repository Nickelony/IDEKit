using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a single text edit returned by the language server.</summary>
/// <param name="Range">The replaced document range.</param>
/// <param name="NewText">The replacement text.</param>
/// <param name="AnnotationId">The change-annotation identifier when the edit is an annotated text edit, or <see langword="null"/>.</param>
/// <remarks>
/// <paramref name="Range"/> and <paramref name="NewText"/> stay nullable although LSP marks them required, so a missing member does not fail
/// deserialization of an entire response. Hosts treat an edit with a <see langword="null"/> member as unusable.
/// </remarks>
public readonly record struct TextEditPayload(
	[property: JsonPropertyName("range")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RangePayload? Range,
	[property: JsonPropertyName("newText")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NewText,
	[property: JsonPropertyName("annotationId")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AnnotationId = null
);
