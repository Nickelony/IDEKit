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

/// <summary>
/// Verifies that the commit prefers the item's edit payload - its replacement text and its own range -
/// and falls back to the completion window's segment for a missing or stale range.
/// </summary>
public sealed partial class TextCompletionItemCompletionDataTests
{
	[TestMethod]
	public void Complete_ItemWithEditPayload_CommitsTheEditText()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "ignored",
			TextEdit = new TextCompletionTextEdit(new TextRange(0, 2), newText: "print()")
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("print()", editor.Text);
	}

	[TestMethod]
	public void Complete_ItemWithEditPayload_UsesTheEditRangeWhenItFitsTheDocument()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr + tail");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			TextEdit = new TextCompletionTextEdit(new TextRange(0, 2), newText: "print()")
		});

		// The window segment covers the first character only; the edit range (0..2) wins because it
		// still fits the document.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 1 }, EventArgs.Empty);

		Assert.AreEqual("print() + tail", editor.Text);
	}

	[TestMethod]
	public void Complete_ItemWithDistinctRanges_InsertModeUsesTheInsertRange()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr + tail");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			TextEdit = new TextCompletionTextEdit(
				new TextRange(0, 2),
				replaceRange: new TextRange(0, 4),
				newText: "print()")
		});

		// Insert mode (the default) replaces the edit's insert range; the window segment is only the
		// fallback for a stale range.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 1 }, EventArgs.Empty);

		Assert.AreEqual("print() + tail", editor.Text);
	}

	[TestMethod]
	public void Complete_ItemWithDistinctRanges_OverstrikeModeUsesTheReplaceRange()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr + tail");
		editor.TextArea.OverstrikeMode = true;
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			TextEdit = new TextCompletionTextEdit(
				new TextRange(0, 2),
				replaceRange: new TextRange(0, 4),
				newText: "print()")
		});

		// Overstrike mode replaces the edit's replace range instead: "pr +" is exchanged for "print()".
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 1 }, EventArgs.Empty);

		Assert.AreEqual("print() tail", editor.Text);
	}

	[TestMethod]
	public void Complete_ItemWithStaleEditRange_FallsBackToTheCompletionSegment()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			TextEdit = new TextCompletionTextEdit(new TextRange(50, 2), newText: "print()")
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("print()", editor.Text);
	}

	[TestMethod]
	public void Complete_ItemWithExplicitEmptyEditText_DeletesTheRangeInsteadOfFallingBack()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr + tail");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print")
		{
			InsertText = "print",
			TextEdit = new TextCompletionTextEdit(new TextRange(0, 2), newText: string.Empty)
		});

		// An explicit empty replacement is distinct from a missing one: the edit deletes the range
		// instead of falling back to the insertion text.
		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 1 }, EventArgs.Empty);

		Assert.AreEqual(" + tail", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItemWithEditPayload_ExpandsTheEditText()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("sp");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("write")
		{
			InsertText = "ignored",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet,
			TextEdit = new TextCompletionTextEdit(new TextRange(0, 2), newText: "write(${1:name})$0")
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write(name)", editor.Text);
		Assert.AreEqual("write(name)".Length, editor.TextArea.Caret.Offset);
	}
}
