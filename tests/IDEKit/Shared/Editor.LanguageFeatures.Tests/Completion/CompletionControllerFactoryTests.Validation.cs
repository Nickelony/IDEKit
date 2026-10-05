using Nickelony.IDEKit.LanguageFeatures.Completion;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class CompletionControllerFactoryTests
{
	[TestMethod]
	public void Create_NullTextArea_ThrowsArgumentNullException()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor();
		using var completion = new CompletionPresenter(editor.TextArea);

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			CompletionControllerFactory.Create(
				textArea: null!,
				documentId: CompletionTestHelpers.TestDocumentId,
				provider: new ScriptedCompletionProvider(),
				presenter: completion
			)
		);

		Assert.AreEqual("textArea", exception.ParamName);
	}

	[TestMethod]
	public void Create_NullProvider_ThrowsArgumentNullException()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor();
		using var completion = new CompletionPresenter(editor.TextArea);

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			CompletionControllerFactory.Create(
				textArea: editor.TextArea,
				documentId: CompletionTestHelpers.TestDocumentId,
				provider: null!,
				presenter: completion
			)
		);

		Assert.AreEqual("provider", exception.ParamName);
	}

	[TestMethod]
	public void Create_NullPresenter_ThrowsArgumentNullException()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor();

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			CompletionControllerFactory.Create(
				textArea: editor.TextArea,
				documentId: CompletionTestHelpers.TestDocumentId,
				provider: new ScriptedCompletionProvider(),
				presenter: null!
			)
		);

		Assert.AreEqual("presenter", exception.ParamName);
	}

	[TestMethod]
	public void Create_BlankDocumentId_ThrowsArgumentException()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor();
		using var completion = new CompletionPresenter(editor.TextArea);

		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			CompletionControllerFactory.Create(
				textArea: editor.TextArea,
				documentId: " ",
				provider: new ScriptedCompletionProvider(),
				presenter: completion
			)
		);

		Assert.AreEqual("documentId", exception.ParamName);
	}

	[TestMethod]
	public void Create_OffEditorThread_ThrowsInvalidOperationException()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor();
		using var completion = new CompletionPresenter(editor.TextArea);

		// The factory verifies the editor thread before it attaches to the text area, so a call from elsewhere subscribes to nothing.
		Exception? captured = TestThreads.CaptureFailure(() =>
			_ = CompletionControllerFactory.Create(
				textArea: editor.TextArea,
				documentId: CompletionTestHelpers.TestDocumentId,
				provider: new ScriptedCompletionProvider(),
				presenter: completion
			)
		);

		Assert.IsInstanceOfType<InvalidOperationException>(captured);
	}
}
