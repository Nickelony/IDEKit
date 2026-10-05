#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Indentation;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Indentation;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Verifies that the selection source reports the caret, selection, and document of a real text area, and raises its notifications for the events
/// the controller depends on. The controller that consumes the source is covered by the controller tests, which run without an editor.
/// </summary>
/// <remarks>
/// The editors are never shown in a window, so the class also runs in a headless leg.
/// </remarks>
[STATestClass]
public sealed class CodeActionSelectionSourceTests
{
	[TestMethod]
	public void Capture_WithSelection_ReportsTheDocumentTextTheCaretAndTheSelectedRange()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		editor.Select(2, 3);

		CodeActionSelection? context = source.Capture();

		Assert.IsNotNull(context);
		Assert.AreEqual("one\ntwo\nthree", context.GetText());
		Assert.AreEqual(editor.CaretOffset, context.CaretOffset);
		Assert.AreEqual(new TextRange(2, 3), context.Range);
	}

	[TestMethod]
	public void Capture_SelectionWithCaretAtItsStart_ReportsTheSelectedRange()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		// The selection spans 2..5 while the caret sits at its start.
		editor.TextArea.Selection = Selection.Create(editor.TextArea, 2, 5);
		editor.CaretOffset = 2;

		CodeActionSelection? context = source.Capture();

		Assert.IsNotNull(context);
		Assert.AreEqual(2, context.CaretOffset);
		Assert.AreEqual(new TextRange(2, 3), context.Range);
	}

	[TestMethod]
	public void Capture_SelectionCreatedBackward_ReportsTheOrderedSelectionRange()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		// A right-to-left drag anchors the selection at the offset after the one it ends at.
		editor.TextArea.Selection = Selection.Create(editor.TextArea, 5, 2);

		CodeActionSelection? context = source.Capture();

		Assert.IsNotNull(context);
		Assert.AreEqual(new TextRange(2, 3), context.Range);
	}

	[TestMethod]
	public void Capture_EmptySelection_CollapsesTheSelectionToTheCaret()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		editor.CaretOffset = 5;

		CodeActionSelection? context = source.Capture();

		Assert.IsNotNull(context);
		Assert.AreEqual(5, context.CaretOffset);
		Assert.AreEqual(new TextRange(5, 0), context.Range);
	}

	[TestMethod]
	public void Capture_DocumentEditedAfterwards_StillReportsTheCapturedText()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		CodeActionSelection? context = source.Capture();

		editor.Document.Insert(0, "zero ");

		// The context carries a snapshot, so a request that is still being built sees the text its offsets refer to.
		Assert.IsNotNull(context);
		Assert.AreEqual("one\ntwo\nthree", context.GetText());
	}

	[TestMethod]
	public void Capture_WithoutDocument_ReturnsNull()
	{
		TextEditor editor = TestHost.CreateEditor("one");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		editor.Document = null!;

		Assert.IsNull(source.Capture());
	}

	[TestMethod]
	public void CaretLineNumber_FollowsTheCaretAndIsNullWithoutADocument()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		Assert.AreEqual(1, source.CaretLineNumber);

		editor.CaretOffset = 4;
		Assert.AreEqual(2, source.CaretLineNumber);

		editor.CaretOffset = editor.Document.TextLength;
		Assert.AreEqual(3, source.CaretLineNumber);

		editor.Document = null!;
		Assert.IsNull(source.CaretLineNumber);
	}

	[TestMethod]
	public void Changed_CaretMove_IsRaised()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		int changedCount = 0;
		source.Changed += (_, _) => changedCount++;

		editor.CaretOffset = 4;

		Assert.AreNotEqual(0, changedCount);
	}

	[TestMethod]
	public void Changed_SelectionChange_IsRaised()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		int changedCount = 0;
		source.Changed += (_, _) => changedCount++;

		// The caret stays where it is, so only the selection notification can raise the change.
		editor.TextArea.Selection = Selection.Create(editor.TextArea, 1, 3);

		Assert.AreNotEqual(0, changedCount);
	}

	[TestMethod]
	public void Capture_AfterABatchOfIndentationEdits_ReportsTheEditedTextAndTheSelectionTheEditorHolds()
	{
		TextEditor editor = TestHost.CreateEditor("a\n    b\n        c\n    d\n");
		editor.Options.IndentationSize = 4;

		using var source = new CodeActionSelectionSource(editor.TextArea);
		int raised = 0;
		source.Changed += (_, _) => raised++;

		editor.Select(6, 9);
		int before = raised;

		editor.ConvertIndentationToTabs();

		CodeActionSelection? context = source.Capture();

		Assert.IsGreaterThan(before, raised);
		Assert.IsNotNull(context);
		Assert.AreEqual("a\n\tb\n\t\tc\n\td\n", context.GetText());
		Assert.AreEqual(new TextRange(editor.SelectionStart, editor.SelectionLength), context.Range);
	}

	[TestMethod]
	public void Changed_DocumentEdit_IsRaisedWithoutMovingTheCaret()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		editor.CaretOffset = editor.Document.TextLength;

		int changedCount = 0;
		source.Changed += (_, _) => changedCount++;

		// An edit on another line leaves the caret position and the selection alone, so only the content notification can raise the change.
		editor.Document.Insert(0, "zero ");

		Assert.AreNotEqual(0, changedCount);
	}

	[TestMethod]
	public void DocumentReplaced_DocumentSwap_IsRaisedOncePerSwap()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		int replacedCount = 0;
		source.DocumentReplaced += (_, _) => replacedCount++;

		editor.Document = new TextDocument("alpha\nbeta");

		Assert.AreEqual(1, replacedCount);

		editor.Document = new TextDocument("gamma");

		Assert.AreEqual(2, replacedCount);
	}

	[TestMethod]
	public void DocumentReplaced_Handler_FindsTheNewDocumentAttachedAlready()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		int changedDuringHandler = 0;
		string? capturedText = null;

		source.DocumentReplaced += (_, _) =>
		{
			capturedText = source.Capture()?.GetText();

			// The source observes the new document by now, so an edit made while the handler runs reaches Changed. The edit lands at the end of
			// the text, away from the caret at the start, so only the content notification can raise the change.
			source.Changed += (_, _) => changedDuringHandler++;
			editor.Document.Insert(editor.Document.TextLength, "x");
		};

		editor.Document = new TextDocument("alpha\nbeta");

		Assert.AreEqual("alpha\nbeta", capturedText);
		Assert.AreNotEqual(0, changedDuringHandler);
	}

	[TestMethod]
	public void DocumentSwap_ObservesTheNewDocumentAndIgnoresTheOldOne()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		using var source = new CodeActionSelectionSource(editor.TextArea);

		TextDocument firstDocument = editor.Document;
		TextDocument secondDocument = new("alpha\nbeta");

		editor.Document = secondDocument;

		int changedCount = 0;
		source.Changed += (_, _) => changedCount++;

		// Only the new document is observed: changing the old one raises nothing.
		firstDocument.Insert(0, "ignored ");

		Assert.AreEqual(0, changedCount);

		// The edit lands at the end of the text, away from the caret at the start, so only the content notification can raise the change.
		secondDocument.Insert(secondDocument.TextLength, " seen");

		Assert.AreNotEqual(0, changedCount);
	}

	[TestMethod]
	public void Dispose_DetachesFromTheCaretTheSelectionAndTheDocuments()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");
		TextDocument document = editor.Document;

		var source = new CodeActionSelectionSource(editor.TextArea);

		int changedCount = 0;
		int replacedCount = 0;

		source.Changed += (_, _) => changedCount++;
		source.DocumentReplaced += (_, _) => replacedCount++;

		source.Dispose();

		editor.CaretOffset = 4;
		editor.TextArea.Selection = Selection.Create(editor.TextArea, 1, 3);
		document.Insert(0, "zero ");
		editor.Document = new TextDocument("alpha");
		editor.Document.Insert(0, "beta ");

		Assert.AreEqual(0, changedCount);
		Assert.AreEqual(0, replacedCount);
	}
}
