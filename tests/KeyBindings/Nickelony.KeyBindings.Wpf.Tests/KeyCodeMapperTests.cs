using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf.Tests;

/// <remarks>
/// <c>[TestClass]</c> is deliberate for this suite: it maps values without touching WPF's event pipeline,
/// so it needs no STA thread. The suites that construct real <c>KeyEventArgs</c> instances carry
/// <c>[STATestClass]</c> instead.
/// </remarks>
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
	[DataRow(Key.Snapshot, KeyCode.PrintScreen)]
	[DataRow(Key.Next, KeyCode.PageDown)]
	[DataRow(Key.Prior, KeyCode.PageUp)]
	[DataRow(Key.Oem1, KeyCode.Semicolon)]
	[DataRow(Key.Oem102, KeyCode.IntlBackslash)]
	public void ToKeyCode_KnownKeys_ReturnsExpectedCode(Key key, KeyCode expected)
		=> Assert.AreEqual(expected, KeyCodeMapper.ToKeyCode(key));

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
	[DataRow(Key.Capital)]
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
	public void ToWpfKey_EveryDefinedKeyCode_MapsToDefinedWpfKeyAndRoundTrips()
	{
		foreach (KeyCode keyCode in Enum.GetValues<KeyCode>())
		{
			Key key = KeyCodeMapper.ToWpfKey(keyCode);

			Assert.IsTrue(Enum.IsDefined(key), $"KeyCode.{keyCode} does not map to a defined WPF key.");
			Assert.AreEqual(keyCode, KeyCodeMapper.ToKeyCode(key), $"KeyCode.{keyCode} does not round-trip.");
		}
	}

	[TestMethod]
	public void ToWpfKey_UndefinedKeyCode_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToWpfKey(default));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToWpfKey((KeyCode)143));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToWpfKey((KeyCode)200));
	}

	[TestMethod]
	[DataRow(KeyModifierSet.None, ModifierKeys.None)]
	[DataRow(KeyModifierSet.Control, ModifierKeys.Control)]
	[DataRow(KeyModifierSet.Alt | KeyModifierSet.Shift | KeyModifierSet.Meta, ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)]
	public void ToWpfModifierKeys_ReturnsWpfFlags(KeyModifierSet modifiers, ModifierKeys expected)
		=> Assert.AreEqual(expected, KeyCodeMapper.ToWpfModifierKeys(modifiers));

	[TestMethod]
	public void ToWpfModifierKeys_UndefinedBits_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToWpfModifierKeys((KeyModifierSet)16));

	[TestMethod]
	[DataRow(ModifierKeys.None, KeyModifierSet.None)]
	[DataRow(ModifierKeys.Control, KeyModifierSet.Control)]
	[DataRow(ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows, KeyModifierSet.Alt | KeyModifierSet.Shift | KeyModifierSet.Meta)]
	public void ToKeyModifierSet_ReturnsNeutralFlags(ModifierKeys modifiers, KeyModifierSet expected)
		=> Assert.AreEqual(expected, KeyCodeMapper.ToKeyModifierSet(modifiers));

	[TestMethod]
	public void ToKeyModifierSet_UndefinedBits_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeMapper.ToKeyModifierSet((ModifierKeys)16));
}
