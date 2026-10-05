using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace Nickelony.Samples.Avalonia.Tests;

/// <summary>Verifies the right press of the sample editors: it keeps a selection it lands in and otherwise moves the caret to the pointer.</summary>
[TestClass]
public sealed class SampleWindowRightPressTests
{
	private const string Text = "alpha beta gamma";

	[TestMethod]
	public async Task RightPress_InsideTheSelection_KeepsTheSelectionAndTheCaret()
	{
		using var directory = new TemporaryDirectory("sample-avalonia-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				TextEditor editor = await PrepareAsync(window);

				// "beta" is selected and the press lands on its third letter.
				window.MouseDown(PointOver(window, editor, Text.IndexOf("beta", StringComparison.Ordinal) + 2), MouseButton.Right);

				Assert.AreEqual(Text.IndexOf("beta", StringComparison.Ordinal), editor.SelectionStart);
				Assert.AreEqual("beta".Length, editor.SelectionLength);
				Assert.AreEqual(Text.IndexOf("beta", StringComparison.Ordinal) + "beta".Length, editor.CaretOffset);
			}
		);
	}

	[TestMethod]
	public async Task RightPress_OutsideTheSelection_MovesTheCaretToThePointerAndClearsTheSelection()
	{
		using var directory = new TemporaryDirectory("sample-avalonia-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				TextEditor editor = await PrepareAsync(window);
				int target = Text.IndexOf("gamma", StringComparison.Ordinal) + 2;

				window.MouseDown(PointOver(window, editor, target), MouseButton.Right);

				Assert.AreEqual(0, editor.SelectionLength);
				Assert.AreEqual(target, editor.CaretOffset);
			}
		);
	}

	// Shows the text with "beta" selected, and waits until the view has laid out the line the presses land on.
	private static async Task<TextEditor> PrepareAsync(SampleWindow window)
	{
		TextEditor editor = window.Workbench.LuaPane.Editor;
		editor.Text = Text;
		editor.Select(Text.IndexOf("beta", StringComparison.Ordinal), "beta".Length);

		await SampleTestHost.WaitUntilAsync(
			() =>
			{
				editor.TextArea.TextView.EnsureVisualLines();
				return editor.TextArea.TextView.VisualLinesValid && editor.TextArea.TextView.IsEffectivelyVisible;
			},

			"The editor did not lay out its line."
		);

		return editor;
	}

	// The window point over the start of a character, one pixel in, which the editor maps to the offset before it.
	private static Point PointOver(SampleWindow window, TextEditor editor, int offset)
	{
		TextView textView = editor.TextArea.TextView;
		VisualLine line = textView.VisualLines[0];

		Point visual = line.GetVisualPosition(offset, VisualYPosition.TextMiddle);
		Point inView = new(visual.X - textView.ScrollOffset.X + 1.0, visual.Y - textView.ScrollOffset.Y);

		return textView.TranslatePoint(inView, window) ?? throw new InvalidOperationException("The text view is not in the window.");
	}
}
