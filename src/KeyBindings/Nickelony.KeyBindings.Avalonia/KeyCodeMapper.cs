using Avalonia.Input;
using System.Collections.Frozen;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.KeyBindings.Avalonia;

/// <summary>
/// Maps between Avalonia <see cref="Key"/> values and the host-neutral <see cref="KeyCode"/> values.
/// </summary>
/// <remarks>
/// <para>
/// The neutral key model owns its own dense numbering, so the conversion is an explicit table rather
/// than an integer cast. Avalonia alias members that share a value, such as the <c>Enter</c>/<c>Return</c>
/// pair, resolve to their single <see cref="KeyCode"/> counterpart because the table is keyed by the
/// shared <see cref="Key"/> value.
/// </para>
/// <para>
/// Keys outside the package's key model - modifier keys, lock keys, processing pseudo-keys, and the
/// excluded legacy keys - have no <see cref="KeyCode"/> member and are reported as
/// <see langword="null"/>.
/// </para>
/// </remarks>
public static class KeyCodeMapper
{
	private const AvaloniaKeyModifiers DefinedModifierKeys = AvaloniaKeyModifiers.Alt | AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Shift | AvaloniaKeyModifiers.Meta;

	/// <summary>
	/// The explicit mapping between an Avalonia <see cref="Key"/> and the <see cref="KeyCode"/> it
	/// represents. Each pair is listed once so the two lookup tables below cannot drift apart.
	/// </summary>
	/// <remarks>
	/// A hand-synced copy of the WPF adapter's table
	/// (<c>src/KeyBindings/Nickelony.KeyBindings.Wpf/KeyCodeMapper.cs</c>). The two toolkits expose
	/// distinct key enums, so the tables cannot be shared; a mapping change belongs in both. The symmetric
	/// all-<see cref="KeyCode"/> round-trip tests in each adapter suite are the current drift guard, and
	/// consolidating the two tables is tracked as backlog item <c>KB2FR-B4</c> (re-prioritised by
	/// <c>KB3FR-B4</c>).
	/// </remarks>
	private static readonly (Key Key, KeyCode Code)[] s_pairs =
	[
		(Key.Cancel, KeyCode.Cancel),
		(Key.Back, KeyCode.Backspace),
		(Key.Tab, KeyCode.Tab),
		(Key.Clear, KeyCode.Clear),
		(Key.Enter, KeyCode.Enter),
		(Key.Pause, KeyCode.Pause),
		(Key.KanaMode, KeyCode.KanaMode),
		(Key.JunjaMode, KeyCode.JunjaMode),
		(Key.FinalMode, KeyCode.FinalMode),
		(Key.HanjaMode, KeyCode.HanjaMode),
		(Key.Escape, KeyCode.Escape),
		(Key.ImeConvert, KeyCode.ImeConvert),
		(Key.ImeNonConvert, KeyCode.ImeNonConvert),
		(Key.ImeAccept, KeyCode.ImeAccept),
		(Key.ImeModeChange, KeyCode.ImeModeChange),
		(Key.Space, KeyCode.Space),
		(Key.PageUp, KeyCode.PageUp),
		(Key.PageDown, KeyCode.PageDown),
		(Key.End, KeyCode.End),
		(Key.Home, KeyCode.Home),
		(Key.Left, KeyCode.Left),
		(Key.Up, KeyCode.Up),
		(Key.Right, KeyCode.Right),
		(Key.Down, KeyCode.Down),
		(Key.Select, KeyCode.Select),
		(Key.Print, KeyCode.Print),
		(Key.Execute, KeyCode.Execute),
		(Key.PrintScreen, KeyCode.PrintScreen),
		(Key.Insert, KeyCode.Insert),
		(Key.Delete, KeyCode.Delete),
		(Key.Help, KeyCode.Help),
		(Key.D0, KeyCode.D0),
		(Key.D1, KeyCode.D1),
		(Key.D2, KeyCode.D2),
		(Key.D3, KeyCode.D3),
		(Key.D4, KeyCode.D4),
		(Key.D5, KeyCode.D5),
		(Key.D6, KeyCode.D6),
		(Key.D7, KeyCode.D7),
		(Key.D8, KeyCode.D8),
		(Key.D9, KeyCode.D9),
		(Key.A, KeyCode.A),
		(Key.B, KeyCode.B),
		(Key.C, KeyCode.C),
		(Key.D, KeyCode.D),
		(Key.E, KeyCode.E),
		(Key.F, KeyCode.F),
		(Key.G, KeyCode.G),
		(Key.H, KeyCode.H),
		(Key.I, KeyCode.I),
		(Key.J, KeyCode.J),
		(Key.K, KeyCode.K),
		(Key.L, KeyCode.L),
		(Key.M, KeyCode.M),
		(Key.N, KeyCode.N),
		(Key.O, KeyCode.O),
		(Key.P, KeyCode.P),
		(Key.Q, KeyCode.Q),
		(Key.R, KeyCode.R),
		(Key.S, KeyCode.S),
		(Key.T, KeyCode.T),
		(Key.U, KeyCode.U),
		(Key.V, KeyCode.V),
		(Key.W, KeyCode.W),
		(Key.X, KeyCode.X),
		(Key.Y, KeyCode.Y),
		(Key.Z, KeyCode.Z),
		(Key.Apps, KeyCode.Apps),
		(Key.Sleep, KeyCode.Sleep),
		(Key.NumPad0, KeyCode.NumPad0),
		(Key.NumPad1, KeyCode.NumPad1),
		(Key.NumPad2, KeyCode.NumPad2),
		(Key.NumPad3, KeyCode.NumPad3),
		(Key.NumPad4, KeyCode.NumPad4),
		(Key.NumPad5, KeyCode.NumPad5),
		(Key.NumPad6, KeyCode.NumPad6),
		(Key.NumPad7, KeyCode.NumPad7),
		(Key.NumPad8, KeyCode.NumPad8),
		(Key.NumPad9, KeyCode.NumPad9),
		(Key.Multiply, KeyCode.Multiply),
		(Key.Add, KeyCode.Add),
		(Key.Separator, KeyCode.Separator),
		(Key.Subtract, KeyCode.Subtract),
		(Key.Decimal, KeyCode.Decimal),
		(Key.Divide, KeyCode.Divide),
		(Key.F1, KeyCode.F1),
		(Key.F2, KeyCode.F2),
		(Key.F3, KeyCode.F3),
		(Key.F4, KeyCode.F4),
		(Key.F5, KeyCode.F5),
		(Key.F6, KeyCode.F6),
		(Key.F7, KeyCode.F7),
		(Key.F8, KeyCode.F8),
		(Key.F9, KeyCode.F9),
		(Key.F10, KeyCode.F10),
		(Key.F11, KeyCode.F11),
		(Key.F12, KeyCode.F12),
		(Key.F13, KeyCode.F13),
		(Key.F14, KeyCode.F14),
		(Key.F15, KeyCode.F15),
		(Key.F16, KeyCode.F16),
		(Key.F17, KeyCode.F17),
		(Key.F18, KeyCode.F18),
		(Key.F19, KeyCode.F19),
		(Key.F20, KeyCode.F20),
		(Key.F21, KeyCode.F21),
		(Key.F22, KeyCode.F22),
		(Key.F23, KeyCode.F23),
		(Key.F24, KeyCode.F24),
		(Key.BrowserBack, KeyCode.BrowserBack),
		(Key.BrowserForward, KeyCode.BrowserForward),
		(Key.BrowserRefresh, KeyCode.BrowserRefresh),
		(Key.BrowserStop, KeyCode.BrowserStop),
		(Key.BrowserSearch, KeyCode.BrowserSearch),
		(Key.BrowserFavorites, KeyCode.BrowserFavorites),
		(Key.BrowserHome, KeyCode.BrowserHome),
		(Key.VolumeMute, KeyCode.VolumeMute),
		(Key.VolumeDown, KeyCode.VolumeDown),
		(Key.VolumeUp, KeyCode.VolumeUp),
		(Key.MediaNextTrack, KeyCode.MediaNextTrack),
		(Key.MediaPreviousTrack, KeyCode.MediaPreviousTrack),
		(Key.MediaStop, KeyCode.MediaStop),
		(Key.MediaPlayPause, KeyCode.MediaPlayPause),
		(Key.LaunchMail, KeyCode.LaunchMail),
		(Key.SelectMedia, KeyCode.SelectMedia),
		(Key.LaunchApplication1, KeyCode.LaunchApplication1),
		(Key.LaunchApplication2, KeyCode.LaunchApplication2),
		(Key.OemSemicolon, KeyCode.Semicolon),
		(Key.OemPlus, KeyCode.Equals),
		(Key.OemComma, KeyCode.Comma),
		(Key.OemMinus, KeyCode.Minus),
		(Key.OemPeriod, KeyCode.Period),
		(Key.OemQuestion, KeyCode.Slash),
		(Key.OemTilde, KeyCode.Grave),
		(Key.AbntC1, KeyCode.AbntC1),
		(Key.AbntC2, KeyCode.AbntC2),
		(Key.OemOpenBrackets, KeyCode.LeftBracket),
		(Key.OemPipe, KeyCode.Backslash),
		(Key.OemCloseBrackets, KeyCode.RightBracket),
		(Key.OemQuotes, KeyCode.Apostrophe),
		(Key.Oem8, KeyCode.Oem8),
		(Key.OemBackslash, KeyCode.IntlBackslash)
	];

