#if AVALONIAEDIT
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Verifies the keys that step through the tabstops of a committed snippet, in an editor shown in a window.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionSnippetNavigationTests
{
	private const string CallSnippet = "rep(${1:s}, ${2:n})$0!";

	[TestMethod]
	public void Tab_AfterCommittingACallSnippet_MovesFromTheFirstParameterToTheSecond()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			Assert.AreEqual("s", editor.SelectedText);

			CompletionInputTestHelpers.PressKey(editor, Key.Tab);

			Assert.AreEqual("rep(s, n)!", editor.Text);
			Assert.AreEqual("n", editor.SelectedText);
			Assert.AreEqual(7, editor.SelectionStart);
		}
	}

	[TestMethod]
	public void ShiftTab_OnTheSecondParameter_MovesBackToTheFirst()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			CompletionInputTestHelpers.PressKey(editor, Key.Tab);
			CompletionInputTestHelpers.PressKey(editor, Key.Tab, shift: true);

			Assert.AreEqual("s", editor.SelectedText);
			Assert.AreEqual(4, editor.SelectionStart);
		}
	}

	[TestMethod]
	public void Tab_OnTheLastParameter_WrapsToTheFirst()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			CompletionInputTestHelpers.PressKey(editor, Key.Tab);
			CompletionInputTestHelpers.PressKey(editor, Key.Tab);

			Assert.AreEqual("s", editor.SelectedText);
			Assert.IsTrue(IsInSnippetMode(editor));
		}
	}

	[TestMethod]
	public void ShiftTab_OnTheFirstParameter_WrapsToTheLast()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			CompletionInputTestHelpers.PressKey(editor, Key.Tab, shift: true);

			Assert.AreEqual("n", editor.SelectedText);
			Assert.IsTrue(IsInSnippetMode(editor));
		}
	}

	[TestMethod]
	public void Tab_InSnippetMode_InsertsNoTabCharacter()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			CompletionInputTestHelpers.PressKey(editor, Key.Tab);
			Assert.AreEqual("rep(s, n)!", editor.Text);
		}
	}

	[TestMethod]
	public void TypingOverEachParameter_ReplacesItAndKeepsTheNextOneInPlace()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			editor.TextArea.PerformTextInput("str");
			CompletionInputTestHelpers.PressKey(editor, Key.Tab);

			Assert.AreEqual("n", editor.SelectedText);

			editor.TextArea.PerformTextInput("3");

			Assert.AreEqual("rep(str, 3)!", editor.Text);
		}
	}

	[TestMethod]
	public void Enter_InSnippetMode_LeavesSnippetModeWithTheCaretAtTheFinalTabstop()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			CompletionInputTestHelpers.PressKey(editor, Key.Return);

			// The key ended the snippet instead of inserting a line terminator, and the caret is where the $0 stands, before the exclamation mark.
			Assert.AreEqual("rep(s, n)!", editor.Text);
			Assert.AreEqual("rep(s, n)".Length, editor.TextArea.Caret.Offset);
			Assert.IsFalse(IsInSnippetMode(editor));
		}
	}

	[TestMethod]
	public void Enter_InSnippetModeWithoutAFinalTabstop_LeavesTheCaretAtTheEndOfTheInsertedText()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("re!");

		using (hostWindow)
		{
			var data = new CompletionItemAdapter(
				new CompletionItem("rep") { InsertText = "rep(${1:s}, ${2:n})", InsertTextFormat = CompletionInsertTextFormat.Snippet }
			);

			data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

			CompletionInputTestHelpers.PressKey(editor, Key.Tab);
			CompletionInputTestHelpers.PressKey(editor, Key.Return);

			Assert.AreEqual("rep(s, n)!", editor.Text);
			Assert.AreEqual("rep(s, n)".Length, editor.TextArea.Caret.Offset);
			Assert.IsFalse(IsInSnippetMode(editor));
		}
	}

	[TestMethod]
	public void Escape_InSnippetMode_LeavesSnippetModeWithTheCaretWhereItIs()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			CompletionInputTestHelpers.PressKey(editor, Key.Tab);
			CompletionInputTestHelpers.PressKey(editor, Key.Escape);

			Assert.AreEqual("rep(s, n)!", editor.Text);
			Assert.AreEqual("rep(s, n".Length, editor.TextArea.Caret.Offset);
			Assert.IsFalse(IsInSnippetMode(editor));
		}
	}

	[TestMethod]
	public void Tab_AfterLeavingSnippetMode_NoLongerSelectsAParameter()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			CompletionInputTestHelpers.PressKey(editor, Key.Escape);
			editor.TextArea.ClearSelection();

			CompletionInputTestHelpers.PressKey(editor, Key.Tab);

			Assert.AreEqual(string.Empty, editor.SelectedText);
		}
	}

	[TestMethod]
	public void Tab_ConsumedByTheHostWindowBeforeTheEditor_DoesNotReachTheSnippet()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			CompletionInputTestHelpers.ConsumeKeyAboveEditor(hostWindow, Key.Tab);

			CompletionInputTestHelpers.PressKey(editor, Key.Tab);

			// A key binding that handles Tab above the text area runs first, in snippet mode too, so the first parameter stays selected.
			Assert.AreEqual("s", editor.SelectedText);
		}
	}

	[TestMethod]
	public void Tab_AfterTheDocumentWasSwapped_ThrowsNothingAndLeavesSnippetMode()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			editor.Document = new TextDocument("ab");

			CompletionInputTestHelpers.PressKey(editor, Key.Tab);

			Assert.IsFalse(IsInSnippetMode(editor));
			Assert.AreEqual(string.Empty, editor.SelectedText);
		}
	}

	[TestMethod]
	public void Enter_AfterTheCaretLeftTheSnippet_IsLeftToTheEditor()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("re\nnext");

		using (hostWindow)
		{
			Commit(editor, snippetText: "rep(${1:s}, ${2:n})", offset: 0, length: 2);
			editor.TextArea.ClearSelection();
			editor.TextArea.Caret.Offset = editor.Document.TextLength;

			CompletionInputTestHelpers.PressKey(editor, Key.Return);

			// The key went to the editor, so the caret did not jump to where the snippet's final tabstop stands.
			Assert.AreEqual("rep(s, n)\nnext", editor.Text);
			Assert.AreEqual(editor.Document.TextLength, editor.TextArea.Caret.Offset);
			Assert.IsFalse(IsInSnippetMode(editor));
		}
	}

	[TestMethod]
	public void Tab_AfterTheCaretLeftTheSnippet_IsLeftToTheEditor()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("re\nnext");

		using (hostWindow)
		{
			Commit(editor, snippetText: "rep(${1:s}, ${2:n})", offset: 0, length: 2);
			editor.TextArea.ClearSelection();
			editor.TextArea.Caret.Offset = editor.Document.TextLength;

			CompletionInputTestHelpers.PressKey(editor, Key.Tab);

			Assert.AreEqual(string.Empty, editor.SelectedText);
			Assert.IsFalse(IsInSnippetMode(editor));
		}
	}

	[TestMethod]
	public void Enter_AfterTypingAKeyAtATimeIntoTheTabstopThatEndsTheSnippet_LandsAfterTheTypedText()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("lo");

		using (hostWindow)
		{
			Commit(editor, snippetText: "local ${1:name}", offset: 0, length: 2);

			foreach (char character in "abc")
				editor.TextArea.PerformTextInput(character.ToString());

			CompletionInputTestHelpers.PressKey(editor, Key.Return);

			Assert.AreEqual("local abc", editor.Text);
			Assert.AreEqual("local abc".Length, editor.TextArea.Caret.Offset);
			Assert.IsFalse(IsInSnippetMode(editor));
		}
	}

	[TestMethod]
	public void Enter_AfterTypingAKeyAtATimeIntoTheTabstopThatTheFinalTabstopFollows_LandsAfterTheTypedText()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("f");

		using (hostWindow)
		{
			Commit(editor, snippetText: "f(${1:x}$0)", offset: 0, length: 1);

			foreach (char character in "ab")
				editor.TextArea.PerformTextInput(character.ToString());

			CompletionInputTestHelpers.PressKey(editor, Key.Return);

			Assert.AreEqual("f(ab)", editor.Text);
			Assert.AreEqual("f(ab".Length, editor.TextArea.Caret.Offset);
		}
	}

	[TestMethod]
	public void Enter_AfterTypingIntoATabstopWhoseMirrorEndsTheSnippet_LandsAfterTheMirror()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("lo");

		using (hostWindow)
		{
			Commit(editor, snippetText: "local ${1:x} = $1", offset: 0, length: 2);

			foreach (char character in "ab")
				editor.TextArea.PerformTextInput(character.ToString());

			CompletionInputTestHelpers.PressKey(editor, Key.Return);

			Assert.AreEqual("local ab = ab", editor.Text);
			Assert.AreEqual("local ab = ab".Length, editor.TextArea.Caret.Offset);
		}
	}

	[TestMethod]
	public void Tab_AfterASnippetCommittedInsideAnother_VisitsOnlyTheInnerTabstops()
	{
		(TextEditor editor, HostWindow hostWindow) = CommitCallSnippet();

		using (hostWindow)
		{
			Commit(editor, snippetText: "f(${1:a})", offset: editor.SelectionStart, length: editor.SelectionLength);

			CompletionInputTestHelpers.PressKey(editor, Key.Tab);

			// The outer snippet ended when the inner one began, so Tab wraps within the inner one and never reaches the second parameter.
			Assert.AreEqual("rep(f(a), n)!", editor.Text);
			Assert.AreEqual("a", editor.SelectedText);

			CompletionInputTestHelpers.PressKey(editor, Key.Return);

			Assert.IsFalse(IsInSnippetMode(editor));
		}
	}

	private static void Commit(TextEditor editor, string snippetText, int offset, int length)
	{
		new CompletionItemAdapter(
			new CompletionItem("item") { InsertText = snippetText, InsertTextFormat = CompletionInsertTextFormat.Snippet }
		).Complete(editor.TextArea, new TextSegment { StartOffset = offset, Length = length }, EventArgs.Empty);
	}

	private static (TextEditor Editor, HostWindow HostWindow) CommitCallSnippet()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("re");

		var data = new CompletionItemAdapter(
			new CompletionItem("rep") { InsertText = CallSnippet, InsertTextFormat = CompletionInsertTextFormat.Snippet }
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.IsTrue(IsInSnippetMode(editor));

		return (editor, hostWindow);
	}

	private static bool IsInSnippetMode(TextEditor editor) => !editor.TextArea.StackedInputHandlers.IsEmpty;
}
