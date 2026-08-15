using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Provides Avalonia headless hosting and element helpers for UI tests, mirroring the WPF
/// <c>WPFTestHost</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every test body is dispatched onto the Avalonia UI thread through the session started by
/// <see cref="Session"/>; the mirror suites reach that through the <c>AvaloniaTestClass</c> attribute. A
/// control constructed off the UI thread throws, so a test must run its body inside a dispatch.
/// </para>
/// <para>
/// The headless platform always hosts a window (no interactive window station is required), so unlike the
/// WPF host there is no environment failure to convert into an inconclusive result. Pixel rendering, in
/// contrast, needs a Skia-backed renderer; the mirror suite leaves the headless drawing backend in place,
/// so the bitmap helpers in <c>TestBitmapRendering</c> report that tier inconclusive instead of asserting on
/// pixels.
/// </para>
/// </remarks>
internal static class AvaloniaTestHost
{
	/// <summary>
	/// The design font size unattached elements inherit. Pinning it keeps layout assertions
	/// independent of the machine's text-size setting.
	/// </summary>
	public const double DesignFontSize = 12.0;

	private static readonly object SessionGate = new();

	private static HeadlessUnitTestSession? _session;

	/// <summary>
	/// Gets the shared headless session, starting it on first use.
	/// </summary>
	internal static HeadlessUnitTestSession Session
	{
		get
		{
			lock (SessionGate)
				return _session ??= HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestApp));
		}
	}

	/// <summary>
	/// Dispatches a synchronous action onto the Avalonia UI thread and waits for it to complete.
	/// </summary>
	/// <param name="action">The action to run on the UI thread.</param>
	public static void Run(Action action)
		=> Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();

	/// <summary>
	/// Dispatches a synchronous function onto the Avalonia UI thread and returns its result.
	/// </summary>
	/// <typeparam name="T">The result type.</typeparam>
	/// <param name="action">The function to run on the UI thread.</param>
	/// <returns>The value the function returned.</returns>
	public static T Run<T>(Func<T> action)
		=> Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();

	/// <summary>
	/// Dispatches an asynchronous operation onto the Avalonia UI thread while the session pumps the
	/// dispatcher, so continuations run on the UI thread.
	/// </summary>
	/// <typeparam name="T">The operation's result type.</typeparam>
	/// <param name="action">The operation to run on the UI thread.</param>
	/// <returns>A task that completes with the operation's result.</returns>
	public static Task<T> RunAsync<T>(Func<Task<T>> action)
		=> Session.Dispatch(action, CancellationToken.None);

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
	/// Creates a text editor with the given text and shows it in a host window.
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
	public static void PinDesignFontSize(Control element, double fontSize = DesignFontSize)
	{
		ArgumentNullException.ThrowIfNull(element);
		element.SetValue(TextBlock.FontSizeProperty, fontSize);
	}

	/// <summary>
	/// Shows the given content in a host window and pumps the dispatcher queue so initial layout runs.
	/// </summary>
	/// <param name="content">The content to host.</param>
	/// <returns>The host window; dispose it to close the window.</returns>
	public static HostWindow ShowInHostWindow(Control content)
	{
		ArgumentNullException.ThrowIfNull(content);

		var window = new HostWindow
		{
			Content = content,
			Width = 800,
			Height = 600,
			ShowInTaskbar = false
		};

		window.Show();
		Dispatcher.UIThread.RunJobs();
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		Dispatcher.UIThread.RunJobs();

		return window;
	}

	/// <summary>
	/// Adds the margin to the editor's left margins and shows the editor in a host window.
	/// </summary>
	/// <param name="editor">The editor to host.</param>
	/// <param name="margin">The margin to add to the editor's left margins.</param>
	/// <returns>The host window; dispose it to close the window.</returns>
	public static HostWindow ShowEditorWithMargin(TextEditor editor, Control margin)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(margin);

		editor.TextArea.LeftMargins.Add(margin);
		return ShowInHostWindow(editor);
	}

	/// <summary>
	/// Runs queued dispatcher work, allowing queued work at higher priorities to run first.
	/// </summary>
	/// <param name="dispatcher">The dispatcher to pump.</param>
	/// <param name="priority">The priority of the queued no-op that gates the pump.</param>
	public static void PumpDispatcher(Dispatcher dispatcher, DispatcherPriority priority)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		dispatcher.Invoke(static () => { }, priority);
	}

	/// <summary>
	/// Enumerates the visual descendants of the given element.
	/// </summary>
	/// <typeparam name="T">The descendant type to yield.</typeparam>
	/// <param name="root">The element to search under.</param>
	/// <returns>The visual descendants of the requested type, in tree order.</returns>
	public static IEnumerable<T> FindVisualDescendants<T>(Visual root) where T : Visual
	{
		ArgumentNullException.ThrowIfNull(root);
		return root.GetVisualDescendants().OfType<T>();
	}
}

/// <summary>
/// A test host window that closes itself when disposed.
/// </summary>
internal sealed class HostWindow : Window, IDisposable
{
	/// <inheritdoc/>
	public void Dispose()
	{
		if (IsVisible)
			Close();
	}
}
