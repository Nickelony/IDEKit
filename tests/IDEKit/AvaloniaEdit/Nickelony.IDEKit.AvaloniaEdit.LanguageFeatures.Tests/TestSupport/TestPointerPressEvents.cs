using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Editing;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>Raises the left press of a text area the way the engine does, so a test drives a click without a real pointer.</summary>
/// <remarks>
/// The press starts at the text view, as a real press over the text does, so it tunnels through the text area and then bubbles to the engine's
/// own press handler; the handled state is read where the tunnel reaches the text view. The event reports the text area's origin as its position,
/// so a test supplies the mapping from position to offset to the code under test.
/// </remarks>
internal static class TestPointerPressEvents
{
	/// <summary>Raises a left-button press of the text area that holds the supplied modifiers.</summary>
	/// <param name="textArea">The text area that receives the press.</param>
	/// <param name="modifiers">The modifier keys the press holds.</param>
	/// <returns>Whether a tunnel handler above the text view took the press.</returns>
	public static bool RaiseLeftPress(TextArea textArea, KeyModifiers modifiers)
	{
		var e = new PointerPressedEventArgs(
			source: textArea.TextView,
			pointer: new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
			rootVisual: textArea,
			rootVisualPosition: default,
			timestamp: 0UL,
			properties: new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
			modifiers
		);

		bool handledWhileTunneling = false;

		void recordHandled(object? sender, PointerPressedEventArgs args) => handledWhileTunneling = args.Handled;

		textArea.TextView.AddHandler(
			routedEvent: InputElement.PointerPressedEvent,
			handler: recordHandled,
			routes: RoutingStrategies.Tunnel,
			handledEventsToo: true
		);

		try
		{
			textArea.TextView.RaiseEvent(e);
		}
		finally
		{
			textArea.TextView.RemoveHandler(InputElement.PointerPressedEvent, recordHandled);
		}

		return handledWhileTunneling;
	}
}
