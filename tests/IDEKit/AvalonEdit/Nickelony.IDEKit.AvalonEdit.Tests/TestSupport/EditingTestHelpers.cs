using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using KeyModifiers = System.Windows.Input.ModifierKeys;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

internal static partial class EditingTestHelpers
{
	/// <summary>Builds a text-composition event argument that carries the given text for the editor.</summary>
	/// <param name="editor">The editor the composition targets.</param>
	/// <param name="text">The composed text.</param>
	/// <returns>The composition event arguments.</returns>
	public static TextCompositionEventArgs CreateTextArgs(TextEditor editor, string text)
	{
		var composition = new TextComposition(InputManager.Current, editor, text);
		return new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.TextInputEvent };
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
		return new(
			keyboard: modifiers == KeyModifiers.None ? Keyboard.PrimaryDevice : FakeKeyboardDevice.ForModifiers(modifiers),
			inputSource: PresentationSource.FromVisual(hostWindow),
			timestamp: 0,
			key: Key.Back
		)
		{
			RoutedEvent = Keyboard.KeyDownEvent,
		};
	}

	/// <summary>Builds the key-down event of an arbitrary key press.</summary>
	/// <param name="hostWindow">The host window that supplies the presentation source.</param>
	/// <param name="key">The key that is pressed.</param>
	/// <returns>The key-down event arguments.</returns>
	public static KeyEventArgs CreateKeyArgs(HostWindow hostWindow, Key key)
	{
		return new(keyboard: Keyboard.PrimaryDevice, inputSource: PresentationSource.FromVisual(hostWindow), timestamp: 0, key)
		{
			RoutedEvent = Keyboard.KeyDownEvent,
		};
	}
}
