using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Editing;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>Raises the key events that close a feature popup, the way the platform raises them to an Avalonia window.</summary>
internal static class TestPopupEvents
{
	/// <summary>Raises a press of Esc on the text area, on the stage that runs before the text area handles keys.</summary>
	/// <param name="textArea">The text area that receives the key.</param>
	public static void PressEscape(TextArea textArea) =>
		textArea.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

	/// <summary>Watches the stage that runs before the text area handles keys, for a test that asks whether a popup took a press of Esc.</summary>
	/// <param name="textArea">The text area that receives the key; the watch must be made before a popup subscribes to it.</param>
	/// <returns>
	/// A press of Esc that reports whether a handler marked the key handled on that stage, before the editor's own key handling, which handles Esc
	/// by itself.
	/// </returns>
	/// <remarks>
	/// The text area runs the handlers of that stage in the reverse order of their subscription, so the watch subscribes now and runs
	/// after every handler a later show subscribes.
	/// </remarks>
	public static Func<bool> WatchEscape(TextArea textArea)
	{
		bool handledBeforeTheEditor = false;

		textArea.AddHandler(
			routedEvent: InputElement.KeyDownEvent,
			handler: (_, e) => handledBeforeTheEditor = e.Handled,
			routes: RoutingStrategies.Tunnel,
			handledEventsToo: true
		);

		return () =>
		{
			handledBeforeTheEditor = false;
			PressEscape(textArea);
			return handledBeforeTheEditor;
		};
	}
}
