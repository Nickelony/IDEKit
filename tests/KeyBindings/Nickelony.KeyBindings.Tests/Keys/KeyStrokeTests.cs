namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class KeyStrokeTests
{
	[TestMethod]
	public void Constructor_DefinedKeyAndModifiers_StoresValues()
	{
		var stroke = new KeyStroke(KeyCode.S, KeyModifiers.Control | KeyModifiers.Shift);

		Assert.AreEqual(KeyCode.S, stroke.Key);
		Assert.AreEqual(KeyModifiers.Control | KeyModifiers.Shift, stroke.Modifiers);
		Assert.IsTrue(stroke.IsInitialized);
	}

	[TestMethod]
	public void Constructor_UndefinedKey_Throws() => Assert.ThrowsExactly<ArgumentException>(() => new KeyStroke(default, KeyModifiers.None));

	[TestMethod]
	public void Constructor_UndefinedModifierBits_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => new KeyStroke(KeyCode.S, (KeyModifiers)16));

	/// <summary>
	/// Pins that a modifier value outside the defined flags - a negative value, an out-of-int-range value, or a bit above the highest defined flag -
	/// is rejected.
	/// </summary>
	[TestMethod]
	[DataRow(-1)]
	[DataRow(int.MinValue)]
	[DataRow(int.MaxValue)]
	[DataRow(64)]
	public void Constructor_OutOfRangeModifierValue_Throws(int rawModifiers) =>
		Assert.ThrowsExactly<ArgumentException>(() => new KeyStroke(KeyCode.S, (KeyModifiers)rawModifiers));

	[TestMethod]
	public void Constructor_EveryDefinedModifierFlagIsAccepted()
	{
		KeyModifiers all = KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta;

		var stroke = new KeyStroke(KeyCode.S, all);

		Assert.AreEqual(all, stroke.Modifiers);
		Assert.IsTrue(stroke.IsInitialized);
	}

	[TestMethod]
	public void DefaultValue_IsNotInitialized()
	{
		KeyStroke stroke = default;
		Assert.IsFalse(stroke.IsInitialized);
	}

	[TestMethod]
	public void Equality_ComparesKeyAndModifiers()
	{
		Assert.AreEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control));
		Assert.AreNotEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.None));
		Assert.AreNotEqual(new KeyStroke(KeyCode.S, KeyModifiers.Control), new KeyStroke(KeyCode.T, KeyModifiers.Control));
	}

	[TestMethod]
	public void GetHashCode_EqualStrokes_AreInterchangeableInHashSet()
	{
		var strokes = new HashSet<KeyStroke>
		{
			new(KeyCode.S, KeyModifiers.Control),
			new(KeyCode.S, KeyModifiers.Control),
			new(KeyCode.S, KeyModifiers.None),
		};

		Assert.HasCount(2, strokes);
	}

	/// <summary>
	/// Pins that the modifier flag order is irrelevant, because the flags are a bit set: two strokes built from the same flags in different order
	/// are interchangeable as dictionary and set keys.
	/// </summary>
	[TestMethod]
	public void ModifierFlagOrder_DoesNotChangeEqualityOrHash()
	{
		var forward = new KeyStroke(KeyCode.S, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta);
		var shuffled = new KeyStroke(KeyCode.S, KeyModifiers.Meta | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Control);

		Assert.AreEqual(forward, shuffled);
		Assert.AreEqual(forward.GetHashCode(), shuffled.GetHashCode());
		Assert.HasCount(1, new HashSet<KeyStroke> { forward, shuffled });
	}

	/// <summary>Pins that the unset value is only equal to itself.</summary>
	/// <remarks>
	/// A constructed stroke always names a defined key, so it can never collide with <see langword="default"/>.
	/// </remarks>
	[TestMethod]
	public void DefaultValue_IsNotEqualToAnyConstructedStroke()
	{
		KeyStroke unset = default;

		Assert.AreEqual(default, unset);
		Assert.AreNotEqual(new KeyStroke(KeyCode.S, KeyModifiers.None), unset);
		Assert.IsFalse(unset.Equals(new KeyStroke(KeyCode.S, KeyModifiers.None)));
		Assert.HasCount(2, new HashSet<KeyStroke> { default, new(KeyCode.S, KeyModifiers.None) });
	}

	// ---- Typed-text parse ----

	[TestMethod]
	[DataRow("S", KeyCode.S, KeyModifiers.None)]
	[DataRow("s", KeyCode.S, KeyModifiers.None)]
	[DataRow("Ctrl+S", KeyCode.S, KeyModifiers.Control)]
	[DataRow("ctrl+s", KeyCode.S, KeyModifiers.Control)]
	[DataRow("Control+K", KeyCode.K, KeyModifiers.Control)]
	[DataRow("Control + Shift + S", KeyCode.S, KeyModifiers.Control | KeyModifiers.Shift)]
	[DataRow("  CTRL+K  ", KeyCode.K, KeyModifiers.Control)]
	[DataRow("Win+S", KeyCode.S, KeyModifiers.Meta)]
	[DataRow("cmd+S", KeyCode.S, KeyModifiers.Meta)]
	[DataRow("command+S", KeyCode.S, KeyModifiers.Meta)]
	[DataRow("super+S", KeyCode.S, KeyModifiers.Meta)]
	[DataRow("windows+S", KeyCode.S, KeyModifiers.Meta)]
	[DataRow("Alt+F4", KeyCode.F4, KeyModifiers.Alt)]
	[DataRow("alt+F4", KeyCode.F4, KeyModifiers.Alt)]
	[DataRow("Meta+S", KeyCode.S, KeyModifiers.Meta)]
	[DataRow("meta+S", KeyCode.S, KeyModifiers.Meta)]
	[DataRow("control+K", KeyCode.K, KeyModifiers.Control)]
	[DataRow("5", KeyCode.D5, KeyModifiers.None)]
	[DataRow("D5", KeyCode.D5, KeyModifiers.None)]
	[DataRow("Ctrl+5", KeyCode.D5, KeyModifiers.Control)]
	[DataRow("Escape", KeyCode.Escape, KeyModifiers.None)]
	[DataRow("Ctrl+Ctrl+S", KeyCode.S, KeyModifiers.Control)]
	public void TryParse_ValidStrokeText_ReturnsTheStroke(string text, KeyCode expectedKey, KeyModifiers expectedModifiers)
	{
		Assert.IsTrue(KeyStroke.TryParse(text, out KeyStroke stroke), $"'{text}' does not parse.");
		Assert.AreEqual(expectedKey, stroke.Key);
		Assert.AreEqual(expectedModifiers, stroke.Modifiers);
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
		Assert.IsFalse(KeyStroke.TryParse(text, out KeyStroke stroke), $"'{text}' must not parse.");
		Assert.AreEqual(default, stroke);
	}
}
