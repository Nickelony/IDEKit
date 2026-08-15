namespace Nickelony.KeyBindings.Tests;

[TestClass]
public class KeyComboTests
{
	[TestMethod]
	public void Constructor_DefinedKeyAndModifiers_StoresValues()
	{
		var combo = new KeyCombo(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift);

		Assert.AreEqual(KeyCode.S, combo.Key);
		Assert.AreEqual(KeyModifierSet.Control | KeyModifierSet.Shift, combo.Modifiers);
		Assert.IsTrue(combo.IsInitialized);
	}

	[TestMethod]
	public void Constructor_UndefinedKey_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new KeyCombo(default, KeyModifierSet.None));

	[TestMethod]
	public void Constructor_UndefinedModifierBits_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new KeyCombo(KeyCode.S, (KeyModifierSet)16));

	/// <summary>
	/// Pins the accepted modifier mask from both sides: every defined flag is accepted together, while a
	/// negative value, an out-of-int-range value, or a bit above the highest defined flag is rejected.
	/// </summary>
	[TestMethod]
	[DataRow(-1)]
	[DataRow(int.MinValue)]
	[DataRow(int.MaxValue)]
	[DataRow(64)]
	public void Constructor_OutOfRangeModifierValue_Throws(int rawModifiers)
		=> Assert.ThrowsExactly<ArgumentException>(() => new KeyCombo(KeyCode.S, (KeyModifierSet)rawModifiers));

	[TestMethod]
	public void Constructor_EveryDefinedModifierFlagIsAccepted()
	{
		KeyModifierSet all = KeyModifierSet.Alt | KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Meta;

		var combo = new KeyCombo(KeyCode.S, all);

		Assert.AreEqual(all, combo.Modifiers);
		Assert.IsTrue(combo.IsInitialized);
	}

	[TestMethod]
	public void DefaultValue_IsNotInitialized()
	{
		KeyCombo combo = default;

		Assert.IsFalse(combo.IsInitialized);
	}

	[TestMethod]
	public void Equality_ComparesKeyAndModifiers()
	{
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control));
		Assert.AreNotEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.None));
		Assert.AreNotEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), new KeyCombo(KeyCode.T, KeyModifierSet.Control));
	}

	[TestMethod]
	public void GetHashCode_EqualCombos_AreInterchangeableInHashSet()
	{
		var combos = new HashSet<KeyCombo>
		{
			new(KeyCode.S, KeyModifierSet.Control),
			new(KeyCode.S, KeyModifierSet.Control),
			new(KeyCode.S, KeyModifierSet.None)
		};

		Assert.HasCount(2, combos);
	}

	/// <summary>
	/// Pins that the modifier complement order is irrelevant, because the flags are a bit set: two combos
	/// built from the same flags in different order are interchangeable as dictionary and set keys.
	/// </summary>
	[TestMethod]
	public void ModifierFlagOrder_DoesNotChangeEqualityOrHash()
	{
		var forward = new KeyCombo(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Alt | KeyModifierSet.Meta);
		var shuffled = new KeyCombo(KeyCode.S, KeyModifierSet.Meta | KeyModifierSet.Alt | KeyModifierSet.Shift | KeyModifierSet.Control);

		Assert.AreEqual(forward, shuffled);
		Assert.AreEqual(forward.GetHashCode(), shuffled.GetHashCode());
		Assert.HasCount(1, new HashSet<KeyCombo> { forward, shuffled });
	}

	/// <summary>
	/// Pins that the unset value is only equal to itself. A constructed combo always names a defined key,
	/// so it can never collide with <see langword="default"/>.
	/// </summary>
	[TestMethod]
	public void DefaultValue_IsNotEqualToAnyConstructedCombo()
	{
		KeyCombo unset = default;

		Assert.AreEqual(default(KeyCombo), unset);
		Assert.AreNotEqual(new KeyCombo(KeyCode.S, KeyModifierSet.None), unset);
		Assert.IsFalse(unset.Equals(new KeyCombo(KeyCode.S, KeyModifierSet.None)));
		Assert.HasCount(2, new HashSet<KeyCombo> { default, new KeyCombo(KeyCode.S, KeyModifierSet.None) });
	}

	// ---- Typed-text parse ----

	[TestMethod]
	[DataRow("S", KeyCode.S, KeyModifierSet.None)]
	[DataRow("s", KeyCode.S, KeyModifierSet.None)]
	[DataRow("Ctrl+S", KeyCode.S, KeyModifierSet.Control)]
	[DataRow("ctrl+s", KeyCode.S, KeyModifierSet.Control)]
	[DataRow("Control+K", KeyCode.K, KeyModifierSet.Control)]
	[DataRow("Control + Shift + S", KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift)]
	[DataRow("  CTRL+K  ", KeyCode.K, KeyModifierSet.Control)]
	[DataRow("Win+S", KeyCode.S, KeyModifierSet.Meta)]
	[DataRow("cmd+S", KeyCode.S, KeyModifierSet.Meta)]
	[DataRow("command+S", KeyCode.S, KeyModifierSet.Meta)]
	[DataRow("super+S", KeyCode.S, KeyModifierSet.Meta)]
	[DataRow("windows+S", KeyCode.S, KeyModifierSet.Meta)]
	[DataRow("Alt+F4", KeyCode.F4, KeyModifierSet.Alt)]
	[DataRow("alt+F4", KeyCode.F4, KeyModifierSet.Alt)]
	[DataRow("Meta+S", KeyCode.S, KeyModifierSet.Meta)]
	[DataRow("meta+S", KeyCode.S, KeyModifierSet.Meta)]
	[DataRow("control+K", KeyCode.K, KeyModifierSet.Control)]
	[DataRow("5", KeyCode.D5, KeyModifierSet.None)]
	[DataRow("D5", KeyCode.D5, KeyModifierSet.None)]
	[DataRow("Ctrl+5", KeyCode.D5, KeyModifierSet.Control)]
	[DataRow("Escape", KeyCode.Escape, KeyModifierSet.None)]
	[DataRow("Ctrl+Ctrl+S", KeyCode.S, KeyModifierSet.Control)]
	public void TryParse_ValidStrokeText_ReturnsTheStroke(string text, KeyCode expectedKey, KeyModifierSet expectedModifiers)
	{
		Assert.IsTrue(KeyCombo.TryParse(text, out KeyCombo combo), $"'{text}' does not parse.");
		Assert.AreEqual(expectedKey, combo.Key);
		Assert.AreEqual(expectedModifiers, combo.Modifiers);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow("Ctrl")]
	[DataRow("Ctrl+")]
	[DataRow("+K")]
	[DataRow("Ctrl++K")]
	[DataRow("K+Ctrl")]
	[DataRow("Ctrl+K+S")]
	[DataRow("NotAKey")]
	[DataRow("62")]
	[DataRow("Ctrl+62")]
	[DataRow("Ctrl+K Ctrl+S")]
	public void TryParse_InvalidStrokeText_ReturnsFalseAndDefault(string? text)
	{
		Assert.IsFalse(KeyCombo.TryParse(text, out KeyCombo combo), $"'{text}' must not parse.");
		Assert.AreEqual(default(KeyCombo), combo);
	}
}
