using System.Windows;
using System.Windows.Input;
using Nickelony.Testing;

namespace Nickelony.KeyBindings.Wpf.Tests;

/// <summary>Creates routed key-down events backed by deterministic keyboard state.</summary>
internal static class TestKeyEvents
{
	/// <summary>Creates a key-down event for the given key with the given keys reported as pressed.</summary>
	internal static KeyEventArgs CreateKeyDown(Key key, params Key[] downModifierKeys)
	{
		return new(keyboard: new FakeKeyboardDevice([key, .. downModifierKeys]), inputSource: new FakePresentationSource(), timestamp: 0, key)
		{
			RoutedEvent = Keyboard.KeyDownEvent,
		};
	}
}
