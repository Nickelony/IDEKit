using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nickelony.Testing;

/// <summary>Provides the WPF hosting and element helpers that the UI tests of every family share.</summary>
/// <remarks>
/// <para>
/// A test that shows a host window (through <see cref="ShowInHostWindow"/>) requires an interactive window station; without one the show fails
/// and the helper reports the test inconclusive.
/// </para>
/// <para>
/// A family's test host derives from this class to add the members that name its own types, so a call through the family type reaches
/// these members too.
/// </para>
/// </remarks>
internal abstract class WpfToolkitTestHost
{
	/// <summary>
	/// The design font size unattached elements inherit. Pinning it keeps layout assertions independent of the machine's text-size setting.
	/// </summary>
	public const double DesignFontSize = 12.0;

	private protected WpfToolkitTestHost() { }

	/// <summary>Pins the element's font size so layout assertions stay independent of the machine's text-size setting.</summary>
	/// <param name="fontSize">The font size to apply; the design font size by default.</param>
	public static void PinDesignFontSize(DependencyObject element, double fontSize = DesignFontSize)
	{
		ArgumentNullException.ThrowIfNull(element);
		TextBlock.SetFontSize(element, fontSize);
	}

	/// <summary>Shows the given content in a non-activating host window and pumps initial background-priority dispatcher work.</summary>
	/// <returns>The host window; dispose it to close the window.</returns>
	/// <remarks>
	/// A session without an interactive window station (a headless agent or a service account) fails while the window is shown or the first
	/// dispatcher work runs. That failure belongs to the environment, not to the test, so it is reported as an inconclusive test result carrying
	/// the underlying error instead of failing the test with WPF's low-level message.
	/// </remarks>
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
			WindowStyle = WindowStyle.None,
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

	/// <summary>Synchronously schedules a no-op at the given priority, allowing queued dispatcher work at higher priorities to run first.</summary>
	public static void PumpDispatcher(Dispatcher dispatcher, DispatcherPriority priority)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		dispatcher.Invoke(priority, new Action(() => { }));
	}

	/// <summary>Enumerates the visual descendants of the given element.</summary>
	/// <returns>The visual descendants of the requested type, in tree order.</returns>
	public static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
		where T : DependencyObject
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

	/// <summary>
	/// Translates a point from one element's coordinate space into another's. Avalonia returns a nullable point, so this helper gives a shared test
	/// one non-nullable call shape for both bindings.
	/// </summary>
	public static Point TranslatePointTo(UIElement source, Point point, UIElement relativeTo)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(relativeTo);

		return source.TranslatePoint(point, relativeTo);
	}
}
