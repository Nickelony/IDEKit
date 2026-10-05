#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
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
	private const string CallSnippet = "rep(${1:s}, ${2:n})$0";

	[TestMethod]
	public void Complete_SnippetWithTabstops_InsertsTheExpandedTextAndSelectsTheFirstTabstop()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("re");

		CommitSnippet(editor, CallSnippet, new TextSegment { StartOffset = 0, Length = 2 });

		Assert.AreEqual("rep(s, n)", editor.Text);
		Assert.AreEqual("s", editor.SelectedText);
		Assert.AreEqual(4, editor.SelectionStart);
		Assert.AreEqual(5, editor.TextArea.Caret.Offset);
		Assert.IsTrue(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_SnippetWithNestedTabstops_InsertsPlainTextAndStaysOutOfSnippetMode()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("f");

		CommitSnippet(editor, "f(${1:foo ${2:bar}})", new TextSegment { StartOffset = 0, Length = 1 });

		Assert.AreEqual("f(foo bar)", editor.Text);
		Assert.IsFalse(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_SnippetWithAdditionalEdits_IsOneUndoUnitThatLeavesSnippetMode()
	{
		const string text = "xx re yy";
		TextEditor editor = CompletionTestHelpers.CreateEditor(text);

		var data = new CompletionItemAdapter(
			new CompletionItem("rep")
			{
				InsertText = CallSnippet,
				InsertTextFormat = CompletionInsertTextFormat.Snippet,

				AdditionalTextEdits =
				[
					new CompletionTextEdit(new TextRange(0, 0), newText: "//"),
					new CompletionTextEdit(new TextRange(text.Length, 0), newText: "!"),
				],
			}
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 3, Length = 2 }, EventArgs.Empty);

		// The edit above the snippet moved it, and the first tabstop is still the selection.
		Assert.AreEqual("//xx rep(s, n) yy!", editor.Text);
		Assert.AreEqual("s", editor.SelectedText);
		Assert.AreEqual("//xx rep(".Length, editor.SelectionStart);
		Assert.IsTrue(IsInSnippetMode(editor));

		// The snippet and both additional edits were one change, so a single undo restores the document.
		editor.Document.UndoStack.Undo();

		Assert.AreEqual(text, editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
		Assert.IsFalse(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_DeclinedSnippetCommit_InsertsNothingAndStaysOutOfSnippetMode()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("re");

		var data = new CompletionItemAdapter(
			new CompletionItem("rep") { InsertText = CallSnippet, InsertTextFormat = CompletionInsertTextFormat.Snippet },
			new CompletionItemAdapterHooks { CanCommit = static _ => false }
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("re", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
		Assert.IsFalse(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_AdjustInsertionOnASnippet_ReceivesTheExpandedTextAtTheItemRangeAndKeepsTheTabstopsWhenTheTextIsUnchanged()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("obj:re");
		int replacementOffset = -1;
		CompletionInsertion received = default;

		var data = new CompletionItemAdapter(
			new CompletionItem("rep")
			{
				TextEdit = new CompletionTextEdit(new TextRange(3, 3), newText: ":rep(${1:s}, ${2:n})$0"),
				InsertTextFormat = CompletionInsertTextFormat.Snippet,
			},

			new CompletionItemAdapterHooks
			{
				AdjustInsertion = (_, offset, insertion) =>
				{
					replacementOffset = offset;
					received = insertion;
					return insertion;
				},
			}
		);

		// The window segment covers the typed word only, and the item's edit range starts at the separator before it.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 4, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual(3, replacementOffset);
		Assert.AreEqual(new CompletionInsertion(":rep(s, n)", CaretOffset: ":rep(s, n)".Length), received);
		Assert.AreEqual("obj:rep(s, n)", editor.Text);
		Assert.AreEqual("s", editor.SelectedText);
		Assert.IsTrue(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_AdjustInsertionThatChangesTheSnippetText_InsertsThePlainTextAtTheAdjustedCaret()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("re");

		var data = new CompletionItemAdapter(
			new CompletionItem("rep") { InsertText = CallSnippet, InsertTextFormat = CompletionInsertTextFormat.Snippet },

			new CompletionItemAdapterHooks
			{
				AdjustInsertion = static (_, _, insertion) => new CompletionInsertion(insertion.Text + ";", CaretOffset: 4),
			}
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The tabstop ranges describe the text before the hook rewrote it, so the rewritten text is inserted as it is.
		Assert.AreEqual("rep(s, n);", editor.Text);
		Assert.AreEqual(4, editor.TextArea.Caret.Offset);
		Assert.AreEqual(0, editor.SelectionLength);
		Assert.IsFalse(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_SnippetWithARepeatedTabstop_MirrorsTheFirstOccurrenceWhileItIsEdited()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("lo");

		CommitSnippet(editor, "local ${1:x} = $1", new TextSegment { StartOffset = 0, Length = 2 });

		Assert.AreEqual("local x = x", editor.Text);
		Assert.AreEqual("x", editor.SelectedText);

		editor.TextArea.PerformTextInput("count");

		Assert.AreEqual("local count = count", editor.Text);
	}

	[TestMethod]
	public void Complete_MultiLineSnippet_IndentsTheContinuationLinesLikeTheEditorSnippet()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("x\n  wh");
		editor.Options.IndentationSize = 2;
		editor.Options.ConvertTabsToSpaces = true;

		CommitSnippet(editor, "while ${1:cond} do\n\t$0\nend", new TextSegment { StartOffset = 4, Length = 2 });

		// The editor's snippet inserts the line terminators and tabs itself: each continuation line takes the indentation of the line
		// the snippet lands on, and a tab becomes the editor's indentation string.
		Assert.AreEqual("x\n  while cond do\n    \n  end", editor.Text);
		Assert.AreEqual("cond", editor.SelectedText);
	}

	[TestMethod]
	public void Complete_MultiLineSnippetInADocumentWithoutLineBreaks_BreaksLinesWithThePlatformLineTerminator()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("wh");

		CommitSnippet(editor, "while ${1:cond} do\n$0\nend", new TextSegment { StartOffset = 0, Length = 2 });

		// The editor's snippet reads the line terminator from the document, and falls back to the platform's when the document has none.
		Assert.AreEqual($"while cond do{Environment.NewLine}{Environment.NewLine}end", editor.Text);
	}

	[TestMethod]
	public void Complete_DocumentSwappedInSnippetMode_LeavesSnippetMode()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("re");

		CommitSnippet(editor, CallSnippet, new TextSegment { StartOffset = 0, Length = 2 });
		Assert.IsTrue(IsInSnippetMode(editor));

		editor.Document = new TextDocument("ab");

		Assert.IsFalse(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_CaretMovedOutOfTheInsertedText_LeavesSnippetMode()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("re\nnext");

		CommitSnippet(editor, CallSnippet, new TextSegment { StartOffset = 0, Length = 2 });
		editor.TextArea.ClearSelection();
		editor.TextArea.Caret.Offset = editor.Document.TextLength;

		Assert.IsFalse(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_CaretMovedBeforeTheInsertedText_LeavesSnippetMode()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("xx re");

		CommitSnippet(editor, CallSnippet, new TextSegment { StartOffset = 3, Length = 2 });
		editor.TextArea.ClearSelection();
		editor.TextArea.Caret.Offset = 1;

		Assert.IsFalse(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_CaretMovedWithinTheInsertedText_StaysInSnippetMode()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("re\nnext");

		CommitSnippet(editor, CallSnippet, new TextSegment { StartOffset = 0, Length = 2 });
		editor.TextArea.ClearSelection();
		editor.TextArea.Caret.Offset = "rep(s, n".Length;

		Assert.IsTrue(IsInSnippetMode(editor));

		editor.TextArea.Caret.Offset = "rep(s, n)".Length;

		// The end of the inserted text still belongs to the snippet.
		Assert.IsTrue(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_SnippetInsideAnActiveSnippet_ReplacesItAndLeavesOneSnippetMode()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("re");

		CommitSnippet(editor, CallSnippet, new TextSegment { StartOffset = 0, Length = 2 });
		CommitSnippet(editor, "f(${1:a})", new TextSegment { StartOffset = editor.SelectionStart, Length = editor.SelectionLength });

		Assert.AreEqual("rep(f(a), n)", editor.Text);
		Assert.AreEqual("a", editor.SelectedText);
		Assert.AreEqual(1, editor.TextArea.StackedInputHandlers.Count());
	}

	[TestMethod]
	public void Complete_SnippetWithTouchingTabstops_InsertsPlainTextAndStaysOutOfSnippetMode()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("x");

		CommitSnippet(editor, "${1:name}${2:args}", new TextSegment { StartOffset = 0, Length = 1 });

		Assert.AreEqual("nameargs", editor.Text);
		Assert.IsFalse(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_TabstopRepeatedBeforeItsDefault_ShowsTheDefaultAtEveryOccurrence()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("x");

		CommitSnippet(editor, "$1 = ${1:value}", new TextSegment { StartOffset = 0, Length = 1 });

		Assert.AreEqual("value = value", editor.Text);
		Assert.AreEqual("value", editor.SelectedText);
		Assert.AreEqual("value = ".Length, editor.SelectionStart);

		editor.TextArea.PerformTextInput("n");

		Assert.AreEqual("n = n", editor.Text);
	}

	[TestMethod]
	public void Complete_InsertionAtTheSnippetStart_StaysOutOfTheFirstTabstop()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("re");

		var data = new CompletionItemAdapter(
			new CompletionItem("rep")
			{
				InsertText = "${1:name} = 1",
				InsertTextFormat = CompletionInsertTextFormat.Snippet,
				AdditionalTextEdits = [new CompletionTextEdit(new TextRange(0, 0), newText: "local ")],
			}
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("local name = 1", editor.Text);
		Assert.AreEqual("name", editor.SelectedText);
		Assert.AreEqual("local ".Length, editor.SelectionStart);

		editor.TextArea.PerformTextInput("abc");

		// The tabstop was only `name`, so typing replaced it and nothing else.
		Assert.AreEqual("local abc = 1", editor.Text);
	}

	private static void CommitSnippet(TextEditor editor, string snippetText, TextSegment segment)
	{
		var data = new CompletionItemAdapter(
			new CompletionItem("item") { InsertText = snippetText, InsertTextFormat = CompletionInsertTextFormat.Snippet }
		);

		data.Complete(editor.TextArea, segment, EventArgs.Empty);
	}

	private static bool IsInSnippetMode(TextEditor editor) => !editor.TextArea.StackedInputHandlers.IsEmpty;
}