	private static readonly FrozenDictionary<Key, KeyCode> s_keyToCode = s_pairs.ToFrozenDictionary(pair => pair.Key, pair => pair.Code);

	private static readonly FrozenDictionary<KeyCode, Key> s_codeToKey = s_pairs.ToFrozenDictionary(pair => pair.Code, pair => pair.Key);

	/// <summary>
	/// Converts an Avalonia key to the matching <see cref="KeyCode"/>.
	/// </summary>
	/// <param name="key">The Avalonia key to convert.</param>
	/// <returns>The matching key code, or <see langword="null"/> when the key cannot be the primary key of a binding.</returns>
	public static KeyCode? ToKeyCode(Key key)
		=> s_keyToCode.TryGetValue(key, out KeyCode code) ? code : null;

	/// <summary>
	/// Converts a <see cref="KeyCode"/> to the matching Avalonia key.
	/// </summary>
	/// <param name="keyCode">The key code to convert.</param>
	/// <returns>The matching Avalonia key.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="keyCode"/> is not a defined <see cref="KeyCode"/> member.</exception>
	public static Key ToAvaloniaKey(KeyCode keyCode)
		=> s_codeToKey.TryGetValue(keyCode, out Key key)
			? key
			: throw new ArgumentOutOfRangeException(nameof(keyCode), keyCode, "The key code must be a defined KeyCode member.");

