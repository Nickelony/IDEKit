using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>Provides the Avalonia half of the text-input helpers that the shared completion, typed-text and signature help tests call.</summary>
/// <remarks>
/// AvaloniaEdit exposes a single text pipeline whose <c>TextEntering</c> stage runs immediately before the character is inserted.
/// The controller accepts a commit item on the tunnel pass of the routed text input, which runs before that stage, so a commit and the typed
/// character complete in one input step and every <c>TextEntering</c> subscriber sees the committed item.
/// </remarks>
internal static class CompletionInputTestHelpers
{
	/// <summary>Types the text the way the keyboard does: the routed text input, which tunnels before the text area applies it.</summary>
	/// <param name="editor">The editor that receives the input.</param>
	/// <param name="text">The typed text.</param>
	internal static void TypeThroughInputRoute(TextEditor editor, string text)
	{
		// The routed TextInput event runs the single text pipeline, which raises TextEntering before it
		// inserts the character.
		var args = new TextInputEventArgs
		{
			Source = editor.TextArea,
			RoutedEvent = InputElement.TextInputEvent,
			Text = text,
		};

		editor.TextArea.RaiseEvent(args);
	}

	/// <summary>Subscribes an input service that consumes the typed text on the stage the controller listens to.</summary>
	/// <param name="editor">The editor that receives the subscription.</param>
	/// <remarks>
	/// The handler sits on the editor, the text area's parent, because Avalonia runs the handlers of one element in reverse order of registration
	/// and the tunnel pass reaches an ancestor before the text area.
	/// </remarks>
	internal static void ConsumeInputBeforeController(TextEditor editor)
	{
		editor.AddHandler(
			InputElement.TextInputEvent,
			new EventHandler<TextInputEventArgs>(static (_, args) => args.Handled = true),
			RoutingStrategies.Tunnel
		);
	}

	/// <summary>Presses a key on the text area the way the input system does: the routed key-down event, which tunnels before it bubbles.</summary>
	/// <param name="editor">The editor that receives the key.</param>
	/// <param name="key">The key that is pressed.</param>
	/// <param name="shift">Whether Shift is held while the key is pressed.</param>
	internal static void PressKey(TextEditor editor, Key key, bool shift = false)
	{
		editor.TextArea.RaiseEvent(
			new KeyEventArgs
			{
				RoutedEvent = InputElement.KeyDownEvent,
				Key = key,
				KeyModifiers = shift ? KeyModifiers.Shift : KeyModifiers.None,
			}
		);
	}

	/// <summary>Marks a key handled on the host window's tunneling stage, which runs before the text area's own.</summary>
	/// <param name="hostWindow">The window that hosts the editor.</param>
	/// <param name="key">The key that the window consumes.</param>
	internal static void ConsumeKeyAboveEditor(HostWindow hostWindow, Key key)
	{
		hostWindow.AddHandler(
			InputElement.KeyDownEvent,

			(_, args) =>
			{
				if (args.Key == key)
					args.Handled = true;
			},

			RoutingStrategies.Tunnel
		);
	}
}
