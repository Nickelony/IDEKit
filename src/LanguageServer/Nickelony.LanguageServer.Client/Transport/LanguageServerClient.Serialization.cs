using System.Text.Json;

namespace Nickelony.LanguageServer.Client;

public sealed partial class LanguageServerClient
{
	// The single options instance the client's own payload serialization uses (client capabilities, initialization
	// options, and the settings snapshot). A caller that needs to extend the options gets its own copy from
	// CreateProtocolSerializerOptions, so this instance is never mutated and stays safe to share.
	internal static JsonSerializerOptions ProtocolSerializerOptions { get; } = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	/// <summary>
	/// Creates serializer options that match the protocol payload convention this client uses: lower-camel member
	/// names, as the language-server protocol wire format requires.
	/// </summary>
	/// <returns>A new options instance owned by the caller.</returns>
	/// <remarks>
	/// <para>
	/// The client applies this naming policy to the host payloads it serializes itself (client capabilities,
	/// initialization options, and the settings snapshot), and its built-in protocol payload records declare their
	/// wire names explicitly with <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/>. A host that
	/// sends a custom request or notification, or a payload built from its own DTO, must reach the same wire casing;
	/// serialize that payload with these options before passing it to <see cref="SendRequestAsync{TResult}"/> or
	/// <see cref="SendNotificationAsync"/>, for example:
	/// <c>client.SendRequestAsync&lt;JsonElement&gt;("vendor/method", JsonSerializer.SerializeToNode(payload, LanguageServerClient.CreateProtocolSerializerOptions())!, cancellationToken)</c>.
	/// </para>
	/// <para>
	/// A fresh instance is returned on every call, so a host can extend the options (for example with a converter)
	/// without changing what the client itself uses.
	/// </para>
	/// </remarks>
	public static JsonSerializerOptions CreateProtocolSerializerOptions()
		=> new(ProtocolSerializerOptions);
}
