using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Describes completion data that declares the characters which accept the item when they are typed while the item is selected.</summary>
/// <remarks>
/// The editor completion presenter reads the declared characters from the selected item when a character is typed: a declared character
/// accepts the item in one input step, and an entry that cannot match a single typed character (entries are compared verbatim) is never used.
/// The default data exposes its item's <see cref="CompletionItem.CommitCharacters"/>; custom data opts in by implementing this interface,
/// and data without it is never accepted on a character.
/// </remarks>
public interface ICompletionCommitCharacters
{
	/// <summary>
	/// The characters that accept this item when they are typed while it is selected, or an empty list when the item declares none.
	/// </summary>
	IReadOnlyList<string> CommitCharacters { get; }
}
