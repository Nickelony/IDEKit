using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.QuickPick.Wpf.Tests;

/// <summary>
/// Raises the key and pointer events that drive a quick-pick window, and reads what its rows drew, the way WPF raises and builds them.
/// </summary>
internal static class TestQuickPickInput
{
	/// <summary>The tag the marker template puts on the element it draws.</summary>
	public const string MarkerTag = "custom-row";

	/// <summary>Raises the previewed press of a key on the input.</summary>
	/// <param name="input">The input that receives the key.</param>
	/// <param name="key">The key to press.</param>
	/// <returns>Whether a handler took the key.</returns>
	public static bool PressKey(TextBox input, Key key)
	{
		PresentationSource source = PresentationSource.FromVisual(input) ?? throw new InvalidOperationException("The input is not shown.");

		var e = new KeyEventArgs(keyboard: Keyboard.PrimaryDevice, inputSource: source, timestamp: 0, key)
		{
			RoutedEvent = Keyboard.PreviewKeyDownEvent,
			Source = input,
		};

		input.RaiseEvent(e);
		return e.Handled;
	}

	/// <summary>Raises a press and a release of the left button over a row of the list.</summary>
	/// <param name="list">The list that holds the row.</param>
	/// <param name="rowIndex">The index of the row.</param>
	public static void ClickRow(ListBox list, int rowIndex)
	{
		var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(rowIndex);

		foreach (RoutedEvent routedEvent in new[] { UIElement.PreviewMouseLeftButtonDownEvent, UIElement.PreviewMouseLeftButtonUpEvent })
		{
			var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent, Source = container };
			list.RaiseEvent(e);
		}
	}

	/// <summary>Gives the container of a row, which exists while the row is realized.</summary>
	public static DependencyObject ContainerOf(ListBox list, int rowIndex) => list.ItemContainerGenerator.ContainerFromIndex(rowIndex);

	/// <summary>Creates a template whose rows draw a border carrying <see cref="MarkerTag"/>.</summary>
	public static DataTemplate CreateMarkerTemplate()
	{
		var border = new FrameworkElementFactory(typeof(Border));

		border.SetValue(FrameworkElement.TagProperty, MarkerTag);

		return new DataTemplate { VisualTree = border };
	}

	/// <summary>Reports whether a row container holds a row that a marker template drew.</summary>
	public static bool IsMarkerRow(DependencyObject container) =>
		TestHost.FindVisualDescendants<Border>(container).Any(border => Equals(border.Tag, MarkerTag));

	/// <summary>Gives the texts of the runs that a row draws bold, in order.</summary>
	public static IReadOnlyList<string> BoldTextsOf(DependencyObject container)
	{
		return
		[
			.. TestHost
				.FindVisualDescendants<TextBlock>(container)
				.SelectMany(textBlock => textBlock.Inlines.OfType<Run>())
				.Where(run => run.FontWeight == FontWeights.Bold)
				.Select(run => run.Text),
		];
	}

	/// <summary>Gives the texts of the text blocks that a row draws, in order.</summary>
	public static IReadOnlyList<string> TextsOf(DependencyObject container)
	{
		return
		[
			.. TestHost
				.FindVisualDescendants<TextBlock>(container)
				.Select(textBlock =>
					textBlock.Inlines.Count > 0 ? string.Concat(textBlock.Inlines.OfType<Run>().Select(run => run.Text)) : textBlock.Text
				),
		];
	}

	/// <summary>Gives the images that a row draws.</summary>
	public static IReadOnlyList<ImageSource?> ImagesOf(DependencyObject container) =>
		[.. TestHost.FindVisualDescendants<Image>(container).Select(image => image.Source)];

	/// <summary>Creates an image to return from the icon hook.</summary>
	public static ImageSource CreateImage() => new DrawingImage();

	/// <summary>Gives the name that the automation peer of an element reports to a screen reader.</summary>
	public static string AutomationNameOf(DependencyObject element) => UIElementAutomationPeer.CreatePeerForElement((UIElement)element).GetName();

	/// <summary>Reports whether an element holds the keyboard focus.</summary>
	public static bool IsFocused(UIElement element) => element.IsKeyboardFocused;

	/// <summary>Reports whether an element takes part in the layout, which the presenter decides by showing or hiding it.</summary>
	public static bool IsShown(UIElement element) => element.Visibility == Visibility.Visible;

	/// <summary>Measures where the frame of the open window sits against the host, as the screen shows it.</summary>
	/// <param name="frame">The frame that draws the window.</param>
	/// <param name="host">The element the window is placed against.</param>
	/// <returns>The gap above the frame, and the gaps left and right of it, in device-independent units.</returns>
	public static (double Top, double Left, double Right) MeasureGaps(FrameworkElement frame, FrameworkElement host)
	{
		double scale = PresentationSource.FromVisual(host)!.CompositionTarget!.TransformToDevice.M11;

		Point hostTopLeft = host.PointToScreen(new Point(0.0, 0.0));
		Point hostTopRight = host.PointToScreen(new Point(host.ActualWidth, 0.0));
		Point frameTopLeft = frame.PointToScreen(new Point(0.0, 0.0));
		Point frameTopRight = frame.PointToScreen(new Point(frame.ActualWidth, 0.0));

		return ((frameTopLeft.Y - hostTopLeft.Y) / scale, (frameTopLeft.X - hostTopLeft.X) / scale, (hostTopRight.X - frameTopRight.X) / scale);
	}
}
