using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;

/// <summary>Gives the hover pointer source its Avalonia calls: the pointer subscriptions on a text area.</summary>
/// <remarks>
/// The pointer events are the spot where the engines disagree on names: Avalonia raises the routed pointer-moved event, which the source listens
/// to on both routing stages, and the pointer-exited event. The bindings declare the same members, so the shared source reads one set of names.
/// </remarks>
internal static class HoverSeam
{
	/// <include file="../../../../../shared/docs/HoverSeam.xml" path="doc/members/member[@name='SubscribePointer']/*"/>
	internal static IDisposable SubscribePointer(TextArea textArea, Action<Point> moved, Action left)
	{
		void movedHandler(object? sender, PointerEventArgs e) => moved(e.GetPosition(textArea));
		void leftHandler(object? sender, PointerEventArgs e) => left();

		textArea.AddHandler(
			routedEvent: InputElement.PointerMovedEvent,
			handler: movedHandler,
			routes: RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
			handledEventsToo: true
		);

		textArea.PointerExited += leftHandler;

		return new ActionSubscription(() =>
		{
			textArea.RemoveHandler(InputElement.PointerMovedEvent, movedHandler);
			textArea.PointerExited -= leftHandler;
		});
	}
}
