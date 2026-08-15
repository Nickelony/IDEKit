using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents one change annotation attached to a workspace edit.
/// </summary>
/// <param name="Label">The human-readable label shown for the annotation.</param>
/// <param name="NeedsConfirmation">Whether applying the annotated change requires confirmation.</param>
/// <param name="Description">The optional description of the annotated change.</param>
public readonly record struct WorkspaceEditChangeAnnotationPayload(
	[property: JsonPropertyName("label")] string? Label,
	[property: JsonPropertyName("needsConfirmation")] bool? NeedsConfirmation,
	[property: JsonPropertyName("description")] string? Description);
