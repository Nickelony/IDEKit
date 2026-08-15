namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers the desktop formatter for the neutral default and the Windows, Linux, and macOS conventions.
/// </summary>
[TestClass]
public class DesktopKeyDisplayTextFormatterTests
{
	private static DesktopKeyDisplayTextFormatter FormatterFor(string convention)
		=> convention switch
		{
			"Windows" => DesktopKeyDisplayTextFormatter.Windows,
			"Linux" => DesktopKeyDisplayTextFormatter.Linux,
			"MacOS" => DesktopKeyDisplayTextFormatter.MacOS,
			_ => throw new ArgumentOutOfRangeException(nameof(convention), convention, "Unknown convention.")
		};

	// The bare convention is the neutral default a host gets before it opts into a preset, so the canonical
	// labels it renders are pinned here.
	[TestMethod]
	public void GetDisplayText_NeutralConventions_RendersTheCanonicalLabels()
	{
		var formatter = new DesktopKeyDisplayTextFormatter(new DesktopKeyDisplayConventions());

		Assert.AreEqual("Control+S", formatter.GetDisplayText(new KeyCombo(KeyCode.S, KeyModifierSet.Control)));
		Assert.AreEqual("Meta+Enter", formatter.GetDisplayText(new KeyCombo(KeyCode.Enter, KeyModifierSet.Meta)));
		Assert.AreEqual("Numpad 5", formatter.GetDisplayText(new KeyCombo(KeyCode.NumPad5, KeyModifierSet.None)));
	}

	// The Windows rows are the exhaustive reference for the shared key table. Linux and MacOS render the
	// same shared table, so they only carry one row per rendering path (an identity key, a shared-table
	// override, and a numpad key) plus their convention-specific rows further down; repeating every
	// shared entry per convention would re-assert the same table without covering another path.
	[TestMethod]
	[DataRow("Windows", KeyCode.D0, KeyModifierSet.None, "0")]
	[DataRow("Windows", KeyCode.D9, KeyModifierSet.None, "9")]
	[DataRow("Windows", KeyCode.F9, KeyModifierSet.None, "F9")]
	[DataRow("Windows", KeyCode.Clear, KeyModifierSet.None, "Clear")]
	[DataRow("Windows", KeyCode.Tab, KeyModifierSet.None, "Tab")]
	[DataRow("Windows", KeyCode.Space, KeyModifierSet.None, "Space")]
	[DataRow("Windows", KeyCode.Home, KeyModifierSet.None, "Home")]
	[DataRow("Windows", KeyCode.End, KeyModifierSet.None, "End")]
	[DataRow("Windows", KeyCode.PrintScreen, KeyModifierSet.None, "PrintScreen")]
	[DataRow("Windows", KeyCode.Escape, KeyModifierSet.None, "Esc")]
	[DataRow("Windows", KeyCode.Semicolon, KeyModifierSet.None, ";")]
	[DataRow("Windows", KeyCode.Equals, KeyModifierSet.None, "=")]
	[DataRow("Windows", KeyCode.Comma, KeyModifierSet.None, ",")]
	[DataRow("Windows", KeyCode.Minus, KeyModifierSet.None, "-")]
	[DataRow("Windows", KeyCode.Period, KeyModifierSet.None, ".")]
	[DataRow("Windows", KeyCode.Slash, KeyModifierSet.None, "/")]
	[DataRow("Windows", KeyCode.Grave, KeyModifierSet.None, "`")]
	[DataRow("Windows", KeyCode.LeftBracket, KeyModifierSet.None, "[")]
	[DataRow("Windows", KeyCode.RightBracket, KeyModifierSet.None, "]")]
	[DataRow("Windows", KeyCode.Backslash, KeyModifierSet.None, "\\")]
	[DataRow("Windows", KeyCode.Apostrophe, KeyModifierSet.None, "'")]
	[DataRow("Windows", KeyCode.IntlBackslash, KeyModifierSet.None, "IntlBackslash")]
	[DataRow("Windows", KeyCode.Oem8, KeyModifierSet.None, "Oem8")]
	[DataRow("Windows", KeyCode.NumPad5, KeyModifierSet.None, "Num 5")]
	[DataRow("Windows", KeyCode.Add, KeyModifierSet.None, "Num +")]
	[DataRow("Windows", KeyCode.Subtract, KeyModifierSet.None, "Num -")]
	[DataRow("Windows", KeyCode.Multiply, KeyModifierSet.None, "Num *")]
	[DataRow("Windows", KeyCode.Divide, KeyModifierSet.None, "Num /")]
	[DataRow("Windows", KeyCode.Decimal, KeyModifierSet.None, "Num .")]
	[DataRow("Linux", KeyCode.D5, KeyModifierSet.None, "5")]
	[DataRow("Linux", KeyCode.Escape, KeyModifierSet.None, "Esc")]
	[DataRow("Linux", KeyCode.NumPad5, KeyModifierSet.None, "Num 5")]
	[DataRow("MacOS", KeyCode.D5, KeyModifierSet.None, "5")]
	[DataRow("MacOS", KeyCode.Escape, KeyModifierSet.None, "Esc")]
	[DataRow("MacOS", KeyCode.NumPad5, KeyModifierSet.None, "Num 5")]
	public void GetDisplayText_CommonKey_ReturnsExpectedText(string convention, KeyCode key, KeyModifierSet modifiers, string expected)
		=> Assert.AreEqual(expected, FormatterFor(convention).GetDisplayText(new KeyCombo(key, modifiers)));

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
	public void GetDisplayText_SpecialKey_ReturnsExpectedText(string convention, KeyCode key, string expected)
		=> Assert.AreEqual(expected, FormatterFor(convention).GetDisplayText(new KeyCombo(key, KeyModifierSet.None)));

