using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.KeyBindings.Avalonia.Tests;

/// <summary>
/// Creates Avalonia key events with a fixed key and modifier snapshot.
/// </summary>
internal static class TestKeyEvents
{
	/// <summary>
	/// Creates a key-down event for the given key with the given modifiers reported as pressed.
	/// </summary>
	internal static KeyEventArgs CreateKeyDown(Key key, AvaloniaKeyModifiers modifiers = AvaloniaKeyModifiers.None)
		=> new() { Key = key, KeyModifiers = modifiers };
}
