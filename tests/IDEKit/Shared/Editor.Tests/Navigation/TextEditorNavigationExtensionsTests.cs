#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>Verifies the point-to-offset and point-to-caret mapping of the editor navigation operations.</summary>
/// <remarks>
/// The <c>TranslatePointTo</c> host hook normalizes the engine's <c>TranslatePoint</c> result so the shared tests call it the same way, and the swept
/// width is the binding's own because the AvaloniaEdit default font is wider.
/// </remarks>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextEditorNavigationExtensionsTests
{
	private const double SweepWidth = EditorViewTestHelpers.SweepWidth;

	[TestMethod]
	public void TryGetOffsetFromPoint_InsideVisualLine_MapsToThatLine()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);

		Point textViewOrigin = TestHost.TranslatePointTo(editor, new Point(0.0, 0.0), editor.TextArea.TextView);
		double lineTop = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop);

		bool resolved = editor.TryGetOffsetFromPoint(new Point(textViewOrigin.X + 2.0, lineTop + 2.0), out int offset);

		Assert.IsTrue(resolved);
		Assert.AreEqual(2, editor.Document.GetLineByOffset(offset).LineNumber);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_OutsideVisualLines_ReturnsFalse()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Point textViewOrigin = TestHost.TranslatePointTo(editor, new Point(0.0, 0.0), editor.TextArea.TextView);

		bool resolved = editor.TryGetOffsetFromPoint(new Point(textViewOrigin.X + 2.0, 100000.0), out int offset);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, offset);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_EditorWithoutDocument_ReturnsFalse()
	{
		// A TextEditor starts with an empty document, so the documented no-document branch is reached
		// by clearing it; the Try contract reports a failed mapping instead of a null-reference failure.
		TextEditor editor = TestHost.CreateEditor("abc");
		editor.Document = null!;

		bool resolved = editor.TryGetOffsetFromPoint(new Point(0.0, 0.0), out int offset);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, offset);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_HorizontalSweep_MapsEveryCharacterCell()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextView textView = editor.TextArea.TextView;

		Assert.IsNotEmpty(textView.VisualLines);

		VisualLine line = textView.VisualLines[0];
		double lineTop = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop);

		double textEndX = line.GetTextLineVisualXPosition(line.TextLines[0], editor.Document.TextLength);

		// The premise: the sample text ends inside the swept width, so the sweep reaches the line end
		// and exercises the clamp. A font-metric change must trip this assertion instead of turning
		// the mapping check vacuous. The swept width is the binding's own.
		Assert.IsGreaterThan(1.0, textEndX);
		Assert.IsLessThan(SweepWidth, textEndX);

		var offsets = new List<int>();

		// Sweep left to right across the first line; every character cell must be hit by at
		// least one point and resolve to that character's exact offset.
		for (double x = 1.0; x < SweepWidth; x += 1.0)
		{
			Point editorPoint = TestHost.TranslatePointTo(textView, new Point(x, lineTop + 2.0), editor);

			if (editor.TryGetOffsetFromPoint(editorPoint, out int offset))
				offsets.Add(offset);
		}

		Assert.IsNotEmpty(offsets);

		for (int i = 1; i < offsets.Count; i++)
			Assert.IsGreaterThanOrEqualTo(offsets[i - 1], offsets[i]);

		// The sweep resolves every offset of "abcdef" in order and clamps to the line end past it.
		Assert.AreSequenceEqual(
			Enumerable.Range(0, editor.Document.TextLength + 1),
			offsets.Distinct(),
			$"Resolved offsets: {string.Join(", ", offsets)}"
		);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_PastLineEnd_ClampsToLineEnd()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextView textView = editor.TextArea.TextView;

		Assert.IsNotEmpty(textView.VisualLines);

		VisualLine line = textView.VisualLines[0];
		double lineTop = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop);

		// The point is beyond the end of the text but still inside the rendered viewport,
		// so the resolved offset is clamped to the line end.
		var textViewPoint = new Point(500.0, lineTop + 2.0);
		Point editorPoint = TestHost.TranslatePointTo(textView, textViewPoint, editor);

		bool resolved = editor.TryGetOffsetFromPoint(editorPoint, out int offset);

		Assert.IsTrue(resolved);
		Assert.AreEqual(editor.Document.TextLength, offset);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_ThirdLine_HorizontalSweep_MapsEveryCharacterCell()
	{
		TextEditor editor = TestHost.CreateEditor("a\r\nbb\r\ncccc");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextView textView = editor.TextArea.TextView;

		Assert.IsNotEmpty(textView.VisualLines);

		VisualLine line = textView.VisualLines.First(candidate => candidate.FirstDocumentLine.LineNumber == 3);
		double lineTop = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop);

		var lineDocument = editor.Document.GetLineByNumber(3);

		double textEndX = line.GetTextLineVisualXPosition(line.TextLines[0], lineDocument.Length);

		// The premise: the third line's text ends inside the swept width, so the sweep reaches the
		// line end and exercises the clamp away from the line start. The swept width is the binding's
		// own.
		Assert.IsGreaterThan(1.0, textEndX);
		Assert.IsLessThan(SweepWidth, textEndX);

		var offsets = new List<int>();

		// Sweep left to right across the third line; every character cell must be hit by at least
		// one point and resolve to that character's exact offset.
		for (double x = 1.0; x < SweepWidth; x += 1.0)
		{
			Point editorPoint = TestHost.TranslatePointTo(textView, new Point(x, lineTop + 2.0), editor);

			if (editor.TryGetOffsetFromPoint(editorPoint, out int offset))
				offsets.Add(offset);
		}

		Assert.IsNotEmpty(offsets);

		for (int i = 1; i < offsets.Count; i++)
			Assert.IsGreaterThanOrEqualTo(offsets[i - 1], offsets[i]);

		// The sweep resolves every offset of the third line in order and clamps to the line end
		// past it; the shorter lines above own different offsets, so a wrong-line mapping fails here.
		Assert.AreSequenceEqual(
			Enumerable.Range(lineDocument.Offset, lineDocument.Length + 1),
			offsets.Distinct(),
			$"Resolved offsets: {string.Join(", ", offsets)}"
		);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_SecondWrappedSegment_MapsToThatSegment()
	{
		// A single document line long enough to wrap several times at the hosted width.
		var document = new TextDocument(string.Join(" ", Enumerable.Repeat("word", 300)));

		var editor = new TextEditor
		{
			Document = document,
			FontSize = 12.0,
			WordWrap = true,
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		IReadOnlyList<VisualLine> visualLines = editor.TextArea.TextView.VisualLines;

		// The premise: word wrap keeps the document line as one visual line made of several text
		// lines, so the second segment exists.
		Assert.HasCount(1, visualLines);
		Assert.IsGreaterThan(1, visualLines[0].TextLines.Count, "The test line must wrap at the hosted width.");

		VisualLine line = visualLines[0];
		var secondSegment = line.TextLines[1];

		int segmentStartOffset = line.FirstDocumentLine.Offset + line.GetTextLineVisualStartColumn(secondSegment);

		// The point sits in the second segment's first character cell.
		double segmentTop = line.GetTextLineVisualYPosition(secondSegment, VisualYPosition.TextTop);
		Point editorPoint = TestHost.TranslatePointTo(editor.TextArea.TextView, new Point(2.0, segmentTop + 2.0), editor);

		bool resolved = editor.TryGetOffsetFromPoint(editorPoint, out int offset);

		Assert.IsTrue(resolved);

		// The resolved offset starts the second wrapped segment instead of collapsing to the line start.
		Assert.AreEqual(segmentStartOffset, offset);
	}

	[TestMethod]
	public void TryMoveCaretToPoint_AtLinePoint_PlacesCaretOnThatLine()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);

		Point textViewOrigin = TestHost.TranslatePointTo(editor, new Point(0.0, 0.0), editor.TextArea.TextView);
		double lineTop = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop);

		bool moved = editor.TryMoveCaretToPoint(new Point(textViewOrigin.X + 2.0, lineTop + 2.0));

		Assert.IsTrue(moved);
		Assert.AreEqual(0, editor.SelectionLength);
		Assert.AreEqual(2, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);
	}

	[TestMethod]
	public void TryMoveCaretToPoint_OutsideVisualLines_ReturnsFalse()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Point textViewOrigin = TestHost.TranslatePointTo(editor, new Point(0.0, 0.0), editor.TextArea.TextView);

		bool moved = editor.TryMoveCaretToPoint(new Point(textViewOrigin.X + 2.0, 100000.0));

		Assert.IsFalse(moved);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void TryMoveCaretToPoint_WithSelection_CollapsesSelectionAndReturnsTrue()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		editor.Select(0, 3);

		// Selecting invalidates the view's visual lines; a render pass restores them before the
		// click position is mapped. The shared bitmap helper pumps the render-priority work and
		// renders the element in both bindings.
		_ = TestBitmapRendering.PumpAndRender(editor);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);

		Point textViewOrigin = TestHost.TranslatePointTo(editor, new Point(0.0, 0.0), editor.TextArea.TextView);
		double lineTop = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop);

		bool moved = editor.TryMoveCaretToPoint(new Point(textViewOrigin.X + 2.0, lineTop + 2.0));

		// A plain click collapses a non-empty selection at the mapped position.
		Assert.IsTrue(moved);
		Assert.AreEqual(0, editor.SelectionLength);
		Assert.AreEqual(2, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_UnhostedEditor_ReturnsFalse()
	{
		// An editor that was never laid out has no visual lines to map against; the documented
		// Try contract reports a failed mapping.
		TextEditor editor = TestHost.CreateEditor("abc");

		bool resolved = editor.TryGetOffsetFromPoint(new Point(0.0, 0.0), out int offset);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, offset);
	}

	[TestMethod]
	public void TryMoveCaretToPoint_EditorWithoutDocument_ReturnsFalse()
	{
		TextEditor editor = TestHost.CreateEditor("abc");
		editor.Document = null!;

		bool moved = editor.TryMoveCaretToPoint(new Point(0.0, 0.0));

		Assert.IsFalse(moved);
	}
}
