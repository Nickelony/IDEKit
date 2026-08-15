using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.KeyBindings.Avalonia.Tests;

[TestClass]
public class KeyCodeMapperTests
{
	[TestMethod]
	[DataRow(Key.S, KeyCode.S)]
	[DataRow(Key.F9, KeyCode.F9)]
	[DataRow(Key.F12, KeyCode.F12)]
	[DataRow(Key.D1, KeyCode.D1)]
	[DataRow(Key.NumPad5, KeyCode.NumPad5)]
	[DataRow(Key.Left, KeyCode.Left)]
	[DataRow(Key.Delete, KeyCode.Delete)]
	[DataRow(Key.Enter, KeyCode.Enter)]
	[DataRow(Key.OemQuestion, KeyCode.Slash)]
	[DataRow(Key.OemComma, KeyCode.Comma)]
	[DataRow(Key.OemPeriod, KeyCode.Period)]
	[DataRow(Key.OemTilde, KeyCode.Grave)]
	[DataRow(Key.PrintScreen, KeyCode.PrintScreen)]
	[DataRow(Key.PageDown, KeyCode.PageDown)]
	[DataRow(Key.PageUp, KeyCode.PageUp)]
	[DataRow(Key.OemSemicolon, KeyCode.Semicolon)]
	[DataRow(Key.OemBackslash, KeyCode.IntlBackslash)]
	[DataRow(Key.KanaMode, KeyCode.KanaMode)]
	[DataRow(Key.HanjaMode, KeyCode.HanjaMode)]
	public void ToKeyCode_KnownKeys_ReturnsExpectedCode(Key key, KeyCode expected)
		=> Assert.AreEqual(expected, KeyCodeMapper.ToKeyCode(key));

	[TestMethod]
	public void ToKeyCode_AvaloniaAliasMembers_ResolveToTheCanonicalCode()
	{
		// Avalonia declares alias members that share a value with the canonical name; the table is keyed
		// by the shared value, so each alias resolves to the same KeyCode.
		Assert.AreEqual(KeyCode.Enter, KeyCodeMapper.ToKeyCode(Key.Return));
		Assert.AreEqual(KeyCode.PrintScreen, KeyCodeMapper.ToKeyCode(Key.Snapshot));
		Assert.AreEqual(KeyCode.PageDown, KeyCodeMapper.ToKeyCode(Key.Next));
		Assert.AreEqual(KeyCode.PageUp, KeyCodeMapper.ToKeyCode(Key.Prior));
		Assert.AreEqual(KeyCode.Semicolon, KeyCodeMapper.ToKeyCode(Key.Oem1));
		Assert.AreEqual(KeyCode.IntlBackslash, KeyCodeMapper.ToKeyCode(Key.Oem102));
		Assert.AreEqual(KeyCode.KanaMode, KeyCodeMapper.ToKeyCode(Key.HangulMode));
		Assert.AreEqual(KeyCode.HanjaMode, KeyCodeMapper.ToKeyCode(Key.KanjiMode));
	}

	[TestMethod]
	[DataRow(Key.None)]
	[DataRow(Key.LeftCtrl)]
	[DataRow(Key.RightCtrl)]
	[DataRow(Key.LeftShift)]
	[DataRow(Key.RightShift)]
	[DataRow(Key.LeftAlt)]
	[DataRow(Key.RightAlt)]
	[DataRow(Key.LWin)]
	[DataRow(Key.RWin)]
	[DataRow(Key.CapsLock)]
	[DataRow(Key.NumLock)]
	[DataRow(Key.Scroll)]
	[DataRow(Key.System)]
	[DataRow(Key.ImeProcessed)]
	[DataRow(Key.DeadCharProcessed)]
	[DataRow(Key.LineFeed)]
	[DataRow(Key.OemClear)]
	public void ToKeyCode_UnbindableKeys_ReturnNull(Key key)
		=> Assert.IsNull(KeyCodeMapper.ToKeyCode(key));

	[TestMethod]
	public void ToAvaloniaKey_EveryDefinedKeyCode_MapsToDefinedAvaloniaKeyAndRoundTrips()
	{
		foreach (KeyCode keyCode in Enum.GetValues<KeyCode>())
		{
			Key key = KeyCodeMapper.ToAvaloniaKey(keyCode);

			Assert.IsTrue(Enum.IsDefined(key), $"KeyCode.{keyCode} does not map to a defined Avalonia key.");
			Assert.AreEqual(keyCode, KeyCodeMapper.ToKeyCode(key), $"KeyCode.{keyCode} does not round-trip.");
		}
	}

	[TestMethod]
	public void ToAvaloniaKey_UndefinedKeyCode_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToAvaloniaKey(default));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToAvaloniaKey((KeyCode)143));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToAvaloniaKey((KeyCode)200));
	}

	[TestMethod]
	[DataRow(KeyModifierSet.None, AvaloniaKeyModifiers.None)]
	[DataRow(KeyModifierSet.Control, AvaloniaKeyModifiers.Control)]
	[DataRow(KeyModifierSet.Alt | KeyModifierSet.Shift | KeyModifierSet.Meta, AvaloniaKeyModifiers.Alt | AvaloniaKeyModifiers.Shift | AvaloniaKeyModifiers.Meta)]
	public void ToAvaloniaKeyModifiers_ReturnsAvaloniaFlags(KeyModifierSet modifiers, AvaloniaKeyModifiers expected)
		=> Assert.AreEqual(expected, KeyCodeMapper.ToAvaloniaKeyModifiers(modifiers));

	[TestMethod]
	public void ToAvaloniaKeyModifiers_UndefinedBits_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToAvaloniaKeyModifiers((KeyModifierSet)16));

	[TestMethod]
	[DataRow(AvaloniaKeyModifiers.None, KeyModifierSet.None)]
	[DataRow(AvaloniaKeyModifiers.Control, KeyModifierSet.Control)]
	[DataRow(AvaloniaKeyModifiers.Alt | AvaloniaKeyModifiers.Shift | AvaloniaKeyModifiers.Meta, KeyModifierSet.Alt | KeyModifierSet.Shift | KeyModifierSet.Meta)]
	public void ToKeyModifierSet_ReturnsNeutralFlags(AvaloniaKeyModifiers modifiers, KeyModifierSet expected)
		=> Assert.AreEqual(expected, KeyCodeMapper.ToKeyModifierSet(modifiers));

	[TestMethod]
	public void ToKeyModifierSet_UndefinedBits_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToKeyModifierSet((AvaloniaKeyModifiers)16));
}
