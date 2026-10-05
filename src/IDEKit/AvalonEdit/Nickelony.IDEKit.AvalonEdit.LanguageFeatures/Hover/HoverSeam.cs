using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;

/// <summary>Gives the hover pointer source its WPF calls: the pointer subscriptions on a text area.</summary>
/// <remarks>
/// The pointer source is shared binding source, and the pointer events are the spot where the engines disagree on names: WPF raises the previewed
/// mouse move and the mouse leave. This type is the WPF half of that seam; the bindings declare the same members, so the shared source reads one set
/// of names.
/// </remarks>
internal static class HoverSeam
{
	/// <include file="../../../../../shared/docs/HoverSeam.xml" path="doc/members/member[@name='SubscribePointer']/*"/>
	internal static IDisposable SubscribePointer(TextArea textArea, Action<Point> moved, Action left)
	{
		void movedHandler(object sender, MouseEventArgs e) => moved(e.GetPosition(textArea));
		void leftHandler(object sender, MouseEventArgs e) => left();

		var movedRoutedHandler = new MouseEventHandler(movedHandler);

		textArea.AddHandler(UIElement.PreviewMouseMoveEvent, movedRoutedHandler, handledEventsToo: true);
		textArea.MouseLeave += leftHandler;

		return new ActionSubscription(() =>
		{
			textArea.RemoveHandler(UIElement.PreviewMouseMoveEvent, movedRoutedHandler);
			textArea.MouseLeave -= leftHandler;
		});
	}
}
