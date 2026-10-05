using System.Windows.Input;

namespace Nickelony.KeyBindings.Wpf.Tests;

/// <summary>Pins the key and modifier conversions of the WPF <see cref="KeyCodeConversion"/> and their round-trips.</summary>
/// <remarks>
/// <c>[TestClass]</c> is deliberate: the suite maps values without touching WPF's event pipeline, so it needs no STA thread.
/// </remarks>
[TestClass]
public sealed class KeyCodeConversionTests
{
	private const string ModifierReason = "A modifier key: a stroke carries it as a modifier and never as its primary key.";
	private const string LockReason = "A lock key: it toggles a keyboard state, and the package's key model has no member for it.";

	private const string ProcessingReason =
		"A processing pseudo-key: the toolkit reports it in place of a key that Alt, an IME or a dead key consumed.";

	private const string ImeReason = "A double-byte IME key: it drives an IME conversion state, and the package's key model has no member for it.";
	private const string LegacyReason = "A legacy key that current keyboards do not have, and the package's key model has no member for it.";

	// Every WPF key that converts to no key code, with the reason. A key of the toolkit enum that is neither here nor in
	// KeyCodeConversionTable fails ToKeyCode_EveryDefinedKey_IsMappedOrListedAsUnbindable, so a new value forces a decision.
	// The enum has alias members that share a value (Capital and CapsLock, DbeAlphanumeric and OemAttn); one entry covers them all.
	private static readonly (Key Key, string Reason)[] s_unbindableKeys =
	[
		(Key.None, "No key: the toolkit reports it when no key is involved."),
		(Key.LeftCtrl, ModifierReason),
		(Key.RightCtrl, ModifierReason),
		(Key.LeftShift, ModifierReason),
		(Key.RightShift, ModifierReason),
		(Key.LeftAlt, ModifierReason),
		(Key.RightAlt, ModifierReason),
		(Key.LWin, ModifierReason),
		(Key.RWin, ModifierReason),
		(Key.Capital, LockReason),
		(Key.NumLock, LockReason),
		(Key.Scroll, LockReason),
		(Key.System, ProcessingReason),
		(Key.ImeProcessed, ProcessingReason),
		(Key.DeadCharProcessed, ProcessingReason),
		(Key.LineFeed, LegacyReason),
		(Key.OemClear, LegacyReason),
		(Key.DbeAlphanumeric, ImeReason),
		(Key.DbeKatakana, ImeReason),
		(Key.DbeHiragana, ImeReason),
		(Key.DbeSbcsChar, ImeReason),
		(Key.DbeDbcsChar, ImeReason),
		(Key.DbeRoman, ImeReason),
		(Key.DbeNoRoman, ImeReason),
		(Key.DbeEnterWordRegisterMode, ImeReason),
		(Key.DbeEnterImeConfigureMode, ImeReason),
		(Key.DbeFlushString, ImeReason),
		(Key.DbeCodeInput, ImeReason),
		(Key.DbeNoCodeInput, ImeReason),
		(Key.DbeDetermineString, ImeReason),
		(Key.DbeEnterDialogConversionMode, ImeReason),
	];

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
	public void ToKeyCode_KnownKeys_ReturnsExpectedCode(Key key, KeyCode expected) => Assert.AreEqual(expected, KeyCodeConversion.ToKeyCode(key));

	[TestMethod]
	public void ToKeyCode_UnbindableKeys_ReturnNull()
	{
		foreach ((Key key, string reason) in s_unbindableKeys)
			Assert.IsNull(KeyCodeConversion.ToKeyCode(key), $"Key.{key} is listed as unbindable ({reason}) but converts to a key code.");
	}

	[TestMethod]
	public void ToKeyCode_UnbindableKeys_ListEachKeyOnceWithItsReason()
	{
		Assert.AreEqual(s_unbindableKeys.Length, s_unbindableKeys.Select(entry => entry.Key).Distinct().Count(), "A key is listed twice.");
		Assert.IsFalse(s_unbindableKeys.Any(entry => string.IsNullOrWhiteSpace(entry.Reason)), "A listed key has no reason.");
	}

	[TestMethod]
	public void ToKeyCode_EveryDefinedKey_IsMappedOrListedAsUnbindable()
	{
		HashSet<Key> listed = [.. s_unbindableKeys.Select(entry => entry.Key)];

		string[] undecided =
		[
			.. Enum.GetValues<Key>().Distinct().Where(key => KeyCodeConversion.ToKeyCode(key) is null && !listed.Contains(key)).Select(NamesOf),
		];

		Assert.IsEmpty(
			undecided,
			$"{string.Join(", ", undecided)} converts to no key code and is not listed as unbindable. Map it in KeyCodeConversionTable, or list it in s_unbindableKeys with the reason it cannot be the primary key of a binding."
		);
	}

	// Names every member that shares the value, because the alias members of the enum give one value several names.
	private static string NamesOf(Key key) => "Key." + string.Join('/', Enum.GetNames<Key>().Where(name => Enum.Parse<Key>(name) == key));

	[TestMethod]
	public void ToWpfKey_EveryDefinedKeyCode_MapsToDefinedWpfKeyAndRoundTrips()
	{
		foreach (KeyCode keyCode in Enum.GetValues<KeyCode>())
		{
			Key key = KeyCodeConversion.ToWpfKey(keyCode);

			Assert.IsTrue(Enum.IsDefined(key), $"KeyCode.{keyCode} does not map to a defined WPF key.");
			Assert.AreEqual(keyCode, KeyCodeConversion.ToKeyCode(key), $"KeyCode.{keyCode} does not round-trip.");
		}
	}

	[TestMethod]
	public void ToWpfKey_UndefinedKeyCode_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeConversion.ToWpfKey(default));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeConversion.ToWpfKey((KeyCode)143));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeConversion.ToWpfKey((KeyCode)200));
	}

	[TestMethod]
	[DataRow(KeyModifiers.None, ModifierKeys.None)]
	[DataRow(KeyModifiers.Control, ModifierKeys.Control)]
	[DataRow(KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta, ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)]
	public void ToWpfModifierKeys_ReturnsWpfFlags(KeyModifiers modifiers, ModifierKeys expected) =>
		Assert.AreEqual(expected, KeyCodeConversion.ToWpfModifierKeys(modifiers));

	[TestMethod]
	public void ToWpfModifierKeys_UndefinedBits_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeConversion.ToWpfModifierKeys((KeyModifiers)16));

	[TestMethod]
	[DataRow(ModifierKeys.None, KeyModifiers.None)]
	[DataRow(ModifierKeys.Control, KeyModifiers.Control)]
	[DataRow(ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows, KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta)]
	public void ToKeyModifiers_ReturnsNeutralFlags(ModifierKeys modifiers, KeyModifiers expected) =>
		Assert.AreEqual(expected, KeyCodeConversion.ToKeyModifiers(modifiers));

	[TestMethod]
	public void ToKeyModifiers_UndefinedBits_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeConversion.ToKeyModifiers((ModifierKeys)16));

	[TestMethod]
	public void ModifierFlags_EveryDefinedCombination_RoundTrips()
	{
		for (int mask = 0; mask < 16; mask++)
		{
			var modifiers = (KeyModifiers)mask;

			Assert.AreEqual(
				modifiers,
				KeyCodeConversion.ToKeyModifiers(KeyCodeConversion.ToWpfModifierKeys(modifiers)),
				$"The 0b{Convert.ToString(mask, 2).PadLeft(4, '0')} combination does not round-trip."
			);
		}
	}
}
