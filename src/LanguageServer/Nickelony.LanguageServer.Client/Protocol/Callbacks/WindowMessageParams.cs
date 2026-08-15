using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a message notification sent from the language server.
/// </summary>
/// <param name="Type">
/// The typed message severity, or <see langword="null"/> when the server omitted it. The protocol requires the
/// field; the callback target maps a missing or unknown value to the least intrusive <see cref="MessageType.Log"/>
/// level. An unknown protocol value stays representable as an unnamed <see cref="MessageType"/> value.
/// </param>
/// <param name="Message">The message text.</param>
internal readonly record struct WindowMessageParams(
	[property: JsonPropertyName("type")] MessageType? Type,
	[property: JsonPropertyName("message")] string? Message);
