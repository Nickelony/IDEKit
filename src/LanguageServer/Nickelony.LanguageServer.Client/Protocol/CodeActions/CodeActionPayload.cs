using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents one usable code-action entry returned by a language server.
/// </summary>
/// <remarks>
/// The tolerant response converter keeps every entry that has a usable title, so the host owns both the
/// <c>workspace/executeCommand</c> and the <c>codeAction/resolve</c> policy. An action that carries both an edit
/// and a command keeps both because a host may execute the command after applying the edit. The edit stays a
/// <see cref="WorkspaceEditResponse"/> so resource operations remain representable for the provider to fail the
/// affected action closed. The opaque <c>data</c> member is preserved as a detached clone, so a resolve-only
/// action (title, kind, and data without an edit or command) is representable; hosts that resolve actions send
/// the data back unchanged.
/// </remarks>
public sealed class CodeActionPayload
{
	/// <summary>
	/// Gets the human-readable action title.
	/// </summary>
	[JsonPropertyName("title")]
	public string? Title { get; init; }

	/// <summary>
	/// Gets the protocol action kind (for example <c>quickfix</c>), or <see langword="null"/> when the
	/// server omitted it.
	/// </summary>
	[JsonPropertyName("kind")]
	public string? Kind { get; init; }

	/// <summary>
	/// Gets a value indicating whether the server marks this action as preferred, or
	/// <see langword="null"/> when the server omitted the flag.
	/// </summary>
	[JsonPropertyName("isPreferred")]
	public bool? IsPreferred { get; init; }

	/// <summary>
	/// Gets the workspace edit the action applies, or <see langword="null"/> when the action carries only a command.
	/// </summary>
	[JsonPropertyName("edit")]
	public WorkspaceEditResponse? Edit { get; init; }

	/// <summary>
	/// Gets the command the server attached to the action, or <see langword="null"/> when the action carries no
	/// command. An action may carry both an edit and a command; the command is expected to run after the edit is
	/// applied.
	/// </summary>
	[JsonPropertyName("command")]
	public CodeActionCommandPayload? Command { get; init; }

	/// <summary>
	/// Gets the opaque server state the action carries for <c>codeAction/resolve</c>, or <see langword="null"/>
	/// when the server sent none. The element is a detached clone and stays valid after its source document is
	/// disposed; hosts that resolve actions should send it back unchanged.
	/// </summary>
	[JsonPropertyName("data")]
	public JsonElement? Data { get; init; }

	/// <summary>
	/// Gets the diagnostics the action addresses, or <see langword="null"/> when the server omitted them.
	/// An attached empty list stays an empty list.
	/// </summary>
	[JsonPropertyName("diagnostics")]
	public IReadOnlyList<DiagnosticPayload>? Diagnostics { get; init; }

	/// <summary>
	/// Gets the disabled state the server attached to the action, or <see langword="null"/> when the action is
	/// enabled. A disabled action stays representable so hosts can gray it out or withhold it.
	/// </summary>
	[JsonPropertyName("disabled")]
	public CodeActionDisabledPayload? Disabled { get; init; }
}

/// <summary>
/// Represents the command of a code action: the identifier to execute plus its optional title and arguments.
/// </summary>
/// <param name="Title">The command title, or <see langword="null"/> when the server omitted it.</param>
/// <param name="Command">The command identifier to execute.</param>
/// <param name="Arguments">The command arguments in wire order, or <see langword="null"/> when the server sent none.</param>
public readonly record struct CodeActionCommandPayload(
	[property: JsonPropertyName("title")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Title,
	[property: JsonPropertyName("command")]
	string? Command,
	[property: JsonPropertyName("arguments")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<JsonElement>? Arguments);

/// <summary>
/// Marks a code action as disabled together with the reason the server supplied.
/// </summary>
/// <param name="Reason">The user-facing reason the action is disabled.</param>
public readonly record struct CodeActionDisabledPayload(
	[property: JsonPropertyName("reason")] string? Reason);
