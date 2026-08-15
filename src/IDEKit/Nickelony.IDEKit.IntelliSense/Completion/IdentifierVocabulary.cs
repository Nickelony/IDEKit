namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// The single home for the custom-identifier rule shared by the open-vocabulary completion types
/// (<see cref="TextCompletionItemKind"/> and <see cref="TextCompletionTrigger"/>): an identifier is
/// trimmed and must not be blank, and a well-known identifier is reserved.
/// </summary>
internal static class IdentifierVocabulary
{
	// Validates that an identifier is not null or blank and returns it with surrounding whitespace
	// trimmed. The rule is identical for every open-vocabulary type, so a custom identifier is
	// normalized the same way regardless of which vocabulary it belongs to.
	public static string Normalize(string identifier)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

		return identifier.Trim();
	}

	// Reports whether a normalized identifier matches a reserved well-known identifier, ignoring case
	// so a differently-cased spelling of the well-known name is still rejected.
	public static bool IsReserved(string normalizedIdentifier, string reservedIdentifier)
		=> string.Equals(normalizedIdentifier, reservedIdentifier, StringComparison.OrdinalIgnoreCase);
}
