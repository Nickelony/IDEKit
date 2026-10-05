using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Reads and writes wire payloads with the message serializer options that the client uses.</summary>
internal static class WireJsonTestHelpers
{
	private static readonly JsonSerializerOptions s_messageOptions = ProtocolSerialization.ForMessages(NullLogger.Instance);

	internal static T Deserialize<T>(string json) =>
		JsonSerializer.Deserialize<T>(json, s_messageOptions) ?? throw new InvalidOperationException("The test payload deserialized to null.");

	internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, s_messageOptions);
}
