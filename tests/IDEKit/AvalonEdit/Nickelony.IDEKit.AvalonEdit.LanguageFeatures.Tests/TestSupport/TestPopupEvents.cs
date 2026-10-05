using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Editing;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>Raises the key events that close a feature popup, the way WPF raises them.</summary>
internal static class TestPopupEvents
{
	/// <summary>Raises a press of Esc on the text area, on the stage that runs before the text area handles keys.</summary>
	/// <param name="textArea">The text area that receives the key.</param>
	public static void PressEscape(TextArea textArea) => RaiseEscape(textArea);

	/// <summary>Watches the stage that runs before the text area handles keys, for a test that asks whether a popup took a press of Esc.</summary>
	/// <param name="textArea">The text area that receives the key.</param>
	/// <returns>A press of Esc that reports whether a handler marked the key handled on that stage, before the editor's own key handling.</returns>
	/// <remarks>
	/// The text area runs the handlers of that stage in the order of their subscription, so each press subscribes the watch last and removes
	/// it after the press.
	/// </remarks>
	public static Func<bool> WatchEscape(TextArea textArea)
	{
		return () =>
		{
			bool handledBeforeTheEditor = false;
			var probe = new KeyEventHandler((_, e) => handledBeforeTheEditor = e.Handled);

			textArea.AddHandler(Keyboard.PreviewKeyDownEvent, probe, handledEventsToo: true);

			try
			{
				RaiseEscape(textArea);
			}
			finally
			{
				textArea.RemoveHandler(Keyboard.PreviewKeyDownEvent, probe);
			}

			return handledBeforeTheEditor;
		};
	}

	private static void RaiseEscape(TextArea textArea)
	{
		PresentationSource source = PresentationSource.FromVisual(textArea) ?? throw new InvalidOperationException("The text area is not shown.");

		textArea.RaiseEvent(
			new KeyEventArgs(keyboard: Keyboard.PrimaryDevice, inputSource: source, timestamp: 0, key: Key.Escape)
			{
				RoutedEvent = Keyboard.PreviewKeyDownEvent,
			}
		);
	}
}
