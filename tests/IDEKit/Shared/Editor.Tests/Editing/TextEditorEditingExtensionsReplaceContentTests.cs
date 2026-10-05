#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Editing;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>
/// Covers how <c>ReplaceContent</c> keeps the caret, the selection and the anchors of an editor that another view of the document changed.
/// </summary>
[STATestClass]
public sealed class TextEditorEditingExtensionsReplaceContentTests
{
	private const string Spaces = "function f()\n    if x then\n        print(y)\n    end\nend\n";
	private const string Tabs = "function f()\n\tif x then\n\t\tprint(y)\n\tend\nend\n";

	private static TextEditor CreateEditor(string text)
	{
		TextEditor editor = TestHost.CreateEditor(text);
		editor.Document.UndoStack.ClearAll();

		return editor;
	}

	[TestMethod]
	[DataRow(true, DisplayName = "TheConversionTheOtherViewMade")]
	[DataRow(false, DisplayName = "ItsUndo")]
	public void ReplaceContent_OfAnIndentationConversion_KeepsTheCaretAndTheAnchorsOnTheirText(bool toTabs)
	{
		string from = toTabs ? Spaces : Tabs;
		string to = toTabs ? Tabs : Spaces;

		TextEditor editor = CreateEditor(from);

		// The caret sits before the y of print(y).
		editor.CaretOffset = from.IndexOf("y)", StringComparison.Ordinal);

		TextAnchor anchor = editor.Document.CreateAnchor(from.IndexOf("end\nend", StringComparison.Ordinal));
		int changes = 0;

		editor.Document.Changed += (_, _) => changes++;

		editor.ReplaceContent(to);

		Assert.AreEqual(to, editor.Text);
		Assert.AreEqual(1, changes, "The batch is one change of the document.");
		Assert.AreEqual("y", editor.Text.Substring(editor.CaretOffset, 1));
		Assert.AreEqual(to.IndexOf("end\nend", StringComparison.Ordinal), anchor.Offset);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "TheConversionTheOtherViewMade")]
	[DataRow(false, DisplayName = "ItsUndo")]
	public void ReplaceContent_OfAnIndentationConversion_KeepsASelectionOnTheLineOnItsText(bool toTabs)
	{
		string from = toTabs ? Spaces : Tabs;
		TextEditor editor = CreateEditor(from);

		editor.Select(from.IndexOf("print", StringComparison.Ordinal), 5);

		editor.ReplaceContent(toTabs ? Tabs : Spaces);

		Assert.AreEqual("print", editor.SelectedText);
	}

	[TestMethod]
	public void ReplaceContent_OfAnIndentationConversion_LeavesAnotherSelectionAcrossTheChangedLinesOnItsText()
	{
		TextEditor editor = CreateEditor(Spaces);

		int start = Spaces.IndexOf("if x", StringComparison.Ordinal);
		int end = Spaces.IndexOf("print", StringComparison.Ordinal) + 5;

		editor.Select(start, end - start);

		editor.ReplaceContent(Tabs);

		Assert.AreEqual("if x then\n\t\tprint", editor.SelectedText);
	}

	[TestMethod]
	public void ReplaceContent_OfAConversionOfTheTerminators_KeepsTheCaretAndTheSelectionOnTheirText()
	{
		const string crlf = "alpha\r\nbeta\r\ngamma\r\ndelta\r\n";
		TextEditor editor = CreateEditor(crlf);

		editor.Select(crlf.IndexOf("beta", StringComparison.Ordinal), 4);
		editor.CaretOffset = crlf.IndexOf("mma", StringComparison.Ordinal);

		editor.ReplaceContent(crlf.Replace("\r\n", "\n", StringComparison.Ordinal));

		Assert.AreEqual("mma", editor.Text.Substring(editor.CaretOffset, 3));
	}

	[TestMethod]
	public void ReplaceContent_OfAnExternalChangeToTwoLines_KeepsTheCaretOnALineBetweenThem()
	{
		const string before = "one\ntwo\nthree\nfour\nfive\nsix\n";
		const string after = "ONE\ntwo\nthree\nfour\nfive\nSIX\n";

		TextEditor editor = CreateEditor(before);

		editor.CaretOffset = before.IndexOf("ree", StringComparison.Ordinal);
		editor.Select(before.IndexOf("four", StringComparison.Ordinal), 4);

		editor.ReplaceContent(after);

		Assert.AreEqual("four", editor.SelectedText);
	}

	[TestMethod]
	public void ReplaceContent_OfAnInsertedBlockOfLines_KeepsTheCaretOnTheLinesAfterIt()
	{
		const string before = "a\nb\nc\nd\n";
		const string after = "a\nb\nx1\nx2\nx3\nc\nd\n";

		TextEditor editor = CreateEditor(before);

		editor.CaretOffset = before.IndexOf('d');
		editor.Select(before.IndexOf('a'), 1);

		editor.ReplaceContent(after);

		Assert.AreEqual("a", editor.SelectedText);
	}

	[TestMethod]
	public void ReplaceContent_OfAChangeInsideOneLine_StillReplacesOneRange()
	{
		TextEditor editor = CreateEditor("one\ntwo\nthree");
		var entries = new List<(int Offset, int Removed, int Inserted)>();

		editor.Document.Changed += (_, e) => entries.Add((e.Offset, e.RemovalLength, e.InsertionLength));

		editor.ReplaceContent("one\ntXo\nthree");

		Assert.AreEqual(1, entries.Count);
		Assert.AreEqual((5, 1, 1), entries[0]);
	}

	[TestMethod]
	public void ReplaceContent_OfAMultiLineChange_IsOneUndoStep()
	{
		TextEditor editor = CreateEditor(Spaces);

		editor.ReplaceContent(Tabs);
		editor.Document.UndoStack.Undo();

		Assert.AreEqual(Spaces, editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}
}
