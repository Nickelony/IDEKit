#if AVALONIAEDIT
using Avalonia.Input;
using AvaloniaEdit;
using Nickelony.IDEKit.Core.Formatting;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.Core.Formatting;
using System.Windows.Input;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

/// <summary>
/// Provides the input-event and formatter doubles shared by the editor editing test classes.
/// </summary>
internal static class EditingTestHelpers
{
	/// <summary>
#if AVALONIAEDIT
	/// Builds the text-input event arguments the editor text area raises for typed text, carrying
	/// the given text for the editor.
#else
	/// Builds a text-composition event argument that carries the given text for the editor.
#endif
	/// </summary>
#if AVALONIAEDIT
	/// <remarks>
	/// the editor raises <see cref="InputElement.TextInputEvent"/> where the WPF reference
	/// suite raised a text-composition event, so this mirrors the reference helper's intent with the
	/// Avalonia event type the mirror's <c>TextAutoClosingService</c> consumes. The Avalonia event carries
	/// no keyboard device, so the WPF <c>ModifierKeyboardDevice</c> double has no counterpart and is not
	/// ported; a test that needs a key event builds the <see cref="KeyEventArgs"/> its code path reads.
	/// </remarks>
	/// <param name="editor">The editor the input targets.</param>
	/// <param name="text">The entered text.</param>
	/// <returns>The text-input event arguments.</returns>
	public static TextInputEventArgs CreateTextInputArgs(TextEditor editor, string text)
#else
	/// <param name="editor">The editor the composition targets.</param>
	/// <param name="text">The composed text.</param>
	/// <returns>The composition event arguments.</returns>
	public static TextCompositionEventArgs CreateTextCompositionArgs(TextEditor editor, string text)
#endif
	{
#if AVALONIAEDIT
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(text);
#else
		var composition = new TextComposition(InputManager.Current, editor, text);
#endif

#if AVALONIAEDIT
		return new TextInputEventArgs
#else
		return new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
#endif
		{
#if AVALONIAEDIT
			Source = editor,
			RoutedEvent = InputElement.TextInputEvent,
			Text = text
#else
			RoutedEvent = TextCompositionManager.TextInputEvent
#endif
		};
	}

	/// <summary>
	/// Supplies a formatter that returns its input unchanged, for tests that only need a reachable formatter.
	/// </summary>
	public sealed class IdentityFormatter : ITextDocumentFormatter
	{
		public string FormatDocument(string content) => content;
	}
#if !AVALONIAEDIT

	/// <summary>
	/// Reports the supplied modifiers as pressed so behavior that depends on
	/// <see cref="KeyEventArgs.KeyboardDevice"/> can be tested without real keyboard input.
	/// </summary>
	public sealed class ModifierKeyboardDevice(ModifierKeys modifiers) : KeyboardDevice(InputManager.Current)
	{
		protected override KeyStates GetKeyStatesFromSystem(Key key)
			=> IsPressedModifierKey(key) ? KeyStates.Down : KeyStates.None;

		private bool IsPressedModifierKey(Key key)
			=> ((modifiers & ModifierKeys.Control) != 0 && key is Key.LeftCtrl or Key.RightCtrl)
				|| ((modifiers & ModifierKeys.Alt) != 0 && key is Key.LeftAlt or Key.RightAlt)
				|| ((modifiers & ModifierKeys.Shift) != 0 && key is Key.LeftShift or Key.RightShift)
				|| ((modifiers & ModifierKeys.Windows) != 0 && key is Key.LWin or Key.RWin);
	}
#endif
}
