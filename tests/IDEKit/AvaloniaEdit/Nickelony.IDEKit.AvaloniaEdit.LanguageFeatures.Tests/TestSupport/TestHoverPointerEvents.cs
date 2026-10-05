using Avalonia.Input;
using AvaloniaEdit.Editing;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Raises the pointer events of a text area the way the engine does, so a test drives a hover pointer source without a real pointer.
/// </summary>
/// <remarks>
/// The events carry no presentation source, so the position they report is the engine's default one; a test that needs a specific offset replaces
/// the mapping from position to offset instead.
/// </remarks>
internal static class TestHoverPointerEvents
{
	/// <summary>Raises the pointer-move event of the text area, the way the engine does when the pointer moves over it.</summary>
	/// <param name="textArea">The text area that receives the event.</param>
	/// <param name="handled">Whether the event arrives already handled, the way it does when a child of the text area consumed it.</param>
	public static void RaiseMoved(TextArea textArea, bool handled = false)
	{
		textArea.RaiseEvent(
			new PointerEventArgs(
				routedEvent: InputElement.PointerMovedEvent,
				source: textArea,
				pointer: null!,
				rootVisual: null,
				rootVisualPosition: default,
				timestamp: 0,
				properties: default,
				modifiers: KeyModifiers.None
			)
			{
				Handled = handled,
			}
		);
	}

	/// <summary>Raises the pointer-leave event of the text area, the way the engine does when the pointer exits it.</summary>
	/// <param name="textArea">The text area that receives the event.</param>
	public static void RaiseLeft(TextArea textArea)
	{
		textArea.RaiseEvent(
			new PointerEventArgs(
				routedEvent: InputElement.PointerExitedEvent,
				source: textArea,
				pointer: null!,
				rootVisual: null,
				rootVisualPosition: default,
				timestamp: 0,
				properties: default,
				modifiers: KeyModifiers.None
			)
		);
	}
}
