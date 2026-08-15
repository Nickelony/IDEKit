namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// Supplies the Roslyn family's language-specific rules to the shared diagnostics mapping pipeline.
/// </summary>
internal static class RoslynDiagnosticMapping
{
	/// <summary>
	/// Gets the diagnostic mapping policy for Roslyn payloads: the fallback message and the identifier word rule
	/// shared by C# and Visual Basic.
	/// </summary>
	internal static DiagnosticMappingPolicy Policy { get; } = new("Unknown diagnostic.", IsWordCharacter);

	/// <summary>
	/// Determines whether <paramref name="character"/> is part of a word for diagnostic range expansion.
	/// The rule extends the identifier rule with <c>.</c>, so a dotted member access resolves as one selectable
	/// run; the Roslyn language server reports precise ranges, so this only widens the degenerate-range fallback.
	/// </summary>
	/// <param name="character">The character to test.</param>
	/// <returns><see langword="true"/> when the character is part of a word.</returns>
	private static bool IsWordCharacter(char character)
		=> char.IsLetterOrDigit(character) || character is '_' or '.';
}
