using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a message notification sent from the language server.</summary>
/// <param name="Type">The message severity, or <see langword="null"/> when the server omitted it.</param>
/// <param name="Message">The message text.</param>
public readonly record struct WindowMessageParams(
	[property: JsonPropertyName("type")] MessageType? Type,
	[property: JsonPropertyName("message")] string? Message
);
