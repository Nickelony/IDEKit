using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Provides WPF/AvalonEdit hosting and element helpers for UI tests.
/// </summary>
/// <remarks>
/// Every visual test needs a window station: a test that shows a host window (through
/// <see cref="ShowInHostWindow"/>) requires an interactive window station, which a desktop session
/// provides but a headless agent does not. On a session without one the show fails and the helper reports
/// the test inconclusive, so the hosted tests drop their coverage there by design; the tier and its policy
/// are documented in <c>tests/TestSupport/README.md</c>.
/// </remarks>
internal static class WPFTestHost
{
	/// <summary>
	/// The design font size unattached elements inherit. Pinning it keeps layout assertions
	/// independent of the machine's text-size setting.
	/// </summary>
	public const double DesignFontSize = 12.0;

	/// <summary>
	/// Creates a text editor with the given document text on the current thread.
	/// </summary>
	/// <param name="text">The document text.</param>
	/// <returns>The created editor; owned by the caller.</returns>
	public static TextEditor CreateEditor(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		return new TextEditor { Document = new TextDocument(text) };
	}

	/// <summary>
	/// Creates a text editor with the given text and shows it in a non-activating host window.
	/// </summary>
	/// <param name="text">The initial editor text.</param>
	/// <param name="configureEditor">
	/// An optional callback that configures the editor before it is shown, for example to pin the design
	/// font size before layout runs.
	/// </param>
	/// <returns>The editor and its host window; dispose the host window to close it.</returns>
	public static (TextEditor Editor, HostWindow HostWindow) ShowHostedEditor(
		string text = "",
		Action<TextEditor>? configureEditor = null)
	{
		TextEditor editor = CreateEditor(text);
		configureEditor?.Invoke(editor);
		return (editor, ShowInHostWindow(editor));
	}

	/// <summary>
	/// Pins the element's font size so layout assertions stay independent of the machine's
	/// text-size setting.
	/// </summary>
	/// <param name="element">The element to pin.</param>
	/// <param name="fontSize">The font size to apply; the design font size by default.</param>
	public static void PinDesignFontSize(DependencyObject element, double fontSize = DesignFontSize)
	{
		ArgumentNullException.ThrowIfNull(element);
		TextBlock.SetFontSize(element, fontSize);
	}

	/// <summary>
	/// Shows the given content in a non-activating host window and pumps initial background-priority
	/// dispatcher work.
	/// </summary>
	/// <remarks>
	/// A session without an interactive window station (a headless agent or a service account) fails while
	/// the window is shown or the first dispatcher work runs. That failure belongs to the environment, not
	/// to the test, so it is reported as an inconclusive test result carrying the underlying error instead
	/// of failing the test with WPF's low-level message. The interactive-window-station tier and its policy
	/// are documented in <c>tests/TestSupport/README.md</c>.
	/// </remarks>
	/// <param name="content">The content to host.</param>
	/// <returns>The host window; dispose it to close the window.</returns>
	public static HostWindow ShowInHostWindow(FrameworkElement content)
	{
		ArgumentNullException.ThrowIfNull(content);

		var window = new HostWindow
		{
			Content = content,
			Width = 800,
			Height = 600,
			ShowActivated = false,
			ShowInTaskbar = false,
			WindowStyle = WindowStyle.None
		};

		try
		{
			window.Show();
			PumpDispatcher(window.Dispatcher, DispatcherPriority.Background);
		}
		catch (Win32Exception ex)
		{
			// Only the interactive-window-station failure is converted to an inconclusive result; every
			// other exception propagates so a product regression cannot masquerade as a headless skip.
			window.Dispose();
			Assert.Inconclusive($"The hosted WPF test requires an interactive window station: {ex.Message}");
		}

		return window;
	}

	/// <summary>
	/// Adds the margin to the editor's left margins and shows the editor in a host window.
	/// </summary>
	/// <param name="editor">The editor to host.</param>
	/// <param name="margin">The margin to add to the editor's left margins.</param>
	/// <returns>The host window; dispose it to close the window.</returns>
	public static HostWindow ShowEditorWithMargin(TextEditor editor, UIElement margin)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(margin);

		editor.TextArea.LeftMargins.Add(margin);
		return ShowInHostWindow(editor);
	}

	/// <summary>
	/// Synchronously schedules a no-op at the given priority, allowing queued dispatcher work at
	/// higher priorities to run first.
	/// </summary>
	/// <param name="dispatcher">The dispatcher to pump.</param>
	/// <param name="priority">The priority at which to schedule the synchronization callback.</param>
	public static void PumpDispatcher(Dispatcher dispatcher, DispatcherPriority priority)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		dispatcher.Invoke(priority, new Action(() => { }));
	}

	/// <summary>
	/// Enumerates the visual descendants of the given element.
	/// </summary>
	/// <typeparam name="T">The descendant type to yield.</typeparam>
	/// <param name="root">The element to search under.</param>
	/// <returns>The visual descendants of the requested type, in tree order.</returns>
	public static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
	{
		ArgumentNullException.ThrowIfNull(root);

		int count = VisualTreeHelper.GetChildrenCount(root);

		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);

			if (child is T match)
				yield return match;

			foreach (T descendant in FindVisualDescendants<T>(child))
				yield return descendant;
		}
	}
}

/// <summary>
/// A non-activating test host window that closes itself when disposed.
/// </summary>
internal sealed class HostWindow : Window, IDisposable
{
	/// <inheritdoc/>
	public void Dispose()
	{
		if (IsVisible || IsLoaded)
			Close();
	}
}
