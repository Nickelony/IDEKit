#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionCommitCharacterObserverTests
{
	[TestMethod]
	public void TextInput_ReadsTheActiveWindow_UntilTheObserverIsDisposed()
	{
		TextEditor editor = CreateEditor();
		int reads = 0;

		var observer = new CompletionCommitCharacterObserver(
			editor.TextArea,

			() =>
			{
				reads++;
				return null;
			},

			acceptOnCommitCharacters: true
		);

		TypeThroughInputRoute(editor, ".");

		Assert.IsGreaterThan(0, reads);

		observer.Dispose();
		reads = 0;

		TypeThroughInputRoute(editor, ".");

		Assert.AreEqual(0, reads);
	}

	[TestMethod]
	public void TunnelingTextInput_CommitCharacter_RequestsInsertionOnceAndTypesTheCharacterAfterTheCommit()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("pr");

		using (hostWindow)
		{
			(CompletionWindow window, Func<int> getRequestCount) = CreateWindowWithCommitItem(editor);

			// The window stays reachable after it closes, so a second stage that repeated the check would request the insertion again.
			using var observer = new CompletionCommitCharacterObserver(editor.TextArea, () => window, acceptOnCommitCharacters: true);

			TypeThroughInputRoute(editor, "(");

			Assert.AreEqual(1, getRequestCount());
			Assert.AreEqual("print(", editor.Text);
			Assert.AreEqual("print(".Length, editor.TextArea.Caret.Offset);
		}
	}

	[TestMethod]
	public void TunnelingTextInput_CharacterOutsideTheCommitSet_PassesThroughWithoutRequestingInsertion()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("pr");

		using (hostWindow)
		{
			(CompletionWindow window, Func<int> getRequestCount) = CreateWindowWithCommitItem(editor);

			using var observer = new CompletionCommitCharacterObserver(editor.TextArea, () => window, acceptOnCommitCharacters: true);

			TypeThroughInputRoute(editor, "n");

			Assert.AreEqual(0, getRequestCount());
			Assert.AreEqual("prn", editor.Text);
		}
	}

	[TestMethod]
	public void ProgrammaticInput_BypassesTheTunnelingStage_AndDoesNotCommit()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("pr");

		using (hostWindow)
		{
			(CompletionWindow window, Func<int> getRequestCount) = CreateWindowWithCommitItem(editor);

			using var observer = new CompletionCommitCharacterObserver(editor.TextArea, () => window, acceptOnCommitCharacters: true);

			editor.TextArea.PerformTextInput("(");

			Assert.AreEqual(0, getRequestCount());
			Assert.AreEqual("pr(", editor.Text);
		}
	}

	private static (CompletionWindow Window, Func<int> GetRequestCount) CreateWindowWithCommitItem(TextEditor editor)
	{
		editor.CaretOffset = 2;

		var item = new StubCompletionData("print", commitCharacters: ["("]);
		var window = new CompletionWindow(editor.TextArea) { StartOffset = 0, EndOffset = 2 };

		window.CompletionList.CompletionData.Add(item);
		window.Show();
		window.CompletionList.SelectedItem = item;

		int requests = 0;
		window.CompletionList.InsertionRequested += (_, _) => requests++;

		return (window, () => requests);
	}
}
