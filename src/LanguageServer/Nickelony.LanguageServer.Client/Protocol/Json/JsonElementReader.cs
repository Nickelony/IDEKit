using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Provides shared read helpers for tolerant protocol payload parsing.</summary>
internal static class JsonElementReader
{
	/// <summary>Reads one property of a payload element, preferring an exact name match and falling back to a case-insensitive match.</summary>
	/// <param name="element">The payload element to read from.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <param name="value">Receives the property value when the property is present.</param>
	/// <returns><see langword="true"/> when the property is present; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// An element that is not a JSON object has no properties, so the method reports failure instead of letting
	/// <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> throw.
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

	/// <summary>Reads one string property of a payload element while rejecting non-string values.</summary>
	/// <param name="element">The payload element to read from.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <returns>The string value, or <see langword="null"/> when the property is absent or is not a JSON string.</returns>
	internal static string? GetString(JsonElement element, string propertyName)
	{
		return TryGetProperty(element, propertyName, out JsonElement propertyElement) && propertyElement.ValueKind == JsonValueKind.String
			? propertyElement.GetString()
			: null;
	}

	/// <summary>Reads one boolean property of a payload element while rejecting non-boolean values.</summary>
	/// <param name="element">The payload element to read from.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <returns>The boolean value, or <see langword="null"/> when the property is absent or is not a JSON boolean.</returns>
	internal static bool? GetBoolean(JsonElement element, string propertyName)
	{
		return
			TryGetProperty(element, propertyName, out JsonElement propertyElement)
			&& propertyElement.ValueKind is JsonValueKind.True or JsonValueKind.False
			? propertyElement.GetBoolean()
			: null;
	}
}
