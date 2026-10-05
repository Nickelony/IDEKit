#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class TextAreaNavigationExtensionsTests
{
	[TestMethod]
	public async Task GoToDefinitionAsync_WithAProvider_SendsTheNameAndTheOffset()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new StubDefinitionProvider(LocationAt(1));
		int offset = DocumentText.IndexOf("bravo", StringComparison.Ordinal) + 2;

		using (hostWindow)
			await editor.TextArea.GoToDefinitionAsync(DocumentId, provider, offset, cancellationToken: TestContext.CancellationToken);

		Assert.IsNotNull(provider.LastRequest);
		Assert.AreEqual("bravo", provider.LastRequest.SymbolName);
		Assert.AreEqual(offset, provider.LastRequest.SymbolOffset);
		Assert.AreEqual(DocumentId, provider.LastRequest.DocumentId);
	}

	[TestMethod]
	public async Task GoToDefinitionAsync_ALocationThatNamesThisDocument_MovesTheCaretWithoutTheCallback()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool callbackCalled = false;

		using (hostWindow)
		{
			// A language server names the requesting document too, so the location carries this document's identifier.
			bool navigated = await editor.TextArea.GoToDefinitionAsync(
				DocumentId,
				provider: new StubDefinitionProvider(LocationAt(3, 8, DocumentId)),
				offset: 0,

				tryNavigateCrossFileDefinitionAsync: (_, _) =>
				{
					callbackCalled = true;
					return Task.FromResult(false);
				},

				cancellationToken: TestContext.CancellationToken
			);

			Assert.IsTrue(navigated);
			Assert.IsFalse(callbackCalled);
			Assert.AreEqual(DocumentText.IndexOf("charlie", StringComparison.Ordinal), editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionAsync_ALocationInAnotherDocument_ReachesTheCallback()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		DefinitionLocation? reported = null;

		using (hostWindow)
		{
			bool navigated = await editor.TextArea.GoToDefinitionAsync(
				DocumentId,
				provider: new StubDefinitionProvider(LocationAt(2, documentId: "other.txt")),
				offset: 0,

				tryNavigateCrossFileDefinitionAsync: (location, _) =>
				{
					reported = location;
					return Task.FromResult(true);
				},

				cancellationToken: TestContext.CancellationToken
			);

			Assert.IsTrue(navigated);
			Assert.AreEqual("other.txt", reported?.DocumentId);
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void GoToDefinitionAsync_DocumentReplacedWhileTheProviderRuns_DoesNotApplyTheStaleLocation()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var answer = new TaskCompletionSource<DefinitionLocation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using (hostWindow)
		{
			Task<bool> navigation = editor.TextArea.GoToDefinitionAsync(
				DocumentId,
				provider: new ScriptedDefinitionProvider((_, _) => answer.Task),
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			);

			// Replacing the document instance invalidates the snapshot even though the returned line and column would still fit the new document.
			editor.Document = new TextDocument("other gamma = 4\nother delta = 5\nother epsilon = 6");
			answer.SetResult(LocationAt(2, 8));

			DispatcherTestHelpers.PumpUntil(() => navigation.IsCompleted);

			Assert.IsFalse(navigation.GetAwaiter().GetResult());
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionAsync_BareTextAreaHost_AppliesTheLocation()
	{
		var textArea = new TextArea { Document = new TextDocument(DocumentText) };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(textArea);

		bool navigated = await textArea.GoToDefinitionAsync(
			DocumentId,
			provider: new StubDefinitionProvider(LocationAt(3, 5)),
			offset: 0,
			cancellationToken: TestContext.CancellationToken
		);

		DocumentLine line = textArea.Document.GetLineByNumber(3);

		Assert.IsTrue(navigated);
		Assert.AreEqual(line.Offset + 4, textArea.Caret.Offset);
		Assert.IsTrue(textArea.Selection.IsEmpty);
	}

	[TestMethod]
	public void GoToDefinitionAsync_TokenCanceledWhileAProviderThatIgnoresItRuns_AppliesNothingAndCancels()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var answer = new TaskCompletionSource<DefinitionLocation?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using (hostWindow)
		{
			using var cancellation = new CancellationTokenSource();

			// The provider answers a location after the cancellation, the way one that cannot cancel its work does.
			Task<bool> navigation = editor.TextArea.GoToDefinitionAsync(
				DocumentId,
				provider: new ScriptedDefinitionProvider((_, _) => answer.Task),
				offset: 0,
				cancellationToken: cancellation.Token
			);

			cancellation.Cancel();
			answer.SetResult(LocationAt(3, 8));

			DispatcherTestHelpers.PumpUntil(() => navigation.IsCompleted);

			Assert.IsTrue(navigation.IsCanceled);
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void GoToDefinitionAsync_TokenCanceledBeforeACrossFileLocationArrives_DoesNotCallTheCallback()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var answer = new TaskCompletionSource<DefinitionLocation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool callbackCalled = false;

		using (hostWindow)
		{
			using var cancellation = new CancellationTokenSource();

			Task<bool> navigation = editor.TextArea.GoToDefinitionAsync(
				DocumentId,
				provider: new ScriptedDefinitionProvider((_, _) => answer.Task),
				offset: 0,

				tryNavigateCrossFileDefinitionAsync: (_, _) =>
				{
					callbackCalled = true;
					return Task.FromResult(true);
				},

				cancellationToken: cancellation.Token
			);

			cancellation.Cancel();
			answer.SetResult(LocationAt(1, documentId: "other.txt"));

			DispatcherTestHelpers.PumpUntil(() => navigation.IsCompleted);

			Assert.IsTrue(navigation.IsCanceled);
			Assert.IsFalse(callbackCalled);
		}
	}
}
