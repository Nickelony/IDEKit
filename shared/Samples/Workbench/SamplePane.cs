using System.IO;
#if AVALONIAEDIT
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
#endif

namespace Nickelony.Samples;

/// <summary>One editor tab of the sample, the file it shows and the session that wires the editor features to it.</summary>
/// <param name="Editor">The editor control.</param>
/// <param name="Session">The session that wires the editor features to <paramref name="Editor"/>.</param>
/// <param name="View">
/// The view through which the document store keeps <paramref name="Editor"/> in step with the file and with every other view of it.
/// </param>
/// <param name="Instance">Which view of the document the pane is: the first one, or a later one that a split opened.</param>
internal sealed record SamplePane(TextEditor Editor, SampleEditorSession Session, SampleDocumentView View, int Instance = 1)
{
	/// <summary>The local path of the file the editor shows, which follows a rename.</summary>
	internal string FilePath => Session.DocumentId;

	/// <summary>The file name, which the tab header shows, with the number of the view for a split.</summary>
	internal string Title => Instance == 1 ? Path.GetFileName(FilePath) : $"{Path.GetFileName(FilePath)} ({Instance})";
}
