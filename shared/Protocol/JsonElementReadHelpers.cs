using Nickelony.LanguageServer.Client;
using System.Text.Json;

namespace Nickelony.LanguageServer.Protocol;

/// <summary>
/// Provides shared read helpers for tolerant protocol payload parsing.
/// </summary>
/// <remarks>
/// <para>
/// This file is compiled into both the client and the provider framework through a <c>&lt;Compile Include&gt;</c>
/// link, so each assembly carries its own <see langword="internal"/> copy. The helpers are protocol parsing
/// machinery and are not part of any package's public surface; the provider framework's own protocol bridges
/// (for example the markup-content reader) reuse them without exposing client internals.
/// </para>
/// <para>
/// The <see cref="ProtocolPosition"/> the position readers build is a public client payload type, so the shared
/// copy resolves it in either assembly.
/// </para>
/// </remarks>
internal static class JsonElementReadHelpers
{
	/// <summary>
	/// Reads one property of a payload element, preferring an exact name match and falling back to a
	/// case-insensitive match.
	/// </summary>
	/// <param name="element">The payload element to read from.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <param name="value">Receives the property value when the property is present.</param>
	/// <returns>
	/// <see langword="true"/> when the property is present; otherwise, <see langword="false"/>. An element that is
	/// not a JSON object has no properties, so the method reports failure instead of letting
	/// <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> throw.
	/// </returns>
	/// <remarks>
	/// The fallback mirrors the case-insensitive binding the transport applies to reflection-bound members, so a
	/// hand-written member lookup cannot silently lose data that the same serializer would bind for another member.
	/// </remarks>
	internal static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			value = default;
			return false;
		}

		if (element.TryGetProperty(propertyName, out value))
			return true;

		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>
	/// Reads one string property of a payload element while rejecting non-string values.
	/// </summary>
	/// <param name="element">The payload element to read from.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <returns>The string value, or <see langword="null"/> when the property is absent or is not a JSON string.</returns>
	internal static string? TryGetString(JsonElement element, string propertyName)
		=> TryGetProperty(element, propertyName, out JsonElement propertyElement)
			&& propertyElement.ValueKind == JsonValueKind.String
				? propertyElement.GetString()
				: null;

	/// <summary>
	/// Reads one boolean property of a payload element while rejecting non-boolean values.
	/// </summary>
	/// <param name="element">The payload element to read from.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <returns>The boolean value, or <see langword="null"/> when the property is absent or is not a JSON boolean.</returns>
	internal static bool? TryGetBoolean(JsonElement element, string propertyName)
		=> TryGetProperty(element, propertyName, out JsonElement propertyElement)
			&& propertyElement.ValueKind is JsonValueKind.True or JsonValueKind.False
				? propertyElement.GetBoolean()
				: null;

	/// <summary>
	/// Reads one position property of a payload element.
	/// </summary>
	/// <param name="element">The containing payload element.</param>
	/// <param name="propertyName">The property that carries the position object.</param>
	/// <param name="position">Receives the parsed position.</param>
	/// <param name="requireNonNegativeCoordinates">Whether negative coordinates should be rejected.</param>
	/// <returns><see langword="true"/> when the property carries an object with integer coordinates.</returns>
	internal static bool TryReadPosition(JsonElement element, string propertyName, out ProtocolPosition position, bool requireNonNegativeCoordinates = false)
	{
		if (!TryGetProperty(element, propertyName, out JsonElement positionElement))
		{
			position = default;
			return false;
		}

		return TryReadPosition(positionElement, out position, requireNonNegativeCoordinates);
	}

	/// <summary>
	/// Reads one position object.
	/// </summary>
	/// <param name="positionElement">The position object to read.</param>
	/// <param name="position">Receives the parsed position.</param>
	/// <param name="requireNonNegativeCoordinates">Whether negative coordinates should be rejected.</param>
	/// <returns><see langword="true"/> when the element carries an object with integer coordinates.</returns>
	internal static bool TryReadPosition(JsonElement positionElement, out ProtocolPosition position, bool requireNonNegativeCoordinates = false)
	{
		position = default;

		if (positionElement.ValueKind != JsonValueKind.Object
			|| !TryGetProperty(positionElement, "line", out JsonElement lineElement)
			|| lineElement.ValueKind != JsonValueKind.Number
			|| !lineElement.TryGetInt32(out int line)
			|| !TryGetProperty(positionElement, "character", out JsonElement characterElement)
			|| characterElement.ValueKind != JsonValueKind.Number
			|| !characterElement.TryGetInt32(out int character))
		{
			return false;
		}

		if (requireNonNegativeCoordinates && (line < 0 || character < 0))
			return false;

		position = new(line, character);
		return true;
	}
}
