using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents one usable code-action entry returned by a language server.</summary>
/// <remarks>
/// Every entry has a title. <c>edit</c> and <c>command</c> can each be present. <c>data</c> is an opaque detached clone
/// for <c>codeAction/resolve</c>.
/// </remarks>
public sealed record CodeActionPayload
{
	/// <summary>The human-readable action title.</summary>
	[JsonPropertyName("title")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Title { get; init; }

	/// <summary>The protocol action kind (for example <c>quickfix</c>), or <see langword="null"/> when the server omitted it.</summary>
	[JsonPropertyName("kind")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Kind { get; init; }

	/// <summary>Whether the server marks this action as preferred, or <see langword="null"/> when the server omitted the flag.</summary>
	[JsonPropertyName("isPreferred")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? IsPreferred { get; init; }

	/// <summary>The workspace edit the action applies, or <see langword="null"/> when the action carries only a command.</summary>
	[JsonPropertyName("edit")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public WorkspaceEditResponse? Edit { get; init; }

	/// <summary>The command the server attached to the action, or <see langword="null"/> when the action carries no command.</summary>
	/// <remarks>
	/// An action may carry an edit and a command; the command is expected to run after the edit is applied.
	/// </remarks>
	[JsonPropertyName("command")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CommandPayload? Command { get; init; }

	/// <summary>
	/// The opaque server state the action carries for <c>codeAction/resolve</c>, or <see langword="null"/> when the server sent none.
	/// </summary>
	/// <remarks>
	/// The element is a detached clone and stays valid after its source document is disposed; hosts that resolve actions should send
	/// it back unchanged.
	/// </remarks>
	[JsonPropertyName("data")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[JsonConverter(typeof(JsonElementJsonConverter))]
	public JsonElement? Data { get; init; }

	/// <summary>The diagnostics the action addresses, or <see langword="null"/> when the server omitted them.</summary>
	/// <remarks>
	/// An attached empty list stays an empty list.
	/// </remarks>
	[JsonPropertyName("diagnostics")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<DiagnosticPayload>? Diagnostics { get; init; }

	/// <summary>The disabled state the server attached to the action, or <see langword="null"/> when the action is enabled.</summary>
	/// <remarks>
	/// A disabled action stays representable so hosts can gray it out or withhold it.
	/// </remarks>
	[JsonPropertyName("disabled")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CodeActionDisabledPayload? Disabled { get; init; }
}

/// <summary>Represents the command of a code action: the identifier to execute plus its optional title and arguments.</summary>
/// <param name="Title">The command title, or <see langword="null"/> when the server omitted it.</param>
/// <param name="Command">The command identifier to execute.</param>
/// <param name="Arguments">The command arguments in wire order, or <see langword="null"/> when the server sent none.</param>
public readonly record struct CommandPayload(
	[property: JsonPropertyName("title")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title,
	[property: JsonPropertyName("command")] string? Command,

	[property: JsonPropertyName("arguments")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		IReadOnlyList<JsonElement>? Arguments
);

/// <summary>Marks a code action as disabled together with the reason the server supplied.</summary>
/// <param name="Reason">The user-facing reason the action is disabled.</param>
public readonly record struct CodeActionDisabledPayload([property: JsonPropertyName("reason")] string? Reason);
