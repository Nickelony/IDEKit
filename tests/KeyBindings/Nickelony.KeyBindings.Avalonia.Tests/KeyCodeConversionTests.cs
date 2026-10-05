using Avalonia.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.KeyBindings.Avalonia.Tests;

[TestClass]
public sealed class KeyCodeConversionTests
{
	private const string ModifierReason = "A modifier key: a stroke carries it as a modifier and never as its primary key.";
	private const string LockReason = "A lock key: it toggles a keyboard state, and the package's key model has no member for it.";

	private const string ProcessingReason =
		"A processing pseudo-key: the toolkit reports it in place of a key that Alt, an IME or a dead key consumed.";

	private const string ImeReason = "A double-byte IME key: it drives an IME conversion state, and the package's key model has no member for it.";
	private const string LegacyReason = "A legacy key that current keyboards do not have, and the package's key model has no member for it.";
	private const string FunctionLayerReason = "An Fn-layer key of a compact keyboard, and the package's key model has no member for it.";

	private const string MediaCenterReason =
		"A media-center or TV-remote key that desktop keyboards do not have, and the package's key model has no member for it.";

	// Every Avalonia key that converts to no key code, with the reason. A key of the toolkit enum that is neither here nor in
	// KeyCodeConversionTable fails ToKeyCode_EveryDefinedKey_IsMappedOrListedAsUnbindable, so a new value forces a decision.
	// The enum has alias members that share a value (Return and Enter, DbeAlphanumeric and OemAttn); one entry covers them all.
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
		(Key.CapsLock, LockReason),
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
		(Key.FnLeftArrow, FunctionLayerReason),
		(Key.FnRightArrow, FunctionLayerReason),
		(Key.FnUpArrow, FunctionLayerReason),
		(Key.FnDownArrow, FunctionLayerReason),
		(Key.MediaHome, MediaCenterReason),
		(Key.MediaChannelList, MediaCenterReason),
		(Key.MediaChannelRaise, MediaCenterReason),
		(Key.MediaChannelLower, MediaCenterReason),
		(Key.MediaPreviousChannel, MediaCenterReason),
		(Key.MediaRecord, MediaCenterReason),
		(Key.MediaRed, MediaCenterReason),
		(Key.MediaGreen, MediaCenterReason),
		(Key.MediaYellow, MediaCenterReason),
		(Key.MediaBlue, MediaCenterReason),
		(Key.MediaMenu, MediaCenterReason),
		(Key.MediaMore, MediaCenterReason),
		(Key.MediaOption, MediaCenterReason),
		(Key.MediaInfo, MediaCenterReason),
		(Key.MediaSearch, MediaCenterReason),
		(Key.MediaSubtitle, MediaCenterReason),
		(Key.MediaTvGuide, MediaCenterReason),
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
	[DataRow(Key.PrintScreen, KeyCode.PrintScreen)]
	[DataRow(Key.PageDown, KeyCode.PageDown)]
	[DataRow(Key.PageUp, KeyCode.PageUp)]
	[DataRow(Key.OemSemicolon, KeyCode.Semicolon)]
	[DataRow(Key.OemBackslash, KeyCode.IntlBackslash)]
	[DataRow(Key.KanaMode, KeyCode.KanaMode)]
	[DataRow(Key.HanjaMode, KeyCode.HanjaMode)]
	public void ToKeyCode_KnownKeys_ReturnsExpectedCode(Key key, KeyCode expected) => Assert.AreEqual(expected, KeyCodeConversion.ToKeyCode(key));

	[TestMethod]
	public void ToKeyCode_AvaloniaAliasMembers_ResolveToTheCanonicalCode()
	{
		// Avalonia declares alias members that share a value with the canonical name; the table is keyed
		// by the shared value, so each alias resolves to the same KeyCode.
		Assert.AreEqual(KeyCode.Enter, KeyCodeConversion.ToKeyCode(Key.Return));
		Assert.AreEqual(KeyCode.PrintScreen, KeyCodeConversion.ToKeyCode(Key.Snapshot));
		Assert.AreEqual(KeyCode.PageDown, KeyCodeConversion.ToKeyCode(Key.Next));
		Assert.AreEqual(KeyCode.PageUp, KeyCodeConversion.ToKeyCode(Key.Prior));
		Assert.AreEqual(KeyCode.Semicolon, KeyCodeConversion.ToKeyCode(Key.Oem1));
		Assert.AreEqual(KeyCode.IntlBackslash, KeyCodeConversion.ToKeyCode(Key.Oem102));
		Assert.AreEqual(KeyCode.KanaMode, KeyCodeConversion.ToKeyCode(Key.HangulMode));
		Assert.AreEqual(KeyCode.HanjaMode, KeyCodeConversion.ToKeyCode(Key.KanjiMode));
	}

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
	public void ToKeyCode_EveryDefinedKeyCode_IsReachableFromSomeAvaloniaKey()
	{
		HashSet<KeyCode> reached = [.. Enum.GetValues<Key>().Select(KeyCodeConversion.ToKeyCode).OfType<KeyCode>()];

		foreach (KeyCode keyCode in Enum.GetValues<KeyCode>())
			Assert.Contains(keyCode, reached, $"KeyCode.{keyCode} has no Avalonia key that converts to it.");
	}

	[TestMethod]
	[DataRow(AvaloniaKeyModifiers.None, KeyModifiers.None)]
	[DataRow(AvaloniaKeyModifiers.Control, KeyModifiers.Control)]
	[DataRow(
		AvaloniaKeyModifiers.Alt | AvaloniaKeyModifiers.Shift | AvaloniaKeyModifiers.Meta,
		KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta
	)]
	public void ToKeyModifiers_ReturnsNeutralFlags(AvaloniaKeyModifiers modifiers, KeyModifiers expected) =>
		Assert.AreEqual(expected, KeyCodeConversion.ToKeyModifiers(modifiers));

	[TestMethod]
	public void ToKeyModifiers_UndefinedBits_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => KeyCodeConversion.ToKeyModifiers((AvaloniaKeyModifiers)16));

	[TestMethod]
	public void ToKeyModifiers_EveryDefinedCombination_MapsEachFlagToItsNeutralFlag()
	{
		for (int mask = 0; mask < 16; mask++)
		{
			var modifiers = (AvaloniaKeyModifiers)mask;
			var converted = KeyCodeConversion.ToKeyModifiers(modifiers);

			string combination = $"The 0b{Convert.ToString(mask, 2).PadLeft(4, '0')} combination";

			Assert.AreEqual(modifiers.HasFlag(AvaloniaKeyModifiers.Alt), converted.HasFlag(KeyModifiers.Alt), $"{combination} does not map Alt.");

			Assert.AreEqual(
				modifiers.HasFlag(AvaloniaKeyModifiers.Control),
				converted.HasFlag(KeyModifiers.Control),
				$"{combination} does not map Control."
			);

			Assert.AreEqual(
				modifiers.HasFlag(AvaloniaKeyModifiers.Shift),
				converted.HasFlag(KeyModifiers.Shift),
				$"{combination} does not map Shift."
			);

			Assert.AreEqual(modifiers.HasFlag(AvaloniaKeyModifiers.Meta), converted.HasFlag(KeyModifiers.Meta), $"{combination} does not map Meta.");
		}
	}
}
