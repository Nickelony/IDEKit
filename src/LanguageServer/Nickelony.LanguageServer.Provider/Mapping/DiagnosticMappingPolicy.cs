namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Supplies the language-specific parts of diagnostic mapping.
/// </summary>
/// <param name="UnknownMessage">The displayed message for a diagnostic whose server message is blank.</param>
/// <param name="IsWordCharacter">
/// Determines whether a character is part of a word for the range anchor that a malformed range falls back to.
/// </param>
/// <remarks>
/// Everything else in <see cref="DiagnosticsParser"/> is protocol-level and shared; only these two rules vary
/// between languages, so a provider spells out its own policy while the mapping pipeline stays in the framework.
/// </remarks>
internal sealed record DiagnosticMappingPolicy(string UnknownMessage, Func<char, bool> IsWordCharacter)
{
	/// <summary>
	/// Gets the language-neutral policy: a fixed fallback message and the identifier word rule
	/// (letters, digits, and <c>_</c>).
	/// </summary>
	internal static DiagnosticMappingPolicy Default { get; } = new("Unknown diagnostic.", DefaultIsWordCharacter);

	/// <summary>
	/// Determines whether <paramref name="character"/> is part of an identifier word.
	/// </summary>
	/// <param name="character">The character to test.</param>
	/// <returns><see langword="true"/> when the character is a letter, digit, or underscore.</returns>
	internal static bool DefaultIsWordCharacter(char character)
		=> char.IsLetterOrDigit(character) || character == '_';
}
