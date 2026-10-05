using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>Provides the WPF half of the text-input helpers that the shared completion, typed-text and signature help tests call.</summary>
/// <remarks>
/// The editor raises a preview stage before its text area handles typed text, and the text area then raises <c>TextEntering</c> on the way
/// to inserting the text. The controller accepts a commit item on the preview stage, so it precedes every <c>TextEntering</c> subscriber.
/// </remarks>
internal static class CompletionInputTestHelpers
{
	/// <summary>Types the text the way the keyboard does: the preview stage first, then the text input that the text area applies.</summary>
	/// <param name="editor">The editor that receives the input.</param>
	/// <param name="text">The typed text.</param>
	/// <remarks>
	/// WPF raises both stages with one argument object, so a preview handler that marks it handled keeps the text area from applying it.
	/// </remarks>
	internal static void TypeThroughInputRoute(TextEditor editor, string text)
	{
		var composition = new TextComposition(InputManager.Current, editor.TextArea, text);
		var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = UIElement.PreviewTextInputEvent };

		editor.TextArea.RaiseEvent(args);

		args.RoutedEvent = UIElement.TextInputEvent;
		editor.TextArea.RaiseEvent(args);
	}

	/// <summary>Subscribes an input service that consumes the typed text on the stage the controller listens to.</summary>
	/// <param name="editor">The editor whose text area receives the subscription.</param>
	internal static void ConsumeInputBeforeController(TextEditor editor) =>
		editor.TextArea.PreviewTextInput += static (_, args) => args.Handled = true;

	/// <summary>Presses a key on the text area the way the input system does: the preview stage first, then the key-down stage.</summary>
	/// <param name="editor">The editor, shown in a window, that receives the key.</param>
	/// <param name="key">The key that is pressed.</param>
	/// <param name="shift">Whether Shift is held while the key is pressed.</param>
	/// <remarks>
	/// Both stages share one argument object, so a handler that marks the preview stage handled keeps the later stage from running its handlers.
	/// The keyboard device reports only the requested modifier, so the test does not depend on the keys the machine holds down.
	/// </remarks>
	internal static void PressKey(TextEditor editor, Key key, bool shift = false)
	{
		PresentationSource source =
			PresentationSource.FromVisual(editor.TextArea) ?? throw new InvalidOperationException("The text area is not shown.");

		KeyboardDevice keyboard = FakeKeyboardDevice.ForModifiers(shift ? ModifierKeys.Shift : ModifierKeys.None);
		var args = new KeyEventArgs(keyboard, inputSource: source, timestamp: 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };

		editor.TextArea.RaiseEvent(args);

		args.RoutedEvent = Keyboard.KeyDownEvent;
		editor.TextArea.RaiseEvent(args);
	}

	/// <summary>Marks a key handled on the host window's preview stage, which runs before the text area's own.</summary>
	/// <param name="hostWindow">The window that hosts the editor.</param>
	/// <param name="key">The key that the window consumes.</param>
	internal static void ConsumeKeyAboveEditor(HostWindow hostWindow, Key key)
	{
		hostWindow.PreviewKeyDown += (_, args) =>
		{
			if (args.Key == key)
				args.Handled = true;
		};
	}
}
