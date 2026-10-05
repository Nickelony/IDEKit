namespace Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

/// <summary>
/// Describes a signature help provider that names the characters which open signature help, and those which refresh it, when typed.
/// </summary>
/// <remarks>
/// The editor signature help wiring reads the characters from its provider
/// each time a character is typed, so a provider whose characters arrive later, such as a language server that names them once its session starts,
/// answers empty lists until then. An entry is compared verbatim with the one typed character, so an entry longer than one character never matches.
/// </remarks>
public interface ISignatureHelpTriggerCharacters
{
	/// <summary>The characters that open signature help when typed, or an empty list when the provider names none.</summary>
	IReadOnlyList<string> TriggerCharacters { get; }

	/// <summary>The characters that refresh signature help that is already showing, or an empty list when the provider names none.</summary>
	/// <remarks>
	/// A trigger character refreshes showing signature help too, so the list holds only the characters that do not open it.
	/// </remarks>
	IReadOnlyList<string> RetriggerCharacters { get; }
}
