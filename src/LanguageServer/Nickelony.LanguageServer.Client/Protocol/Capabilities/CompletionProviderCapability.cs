using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents completion-specific capabilities advertised by the server.</summary>
internal readonly record struct CompletionProviderCapability
{
	/// <summary>Whether the server supports completion-item resolve, or <see langword="null"/> when the server did not advertise the flag.</summary>
	[JsonPropertyName("resolveProvider")]
	public bool? ResolveProvider { get; init; }

	/// <summary>The characters that open completion when typed, or <see langword="null"/> when the server named none.</summary>
	/// <remarks>
	/// A malformed entry is skipped, so one bad character does not fail the handshake.
	/// </remarks>
	[JsonPropertyName("triggerCharacters")]
	[JsonConverter(typeof(TolerantCollectionJsonConverter<string>))]
	public IReadOnlyList<string>? TriggerCharacters { get; init; }
}
