using System.Globalization;

namespace Nickelony.KeyBindings.Wpf.Tests;

[STATestClass]
public sealed class KeyGestureDisplayTextFormatterTests
{
	private static readonly KeyGestureDisplayTextFormatter s_formatter = new(CultureInfo.InvariantCulture);

	[TestMethod]
	[DataRow(KeyCode.S, KeyModifiers.Control, "Ctrl+S")]
	[DataRow(KeyCode.S, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+S")]
	[DataRow(KeyCode.F4, KeyModifiers.Alt, "Alt+F4")]
	[DataRow(KeyCode.S, KeyModifiers.Meta, "Windows+S")]
	[DataRow(KeyCode.F9, KeyModifiers.None, "F9")]
	[DataRow(KeyCode.NumPad5, KeyModifiers.None, "NumPad5")]
	[DataRow(KeyCode.Delete, KeyModifiers.None, "Delete")]
	[DataRow(KeyCode.Left, KeyModifiers.Control, "Ctrl+Left")]
	[DataRow(KeyCode.Slash, KeyModifiers.Control, "Ctrl+OemQuestion")]
	public void GetDisplayText_ReturnsWpfGestureText(KeyCode key, KeyModifiers modifiers, string expected) =>
		Assert.AreEqual(expected, s_formatter.GetDisplayText(new KeyStroke(key, modifiers)));

	[TestMethod]
	[DataRow(KeyCode.S, KeyModifiers.None, "S")]
	[DataRow(KeyCode.S, KeyModifiers.Shift, "Shift+S")]
	[DataRow(KeyCode.D5, KeyModifiers.None, "5")]
	[DataRow(KeyCode.D5, KeyModifiers.Shift, "Shift+5")]
	public void GetDisplayText_GestureRejectedStroke_FallsBackToConverterText(KeyCode key, KeyModifiers modifiers, string expected) =>
		Assert.AreEqual(expected, s_formatter.GetDisplayText(new KeyStroke(key, modifiers)));

	[TestMethod]
	public void GetDisplayText_UninitializedStroke_Throws() => Assert.ThrowsExactly<ArgumentException>(() => s_formatter.GetDisplayText(default));

	/// <summary>
	/// Pins that <see cref="KeyGestureDisplayTextFormatter.CurrentCulture"/> renders through the WPF gesture path (<c>Ctrl</c> rather than the base
	/// formatter's <c>Control</c>).
	/// </summary>
	[TestMethod]
	public void CurrentCulture_RendersThroughTheGesturePath() =>
		Assert.AreEqual("Ctrl+S", KeyGestureDisplayTextFormatter.CurrentCulture.GetDisplayText(new KeyStroke(KeyCode.S, KeyModifiers.Control)));

	[TestMethod]
	public void GetDisplayText_Chord_JoinsStrokeGesturesWithAComma()
	{
		var chord = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control));
		Assert.AreEqual("Ctrl+K, Ctrl+S", s_formatter.GetDisplayText(chord));
	}

	[TestMethod]
	public void GetDisplayText_UninitializedChord_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => s_formatter.GetDisplayText(default(KeyChord)));

	/// <summary>Pins that a stroke WPF rejects as a gesture renders its fallback text inside the joined chord instead of throwing.</summary>
	[TestMethod]
	public void GetDisplayText_ChordWithARejectedGestureStroke_RendersTheFallbackTextInTheChord()
	{
		var chord = new KeyChord(new KeyStroke(KeyCode.D5, KeyModifiers.None), new KeyStroke(KeyCode.S, KeyModifiers.Control));
		Assert.AreEqual("5, Ctrl+S", s_formatter.GetDisplayText(chord));
	}

	/// <summary>
	/// Pins that the gesture memo dedups equal-culture instances: a formatter without a fixed culture reuses one render when the current culture
	/// is replaced by a fresh instance of the same culture.
	/// </summary>
	/// <remarks>
	/// The test mutates process-global state, so it carries <c>[DoNotParallelize]</c>.
	/// </remarks>
	[TestMethod]
	[DoNotParallelize]
	public void GetDisplayText_EqualCultureInstances_ReuseOneRender()
	{
		CultureInfo original = CultureInfo.CurrentCulture;

		try
		{
			var stroke = new KeyStroke(KeyCode.S, KeyModifiers.Control);
			var formatter = new KeyGestureDisplayTextFormatter();

			CultureInfo.CurrentCulture = new CultureInfo("en-US");
			string first = formatter.GetDisplayText(stroke);

			CultureInfo.CurrentCulture = new CultureInfo("en-US");

			string second = formatter.GetDisplayText(stroke);

			Assert.AreEqual("Ctrl+S", first);
			Assert.AreEqual(first, second);
			Assert.AreSame(first, second);
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}
}
