namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>
/// The single home for the custom-identifier rule shared by the open-vocabulary completion types: an identifier is trimmed and must not be blank,
/// and a well-known identifier is reserved.
/// </summary>
/// <remarks>
/// <see cref="CompletionItemKind"/> and <see cref="CompletionContext"/> share only these rules; a shared base type for the open-vocabulary types
/// waits for a third open vocabulary.
/// </remarks>
internal static class IdentifierVocabulary
{
	// Validates that an identifier is not null or blank and returns it with surrounding whitespace
	// trimmed; the rule is identical for every open-vocabulary type.
	internal static string Normalize(string identifier)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		return identifier.Trim();
	}

	// Reports whether a normalized identifier matches a reserved well-known identifier, ignoring case
	// so a differently-cased spelling of the well-known name is still rejected.
	internal static bool IsReserved(string normalizedIdentifier, string reservedIdentifier) =>
		string.Equals(normalizedIdentifier, reservedIdentifier, StringComparison.OrdinalIgnoreCase);
}
