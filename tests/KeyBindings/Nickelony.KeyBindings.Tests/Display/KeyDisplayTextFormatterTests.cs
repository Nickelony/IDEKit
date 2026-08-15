namespace Nickelony.KeyBindings.Tests;

[TestClass]
public class KeyDisplayTextFormatterTests
{
	[TestMethod]
	[DataRow(KeyCode.S, KeyModifierSet.None, "S")]
	[DataRow(KeyCode.S, KeyModifierSet.Control, "Control+S")]
	[DataRow(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Alt | KeyModifierSet.Meta, "Control+Shift+Alt+Meta+S")]
	[DataRow(KeyCode.D5, KeyModifierSet.None, "5")]
	[DataRow(KeyCode.F9, KeyModifierSet.None, "F9")]
	[DataRow(KeyCode.Enter, KeyModifierSet.None, "Enter")]
	[DataRow(KeyCode.Slash, KeyModifierSet.Control, "Control+Slash")]
	[DataRow(KeyCode.NumPad5, KeyModifierSet.Meta, "Meta+NumPad5")]
	public void GetDisplayText_Combo_ReturnsCanonicalNames(KeyCode key, KeyModifierSet modifiers, string expected)
		=> Assert.AreEqual(expected, KeyDisplayTextFormatter.Default.GetDisplayText(new KeyCombo(key, modifiers)));

	[TestMethod]
	public void GetDisplayText_UninitializedCombo_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => KeyDisplayTextFormatter.Default.GetDisplayText(default));

	[TestMethod]
	public void GetDisplayText_Chord_JoinsStrokeTextsWithAComma()
	{
		var chord = new KeyChord(
			new KeyCombo(KeyCode.K, KeyModifierSet.Control),
			new KeyCombo(KeyCode.S, KeyModifierSet.Control),
			new KeyCombo(KeyCode.F9, KeyModifierSet.None));

		Assert.AreEqual("Control+K, Control+S, F9", KeyDisplayTextFormatter.Default.GetDisplayText(chord));
	}

	[TestMethod]
	public void GetDisplayText_OneStrokeChord_RendersAsItsCombo()
		=> Assert.AreEqual(
			"Control+Shift+S",
			KeyDisplayTextFormatter.Default.GetDisplayText(new KeyChord(new KeyCombo(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift))));

	[TestMethod]
	public void GetDisplayText_UninitializedChord_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => KeyDisplayTextFormatter.Default.GetDisplayText(default(KeyChord)));

	/// <summary>
	/// Pins the extension point: a formatter that only relabels keys and modifiers derives from the
	/// default and inherits its stroke sequencing and its uninitialized-combo contract.
	/// </summary>
	[TestMethod]
	public void GetDisplayText_DerivedFormatter_RelabelsKeysAndModifiers()
	{
		var formatter = new RelabelingFormatter();

		Assert.AreEqual("ctrl+s", formatter.GetDisplayText(new KeyCombo(KeyCode.S, KeyModifierSet.Control)));
		Assert.AreEqual(
			"ctrl+s, F9",
			formatter.GetDisplayText(new KeyChord(new KeyCombo(KeyCode.S, KeyModifierSet.Control), new KeyCombo(KeyCode.F9, KeyModifierSet.None))));
		Assert.ThrowsExactly<ArgumentException>(() => formatter.GetDisplayText(default));
	}

	/// <summary>
	/// A formatter that relabels the control modifier and the S key, and falls back to the base text for
	/// everything else.
	/// </summary>
	private sealed class RelabelingFormatter : KeyDisplayTextFormatter
	{
		protected override string GetKeyText(KeyCode key)
			=> key == KeyCode.S ? "s" : base.GetKeyText(key);

		protected override string GetModifierText(KeyModifierSet modifier)
			=> modifier == KeyModifierSet.Control ? "ctrl" : base.GetModifierText(modifier);
	}

	/// <summary>
	/// Pins the order, separator, and stroke-separator extension points: a formatter that renders the
	/// modifiers in another order, joins them with another separator, and separates the chord strokes
	/// differently still inherits the uninitialized-combo contract.
	/// </summary>
	[TestMethod]
	public void GetDisplayText_DerivedFormatter_CanChangeOrderAndSeparators()
	{
		var formatter = new ReorderingFormatter();

		Assert.AreEqual(
			"Meta-Control-S",
			formatter.GetDisplayText(new KeyCombo(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Meta)));
		Assert.AreEqual(
			"Meta-Control-K, Meta-S",
			formatter.GetDisplayText(new KeyChord(
				new KeyCombo(KeyCode.K, KeyModifierSet.Control | KeyModifierSet.Meta),
				new KeyCombo(KeyCode.S, KeyModifierSet.Meta))));
		Assert.ThrowsExactly<ArgumentException>(() => formatter.GetDisplayText(default));
		Assert.ThrowsExactly<ArgumentException>(() => formatter.GetDisplayText(default(KeyChord)));
	}

	/// <summary>
	/// Pins that a formatter whose convention order omits a modifier still renders it: the omitted flag is
	/// appended in the base order instead of being silently dropped from the text.
	/// </summary>
	[TestMethod]
	public void GetDisplayText_ShortenedModifierOrder_RendersTheOmittedModifier()
	{
		var formatter = new ShortenedOrderFormatter();

		Assert.AreEqual(
			"Control+Alt+S",
			formatter.GetDisplayText(new KeyCombo(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Alt)));
		Assert.AreEqual(
			"Control+Shift+Alt+Meta+S",
			formatter.GetDisplayText(new KeyCombo(
				KeyCode.S,
				KeyModifierSet.Control | KeyModifierSet.Shift | KeyModifierSet.Alt | KeyModifierSet.Meta)));
	}

	/// <summary>
	/// A formatter that renders only the Control modifier in its order, so the other defined modifiers are
	/// leftovers the base order must still render.
	/// </summary>
	private sealed class ShortenedOrderFormatter : KeyDisplayTextFormatter
	{
		protected override IReadOnlyList<KeyModifierSet> GetModifierOrder() => [KeyModifierSet.Control];
	}

	/// <summary>
	/// A formatter that renders the modifiers in a custom order, joins them with a hyphen, and separates
	/// the chord strokes with a comma.
	/// </summary>
	private sealed class ReorderingFormatter : KeyDisplayTextFormatter
	{
		protected override IReadOnlyList<KeyModifierSet> GetModifierOrder() =>
			[KeyModifierSet.Meta, KeyModifierSet.Control];

		protected override string GetModifierSeparator() => "-";

		protected override string GetStrokeSeparator() => ", ";
	}
}
