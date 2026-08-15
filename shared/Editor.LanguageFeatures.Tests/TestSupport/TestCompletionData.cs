#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Media;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Provides a minimal completion item for tests that never exercise completion insertion; the item can
/// opt into preselection through <see cref="IPreselectedCompletionData"/> and commit characters through
/// <see cref="ICommitCharacterCompletionData"/> - an empty set (the default) declares none - and supply
/// an image for measurement tests.
/// </summary>
/// <param name="text">The item text.</param>
/// <param name="description">The item's synchronous description, or <see langword="null"/> for none.</param>
/// <param name="image">The item's image, or <see langword="null"/> when the item supplies none.</param>
/// <param name="commitCharacters">
/// The characters that accept the item when typed, or <see langword="null"/> when the item declares none.
/// </param>
internal sealed class TestCompletionData(
	string text,
	object? description = null,
#if AVALONIAEDIT
	IImage? image = null,
#else
	ImageSource? image = null,
#endif
	IReadOnlyList<string>? commitCharacters = null)
	: ICompletionData, IPreselectedCompletionData, ICommitCharacterCompletionData
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
	public IReadOnlyList<string> CommitCharacters => commitCharacters ?? Array.Empty<string>();

	/// <inheritdoc/>
	public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
		=> textArea.Document.Replace(completionSegment, text);
}
