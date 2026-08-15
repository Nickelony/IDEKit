#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Describes completion data that declares the characters which accept the item when they are typed
/// while the item is selected.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="TextCompletionController"/> reads the declared characters from the selected item
/// when a character is typed: a declared character accepts the item while being typed, in one input
/// step (the commit-character policy, controlled by
/// <see cref="TextCompletionControllerOptions.AcceptOnCommitCharacters"/>). The package's default
/// <see cref="TextCompletionItemCompletionData"/> implements this interface by exposing its item's
/// <see cref="Nickelony.IDEKit.IntelliSense.Completion.TextCompletionItem.CommitCharacters"/>; a
/// host that maps items to custom completion data implements the interface to opt into the same
/// policy, and data without it is never accepted on a character.
/// </para>
/// <para>
/// Entries are compared verbatim against the typed character. A protocol-conforming provider sends
/// single-character entries; an entry that cannot match a single typed character is never used.
/// </para>
/// </remarks>
public interface ICommitCharacterCompletionData
{
	/// <summary>
	/// Gets the characters that accept this item when they are typed while it is selected, or an
	/// empty list when the item declares none.
	/// </summary>
	IReadOnlyList<string> CommitCharacters { get; }
}
