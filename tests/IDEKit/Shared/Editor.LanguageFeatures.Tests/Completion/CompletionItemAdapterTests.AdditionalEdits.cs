#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class CompletionItemAdapterTests
{
	[TestMethod]
	public void Complete_AdditionalEditBeforeTheSegment_AppliesTheWholeCommitAsOneUndoUnit()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("alpha x = 1\npr");

		var data = new CompletionItemAdapter(
			new CompletionItem("print")
			{
				InsertText = "print",
				AdditionalTextEdits = [new CompletionTextEdit(new TextRange(0, 0), newText: "include 'm'\n")],
			}
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 12, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("include 'm'\nalpha x = 1\nprint", editor.Text);

		// The insertion and the additional edit were applied as one change, so a single undo
		// restores the whole commit.
		editor.Document.UndoStack.Undo();

		Assert.AreEqual("alpha x = 1\npr", editor.Text);
	}

	[TestMethod]
	public void Complete_StaleAdditionalEdit_IsSkipped()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("pr");

		var data = new CompletionItemAdapter(
			new CompletionItem("print") { InsertText = "print", AdditionalTextEdits = [new CompletionTextEdit(new TextRange(50, 1), newText: "X")] }
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The range lies outside the current document, so the entry contributed nothing.
		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItemWithLeadingAdditionalEdit_KeepsTheFinalTabstopCaret()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("xx sp");

		var data = new CompletionItemAdapter(
			new CompletionItem("write")
			{
				InsertText = "write(name)$0",
				InsertTextFormat = CompletionInsertTextFormat.Snippet,
				AdditionalTextEdits = [new CompletionTextEdit(new TextRange(0, 0), newText: "//")],
			}
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 3, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("//xx write(name)", editor.Text);

		// The caret follows the expanded snippet and is shifted by the leading edit's length delta.
		Assert.AreEqual("//xx write(name)".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_PlainItemWithLeadingAdditionalEdit_LeavesTheCaretAfterTheInsertedText()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("xx pr");
		editor.CaretOffset = 5;

		var data = new CompletionItemAdapter(
			new CompletionItem("print") { InsertText = "print", AdditionalTextEdits = [new CompletionTextEdit(new TextRange(0, 0), newText: "//")] }
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 3, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("//xx print", editor.Text);
		Assert.AreEqual("//xx print".Length, editor.TextArea.Caret.Offset);
	}
}
