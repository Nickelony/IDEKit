#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <remarks>
/// The editors are never shown in a window, so the class also runs in a headless leg.
/// </remarks>
[STATestClass]
public sealed class TextEditorWorkspaceEditExtensionsTests
{
	private const string DocumentId = "edit-test.lua";

	[TestMethod]
	public void ApplyDocumentEdits_AppliesTheEditsOfTheNamedDocument_AsOneUndoStep()
	{
		TextEditor editor = TestHost.CreateEditor("local a = 1\nprint(a)");
		editor.Document.UndoStack.ClearAll();

		var edit = new WorkspaceEdit([
			new TextDocumentEdit(
				DocumentId,
				[EditAt(line: 0, start: 6, end: 7, newText: "count"), EditAt(line: 1, start: 6, end: 7, newText: "count")]
			),

			new TextDocumentEdit("other.lua", [EditAt(line: 0, start: 0, end: 0, newText: "-- not this file\n")]),
		]);

		Assert.AreEqual(TextEditConversionOutcome.Converted, editor.ApplyDocumentEdits(edit, DocumentId));
		Assert.AreEqual("local count = 1\nprint(count)", editor.Text);

		editor.Undo();
		Assert.AreEqual("local a = 1\nprint(a)", editor.Text);
	}

	[TestMethod]
	public void ApplyDocumentEdits_OverlappingEdits_ChangeNothing()
	{
		TextEditor editor = TestHost.CreateEditor("local a = 1");

		var edit = new WorkspaceEdit([
			new TextDocumentEdit(DocumentId, [EditAt(line: 0, start: 0, end: 5, newText: "x"), EditAt(line: 0, start: 3, end: 8, newText: "y")]),
		]);

		Assert.AreEqual(TextEditConversionOutcome.Conflict, editor.ApplyDocumentEdits(edit, DocumentId));
		Assert.AreEqual("local a = 1", editor.Text);
	}

	[TestMethod]
	public void ApplyDocumentEdits_NoEditForTheDocument_LeavesTheTextAlone()
	{
		TextEditor editor = TestHost.CreateEditor("local a = 1");

		var edit = new WorkspaceEdit([new TextDocumentEdit("other.lua", [EditAt(line: 0, start: 0, end: 0, newText: "x")])]);

		Assert.AreEqual(TextEditConversionOutcome.Converted, editor.ApplyDocumentEdits(edit, DocumentId));
		Assert.AreEqual("local a = 1", editor.Text);
	}

	[TestMethod]
	public void ApplyDocumentEdits_BlankDocumentId_ThrowsArgumentException()
	{
		TextEditor editor = TestHost.CreateEditor("local a = 1");
		Assert.ThrowsExactly<ArgumentException>(() => editor.ApplyDocumentEdits(new WorkspaceEdit([]), " "));
	}

	private static TextEdit EditAt(int line, int start, int end, string newText) =>
		new(new TextPositionRange(new TextPosition(line, start), new TextPosition(line, end)), newText);
}
