#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// Provides document-position and viewport helpers shared by the editor test classes.
/// </summary>
internal static class DocumentTestHelpers
{
	/// <summary>
	/// Gets the start offset of the given one-based document line.
	/// </summary>
	/// <param name="document">The document to resolve the line in.</param>
	/// <param name="lineNumber">The one-based line number.</param>
	/// <returns>The line's start offset.</returns>
	public static int GetLineOffset(TextDocument document, int lineNumber)
		=> document.GetLineByNumber(lineNumber).Offset;

	/// <summary>
	/// Determines whether the given one-based document line is currently in the text view's visual lines.
	/// </summary>
	/// <param name="editor">The editor to inspect.</param>
	/// <param name="lineNumber">The one-based line number.</param>
	/// <returns><see langword="true"/> when the line is visible; otherwise, <see langword="false"/>.</returns>
	public static bool IsLineVisible(TextEditor editor, int lineNumber)
		=> IsLineVisible(editor.TextArea, lineNumber);

	/// <summary>
	/// Determines whether the given one-based document line is currently in the text view's visual lines.
	/// </summary>
	/// <param name="textArea">The text area to inspect.</param>
	/// <param name="lineNumber">The one-based line number.</param>
	/// <returns><see langword="true"/> when the line is visible; otherwise, <see langword="false"/>.</returns>
	public static bool IsLineVisible(TextArea textArea, int lineNumber)
		=> textArea.TextView.VisualLines.Any(line => line.FirstDocumentLine.LineNumber == lineNumber);
}
