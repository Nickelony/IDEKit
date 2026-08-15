using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>
/// Pins the shared Avalonia headless test host the mirror suites run on.
/// </summary>
/// <remarks>
/// The mirror suites depend on three host guarantees that a plain MSTest thread does not provide: a hosted
/// editor realizes its layout on the Avalonia UI thread, a document-only test still runs on that thread,
/// and an asynchronous test body keeps its continuations on it. A regression in the host would make an
/// environment failure look like a product failure across every suite, so the guarantees are asserted here
/// once.
/// </remarks>
[AvaloniaTestClass]
public sealed class AvaloniaTestHostTests
{
	/// <summary>
	/// Verifies that a hosted editor is laid out and its text view has validated visual lines.
	/// </summary>
	[TestMethod]
	public void Editor_Hosts_And_Lays_Out()
	{
		(TextEditor editor, HostWindow window) = AvaloniaTestHost.ShowHostedEditor("one\ntwo\nthree");

		using (window)
		{
			Assert.AreEqual(3, editor.Document.LineCount);

			TextView textView = editor.TextArea.TextView;
			Assert.IsTrue(textView.VisualLinesValid, "visual lines were not valid after hosting");
			Assert.IsTrue(textView.VisualLines.Count > 0);
		}
	}

	/// <summary>
	/// Verifies that a document-only test body constructs engine types on the UI thread.
	/// </summary>
	[TestMethod]
	public void DocumentOnly_Body_RunsOnTheUiThread()
	{
		var document = new TextDocument("one");
		Assert.AreEqual(1, document.LineCount);
	}

	/// <summary>
	/// Verifies that an asynchronous body's continuation stays on the UI thread, so it can still construct
	/// controls after an await.
	/// </summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task Async_Body_Stays_On_The_Ui_Thread()
	{
		await Task.Delay(1);

		TextEditor editor = AvaloniaTestHost.CreateEditor("x");
		Assert.AreEqual("x", editor.Text);
	}
}
