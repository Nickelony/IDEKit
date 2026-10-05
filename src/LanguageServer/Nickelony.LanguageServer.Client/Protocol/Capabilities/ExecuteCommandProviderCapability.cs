using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the <c>workspace/executeCommand</c> capability that the server advertises.</summary>
internal readonly record struct ExecuteCommandProviderCapability
{
	/// <summary>The commands the server executes, or <see langword="null"/> when the server named none.</summary>
	/// <remarks>
	/// A malformed entry is skipped, so one bad command does not fail the handshake.
	/// </remarks>
	[JsonPropertyName("commands")]
	[JsonConverter(typeof(TolerantCollectionJsonConverter<string>))]
	public IReadOnlyList<string>? Commands { get; init; }
}
