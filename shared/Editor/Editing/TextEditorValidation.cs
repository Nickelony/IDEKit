#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Editing;

#else

namespace Nickelony.IDEKit.AvalonEdit.Editing;

#endif

/// <summary>Checks the state of the <see cref="TextEditor"/> that an editing operation requires.</summary>
internal static class TextEditorValidation
{
	/// <summary>Returns the document of the editor after checking that one is assigned.</summary>
	/// <param name="textEditor">The editor to check.</param>
	/// <returns>The editor's document.</returns>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	internal static TextDocument RequireDocument(TextEditor textEditor) =>
		textEditor.Document ?? throw new InvalidOperationException("The editor has no document assigned.");
}
