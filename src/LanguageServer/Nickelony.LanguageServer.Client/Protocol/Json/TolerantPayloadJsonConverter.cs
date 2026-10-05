using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Provides the shared logger plumbing and tolerant element reading for the client's payload converters.</summary>
/// <typeparam name="T">The payload type the converter reads and writes.</typeparam>
/// <remarks>
/// Only reading is tolerant; each derived converter owns its own writing contract.
/// </remarks>
internal abstract class TolerantPayloadJsonConverter<T> : JsonConverter<T>
{
	/// <summary>Initializes a <see cref="TolerantPayloadJsonConverter{T}"/> that records no malformed-payload diagnostics.</summary>
	protected TolerantPayloadJsonConverter()
		: this(NullLogger.Instance) { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	protected TolerantPayloadJsonConverter(ILogger? logger) => Logger = logger ?? NullLogger.Instance;

	/// <summary>The logger used for malformed-payload diagnostics.</summary>
	protected ILogger Logger { get; }

	/// <summary>
	/// Deserializes one array element tolerantly, treating a JSON <see langword="null"/> or an unreadable element as a skip and reporting
	/// it with a warning.
	/// </summary>
	/// <typeparam name="TElement">The element type to deserialize.</typeparam>
	/// <param name="element">The array element to deserialize.</param>
	/// <param name="options">The serializer options that carry the element converters.</param>
	/// <param name="elementDescription">A short element description used in the diagnostic, for example <c>diagnostic</c>.</param>
	/// <param name="value">Receives the deserialized element when it binds to a non-<see langword="null"/> value.</param>
	/// <returns><see langword="true"/> when the element bound to a non-<see langword="null"/> value; otherwise, <see langword="false"/>.</returns>
	protected bool TryReadElement<TElement>(
		JsonElement element,
		JsonSerializerOptions options,
		string elementDescription,
		[NotNullWhen(true)] out TElement? value
	)
		where TElement : class
	{
		try
		{
			value = element.Deserialize<TElement>(options);
		}
		catch (Exception exception) when (exception is JsonException or InvalidOperationException)
		{
			Logger.LogWarning(exception, "Skipping a malformed {Element} element because its payload could not be deserialized.", elementDescription);

			value = null;
			return false;
		}

		if (value is null)
		{
			Logger.LogWarning("Skipping a {Element} element because its JSON value is null.", elementDescription);
			return false;
		}

		return true;
	}
}
