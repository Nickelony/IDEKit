#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Verifies the default completion-data adapter, including its snippet expansion at commit time.</summary>
[STATestClass]
public sealed partial class CompletionItemAdapterTests
{
	[TestMethod]
	public void Complete_PlainTextItem_ReplacesTheCompletionSegment()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("pr");
		var data = new CompletionItemAdapter(new CompletionItem("print") { InsertText = "print" });

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItemWithOnlyTheFinalTabstop_InsertsExpandedTextAndPlacesTheCaretThere()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("sp");

		var data = new CompletionItemAdapter(
			new CompletionItem("write") { InsertText = "write(name)$0", InsertTextFormat = CompletionInsertTextFormat.Snippet }
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write(name)", editor.Text);
		Assert.AreEqual("write(name)".Length, editor.TextArea.Caret.Offset);
		Assert.IsFalse(IsInSnippetMode(editor));
	}

	[TestMethod]
	public void Complete_SnippetItemWithDefaultedFinalTabstop_PlacesTheCaretAfterTheDefaultText()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("sp");

		var data = new CompletionItemAdapter(
			new CompletionItem("write") { InsertText = "write()${0:tail}", InsertTextFormat = CompletionInsertTextFormat.Snippet }
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write()tail", editor.Text);
		Assert.AreEqual("write()tail".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_SnippetItemWithoutFinalTabstop_InsertsExpandedText()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("sp");

		var data = new CompletionItemAdapter(
			new CompletionItem("write") { InsertText = "write(${1:name})", InsertTextFormat = CompletionInsertTextFormat.Snippet }
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write(name)", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItemWithMalformedSnippet_InsertsTheTextAsIs()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("a");

		var data = new CompletionItemAdapter(
			new CompletionItem("a") { InsertText = "a${1:b", InsertTextFormat = CompletionInsertTextFormat.Snippet }
		);

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 1 }, EventArgs.Empty);

		Assert.AreEqual("a${1:b", editor.Text);
	}
}
