#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.AvaloniaEdit.Comments;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Formatting;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Scanning;
using Nickelony.IDEKit.Core.Text;
using static Nickelony.IDEKit.AvaloniaEdit.Tests.EditingTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.AutoClosing;
using Nickelony.IDEKit.AvalonEdit.Comments;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Formatting;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Scanning;
using Nickelony.IDEKit.Core.Text;
using static Nickelony.IDEKit.AvalonEdit.Tests.EditingTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>Verifies the documented no-document behavior of the editor helpers.</summary>
/// <remarks>
/// This class holds only the non-hosted cases, so it carries no interactive-window category and runs on a headless leg. The single hosted case
/// (a backspace over a real window) lives in <see cref="NoDocumentHostedBehaviorTests"/>.
/// </remarks>
[STATestClass]
public sealed class NoDocumentOperationTests
{
	private const string NoDocumentMessage = "The editor has no document assigned.";

	private static readonly CommentSyntax s_doubleSlashSyntax = new("//", null, StringLiteralStyles.None);

	/// <summary>Creates an editor without a document by clearing the empty one the parameterless constructor assigns.</summary>
	private static TextEditor CreateEditorWithoutDocument() => new() { Document = null };

	[TestMethod]
	public void InsertText_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => editor.InsertText(0, "x"));

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void ApplyEdit_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => editor.ApplyEdit(new TextEditRequest(new TextRange(0, 0), "x")));

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
		var service = new DocumentFormatterService();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() => service.FormatDocument(editor, new IdentityFormatter()));

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void CommentServiceApplyEdit_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var editor = CreateEditorWithoutDocument();
		var service = new LineCommentService();

		var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
			service.ApplyEdit(editor, s_doubleSlashSyntax, LineCommentActionKind.Comment)
		);

		Assert.AreEqual(NoDocumentMessage, exception.Message);
	}

	[TestMethod]
	public void ApplyTextEntering_EditorWithoutDocument_ReturnsNoneResult()
	{
		var editor = CreateEditorWithoutDocument();
		var service = new AutoClosingService();
		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = service.ApplyTextEntering(editor, e, AutoClosingOptions.Default);

		// There is nothing to resolve against or to insert into, so the input stays unhandled and
		// normal text input applies.
		Assert.AreEqual(AutoClosingActionKind.None, result.Action.Kind);
		Assert.IsFalse(e.Handled);
	}

	[TestMethod]
	public void TryApply_EditorWithoutDocumentAndAllNoOpBatch_ReportsSuccess()
	{
		var editor = CreateEditorWithoutDocument();
		var target = new TextEditorEditTarget(editor);

		// An all-no-op batch changes neither the document nor its undo stack, so it needs no document.
		Assert.IsTrue(target.TryApply(PreparedBatchFactory.Create([]), expectedVersion: 0));
	}

	[TestMethod]
	public void TryApply_EditorWithoutDocumentAndApplicableBatch_ReportsFailure()
	{
		var editor = CreateEditorWithoutDocument();
		var target = new TextEditorEditTarget(editor);

		Assert.IsFalse(
			target.TryApply(
				PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(0, 0), newText: "x", editIndex: 0)]),
				expectedVersion: 0
			)
		);
	}
}
