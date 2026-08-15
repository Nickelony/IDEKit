using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.KeyBindings.Wpf.Tests;

/// <summary>
/// Creates routed key-down events backed by deterministic keyboard state.
/// </summary>
internal static class TestKeyEvents
{
	/// <summary>
	/// Creates a key-down event for the given key with the given keys reported as pressed.
	/// </summary>
	internal static KeyEventArgs CreateKeyDown(Key key, params Key[] downModifierKeys)
		=> new(new FakeKeyboardDevice([key, .. downModifierKeys]), new FakePresentationSource(), 0, key)
		{
			RoutedEvent = Keyboard.KeyDownEvent
		};
}

/// <summary>
/// Keyboard device that reports a fixed set of pressed keys.
/// </summary>
internal sealed class FakeKeyboardDevice : KeyboardDevice
{
	private readonly HashSet<Key> _downKeys;

	internal FakeKeyboardDevice(params Key[] downKeys)
		: base(InputManager.Current)
	{
		_downKeys = [.. downKeys];
	}

	protected override KeyStates GetKeyStatesFromSystem(Key key)
		=> _downKeys.Contains(key) ? KeyStates.Down : KeyStates.None;
}

/// <summary>
/// Presentation source stub that allows constructing routed key events without a visual host.
/// </summary>
internal sealed class FakePresentationSource : PresentationSource
{
	public override Visual RootVisual { get; set; } = new ContainerVisual();

	public override bool IsDisposed => false;

	protected override CompositionTarget GetCompositionTargetCore()
		=> null!;
}
