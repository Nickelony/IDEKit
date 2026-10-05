#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Provides a minimal completion item for tests that never exercise completion insertion; the item can opt
/// into preselection through <see cref="ICompletionPreselection"/>, declare commit characters through <see cref="ICompletionCommitCharacters"/>
/// (an empty set by default), and supply an image for measurement tests.
/// </summary>
/// <param name="text">The item text.</param>
/// <param name="description">The item's synchronous description, or <see langword="null"/> for none.</param>
/// <param name="image">The item's image, or <see langword="null"/> when the item supplies none.</param>
/// <param name="commitCharacters">The characters that accept the item when typed, or <see langword="null"/> when the item declares none.</param>
internal sealed class StubCompletionData(
	string text,
	object? description = null,
#if AVALONIAEDIT
	IImage? image = null,
#else
	ImageSource? image = null,
#endif
	IReadOnlyList<string>? commitCharacters = null
) : ICompletionData, ICompletionPreselection, ICompletionCommitCharacters
{
	/// <inheritdoc/>
#if AVALONIAEDIT
	public IImage? Image => image;
#else
	public ImageSource? Image => image;
#endif

	/// <inheritdoc/>
	public string Text => text;

	/// <inheritdoc/>
	public object Content => text;

	/// <inheritdoc/>
	public object Description => description!;

	/// <inheritdoc/>
	public double Priority => 0.0;

	/// <inheritdoc/>
	public bool IsPreselected { get; set; }

	/// <inheritdoc/>
	public IReadOnlyList<string> CommitCharacters => commitCharacters ?? [];

	/// <inheritdoc/>
	public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
		textArea.Document.Replace(completionSegment, text);
}
