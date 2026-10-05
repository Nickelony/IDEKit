using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;

/// <summary>Gives the pointer navigation session its Avalonia calls: the left-press subscription on a text area.</summary>
/// <remarks>
/// The handler listens to the pointer press on the text area while it tunnels, before the bubbling press that the engine's selection handler takes.
/// An Avalonia pointer event carries the modifiers held at the press, so the handler reads them from the event.
/// </remarks>
internal static class PointerNavigationSeam
{
	/// <include file="../../../../../shared/docs/PointerNavigationSeam.xml" path="doc/members/member[@name='SubscribePress']/*"/>
	internal static IDisposable SubscribePress(TextArea textArea, Func<Point, KeyModifiers, bool> pressed, Func<KeyModifiers>? heldModifiers)
	{
		void pressedHandler(object? sender, PointerPressedEventArgs e)
		{
			// The update kind names the button this press changed, so a press of another button while the left one is held is not taken.
			if (
				e.ClickCount <= 1
				&& e.GetCurrentPoint(textArea).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
				&& pressed(e.GetPosition(textArea), heldModifiers?.Invoke() ?? e.KeyModifiers)
			)
			{
				e.Handled = true;
			}
		}

		textArea.AddHandler(InputElement.PointerPressedEvent, pressedHandler, RoutingStrategies.Tunnel);

		return new ActionSubscription(() => textArea.RemoveHandler(InputElement.PointerPressedEvent, pressedHandler));
	}
}
