#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class CompletionItemAdapterTests
{
	[TestMethod]
	public void Description_MarkdownDocumentation_IsTheMarkdownText()
	{
		var data = new CompletionItemAdapter(
			new CompletionItem("format") { Documentation = new MarkupContent("**Formats** its arguments.", MarkupKind.Markdown) }
		);

		// The editor's own tooltip shows this text, so the kind travels beside it and never replaces it.
		Assert.AreEqual("**Formats** its arguments.", data.Description);
	}

	[TestMethod]
	public void Description_PlainTextDocumentation_IsTheText()
	{
		var data = new CompletionItemAdapter(new CompletionItem("print") { Documentation = "Prints its arguments." });
		Assert.AreEqual("Prints its arguments.", data.Description);
	}

	[TestMethod]
	public void Description_WithoutDocumentation_IsNull()
	{
		var data = new CompletionItemAdapter(new CompletionItem("print"));
		Assert.IsNull(data.Description);
	}

	[TestMethod]
	public void Content_PlainItem_IsTheLabel()
	{
		var data = new CompletionItemAdapter(new CompletionItem("print"));
		Assert.AreEqual("print", data.Content);
	}

	[TestMethod]
	public void Content_DeprecatedItem_IsTheStruckThroughLabel()
	{
		var data = new CompletionItemAdapter(new CompletionItem("print") { Tags = [CompletionItemTag.Deprecated] });

		var content = (TextBlock)data.Content;

		Assert.AreEqual("print", content.Text);
		Assert.AreSame(TextDecorations.Strikethrough, content.TextDecorations);

		// The same element is returned on every read, and the text used for filtering is unchanged.
		Assert.AreSame(content, data.Content);
		Assert.AreEqual("print", data.Text);
	}

	[TestMethod]
	public void Complete_DeprecatedItem_CommitsLikeAnyOtherItem()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("pr");
		var data = new CompletionItemAdapter(new CompletionItem("print") { InsertText = "print", Tags = [CompletionItemTag.Deprecated] });

		data.Complete(editor.TextArea, new TextSegment { StartOffset = 0, Length = 2 }, EventArgs.Empty);

		// The tag changes the rendering only; the item stays committable.
		Assert.AreEqual("print", editor.Text);
	}

	[TestMethod]
	public void Text_ItemWithFilterText_IsTheFilterTextWhileTheContentStaysTheLabel()
	{
		var data = new CompletionItemAdapter(new CompletionItem("print(a, b)") { FilterText = "print" });

		// the editor filters and ranks against Text, which follows the shared item's filter text, while the
		// displayed content stays the faithful label.
		Assert.AreEqual("print", data.Text);
		Assert.AreEqual("print(a, b)", data.Content);
	}

	[TestMethod]
	public void Text_ItemWithoutFilterText_FallsBackToTheLabel()
	{
		var data = new CompletionItemAdapter(new CompletionItem("print"));
		Assert.AreEqual("print", data.Text);
	}
}
