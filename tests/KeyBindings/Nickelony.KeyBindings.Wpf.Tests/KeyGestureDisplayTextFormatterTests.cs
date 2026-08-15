using System.Globalization;

namespace Nickelony.KeyBindings.Wpf.Tests;

[STATestClass]
public class KeyGestureDisplayTextFormatterTests
{
	private static readonly KeyGestureDisplayTextFormatter s_formatter = new(CultureInfo.InvariantCulture);

	[TestMethod]
	[DataRow(KeyCode.S, KeyModifierSet.Control, "Ctrl+S")]
	[DataRow(KeyCode.S, KeyModifierSet.Control | KeyModifierSet.Shift, "Ctrl+Shift+S")]
	[DataRow(KeyCode.F4, KeyModifierSet.Alt, "Alt+F4")]
	[DataRow(KeyCode.S, KeyModifierSet.Meta, "Windows+S")]
	[DataRow(KeyCode.F9, KeyModifierSet.None, "F9")]
	[DataRow(KeyCode.NumPad5, KeyModifierSet.None, "NumPad5")]
	[DataRow(KeyCode.Delete, KeyModifierSet.None, "Delete")]
	[DataRow(KeyCode.Left, KeyModifierSet.Control, "Ctrl+Left")]
	[DataRow(KeyCode.Slash, KeyModifierSet.Control, "Ctrl+OemQuestion")]
	public void GetDisplayText_ReturnsWpfGestureText(KeyCode key, KeyModifierSet modifiers, string expected)
		=> Assert.AreEqual(expected, s_formatter.GetDisplayText(new KeyCombo(key, modifiers)));

	[TestMethod]
	[DataRow(KeyCode.S, KeyModifierSet.None, "S")]
	[DataRow(KeyCode.S, KeyModifierSet.Shift, "Shift+S")]
	[DataRow(KeyCode.D5, KeyModifierSet.None, "5")]
	[DataRow(KeyCode.D5, KeyModifierSet.Shift, "Shift+5")]
	public void GetDisplayText_GestureRejectedCombo_FallsBackToConverterText(KeyCode key, KeyModifierSet modifiers, string expected)
		=> Assert.AreEqual(expected, s_formatter.GetDisplayText(new KeyCombo(key, modifiers)));

	[TestMethod]
	public void GetDisplayText_UninitializedCombo_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => s_formatter.GetDisplayText(default));

	/// <summary>
	/// Pins that the formatter renders for the culture in effect on each call instead of a culture captured
	/// at construction, and that the memo is keyed by that culture. WPF's gesture converters ignore the
	/// culture today, so the text is the same; the distinct instances are what shows the culture was part
	/// of the key (a text-only assertion would pass even if the culture were ignored).
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void GetDisplayText_WithoutCulture_KeysTheMemoByTheCurrentCulture()
	{
		var combo = new KeyCombo(KeyCode.S, KeyModifierSet.Control);
		var formatter = new KeyGestureDisplayTextFormatter();
		CultureInfo original = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			string invariant = formatter.GetDisplayText(combo);

			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
			string french = formatter.GetDisplayText(combo);

			Assert.AreEqual(invariant, french);
			Assert.AreNotSame(invariant, french);
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}

	/// <summary>
	/// Pins that <see cref="KeyGestureDisplayTextFormatter.Default"/> renders through the WPF gesture path
	/// (<c>Ctrl</c> rather than the base formatter's <c>Control</c>).
	/// </summary>
	[TestMethod]
	public void Default_RendersThroughTheGesturePath()
		=> Assert.AreEqual("Ctrl+S",
			KeyGestureDisplayTextFormatter.Default.GetDisplayText(new KeyCombo(KeyCode.S, KeyModifierSet.Control)));

	[TestMethod]
	public void GetDisplayText_Chord_JoinsStrokeGesturesWithAComma()
	{
		var chord = new KeyChord(
			new KeyCombo(KeyCode.K, KeyModifierSet.Control),
			new KeyCombo(KeyCode.S, KeyModifierSet.Control));

		Assert.AreEqual("Ctrl+K, Ctrl+S", s_formatter.GetDisplayText(chord));
	}

	[TestMethod]
	public void GetDisplayText_UninitializedChord_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => s_formatter.GetDisplayText(default(KeyChord)));

	/// <summary>
	/// Pins that a stroke WPF rejects as a gesture renders its fallback text inside the joined chord
	/// instead of throwing.
	/// </summary>
	[TestMethod]
	public void GetDisplayText_ChordWithARejectedGestureStroke_RendersTheFallbackTextInTheChord()
	{
		var chord = new KeyChord(
			new KeyCombo(KeyCode.D5, KeyModifierSet.None),
			new KeyCombo(KeyCode.S, KeyModifierSet.Control));

		Assert.AreEqual("5, Ctrl+S", s_formatter.GetDisplayText(chord));
	}

	/// <summary>
	/// Pins that the gesture memo dedups equal-culture instances: <see cref="CultureInfo"/> compares and
	/// hashes by name, so a formatter without a fixed culture reuses one render when the current culture
	/// is replaced by a fresh instance of the same culture instead of growing a second cache entry. The
	/// test mutates process-global state, so it carries <c>[DoNotParallelize]</c>; this suite sets no
	/// assembly-level <c>[assembly: Parallelize]</c>, so the marker is preemptive (see
	/// <c>tests/TestSupport/README.md</c>).
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void GetDisplayText_EqualCultureInstances_ReuseOneRender()
	{
		CultureInfo original = CultureInfo.CurrentCulture;

		try
		{
			var combo = new KeyCombo(KeyCode.S, KeyModifierSet.Control);
			var formatter = new KeyGestureDisplayTextFormatter();

			CultureInfo.CurrentCulture = new CultureInfo("en-US");
			string first = formatter.GetDisplayText(combo);

			CultureInfo.CurrentCulture = new CultureInfo("en-US");
			string second = formatter.GetDisplayText(combo);

			Assert.AreEqual(first, second);
			Assert.AreSame(first, second);
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}
}
