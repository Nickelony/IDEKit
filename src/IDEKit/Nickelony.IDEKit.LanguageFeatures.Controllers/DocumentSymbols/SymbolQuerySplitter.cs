namespace Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

/// <summary>
/// Splits the text typed in a symbol mode into the words that name the symbol and the words that name its container, as VS Code does.
/// </summary>
internal static class SymbolQuerySplitter
{
	/// <summary>Splits a query at its first run of whitespace.</summary>
	/// <param name="query">The text typed after the prefix of the mode.</param>
	/// <returns>
	/// The first word, which names the symbol, and the words after it, which name its container. Both are empty for a blank query, and the second
	/// is empty for a query of one word.
	/// </returns>
	internal static (string Name, string Container) Split(string query)
	{
		string trimmed = query.Trim();

		for (int index = 0; index < trimmed.Length; index++)
		{
			if (char.IsWhiteSpace(trimmed[index]))
				return (trimmed[..index], trimmed[index..].Trim());
		}

		return (trimmed, string.Empty);
	}
}
