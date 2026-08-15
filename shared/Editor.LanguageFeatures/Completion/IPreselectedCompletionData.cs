#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Describes completion data that declares whether the producer preselected it.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="TextCompletionController"/> reads the flag after an open or an in-place refresh and
/// selects a preselected item that survived the list's filtering, because the flag is the producer's
/// explicit answer to the query and overrides the editor's best-match ranking. A host that maps items
/// to custom completion data implements this interface to opt into the same policy, like
/// <see cref="ICommitCharacterCompletionData"/>; data without it is never preselected by the library.
/// </para>
/// <para>
/// The package's default <see cref="TextCompletionItemCompletionData"/> implements this interface by
/// exposing its item's <see cref="Nickelony.IDEKit.IntelliSense.Completion.TextCompletionItem.IsPreselected"/>
/// flag.
/// </para>
/// </remarks>
public interface IPreselectedCompletionData
{
	/// <summary>
	/// Gets a value indicating whether the item should be selected over the best-matching item when it
	/// survives the list's filtering.
	/// </summary>
	bool IsPreselected { get; }
}
