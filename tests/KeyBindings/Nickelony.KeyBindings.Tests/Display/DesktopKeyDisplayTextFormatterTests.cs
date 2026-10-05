namespace Nickelony.KeyBindings.Tests;

/// <summary>Covers the desktop formatter for the neutral default and the Windows, Linux, and macOS profiles.</summary>
[TestClass]
public sealed class DesktopKeyDisplayTextFormatterTests
{
	private static DesktopKeyDisplayTextFormatter FormatterFor(string platform)
	{
		return platform switch
		{
			"Windows" => new DesktopKeyDisplayTextFormatter(KeyBindingPlatformProfile.Windows),
			"Linux" => new DesktopKeyDisplayTextFormatter(KeyBindingPlatformProfile.Linux),
			"MacOS" => new DesktopKeyDisplayTextFormatter(KeyBindingPlatformProfile.MacOS),
			_ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown platform."),
		};
	}

	// The default profile is the neutral one a host gets before it opts into a preset, so the canonical
	// labels it renders are pinned here.
	[TestMethod]
	public void GetDisplayText_DefaultProfile_RendersTheCanonicalLabels()
	{
		var formatter = new DesktopKeyDisplayTextFormatter(KeyBindingPlatformProfile.Default);

		Assert.AreEqual("Control+S", formatter.GetDisplayText(new KeyStroke(KeyCode.S, KeyModifiers.Control)));
		Assert.AreEqual("Meta+Enter", formatter.GetDisplayText(new KeyStroke(KeyCode.Enter, KeyModifiers.Meta)));
		Assert.AreEqual("NumPad 5", formatter.GetDisplayText(new KeyStroke(KeyCode.NumPad5, KeyModifiers.None)));
	}

	// The Windows rows are the exhaustive reference for the shared key table; Linux and MacOS cover one
	// row per rendering path plus their platform-specific rows.
	[TestMethod]
	[DataRow("Windows", KeyCode.D0, KeyModifiers.None, "0")]
	[DataRow("Windows", KeyCode.D9, KeyModifiers.None, "9")]
	[DataRow("Windows", KeyCode.F9, KeyModifiers.None, "F9")]
	[DataRow("Windows", KeyCode.Clear, KeyModifiers.None, "Clear")]
	[DataRow("Windows", KeyCode.Separator, KeyModifiers.None, "Separator")]
	[DataRow("Windows", KeyCode.Tab, KeyModifiers.None, "Tab")]
	[DataRow("Windows", KeyCode.Space, KeyModifiers.None, "Space")]
	[DataRow("Windows", KeyCode.Home, KeyModifiers.None, "Home")]
	[DataRow("Windows", KeyCode.End, KeyModifiers.None, "End")]
	[DataRow("Windows", KeyCode.PrintScreen, KeyModifiers.None, "PrintScreen")]
	[DataRow("Windows", KeyCode.Escape, KeyModifiers.None, "Esc")]
	[DataRow("Windows", KeyCode.Semicolon, KeyModifiers.None, ";")]
	[DataRow("Windows", KeyCode.Equals, KeyModifiers.None, "=")]
	[DataRow("Windows", KeyCode.Comma, KeyModifiers.None, ",")]
	[DataRow("Windows", KeyCode.Minus, KeyModifiers.None, "-")]
	[DataRow("Windows", KeyCode.Period, KeyModifiers.None, ".")]
	[DataRow("Windows", KeyCode.Slash, KeyModifiers.None, "/")]
	[DataRow("Windows", KeyCode.Grave, KeyModifiers.None, "`")]
	[DataRow("Windows", KeyCode.LeftBracket, KeyModifiers.None, "[")]
	[DataRow("Windows", KeyCode.RightBracket, KeyModifiers.None, "]")]
	[DataRow("Windows", KeyCode.Backslash, KeyModifiers.None, "\\")]
	[DataRow("Windows", KeyCode.Apostrophe, KeyModifiers.None, "'")]
	[DataRow("Windows", KeyCode.IntlBackslash, KeyModifiers.None, "IntlBackslash")]
	[DataRow("Windows", KeyCode.Oem8, KeyModifiers.None, "Oem8")]
	[DataRow("Windows", KeyCode.NumPad5, KeyModifiers.None, "Num 5")]
	[DataRow("Windows", KeyCode.Add, KeyModifiers.None, "Num +")]
	[DataRow("Windows", KeyCode.Subtract, KeyModifiers.None, "Num -")]
	[DataRow("Windows", KeyCode.Multiply, KeyModifiers.None, "Num *")]
	[DataRow("Windows", KeyCode.Divide, KeyModifiers.None, "Num /")]
	[DataRow("Windows", KeyCode.Decimal, KeyModifiers.None, "Num .")]
	[DataRow("Linux", KeyCode.D5, KeyModifiers.None, "5")]
	[DataRow("Linux", KeyCode.Escape, KeyModifiers.None, "Esc")]
	[DataRow("Linux", KeyCode.NumPad5, KeyModifiers.None, "Num 5")]
	[DataRow("MacOS", KeyCode.D5, KeyModifiers.None, "5")]
	[DataRow("MacOS", KeyCode.Escape, KeyModifiers.None, "Esc")]
	[DataRow("MacOS", KeyCode.NumPad5, KeyModifiers.None, "Num 5")]
	public void GetDisplayText_CommonKey_ReturnsExpectedText(string platform, KeyCode key, KeyModifiers modifiers, string expected) =>
		Assert.AreEqual(expected, FormatterFor(platform).GetDisplayText(new KeyStroke(key, modifiers)));

