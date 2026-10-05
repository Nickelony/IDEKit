using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Editing;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Raises the left press of a text area the way the engine does while the press tunnels, so a test drives a click without a real pointer.
/// </summary>
/// <remarks>
/// The event carries no keyboard state and the position of the real pointer, so a test supplies the held modifiers and the mapping from position
/// to offset to the code under test.
/// </remarks>
internal static class TestPointerPressEvents
{
	/// <summary>Raises the previewed left-button press of the text area.</summary>
	/// <param name="textArea">The text area that receives the press.</param>
	/// <returns>Whether a handler took the press.</returns>
	public static bool RaiseLeftPress(TextArea textArea)
	{
		var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
		{
			RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
			Source = textArea,
		};

		textArea.RaiseEvent(e);
		return e.Handled;
	}
}
