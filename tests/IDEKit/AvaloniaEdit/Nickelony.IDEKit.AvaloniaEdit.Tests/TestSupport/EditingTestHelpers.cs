using Avalonia.Input;
using AvaloniaEdit;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

internal static partial class EditingTestHelpers
{
	/// <summary>Builds the text-input event arguments the editor text area raises for typed text, carrying the given text for the editor.</summary>
	/// <param name="editor">The editor the input targets.</param>
	/// <param name="text">The entered text.</param>
	/// <returns>The text-input event arguments.</returns>
	/// <remarks>
	/// The editor raises <see cref="InputElement.TextInputEvent"/> for typed text. The Avalonia event carries no keyboard device, so a test
	/// that needs a key event builds the <see cref="KeyEventArgs"/> its code path reads.
	/// </remarks>
	public static TextInputEventArgs CreateTextArgs(TextEditor editor, string text)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(text);

		return new TextInputEventArgs
		{
			Source = editor,
			RoutedEvent = InputElement.TextInputEvent,
			Text = text,
		};
	}

	/// <summary>Builds the key-down event of a Backspace press with the given modifiers.</summary>
	/// <param name="hostWindow">The host window that supplies the presentation source.</param>
	/// <param name="modifiers">The modifiers pressed with the key.</param>
	/// <returns>The key-down event arguments.</returns>
	/// <remarks>
	/// The engines build a key event differently: one needs a presentation source and a keyboard device, the other exposes the key and its modifiers
	/// as init-only properties and carries no presentation source. The helper exposes one call shape so a shared test names the modifiers
	/// without an <c>#if</c>.
	/// </remarks>
	public static KeyEventArgs CreateBackspaceArgs(HostWindow hostWindow, KeyModifiers modifiers = KeyModifiers.None)
	{
		return new()
		{
			RoutedEvent = InputElement.KeyDownEvent,
			Key = Key.Back,
			KeyModifiers = modifiers,
		};
	}

	/// <summary>Builds the key-down event of an arbitrary key press.</summary>
	/// <param name="hostWindow">The host window that supplies the presentation source.</param>
	/// <param name="key">The key that is pressed.</param>
	/// <returns>The key-down event arguments.</returns>
	public static KeyEventArgs CreateKeyArgs(HostWindow hostWindow, Key key) => new() { RoutedEvent = InputElement.KeyDownEvent, Key = key };
}
