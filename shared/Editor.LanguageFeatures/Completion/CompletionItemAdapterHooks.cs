using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;
#if AVALONIAEDIT
using ImageSource = Avalonia.Media.IImage;
#else
using ImageSource = System.Windows.Media.ImageSource;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>Groups the optional host hooks of a <see cref="CompletionItemAdapter"/>.</summary>
/// <remarks>
/// A hook runs on the text area's thread. <see cref="CanCommit"/> and <see cref="AdjustInsertion"/> run inside the editor's commit
/// before the document changes, so an exception that either one throws propagates to the editor and leaves the document unchanged.
/// </remarks>
public sealed class CompletionItemAdapterHooks
{
	/// <summary>The hook that returns an item's icon, or <see langword="null"/> for an item without one.</summary>
	/// <remarks>
	/// The adapter calls it on each read of <see cref="CompletionItemAdapter.Image"/>, so a hook that builds its image caches it.
	/// </remarks>
	public Func<CompletionItem, ImageSource?>? ImageFor { get; init; }

	/// <summary>The hook that answers whether an item may still commit, such as whether the request that produced it is current.</summary>
	/// <remarks>
	/// An answer of <see langword="false"/> declines the commit: the adapter inserts nothing, applies none of the item's additional text edits,
	/// and the editor closes the window as it does after any commit.
	/// </remarks>
	public Func<CompletionItem, bool>? CanCommit { get; init; }

	/// <summary>The hook that rewrites the text an item inserts and the caret inside it, such as to indent a multi-line insertion.</summary>
	/// <remarks>
	/// The adapter passes the snapshot of the document before the commit, the start offset of the range the insertion replaces, and the insertion
	/// that <see cref="CompletionInsertionResolver.Resolve(CompletionItem)"/> returned, with a snippet already expanded. The returned insertion
	/// is what the commit applies, together with the item's additional text edits in one undo unit. A snippet stays navigable only while the returned
	/// text equals the received text.
	/// </remarks>
	public Func<ITextSnapshot, int, CompletionInsertion, CompletionInsertion>? AdjustInsertion { get; init; }
}
