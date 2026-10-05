using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf.Tests;

[STATestClass]
public sealed class KeyEventArgsExtensionsTests
{
	[TestMethod]
	public void ToKeyStroke_LeftHandModifiers_ReturnsStroke()
	{
		var stroke = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl, Key.LeftShift).ToKeyStroke();

		Assert.IsNotNull(stroke);
		Assert.AreEqual(KeyCode.S, stroke.Value.Key);
		Assert.AreEqual(KeyModifiers.Control | KeyModifiers.Shift, stroke.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyStroke_RightHandAndOsModifiers_AreRecognized()
	{
		var stroke = TestKeyEvents.CreateKeyDown(Key.F9, Key.RightCtrl, Key.RightShift, Key.RightAlt, Key.RWin).ToKeyStroke();

		Assert.IsNotNull(stroke);
		Assert.AreEqual(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta, stroke.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyStroke_WithoutModifiers_ReturnsStroke()
	{
		var stroke = TestKeyEvents.CreateKeyDown(Key.F9).ToKeyStroke();

		Assert.IsNotNull(stroke);
		Assert.AreEqual(KeyCode.F9, stroke.Value.Key);
		Assert.AreEqual(KeyModifiers.None, stroke.Value.Modifiers);
	}

	[TestMethod]
	public void ToKeyStroke_ModifierOnlyKey_ReturnsNull() => Assert.IsNull(TestKeyEvents.CreateKeyDown(Key.LeftCtrl).ToKeyStroke());

	[TestMethod]
	public void ToKeyStroke_ImeProcessedKey_ReturnsNull()
	{
		// WPF's KeyEventArgs constructor rejects Key.DeadCharProcessed, so only the IME pseudo-key is
		// simulatable through the public API.
		Assert.IsNull(TestKeyEvents.CreateKeyDown(Key.ImeProcessed).ToKeyStroke());
	}

	[TestMethod]
	public void ToKeyStroke_SystemKeyEvent_NormalizesToTheUnderlyingKey()
	{
		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.Q);

		WpfInternalsTestHelpers.MarkSystem(args);

		var stroke = args.ToKeyStroke();

		Assert.IsNotNull(stroke);
		Assert.AreEqual(KeyCode.Q, stroke.Value.Key);
	}

	[TestMethod]
	public void ToKeyStroke_NullEvent_Throws()
	{
		KeyEventArgs? args = null;
		Assert.ThrowsExactly<ArgumentNullException>(() => args!.ToKeyStroke());
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
	public void IsAltGr_NonCharacterKeyUnderLeftCtrlAndRightAlt_ReturnsFalse()
	{
		// A Ctrl+RightAlt chord on a key that types no character is a genuine binding, not AltGr, so the
		// default filter must leave it for the dispatcher.
		Assert.IsFalse(TestKeyEvents.CreateKeyDown(Key.F5, Key.LeftCtrl, Key.RightAlt).IsAltGr());
		Assert.IsFalse(TestKeyEvents.CreateKeyDown(Key.Left, Key.LeftCtrl, Key.RightAlt).IsAltGr());
	}

	[TestMethod]
	public void IsAltGr_NullEvent_Throws()
	{
		KeyEventArgs? args = null;
		Assert.ThrowsExactly<ArgumentNullException>(() => args!.IsAltGr());
	}
}
