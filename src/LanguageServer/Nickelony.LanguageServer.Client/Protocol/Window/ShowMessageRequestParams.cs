using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a <c>window/showMessageRequest</c> request: a message that asks the user to choose one of its actions.</summary>
/// <param name="Type">The message severity, or <see langword="null"/> when the server omitted it.</param>
/// <param name="Message">The message text.</param>
/// <param name="Actions">The actions the user can choose from, or <see langword="null"/> when the server offers none.</param>
public readonly record struct ShowMessageRequestParams(
	[property: JsonPropertyName("type")] MessageType? Type,
	[property: JsonPropertyName("message")] string? Message,
	[property: JsonPropertyName("actions")] IReadOnlyList<MessageActionItemPayload>? Actions
);

/// <summary>Represents one action of a <see cref="ShowMessageRequestParams"/>, and the answer that selects it.</summary>
/// <param name="Title">The action title, which is also the answer's identity.</param>
public readonly record struct MessageActionItemPayload([property: JsonPropertyName("title")] string? Title);
