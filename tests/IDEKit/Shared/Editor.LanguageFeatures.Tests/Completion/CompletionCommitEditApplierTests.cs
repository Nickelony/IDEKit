#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Verifies that the completion commit applier writes a plan into the text area: the caret derived from a snippet placeholder lands
/// inside the document, and a text area without a document is rejected.
/// </summary>
[STATestClass]
public sealed class CompletionCommitEditApplierTests
{
	[TestMethod]
	public void Apply_TextAreaWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = new TextEditor { Document = null };

		var request = new CompletionCommitRequest(
			TextArea: editor.TextArea,
			CompletionSegment: new TextSegment { StartOffset = 0, Length = 0 },
			InsertText: "x",
			CaretOffsetInInsertText: null,
			AdditionalTextEdits: [],
			PrimaryEdit: null
		);

		Assert.ThrowsExactly<InvalidOperationException>(() => CompletionCommitEditApplier.Apply(request));
	}

	[TestMethod]
	public void Apply_CaretOffsetBeyondTheDocument_IsClampedToTheDocumentEnd()
	{
		TextEditor editor = CompletionTestHelpers.CreateEditor("ab");

		CompletionCommitEditApplier.Apply(
			new CompletionCommitRequest(
				TextArea: editor.TextArea,
				CompletionSegment: new TextSegment { StartOffset = 2, Length = 0 },
				InsertText: "x",
				CaretOffsetInInsertText: 40,
				AdditionalTextEdits: [],
				PrimaryEdit: null
			)
		);

		Assert.AreEqual("abx", editor.Text);
		Assert.AreEqual(editor.Document.TextLength, editor.TextArea.Caret.Offset);
	}
}
