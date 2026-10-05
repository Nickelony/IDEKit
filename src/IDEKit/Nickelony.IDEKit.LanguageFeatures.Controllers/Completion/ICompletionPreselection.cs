using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Describes completion data that declares whether the producer preselected it.</summary>
/// <remarks>
/// The editor completion presenter reads the flag after an open or an in-place refresh and selects a preselected
/// item that survived the list's filtering, because the producer's explicit answer overrides the editor's best-match ranking. The default data
/// exposes its item's <see cref="CompletionItem.IsPreselected"/>; custom data opts in like <see cref="ICompletionCommitCharacters"/>,
/// and data without it is never preselected.
/// </remarks>
public interface ICompletionPreselection
{
	/// <summary>Whether the item should be selected over the best-matching item when it survives the list's filtering.</summary>
	bool IsPreselected { get; }
}