	/// <summary>
	/// Converts <see cref="KeyModifierSet"/> flags to the matching Avalonia
	/// <c>Avalonia.Input.KeyModifiers</c> flags.
	/// </summary>
	/// <param name="modifiers">The modifier flags to convert.</param>
	/// <returns>The matching Avalonia modifier flags.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="modifiers"/> sets bits outside the defined <see cref="KeyModifierSet"/> flags.</exception>
	public static AvaloniaKeyModifiers ToAvaloniaKeyModifiers(KeyModifierSet modifiers)
	{
		if ((modifiers & ~KeyModifierMasks.Defined) != KeyModifierSet.None)
			throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, "The modifier flags must be defined KeyModifierSet flags.");

		AvaloniaKeyModifiers result = AvaloniaKeyModifiers.None;

		if ((modifiers & KeyModifierSet.Alt) != KeyModifierSet.None)
			result |= AvaloniaKeyModifiers.Alt;

		if ((modifiers & KeyModifierSet.Control) != KeyModifierSet.None)
			result |= AvaloniaKeyModifiers.Control;

		if ((modifiers & KeyModifierSet.Shift) != KeyModifierSet.None)
			result |= AvaloniaKeyModifiers.Shift;

		if ((modifiers & KeyModifierSet.Meta) != KeyModifierSet.None)
			result |= AvaloniaKeyModifiers.Meta;

		return result;
	}

	/// <summary>
	/// Converts Avalonia <c>Avalonia.Input.KeyModifiers</c> flags to the matching
	/// <see cref="KeyModifierSet"/> flags.
	/// </summary>
	/// <param name="modifiers">The Avalonia modifier flags to convert.</param>
	/// <returns>The matching modifier flags.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="modifiers"/> sets bits outside the defined Avalonia <c>Avalonia.Input.KeyModifiers</c> flags.</exception>
	public static KeyModifierSet ToKeyModifierSet(AvaloniaKeyModifiers modifiers)
	{
		if ((modifiers & ~DefinedModifierKeys) != 0)
			throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, "The modifier flags must be defined Avalonia modifier flags.");

		KeyModifierSet result = KeyModifierSet.None;

		if ((modifiers & AvaloniaKeyModifiers.Alt) != 0)
			result |= KeyModifierSet.Alt;

		if ((modifiers & AvaloniaKeyModifiers.Control) != 0)
			result |= KeyModifierSet.Control;

		if ((modifiers & AvaloniaKeyModifiers.Shift) != 0)
			result |= KeyModifierSet.Shift;

		if ((modifiers & AvaloniaKeyModifiers.Meta) != 0)
			result |= KeyModifierSet.Meta;

		return result;
	}
}
