using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;

/// <summary>Gives the pointer navigation session its WPF calls: the left-press subscription on a text area.</summary>
/// <remarks>
/// WPF raises the previewed left-button press on the text area while the press tunnels, before the bubbling press that the engine's
/// selection handler takes. A WPF mouse event carries no keyboard state, so the held modifiers are the ambient <see cref="Keyboard.Modifiers"/>.
/// </remarks>
internal static class PointerNavigationSeam
{
	/// <include file="../../../../../shared/docs/PointerNavigationSeam.xml" path="doc/members/member[@name='SubscribePress']/*"/>
	internal static IDisposable SubscribePress(TextArea textArea, Func<Point, ModifierKeys, bool> pressed, Func<ModifierKeys>? heldModifiers)
	{
		Func<ModifierKeys> readModifiers = heldModifiers ?? (static () => Keyboard.Modifiers);

		void pressedHandler(object sender, MouseButtonEventArgs e)
		{
			if (e.ClickCount <= 1 && pressed(e.GetPosition(textArea), readModifiers()))
				e.Handled = true;
		}

		var handler = new MouseButtonEventHandler(pressedHandler);

		textArea.PreviewMouseLeftButtonDown += handler;

		return new ActionSubscription(() => textArea.PreviewMouseLeftButtonDown -= handler);
	}
}
