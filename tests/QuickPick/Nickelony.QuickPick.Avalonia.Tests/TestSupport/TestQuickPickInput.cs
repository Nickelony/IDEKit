using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Nickelony.QuickPick.Avalonia.Tests;

/// <summary>
/// Raises the key and pointer events that drive a quick-pick window, and reads what its rows drew, the way Avalonia raises and builds them.
/// </summary>
internal static class TestQuickPickInput
{
	/// <summary>The tag the marker template puts on the element it draws.</summary>
	public const string MarkerTag = "custom-row";

	/// <summary>Raises the tunneled press of a key on the input.</summary>
	/// <param name="input">The input that receives the key.</param>
	/// <param name="key">The key to press.</param>
	/// <returns>Whether a handler took the key.</returns>
	public static bool PressKey(TextBox input, Key key)
	{
		var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };

		input.RaiseEvent(e);
		return e.Handled;
	}

	/// <summary>Raises a press and a release of the left button over a row of the list.</summary>
	/// <param name="list">The list that holds the row.</param>
	/// <param name="rowIndex">The index of the row.</param>
	public static void ClickRow(ListBox list, int rowIndex)
	{
		Control container = list.ContainerFromIndex(rowIndex) ?? throw new InvalidOperationException("The row is not realized.");
		var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
		TopLevel root = TopLevel.GetTopLevel(list) ?? throw new InvalidOperationException("The list is not shown.");

		var pressed = new PointerPressedEventArgs(
			source: container,
			pointer,
			rootVisual: root,
			rootVisualPosition: default,
			timestamp: 0UL,
			properties: new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
			modifiers: KeyModifiers.None
		);

		var released = new PointerReleasedEventArgs(
			source: container,
			pointer,
			rootVisual: root,
			rootVisualPosition: default,
			timestamp: 0UL,
			properties: new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
			modifiers: KeyModifiers.None,
			initialPressMouseButton: MouseButton.Left
		);

		container.RaiseEvent(pressed);
		container.RaiseEvent(released);
	}

	/// <summary>Gives the container of a row, which exists while the row is realized.</summary>
	public static Control ContainerOf(ListBox list, int rowIndex) =>
		list.ContainerFromIndex(rowIndex) ?? throw new InvalidOperationException("The row is not realized.");

	/// <summary>Creates a template whose rows draw a border carrying <see cref="MarkerTag"/>.</summary>
	public static IDataTemplate CreateMarkerTemplate() => new FuncDataTemplate<QuickPickRow>((_, _) => new Border { Tag = MarkerTag });

	/// <summary>Reports whether a row container holds a row that a marker template drew.</summary>
	public static bool IsMarkerRow(Control container) =>
		TestHost.FindVisualDescendants<Border>(container).Any(border => Equals(border.Tag, MarkerTag));

	/// <summary>Gives the texts of the runs that a row draws bold, in order.</summary>
	public static IReadOnlyList<string> BoldTextsOf(Control container)
	{
		return
		[
			.. TestHost
				.FindVisualDescendants<TextBlock>(container)
				.SelectMany(textBlock => textBlock.Inlines?.OfType<Run>() ?? [])
				.Where(run => run.FontWeight == FontWeight.Bold)
				.Select(run => run.Text ?? string.Empty),
		];
	}

	/// <summary>Gives the texts of the text blocks that a row draws, in order.</summary>
	public static IReadOnlyList<string> TextsOf(Control container)
	{
		return
		[
			.. TestHost
				.FindVisualDescendants<TextBlock>(container)
				.Select(textBlock =>
					textBlock.Inlines is { Count: > 0 } inlines
						? string.Concat(inlines.OfType<Run>().Select(run => run.Text))
						: textBlock.Text ?? string.Empty
				),
		];
	}

	/// <summary>Gives the images that a row draws.</summary>
	public static IReadOnlyList<IImage?> ImagesOf(Control container) =>
		[.. TestHost.FindVisualDescendants<Image>(container).Select(image => image.Source)];

	/// <summary>Creates an image to return from the icon hook.</summary>
	public static IImage CreateImage() => new DrawingImage();

	/// <summary>Gives the name that the automation peer of an element reports to a screen reader.</summary>
	public static string AutomationNameOf(Control element) => ControlAutomationPeer.CreatePeerForElement(element).GetName();

	/// <summary>Reports whether an element holds the keyboard focus.</summary>
	public static bool IsFocused(InputElement element) => element.IsFocused;

	/// <summary>Reports whether an element takes part in the layout, which the presenter decides by showing or hiding it.</summary>
	public static bool IsShown(Control element) => element.IsVisible;

	/// <summary>Measures where the frame of the open window sits against the host, as the screen shows it.</summary>
	/// <param name="frame">The frame that draws the window.</param>
	/// <param name="host">The element the window is placed against.</param>
	/// <returns>The gap above the frame, and the gaps left and right of it, in device-independent units.</returns>
	public static (double Top, double Left, double Right) MeasureGaps(Control frame, Control host)
	{
		double scale = TopLevel.GetTopLevel(host)!.RenderScaling;

		PixelPoint hostTopLeft = host.PointToScreen(new Point(0.0, 0.0));
		PixelPoint hostTopRight = host.PointToScreen(new Point(host.Bounds.Width, 0.0));
		PixelPoint frameTopLeft = frame.PointToScreen(new Point(0.0, 0.0));
		PixelPoint frameTopRight = frame.PointToScreen(new Point(frame.Bounds.Width, 0.0));

		return ((frameTopLeft.Y - hostTopLeft.Y) / scale, (frameTopLeft.X - hostTopLeft.X) / scale, (hostTopRight.X - frameTopRight.X) / scale);
	}
}
