using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Comments;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using System.Windows;
using System.Windows.Input;
using static Nickelony.IDEKit.AvalonEdit.Tests.EditingTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// Verifies the documented behavior of the editing, formatting, comment, and auto-closing helpers on an
/// editor without a document: operations that need the document fail fast with the shared message, while
/// the input handlers leave the input to the editor's own handling.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class NoDocumentBehaviorTests
{
	private const string NoDocumentMessage = "The editor has no document assigned.";

	private static readonly CommentSyntax s_doubleSlashSyntax = new("//", null, StringLiteralStyle.None);

	[TestMethod]
	public void InsertText_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => editor.InsertText(0, "x"));

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void ApplySingleEdit_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => editor.ApplyEdit(new TextEditRequest(0, 0, "x")));

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void SelectLine_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();
		DocumentLine line = new TextDocument("line").GetLineByNumber(1);

		// The missing document is rejected before the line is resolved, so the foreign line never runs
		// through the live-line check.
		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => editor.SelectLine(line));

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void ReplaceLine_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();
		DocumentLine line = new TextDocument("line").GetLineByNumber(1);

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => editor.ReplaceLine(line, "replacement"));

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void ReplaceContent_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => editor.ReplaceContent("content"));

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void FormatDocument_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();
		var service = new TextDocumentFormattingService();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => service.FormatDocument(editor, new IdentityFormatter()));

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void ApplyEdit_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();
		var service = new TextLineCommentService();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
			service.ApplyEdit(editor, s_doubleSlashSyntax, TextLineCommentAction.Comment));

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void HandleBackspace_EditorWithoutDocument_ReturnsFalse()
	{
		var editor = CreateEditorWithoutDocument();
		var service = new TextAutoClosingService();

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(hostWindow), 0, Key.Back)
		{
			RoutedEvent = Keyboard.KeyDownEvent
		};

		// An editor without a document has no pair to delete, so the editor's own Backspace applies.
		Assert.IsFalse(service.HandleBackspace(editor, e, TextAutoClosingOptions.Default));
		Assert.IsFalse(e.Handled);
	}

	[TestMethod]
	public void HandleTextEntering_EditorWithoutDocument_ReturnsNoneResult()
	{
		var editor = CreateEditorWithoutDocument();
		var service = new TextAutoClosingService();
		var e = CreateTextCompositionArgs(editor, "(");

		TextAutoClosingResult result = service.HandleTextEntering(editor, e, TextAutoClosingOptions.Default);

		// There is nothing to resolve against or to insert into, so the input stays unhandled and
		// normal text input applies.
		Assert.AreEqual(TextAutoClosingActionKind.None, result.Action.Kind);
		Assert.IsFalse(e.Handled);
	}

	/// <summary>
	/// Creates an editor without a document. The parameterless constructor assigns an empty document,
	/// so the document must be cleared explicitly to reach the documented no-document state.
	/// </summary>
	private static TextEditor CreateEditorWithoutDocument() => new() { Document = null };

	[TestMethod]
	public void TryApply_EditorWithoutDocumentAndAllNoOpBatch_ReportsSuccess()
	{
		var editor = CreateEditorWithoutDocument();
		var target = new TextEditorEditTarget(editor);

		// An all-no-op batch changes neither the document nor its undo stack, so it needs no document.
		Assert.IsTrue(target.TryApply(PreparedBatch.From([]), expectedVersion: 0));
	}

	[TestMethod]
	public void TryApply_EditorWithoutDocumentAndApplicableBatch_ReportsFailure()
	{
		var editor = CreateEditorWithoutDocument();
		var target = new TextEditorEditTarget(editor);

		Assert.IsFalse(target.TryApply(PreparedBatch.From([new TextEditOperation(0, 0, "x", 0)]), expectedVersion: 0));
	}
}
