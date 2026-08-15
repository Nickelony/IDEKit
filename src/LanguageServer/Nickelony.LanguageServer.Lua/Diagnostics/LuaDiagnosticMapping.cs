namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Supplies Lua's language-specific rules to the shared diagnostics mapping pipeline.
/// </summary>
internal static class LuaDiagnosticMapping
{
	/// <summary>
	/// Gets the diagnostic mapping policy for LuaLS payloads: the Lua fallback message and the Lua word rule.
	/// </summary>
	internal static DiagnosticMappingPolicy Policy { get; } = new("Unknown Lua diagnostic.", IsWordCharacter);

	/// <summary>
	/// Determines whether <paramref name="character"/> is part of a word for diagnostic range expansion.
	/// The rule extends the identifier rule with <c>.</c>, <c>:</c>, <c>'</c>, and <c>"</c>, so member
	/// accesses and quoted names resolve as one selectable run.
	/// </summary>
	/// <param name="character">The character to test.</param>
	/// <returns><see langword="true"/> when the character is part of a Lua word.</returns>
	private static bool IsWordCharacter(char character)
		=> char.IsLetterOrDigit(character) || character is '_' or '.' or ':' or '\'' or '"';
}
