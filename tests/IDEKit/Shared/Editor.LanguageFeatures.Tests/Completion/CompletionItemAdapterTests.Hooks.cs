#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class CompletionItemAdapterTests
{
	[TestMethod]
	public void Image_WithImageHook_IsTheImageTheHookReturnsForTheItem()
	{
		var image = new DrawingImage();
		var item = new CompletionItem("print");

		CompletionItem? received = null;

		var data = new CompletionItemAdapter(
			item,

			new CompletionItemAdapterHooks
			{
				ImageFor = candidate =>
				{
					received = candidate;
					return image;
				},
			}
		);

		Assert.AreSame(image, data.Image);
		Assert.AreSame(item, received);
	}

	[TestMethod]
	public void Complete_NullHooks_KeepsTheDefaultImageAndCommit()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("alpha\npr");
		editor.CaretOffset = 8;

		var data = new CompletionItemAdapter(
			new CompletionItem("print")
			{
				InsertText = "print",
				AdditionalTextEdits = [new CompletionTextEdit(new TextRange(0, 0), newText: "-- m\n")],
			},

			hooks: null
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 6, Length = 2 }, EventArgs.Empty);

		Assert.IsNull(data.Image);
		Assert.AreEqual("-- m\nalpha\nprint", editor.Text);
		Assert.AreEqual("-- m\nalpha\nprint".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_CommitDeclined_InsertsNothingAndLeavesTheDocumentUnchanged()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("alpha\npr");
		editor.CaretOffset = 8;

		var item = new CompletionItem("print")
		{
			InsertText = "print",
			AdditionalTextEdits = [new CompletionTextEdit(new TextRange(0, 0), newText: "-- m\n")],
		};

		CompletionItem? asked = null;
		bool adjusted = false;

		var data = new CompletionItemAdapter(
			item,

			new CompletionItemAdapterHooks
			{
				CanCommit = candidate =>
				{
					asked = candidate;
					return false;
				},

				AdjustInsertion = (_, _, insertion) =>
				{
					adjusted = true;
					return insertion;
				},
			}
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 6, Length = 2 }, EventArgs.Empty);

		// The declined commit applied neither the insertion nor the additional edit, and it asked for no adjustment.
		Assert.AreSame(item, asked);
		Assert.IsFalse(adjusted);
		Assert.AreEqual("alpha\npr", editor.Text);
		Assert.AreEqual(8, editor.TextArea.Caret.Offset);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void Complete_CommitAllowed_CommitsTheItem()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("pr");

		var data = new CompletionItemAdapter(
			new CompletionItem("print") { InsertText = "print" },
			new CompletionItemAdapterHooks { CanCommit = static _ => true }
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Complete_AdjustedInsertion_LandsAsAdjustedWithTheAdditionalEditsInOneUndoUnit()
	{
		const string text = "local x\n  wh";
		TextEditor editor = CompletionTestHelpers.CreateEditor(text);

		string? snapshotText = null;
		int replacementOffset = -1;
		CompletionInsertion resolved = default;

		var data = new CompletionItemAdapter(
			new CompletionItem("while")
			{
				InsertText = "while ${1:cond} do\n$0\nend",
				InsertTextFormat = CompletionInsertTextFormat.Snippet,
				AdditionalTextEdits = [new CompletionTextEdit(new TextRange(0, 0), newText: "-- m\n")],
			},

			new CompletionItemAdapterHooks
			{
				AdjustInsertion = (snapshot, offset, insertion) =>
				{
					snapshotText = snapshot.GetText(0, snapshot.TextLength);
					replacementOffset = offset;
					resolved = insertion;

					// Indents each continuation line to the two spaces of the line the insertion lands on, and the
					// body line one step further, with the caret on the body line.
					return new CompletionInsertion("while cond do\n    \n  end", CaretOffset: "while cond do\n    ".Length);
				},
			}
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 10, Length = 2 }, EventArgs.Empty);

		// The hook saw the document before the commit, the start of the replaced range and the expanded snippet.
		Assert.AreEqual(text, snapshotText);
		Assert.AreEqual(10, replacementOffset);
		Assert.AreEqual(new CompletionInsertion("while cond do\n\nend", CaretOffset: "while cond do\n".Length), resolved);

		const string committed = "-- m\nlocal x\n  while cond do\n    \n  end";
		Assert.AreEqual(committed, editor.Text);

		// The caret is the adjusted one, shifted by the leading additional edit.
		Assert.AreEqual("-- m\nlocal x\n  while cond do\n    ".Length, editor.TextArea.Caret.Offset);

		// The adjusted insertion and the additional edit were one change, so one undo restores the document.
		editor.Document.UndoStack.Undo();

		Assert.AreEqual(text, editor.Text);
	}

	[TestMethod]
	public void Complete_AdjustInsertion_ReceivesTheStartOfTheItemEditRange()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("obj:pr");
		int replacementOffset = -1;

		var data = new CompletionItemAdapter(
			new CompletionItem("print") { TextEdit = new CompletionTextEdit(new TextRange(3, 3), newText: ".print") },

			new CompletionItemAdapterHooks
			{
				AdjustInsertion = (_, offset, insertion) =>
				{
					replacementOffset = offset;
					return insertion;
				},
			}
		);

		// The window segment covers the typed word only, and the item's edit range starts at the separator before it.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 4, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual(3, replacementOffset);
		Assert.AreEqual("obj.print", editor.Text);
	}

	[TestMethod]
	public void Complete_AdjustedInsertionWithoutText_ThrowsAndLeavesTheDocumentUnchanged()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("pr");

		var data = new CompletionItemAdapter(
			new CompletionItem("print") { InsertText = "print" },
			new CompletionItemAdapterHooks { AdjustInsertion = static (_, _, _) => default }
		);

		Assert.ThrowsExactly<InvalidOperationException>(() =>
			data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty)
		);

		Assert.AreEqual("pr", editor.Text);
	}
}
