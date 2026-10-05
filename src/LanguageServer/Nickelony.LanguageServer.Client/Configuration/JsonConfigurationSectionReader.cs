using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Resolves dotted workspace/configuration section paths against a serialized settings payload.</summary>
internal static class JsonConfigurationSectionReader
{
	/// <summary>Extracts a nested configuration section from a serialized settings payload.</summary>
	/// <param name="settingsElement">The serialized root settings element.</param>
	/// <param name="section">The dotted configuration section path.</param>
	/// <returns>The extracted section element, or <see langword="null"/> when the section is missing.</returns>
	/// <remarks>
	/// A blank section returns the whole settings payload. The section is split on <c>.</c> and each segment is resolved
	/// as a nested object property, so a payload that stores a dotted section as one flat property name does not match. Property lookup prefers
	/// an exact name match and otherwise falls back to a case-insensitive match per path segment; when several properties differ only in casing,
	/// the first one in document order wins. The returned element is a view into the supplied payload rather than a detached copy, so the caller
	/// must keep the source document alive while it reads the result.
	/// </remarks>
	internal static JsonElement? GetSection(JsonElement settingsElement, string? section)
	{
		if (string.IsNullOrWhiteSpace(section))
			return settingsElement;

		JsonElement currentSection = settingsElement;
		string[] parts = section.Split('.');

		for (int i = 0; i < parts.Length; i++)
		{
			if (
				currentSection.ValueKind is not JsonValueKind.Object
				|| !JsonElementReader.TryGetProperty(currentSection, parts[i], out JsonElement nextSection)
			)
			{
				return null;
			}

			currentSection = nextSection;
		}

		return currentSection;
	}
}
