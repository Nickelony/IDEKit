using Avalonia;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>Verifies the caret move that takes its position from a pointer event of the AvaloniaEdit editor navigation operations.</summary>
/// <remarks>
/// The tests build the event with the window as its root visual, so the position it carries is a window coordinate that the member translates
/// into the text view.
/// </remarks>
[AvaloniaTestClass]
public sealed class TextEditorNavigationExtensionsPointerTests
{
	[TestMethod]
	public void TryMoveCaretToMousePosition_PointerOverText_MovesTheCaretAndCollapsesTheSelection()
	{
		using HostWindow hostWindow = ShowEditor(out TextEditor editor);

		editor.Select(0, 3);
		_ = TestBitmapRendering.PumpAndRender(editor);

		bool moved = editor.TryMoveCaretToMousePosition(CreatePointerEvent(editor, hostWindow, PositionOverLine(editor, lineNumber: 2)));

		Assert.IsTrue(moved);
		Assert.AreEqual(0, editor.SelectionLength);
		Assert.AreEqual(2, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);
	}

	[TestMethod]
	public void TryMoveCaretToMousePosition_PointerMidLine_PlacesTheCaretAtThePointerColumn()
	{
		using HostWindow hostWindow = ShowEditor(out TextEditor editor);

		VisualLine line = editor.TextArea.TextView.VisualLines.First(visualLine => visualLine.FirstDocumentLine.LineNumber == 3);

		Point textViewOrigin = TestHost.TranslatePointTo(editor.TextArea.TextView, new Point(0.0, 0.0), editor);
		Point boundary = line.GetVisualPosition(3, VisualYPosition.TextTop);

		var pointer = new Point(textViewOrigin.X + boundary.X + 1.0, textViewOrigin.Y + boundary.Y + 2.0);

		bool moved = editor.TryMoveCaretToMousePosition(CreatePointerEvent(editor, hostWindow, pointer));

		Assert.IsTrue(moved);
		Assert.AreEqual(editor.Document.GetLineByNumber(3).Offset + 3, editor.CaretOffset);
	}

	[TestMethod]
	public void TryMoveCaretToMousePosition_PointerPastTheLastLine_ReturnsFalseAndLeavesTheCaret()
	{
		using HostWindow hostWindow = ShowEditor(out TextEditor editor);

		Point belowTheText = PositionOverLine(editor, lineNumber: 3) + new Vector(0.0, 200.0);

		bool moved = editor.TryMoveCaretToMousePosition(CreatePointerEvent(editor, hostWindow, belowTheText));

		Assert.IsFalse(moved);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void TryMoveCaretToMousePosition_PointerOverAMargin_ReturnsFalseAndLeavesTheCaret()
	{
		using HostWindow hostWindow = ShowEditor(out TextEditor editor);

		Point overTheLineNumbers = PositionOverLine(editor, lineNumber: 2) - new Vector(4.0, 0.0);

		bool moved = editor.TryMoveCaretToMousePosition(CreatePointerEvent(editor, hostWindow, overTheLineNumbers));

		Assert.IsFalse(moved);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void TryMoveCaretToMousePosition_PointerOutsideTheEditor_ReturnsFalseAndLeavesTheCaret()
	{
		using HostWindow hostWindow = ShowEditor(out TextEditor editor);

		bool moved = editor.TryMoveCaretToMousePosition(CreatePointerEvent(editor, hostWindow, new Point(-50.0, -50.0)));

		Assert.IsFalse(moved);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void TryMoveCaretToMousePosition_NullArguments_Throw()
	{
		using HostWindow hostWindow = ShowEditor(out TextEditor editor);

		Assert.ThrowsExactly<ArgumentNullException>(() => editor.TryMoveCaretToMousePosition((PointerEventArgs)null!));

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			TextEditorNavigationExtensions.TryMoveCaretToMousePosition(null!, CreatePointerEvent(editor, hostWindow, new Point(0.0, 0.0)))
		);
	}

	/// <summary>Shows a three-line editor with line numbers, so a margin lies left of the text view, and returns its host window.</summary>
	private static HostWindow ShowEditor(out TextEditor editor)
	{
		(editor, HostWindow hostWindow) = TestHost.ShowHostedEditor("one\r\ntwo\r\nthree", e => e.ShowLineNumbers = true);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		return hostWindow;
	}

	/// <summary>
	/// The point of the editor two device-independent units inside the first row of a line, measured from the left edge of the text view.
	/// </summary>
	private static Point PositionOverLine(TextEditor editor, int lineNumber)
	{
		VisualLine line = editor.TextArea.TextView.VisualLines.First(visualLine => visualLine.FirstDocumentLine.LineNumber == lineNumber);
		Point textViewOrigin = TestHost.TranslatePointTo(editor.TextArea.TextView, new Point(0.0, 0.0), editor);
		double lineTop = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop);

		return new Point(textViewOrigin.X + 2.0, textViewOrigin.Y + lineTop + 2.0);
	}

	/// <summary>
	/// Builds a pointer-pressed event whose position is the given point of the editor, in the coordinates of the window that hosts it.
	/// </summary>
	private static PointerPressedEventArgs CreatePointerEvent(TextEditor editor, HostWindow hostWindow, Point editorPoint)
	{
		return new(
			source: editor,
			pointer: new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
			rootVisual: hostWindow,
			rootVisualPosition: TestHost.TranslatePointTo(editor, editorPoint, hostWindow),
			timestamp: 0UL,
			properties: new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed),
			modifiers: KeyModifiers.None
		);
	}
}