	[TestMethod]
	[DataRow("Windows", KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Alt | KeyModifierSet.Meta, "Ctrl+Shift+Alt+Win+S")]
	[DataRow("Windows", KeyCode.S, KeyModifierSet.Meta, "Win+S")]
	[DataRow("Windows", KeyCode.Space, KeyModifierSet.Control, "Ctrl+Space")]
	[DataRow("Linux", KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Alt | KeyModifierSet.Meta, "Ctrl+Shift+Alt+Super+S")]
	[DataRow("Linux", KeyCode.S, KeyModifierSet.Meta, "Super+S")]
	[DataRow("Linux", KeyCode.Space, KeyModifierSet.Meta, "Super+Space")]
	[DataRow("MacOS", KeyCode.S, KeyModifierSet.Meta, "⌘S")]
	[DataRow("MacOS", KeyCode.Z, KeyModifierSet.Shift | KeyModifierSet.Meta, "⇧⌘Z")]
	[DataRow("MacOS", KeyCode.Escape, KeyModifierSet.Alt | KeyModifierSet.Meta, "⌥⌘Esc")]
	[DataRow("MacOS", KeyCode.Slash, KeyModifierSet.Control, "⌃/")]
	[DataRow("MacOS", KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Alt | KeyModifierSet.Meta, "⌃⌥⇧⌘S")]
	public void GetDisplayText_Modifiers_ReturnExpectedText(string convention, KeyCode key, KeyModifierSet modifiers, string expected)
		=> Assert.AreEqual(expected, FormatterFor(convention).GetDisplayText(new KeyCombo(key, modifiers)));

	[TestMethod]
	[DataRow("Windows", "Ctrl+K, Ctrl+S")]
	[DataRow("Linux", "Ctrl+K, Ctrl+S")]
	[DataRow("MacOS", "⌘K ⌘S")]
	public void GetDisplayText_Chord_JoinsStrokesWithTheConventionSeparator(string convention, string expected)
	{
		var chord = new KeyChord(
			new KeyCombo(KeyCode.K, convention == "MacOS" ? KeyModifierSet.Meta : KeyModifierSet.Control),
			new KeyCombo(KeyCode.S, convention == "MacOS" ? KeyModifierSet.Meta : KeyModifierSet.Control));

		Assert.AreEqual(expected, FormatterFor(convention).GetDisplayText(chord));
	}

	[TestMethod]
	[DataRow("Windows")]
	[DataRow("Linux")]
	[DataRow("MacOS")]
	public void GetDisplayText_UninitializedCombo_Throws(string convention)
		=> Assert.ThrowsExactly<ArgumentException>(() => FormatterFor(convention).GetDisplayText(default));

	[TestMethod]
	[DataRow("Windows")]
	[DataRow("Linux")]
	[DataRow("MacOS")]
	public void GetDisplayText_UninitializedChord_Throws(string convention)
		=> Assert.ThrowsExactly<ArgumentException>(() => FormatterFor(convention).GetDisplayText(default(KeyChord)));

	[TestMethod]
	public void Constructor_NullConventions_Throws()
		=> Assert.ThrowsExactly<ArgumentNullException>(() => new DesktopKeyDisplayTextFormatter(null!));

	[TestMethod]
	public void Conventions_ReturnsTheSuppliedValue()
	{
		DesktopKeyDisplayConventions conventions = DesktopKeyDisplayConventions.Linux;

		Assert.AreSame(conventions, new DesktopKeyDisplayTextFormatter(conventions).Conventions);
	}

	[TestMethod]
	public void Conventions_WithConfiguration_ChangesOnlyTheEditedLabel()
	{
		DesktopKeyDisplayConventions custom = DesktopKeyDisplayConventions.Windows with { MetaLabel = "Cmd" };
		var formatter = new DesktopKeyDisplayTextFormatter(custom);

		Assert.AreEqual("Cmd+S", formatter.GetDisplayText(new KeyCombo(KeyCode.S, KeyModifierSet.Meta)));
		Assert.AreEqual("Ctrl+S", formatter.GetDisplayText(new KeyCombo(KeyCode.S, KeyModifierSet.Control)));
	}
}
