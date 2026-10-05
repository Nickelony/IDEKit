using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>Verifies the ambient-pointer caret move of the WPF editor navigation operations and the resolver overload it builds on.</summary>
/// <remarks>
/// The AvaloniaEdit binding takes the position from a pointer event instead, and its own suite covers that member.
/// The wrapper reads the real mouse state; an editor that was never hosted cannot be under the mouse, so the first scenario exercises the real
/// not-over-the-editor branch.
/// </remarks>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class TextEditorNavigationExtensionsPointerTests
{
	[TestMethod]
	public void TryMoveCaretToMousePosition_MouseNotOverEditor_ReturnsFalse()
	{
		TextEditor editor = TestHost.CreateEditor("abc");

		bool moved = editor.TryMoveCaretToMousePosition();

		Assert.IsFalse(moved);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void TryMoveCaretToPointerPosition_PointerOverEditor_MovesTheCaretToThePointer()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.IsNotEmpty(editor.TextArea.TextView.VisualLines);

		VisualLine secondLine = editor.TextArea.TextView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);

		Point textViewOrigin = TestHost.TranslatePointTo(editor, new Point(0.0, 0.0), editor.TextArea.TextView);
		double lineTop = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop);

		bool moved = TextEditorNavigationExtensions.TryMoveCaretToPointerPosition(editor, _ => new Point(textViewOrigin.X + 2.0, lineTop + 2.0));

		Assert.IsTrue(moved);
		Assert.AreEqual(0, editor.SelectionLength);
		Assert.AreEqual(2, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);
	}

	[TestMethod]
	public void TryMoveCaretToPointerPosition_PointerPastTheLastLine_ReturnsFalseAndLeavesTheCaret()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Point textViewOrigin = TestHost.TranslatePointTo(editor, new Point(0.0, 0.0), editor.TextArea.TextView);

		bool moved = TextEditorNavigationExtensions.TryMoveCaretToPointerPosition(editor, _ => new Point(textViewOrigin.X + 2.0, 100000.0));

		Assert.IsFalse(moved);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void TryMoveCaretToPointerPosition_ResolverReportsNoPointer_ReturnsFalse()
	{
		TextEditor editor = TestHost.CreateEditor("abc");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		bool moved = TextEditorNavigationExtensions.TryMoveCaretToPointerPosition(editor, _ => null);

		Assert.IsFalse(moved);
		Assert.AreEqual(0, editor.CaretOffset);
	}
}
