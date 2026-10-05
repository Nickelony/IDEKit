using System.Windows.Input;

namespace Nickelony.Testing;

/// <summary>
/// A WPF keyboard device that reports a fixed set of keys as pressed, so behavior that reads <see cref="KeyEventArgs.KeyboardDevice"/> runs
/// without real keyboard input.
/// </summary>
/// <remarks>
/// It lives in the WPF-only folder of the shared test support, so the toolkit-free helpers beside it stay free of WPF types.
/// </remarks>
internal sealed class FakeKeyboardDevice : KeyboardDevice
{
	private static readonly (ModifierKeys Modifier, Key Left, Key Right)[] s_modifierKeys =
	[
		(ModifierKeys.Control, Key.LeftCtrl, Key.RightCtrl),
		(ModifierKeys.Alt, Key.LeftAlt, Key.RightAlt),
		(ModifierKeys.Shift, Key.LeftShift, Key.RightShift),
		(ModifierKeys.Windows, Key.LWin, Key.RWin),
	];

	private readonly HashSet<Key> _downKeys;

	/// <param name="downKeys">The keys that the device reports as pressed.</param>
	internal FakeKeyboardDevice(params Key[] downKeys)
		: base(InputManager.Current)
	{
		_downKeys = [.. downKeys];
	}

	/// <summary>Creates a device that reports both sides of each given modifier as pressed.</summary>
	/// <param name="modifiers">The modifiers to report as pressed; <see cref="ModifierKeys.None"/> reports none.</param>
	internal static FakeKeyboardDevice ForModifiers(ModifierKeys modifiers)
	{
		IEnumerable<Key> pressed = s_modifierKeys
			.Where(entry => (modifiers & entry.Modifier) != 0)
			.SelectMany(entry => new[] { entry.Left, entry.Right });

		return new([.. pressed]);
	}

	/// <inheritdoc/>
	protected override KeyStates GetKeyStatesFromSystem(Key key) => _downKeys.Contains(key) ? KeyStates.Down : KeyStates.None;
}
