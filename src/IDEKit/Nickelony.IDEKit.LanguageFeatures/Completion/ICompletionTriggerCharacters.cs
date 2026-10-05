namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Describes a completion provider that names the characters which open completion at once when they are typed.</summary>
/// <remarks>
/// The editor completion controller reads the characters from its provider
/// each time a character is typed, so a provider whose characters arrive later, such as a language server that names them once its session starts,
/// answers an empty list until then. An entry is compared verbatim with the one typed character, so an entry longer than one character never matches.
/// A provider without this interface opens completion only on typed word characters.
/// </remarks>
public interface ICompletionTriggerCharacters
{
	/// <summary>The characters that open completion when typed, or an empty list when the provider names none.</summary>
	IReadOnlyList<string> TriggerCharacters { get; }
}
