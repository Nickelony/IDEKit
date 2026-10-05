using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Builds protocol payloads from anonymous JSON shapes, the way a server response arrives.</summary>
internal static class TestPayloads
{
	/// <summary>Serializes a shape and deserializes it as the payload type.</summary>
	/// <typeparam name="TPayload">The payload type.</typeparam>
	/// <param name="shape">The anonymous object that mirrors the wire JSON.</param>
	/// <returns>The deserialized payload.</returns>
	internal static TPayload Deserialize<TPayload>(object shape)
	{
		return JsonSerializer.Deserialize<TPayload>(JsonSerializer.Serialize(shape))
			?? throw new InvalidOperationException($"The {typeof(TPayload).Name} test payload deserialized to null.");
	}
}
