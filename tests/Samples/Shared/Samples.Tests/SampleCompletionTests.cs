using Nickelony.IDEKit.LanguageFeatures.Completion;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.Samples.Avalonia.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers how a completion item of the sample commits into an editor that no window shows.</summary>
[STATestClass]
public sealed class SampleCompletionTests
{
	[TestMethod]
	public async Task TheFunctionSnippet_ExpandsItsPlaceholdersAndSelectsTheFirstWhenItCommits()
	{
		await SampleTestHost.RunAsync(async () =>
		{
			// A first line with a line terminator makes the editor's snippet break its lines with that terminator.
			var editor = new TextEditor { Text = "x\nfun" };

			IReadOnlyList<CompletionItem> items = await new KeywordCompletionProvider().GetCompletionItemsAsync(
				new CompletionRequest("snippet-test.lua", editor.Text, caretOffset: 5)
			);

			CompletionItem snippet = items.Single(item => item.Label == "function" && item.Kind == CompletionItemKind.Snippet);

			new CompletionItemAdapter(snippet).Complete(editor.TextArea, new TextSegment { StartOffset = 2, Length = 3 }, EventArgs.Empty);

			Assert.AreEqual("x\nfunction name(arguments)\n    \nend", editor.Text);
			Assert.AreEqual("name", editor.SelectedText);
		});
	}
}
