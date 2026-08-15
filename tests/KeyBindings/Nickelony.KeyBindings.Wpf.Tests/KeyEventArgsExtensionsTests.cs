using System.Reflection;
using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf.Tests;

[STATestClass]
public class KeyEventArgsExtensionsTests
{
	[TestMethod]
	public void ToKeyCombo_LeftHandModifiers_ReturnsCombo()
	{
		KeyCombo? combo = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl, Key.LeftShift).ToKeyCombo();

		Assert.IsNotNull(combo);
		Assert.AreEqual(KeyCode.S, combo.Value.Key);
		Assert.AreEqual(KeyModifierSet.Control | KeyModifierSet.Shift, combo.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyCombo_RightHandAndOsModifiers_AreRecognized()
	{
		KeyCombo? combo = TestKeyEvents.CreateKeyDown(Key.F9, Key.RightCtrl, Key.RightShift, Key.RightAlt, Key.RWin).ToKeyCombo();

		Assert.IsNotNull(combo);
		Assert.AreEqual(KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Alt | KeyModifierSet.Meta, combo.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyCombo_WithoutModifiers_ReturnsCombo()
	{
		KeyCombo? combo = TestKeyEvents.CreateKeyDown(Key.F9).ToKeyCombo();

		Assert.IsNotNull(combo);
		Assert.AreEqual(KeyCode.F9, combo.Value.Key);
		Assert.AreEqual(KeyModifierSet.None, combo.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyCombo_ModifierOnlyKey_ReturnsNull()
		=> Assert.IsNull(TestKeyEvents.CreateKeyDown(Key.LeftCtrl).ToKeyCombo());

	[TestMethod]
	public void ToKeyCombo_ImeProcessedKey_ReturnsNull()
	{
		// WPF's KeyEventArgs constructor rejects Key.DeadCharProcessed, so only the IME pseudo-key is
		// simulatable through the public API.
		Assert.IsNull(TestKeyEvents.CreateKeyDown(Key.ImeProcessed).ToKeyCombo());
	}

	[TestMethod]
	public void ToKeyCombo_SystemKeyEvent_NormalizesToTheUnderlyingKey()
	{
		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.Q);

		MethodInfo? markSystem = typeof(KeyEventArgs).GetMethod("MarkSystem", BindingFlags.Instance | BindingFlags.NonPublic);

		Assert.IsNotNull(markSystem, "WPF's KeyEventArgs.MarkSystem internals changed.");
		markSystem.Invoke(args, null);

		KeyCombo? combo = args.ToKeyCombo();

		Assert.IsNotNull(combo);
		Assert.AreEqual(KeyCode.Q, combo.Value.Key);
	}

	[TestMethod]
	public void ToKeyCombo_NullEvent_Throws()
	{
		KeyEventArgs? args = null;

		Assert.ThrowsExactly<ArgumentNullException>(() => args!.ToKeyCombo());
	}

	[TestMethod]
	public void IsAltGr_LeftCtrlWithRightAlt_ReturnsTrue()
	{
		// Windows synthesizes AltGr as a left Ctrl press followed by the right Alt key.
		Assert.IsTrue(TestKeyEvents.CreateKeyDown(Key.Q, Key.LeftCtrl, Key.RightAlt).IsAltGr());
	}

	[TestMethod]
	public void IsAltGr_OtherCtrlAndAltPairs_ReturnFalse()
	{
		// A genuine Ctrl+Alt chord presses the left Alt key or the right Ctrl key, and neither modifier alone
		// is an AltGr stroke.
		Assert.IsFalse(TestKeyEvents.CreateKeyDown(Key.Q, Key.LeftCtrl, Key.LeftAlt).IsAltGr());
		Assert.IsFalse(TestKeyEvents.CreateKeyDown(Key.Q, Key.RightCtrl, Key.RightAlt).IsAltGr());
		Assert.IsFalse(TestKeyEvents.CreateKeyDown(Key.Q, Key.RightAlt).IsAltGr());
		Assert.IsFalse(TestKeyEvents.CreateKeyDown(Key.Q).IsAltGr());
	}

	[TestMethod]
	public void IsAltGr_NullEvent_Throws()
	{
		KeyEventArgs? args = null;

		Assert.ThrowsExactly<ArgumentNullException>(() => args!.IsAltGr());
	}

}