	[TestMethod]
	[DataRow("Windows", KeyCode.Enter, "Enter")]
	[DataRow("Windows", KeyCode.Backspace, "Backspace")]
	[DataRow("Windows", KeyCode.Delete, "Delete")]
	[DataRow("Windows", KeyCode.PageUp, "PageUp")]
	[DataRow("Windows", KeyCode.PageDown, "PageDown")]
	[DataRow("Windows", KeyCode.Left, "Left")]
	[DataRow("Windows", KeyCode.Right, "Right")]
	[DataRow("Linux", KeyCode.Enter, "Enter")]
	[DataRow("Linux", KeyCode.Backspace, "Backspace")]
	[DataRow("Linux", KeyCode.Delete, "Delete")]
	[DataRow("Linux", KeyCode.PageUp, "PageUp")]
	[DataRow("Linux", KeyCode.PageDown, "PageDown")]
	[DataRow("Linux", KeyCode.Up, "Up")]
	[DataRow("Linux", KeyCode.Down, "Down")]
	[DataRow("MacOS", KeyCode.Enter, "Return")]
	[DataRow("MacOS", KeyCode.Backspace, "Delete")]
	[DataRow("MacOS", KeyCode.Delete, "Forward Delete")]
	[DataRow("MacOS", KeyCode.PageUp, "Page Up")]
	[DataRow("MacOS", KeyCode.PageDown, "Page Down")]
	[DataRow("MacOS", KeyCode.Left, "←")]
	[DataRow("MacOS", KeyCode.Up, "↑")]
	[DataRow("MacOS", KeyCode.Right, "→")]
	[DataRow("MacOS", KeyCode.Down, "↓")]
	public void GetDisplayText_SpecialKey_ReturnsExpectedText(string platform, KeyCode key, string expected) =>
		Assert.AreEqual(expected, FormatterFor(platform).GetDisplayText(new KeyStroke(key, KeyModifiers.None)));

	[TestMethod]
	[DataRow("Windows", KeyCode.S, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta, "Ctrl+Shift+Alt+Win+S")]
	[DataRow("Windows", KeyCode.S, KeyModifiers.Meta, "Win+S")]
	[DataRow("Windows", KeyCode.Space, KeyModifiers.Control, "Ctrl+Space")]
	[DataRow("Linux", KeyCode.S, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta, "Ctrl+Shift+Alt+Super+S")]
	[DataRow("Linux", KeyCode.S, KeyModifiers.Meta, "Super+S")]
	[DataRow("Linux", KeyCode.Space, KeyModifiers.Meta, "Super+Space")]
	[DataRow("MacOS", KeyCode.S, KeyModifiers.Meta, "⌘S")]
	[DataRow("MacOS", KeyCode.Z, KeyModifiers.Shift | KeyModifiers.Meta, "⇧⌘Z")]
	[DataRow("MacOS", KeyCode.Escape, KeyModifiers.Alt | KeyModifiers.Meta, "⌥⌘Esc")]
	[DataRow("MacOS", KeyCode.Slash, KeyModifiers.Control, "⌃/")]
	[DataRow("MacOS", KeyCode.S, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta, "⌃⌥⇧⌘S")]
	public void GetDisplayText_Modifiers_ReturnExpectedText(string platform, KeyCode key, KeyModifiers modifiers, string expected) =>
		Assert.AreEqual(expected, FormatterFor(platform).GetDisplayText(new KeyStroke(key, modifiers)));

	[TestMethod]
	[DataRow("Windows", "Ctrl+K, Ctrl+S")]
	[DataRow("Linux", "Ctrl+K, Ctrl+S")]
	[DataRow("MacOS", "⌘K ⌘S")]
	public void GetDisplayText_Chord_JoinsStrokesWithThePlatformSeparator(string platform, string expected)
	{
		var chord = new KeyChord(
			new KeyStroke(KeyCode.K, platform == "MacOS" ? KeyModifiers.Meta : KeyModifiers.Control),
			new KeyStroke(KeyCode.S, platform == "MacOS" ? KeyModifiers.Meta : KeyModifiers.Control)
		);

		Assert.AreEqual(expected, FormatterFor(platform).GetDisplayText(chord));
	}

	[TestMethod]
	[DataRow("Windows")]
	[DataRow("Linux")]
	[DataRow("MacOS")]
	public void GetDisplayText_UninitializedStroke_Throws(string platform) =>
		Assert.ThrowsExactly<ArgumentException>(() => FormatterFor(platform).GetDisplayText(default));

	[TestMethod]
	[DataRow("Windows")]
	[DataRow("Linux")]
	[DataRow("MacOS")]
	public void GetDisplayText_UninitializedChord_Throws(string platform) =>
		Assert.ThrowsExactly<ArgumentException>(() => FormatterFor(platform).GetDisplayText(default(KeyChord)));

	[TestMethod]
	public void Constructor_NullProfile_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new DesktopKeyDisplayTextFormatter(null!));

	[TestMethod]
	public void Profile_ReturnsTheSuppliedValue()
	{
		KeyBindingPlatformProfile profile = KeyBindingPlatformProfile.Linux;
		Assert.AreSame(profile, new DesktopKeyDisplayTextFormatter(profile).Profile);
	}

	[TestMethod]
	public void Profile_WithConfiguration_ChangesOnlyTheEditedLabel()
	{
		KeyBindingPlatformProfile custom = KeyBindingPlatformProfile.Windows with { MetaLabel = "Cmd" };

		var formatter = new DesktopKeyDisplayTextFormatter(custom);

		Assert.AreEqual("Cmd+S", formatter.GetDisplayText(new KeyStroke(KeyCode.S, KeyModifiers.Meta)));
		Assert.AreEqual("Ctrl+S", formatter.GetDisplayText(new KeyStroke(KeyCode.S, KeyModifiers.Control)));
	}
}
