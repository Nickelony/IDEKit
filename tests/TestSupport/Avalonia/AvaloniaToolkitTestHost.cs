using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Nickelony.Testing;

/// <summary>Provides the Avalonia headless hosting and element helpers that the UI tests of every family share.</summary>
/// <remarks>
/// <para>
/// Every test body is dispatched onto the Avalonia UI thread through the session started by <see cref="Session"/>;
/// a suite reaches that through the <c>AvaloniaTestClass</c> attribute. A control constructed off the UI thread throws, so a test must run its body
/// inside a dispatch. The headless platform always hosts a window, so no interactive window station is required.
/// </para>
/// <para>
/// The session builds its application from the <c>AvaloniaTestApp</c> type that the compiling project supplies: the plain application of this folder,
/// or a family's own where its controls need more. A family's test host derives from this class to add the members that name its own types, so a call
/// through the family type reaches these members too.
/// </para>
/// </remarks>
internal abstract class AvaloniaToolkitTestHost
{
	/// <summary>
	/// The design font size unattached elements inherit. Pinning it keeps layout assertions independent of the machine's text-size setting.
	/// </summary>
	public const double DesignFontSize = 12.0;

	private static readonly object s_sessionGate = new();

	private static HeadlessUnitTestSession? s_session;

	private protected AvaloniaToolkitTestHost() { }

	/// <summary>The shared headless session, starting it on first use.</summary>
	internal static HeadlessUnitTestSession Session
	{
		get
		{
			lock (s_sessionGate)
				return s_session ??= HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestApp));
		}
	}

	/// <summary>Dispatches a synchronous action onto the Avalonia UI thread and waits for it to complete.</summary>
	public static void Run(Action action) => Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();

	/// <summary>Dispatches a synchronous function onto the Avalonia UI thread and returns its result.</summary>
	public static T Run<T>(Func<T> action) => Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();

	/// <summary>
	/// Dispatches an asynchronous operation onto the Avalonia UI thread while the session pumps the dispatcher, so continuations run
	/// on the UI thread.
	/// </summary>
	public static Task<T> RunAsync<T>(Func<Task<T>> action) => Session.Dispatch(action, CancellationToken.None);

	/// <summary>Pins the element's font size so layout assertions stay independent of the machine's text-size setting.</summary>
	/// <param name="fontSize">The font size to apply; the design font size by default.</param>
	public static void PinDesignFontSize(Control element, double fontSize = DesignFontSize)
	{
		ArgumentNullException.ThrowIfNull(element);
		element.SetValue(TextBlock.FontSizeProperty, fontSize);
	}

	/// <summary>Shows the given content in a host window and pumps the dispatcher queue so initial layout runs.</summary>
	/// <returns>The host window; dispose it to close the window.</returns>
	public static HostWindow ShowInHostWindow(Control content)
	{
		ArgumentNullException.ThrowIfNull(content);

		var window = new HostWindow
		{
			Content = content,
			Width = 800,
			Height = 600,
			ShowInTaskbar = false,
		};

		window.Show();
		Dispatcher.UIThread.RunJobs();
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		Dispatcher.UIThread.RunJobs();

		return window;
	}

	/// <summary>Runs queued dispatcher work, allowing queued work at higher priorities to run first.</summary>
	/// <param name="dispatcher">
	/// The dispatcher to pump, or <see langword="null"/> to pump the UI dispatcher. An element's dispatcher is nullable on Avalonia, so a shared test
	/// can pass it directly.
	/// </param>
	public static void PumpDispatcher(Dispatcher? dispatcher, DispatcherPriority priority) =>
		(dispatcher ?? Dispatcher.UIThread).Invoke(static () => { }, priority);

	/// <summary>Enumerates the visual descendants of the given element.</summary>
	/// <returns>The visual descendants of the requested type, in tree order.</returns>
	public static IEnumerable<T> FindVisualDescendants<T>(Visual root)
		where T : Visual
	{
		ArgumentNullException.ThrowIfNull(root);
		return root.GetVisualDescendants().OfType<T>();
	}

	/// <summary>
	/// Translates a point from one element's coordinate space into another's, unwrapping the nullable result Avalonia returns so a shared test uses
	/// one non-nullable call shape for both bindings.
	/// </summary>
	public static Point TranslatePointTo(Visual source, Point point, Visual relativeTo)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(relativeTo);

		return source.TranslatePoint(point, relativeTo)!.Value;
	}
}
