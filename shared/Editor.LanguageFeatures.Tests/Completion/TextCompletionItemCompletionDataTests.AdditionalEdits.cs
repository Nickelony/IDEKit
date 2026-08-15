#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextCompletionItemCompletionDataTests
{
	[TestMethod]
	public void Complete_AdditionalEditBeforeTheSegment_AppliesTheWholeCommitAsOneUndoUnit()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("alpha x = 1\npr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 0), newText: "include 'm'\n")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 12, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("include 'm'\nalpha x = 1\nprint", editor.Text);

		// The insertion and the additional edit were applied as one change, so a single undo
		// restores the whole commit.
		editor.Document.UndoStack.Undo();

		Assert.AreEqual("alpha x = 1\npr", editor.Text);
	}

	[TestMethod]
	public void Complete_AdditionalEditAfterTheSegment_UsesOriginalDocumentOffsets()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr tail");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(2, 1), newText: "_")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The edit replaced the space at its original offset; the insertion did not shift it.
		Assert.AreEqual("print_tail", editor.Text);
	}

	[TestMethod]
	public void Complete_AdditionalEditEndingAtTheSegmentStart_IsApplied()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("abpr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 2), newText: "AB")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 2, Length = 2 }, EventArgs.Empty);

		// A range that merely touches the completion segment does not overlap it.
		Assert.AreEqual("ABprint", editor.Text);
	}

	[TestMethod]
	public void Complete_StaleAdditionalEdit_IsSkipped()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(50, 1), newText: "X")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The range lies outside the current document, so the entry contributed nothing.
		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Complete_AdditionalEditOverlappingTheInsertion_IsSkipped()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("prx");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(1, 2), newText: "X")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("printx", editor.Text);
	}

	[TestMethod]
	public void Complete_AdditionalEditEnclosingTheInsertionPoint_DoesNotDropThePrimary()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("prxyz");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 3), newText: "AB")]
		});

		// The additional edit starts before the insertion point and covers it, so it conflicts with the
		// primary insertion. The primary is the batch's first entry, so the conflict is resolved in its
		// favor: the commit's own text is applied and only the secondary edit is dropped.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 2, Length = 0 }, EventArgs.Empty);

		Assert.AreEqual("prprintxyz", editor.Text);
	}

	[TestMethod]
	public void Complete_OverlappingAdditionalEdits_KeepTheFirstEntry()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("prwxyz");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits =
			[
				new TextCompletionTextEdit(new TextRange(2, 2), newText: "A"),
				new TextCompletionTextEdit(new TextRange(3, 2), newText: "B")
			]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The second entry overlaps the first accepted one, so only the first was applied.
		Assert.AreEqual("printAyz", editor.Text);
	}

	[TestMethod]
	public void Complete_AdditionalEditWithoutReplacementText_IsSkipped()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(2, 0))]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItemWithLeadingAdditionalEdit_KeepsTheFinalTabstopCaret()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("xx sp");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("write")
		{
			InsertText = "write(${1:name})$0",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet,
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 0), newText: "//")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 3, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("//xx write(name)", editor.Text);

		// The caret follows the expanded snippet and is shifted by the leading edit's length delta.
		Assert.AreEqual("//xx write(name)".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_PlainItemWithLeadingAdditionalEdit_LeavesTheCaretAfterTheInsertedText()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("xx pr");
		editor.CaretOffset = 5;

		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(0, 0), newText: "//")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 3, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("//xx print", editor.Text);
		Assert.AreEqual("//xx print".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_ZeroLengthSegment_AdditionalEditStartingAtTheInsertionPoint_IsApplied()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("prx");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(2, 1), newText: "X")]
		});

		// The edit replaces the character after the insertion point. The kernel applies a replacement
		// before an insertion that shares its start offset, so both are honored: the inserted text lands
		// at the insertion point and the replacement replaces the character that followed it.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 2, Length = 0 }, EventArgs.Empty);

		Assert.AreEqual("prprintX", editor.Text);
	}

	[TestMethod]
	public void Complete_ZeroLengthSegment_ZeroLengthEditAtTheInsertionPoint_IsApplied()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(2, 0), newText: "X")]
		});

		// A zero-length edit at the insertion point merely touches it, so both insertions are applied.
		// The kernel applies the higher edit index first at one offset, so the primary (edit index zero)
		// is applied last and its text lands leftmost.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 2, Length = 0 }, EventArgs.Empty);

		Assert.AreEqual("prprintX", editor.Text);
	}

	[TestMethod]
	public void Complete_ZeroLengthEditAtTheInsertionPointWithSnippetCaret_DoesNotShiftTheCaret()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print$0",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet,
			AdditionalTextEdits = [new TextCompletionTextEdit(new TextRange(2, 0), newText: "X")]
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 2, Length = 0 }, EventArgs.Empty);

		// Both insertions are applied and the primary text lands leftmost (it is edit index zero). The
		// same-offset insertion is ordered before the primary insertion, so it must not shift the caret:
		// the caret stays at the end of the primary text ("print"), not after the secondary "X".
		Assert.AreEqual("prprintX", editor.Text);
		Assert.AreEqual("prprint".Length, editor.TextArea.Caret.Offset);
	}
}
