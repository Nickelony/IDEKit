#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.IntelliSense.Completion;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Verifies the default completion-data adapter, including its snippet expansion at commit time.
/// </summary>
[STATestClass]
public sealed partial class TextCompletionItemCompletionDataTests
{
	[TestMethod]
	public void Complete_PlainTextItem_ReplacesTheCompletionSegment()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("pr");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("print") { InsertText = "print" });

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItem_InsertsExpandedTextAndPlacesTheCaretAtTheFinalTabstop()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("sp");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("write")
		{
			InsertText = "write(${1:name})$0",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write(name)", editor.Text);
		Assert.AreEqual("write(name)".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_SnippetItemWithDefaultedFinalTabstop_PlacesTheCaretAfterTheDefaultText()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("sp");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("write")
		{
			InsertText = "write()${0:tail}",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write()tail", editor.Text);
		Assert.AreEqual("write()tail".Length, editor.TextArea.Caret.Offset);
	}

	[TestMethod]
	public void Complete_SnippetItemWithoutFinalTabstop_InsertsExpandedText()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("sp");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("write")
		{
			InsertText = "write(${1:name})",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		Assert.AreEqual("write(name)", editor.Text);
	}

	[TestMethod]
	public void Complete_SnippetItemWithMalformedSnippet_InsertsTheTextAsIs()
	{
		TextEditor editor = CompletionTestHost.CreateEditor("a");
		var data = new TextCompletionItemCompletionData(new TextCompletionItem("a")
		{
			InsertText = "a${1:b",
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		});

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 1 }, EventArgs.Empty);

		Assert.AreEqual("a${1:b", editor.Text);
	}
}
