using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a <c>workspace/applyEdit</c> request: an edit that the server asks the client to apply to the workspace.</summary>
/// <param name="Label">The label that describes the edit to the user, or <see langword="null"/> when the server sent none.</param>
/// <param name="Edit">The edit to apply.</param>
public readonly record struct ApplyWorkspaceEditParams(
	[property: JsonPropertyName("label")] string? Label,
	[property: JsonPropertyName("edit")] WorkspaceEditResponse Edit
);

/// <summary>Represents the answer to a <c>workspace/applyEdit</c> request.</summary>
/// <param name="Applied">Whether the client applied the edit.</param>
/// <param name="FailureReason">Why the client did not apply the edit, or <see langword="null"/> when it did.</param>
public readonly record struct ApplyWorkspaceEditResponse(
	[property: JsonPropertyName("applied")] bool Applied,
	[property: JsonPropertyName("failureReason")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureReason = null
);
