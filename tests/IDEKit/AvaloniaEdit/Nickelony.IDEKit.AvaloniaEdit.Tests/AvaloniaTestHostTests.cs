using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>Pins the shared Avalonia headless test host the AvaloniaEdit test suites run on.</summary>
/// <remarks>
/// The AvaloniaEdit suites depend on host guarantees that a plain MSTest thread does not provide: a hosted editor realizes its layout
/// on the Avalonia UI thread, a document-only test still runs on that thread, an asynchronous test body keeps its continuations on it, and a disposed
/// host window closes. A regression in the host would make an environment failure look like a product failure across every suite, so the guarantees
/// are asserted here once.
/// </remarks>
[AvaloniaTestClass]
public sealed class AvaloniaTestHostTests
{
	[TestMethod]
	public void Editor_HostsAndLaysOutTheDocument()
	{
		(TextEditor editor, HostWindow window) = AvaloniaTestHost.ShowHostedEditor("one\ntwo\nthree");

		using (window)
		{
			Assert.AreEqual(3, editor.Document.LineCount);

			TextView textView = editor.TextArea.TextView;

			Assert.IsTrue(textView.VisualLinesValid, "visual lines were not valid after hosting");
			Assert.IsNotEmpty(textView.VisualLines);
		}
	}

	[TestMethod]
	public void DocumentOnlyBody_RunsOnTheUiThread()
	{
		Assert.IsTrue(Dispatcher.UIThread.CheckAccess(), "the document-only body must run on the UI thread");

		// Constructing an engine type is the functional proof: TextDocument cannot be created off-thread.
		_ = new TextDocument("one");
	}

	/// <summary>Verifies that an asynchronous body stays on the UI thread across an await, so it can still construct controls afterwards.</summary>
	/// <returns>A task that represents the asynchronous test.</returns>
	[TestMethod]
	public async Task AsyncBody_StaysOnTheUiThread()
	{
		Assert.IsTrue(Dispatcher.UIThread.CheckAccess(), "the asynchronous body must start on the UI thread");

		await Task.Delay(1, TestContext.CancellationToken);

		Assert.IsTrue(Dispatcher.UIThread.CheckAccess(), "the continuation must resume on the UI thread");
		_ = AvaloniaTestHost.CreateEditor("x");
	}

	/// <summary>Verifies that a host window is closed when it is disposed, so the suite's <c>using</c> dispose discipline leaks no window.</summary>
	[TestMethod]
	public void HostWindow_Disposed_IsClosed()
	{
		HostWindow window = AvaloniaTestHost.ShowInHostWindow(new TextBlock { Text = "leak probe" });

		Assert.IsTrue(window.IsVisible, "the host window must be visible while it is shown");

		window.Dispose();

		Assert.IsFalse(window.IsVisible, "a disposed host window must be closed");
	}

	public TestContext? TestContext { get; set; }
}
