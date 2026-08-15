namespace Nickelony.KeyBindings.Tests;

[TestClass]
public class KeyChordTests
{
	private static KeyCombo Ctrl(KeyCode key) => new(key, KeyModifierSet.Control);

	[TestMethod]
	public void Constructor_StoresStrokesInPressOrder()
	{
		var chord = new KeyChord(Ctrl(KeyCode.K), new KeyCombo(KeyCode.S, KeyModifierSet.None));

		Assert.AreEqual(2, chord.StrokeCount);
		Assert.IsTrue(chord.IsInitialized);
		Assert.AreEqual(Ctrl(KeyCode.K), chord.Strokes[0]);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.None), chord.Strokes[1]);
	}

	[TestMethod]
	public void Constructor_FromSequence_StoresStrokesInPressOrder()
	{
		List<KeyCombo> strokes = [Ctrl(KeyCode.K), Ctrl(KeyCode.S)];

		var chord = new KeyChord(strokes);

		Assert.AreEqual(2, chord.StrokeCount);
		Assert.AreEqual(Ctrl(KeyCode.S), chord.Strokes[1]);
	}

	[TestMethod]
	public void Constructor_SingleStroke_IsAOneStrokeChord()
	{
		var chord = new KeyChord(Ctrl(KeyCode.S));

		Assert.AreEqual(1, chord.StrokeCount);
		Assert.IsTrue(chord.IsInitialized);
	}

	[TestMethod]
	public void Constructor_NoStrokes_Throws()
	{
		// A zero-argument call selects the implicit default constructor of the struct, so the empty case
		// only reaches the constructor through an explicit empty sequence.
		Assert.ThrowsExactly<ArgumentException>(() => new KeyChord(Array.Empty<KeyCombo>()));
		Assert.ThrowsExactly<ArgumentException>(() => new KeyChord(new List<KeyCombo>()));
	}

	[TestMethod]
	public void Constructor_UninitializedStroke_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new KeyChord(Ctrl(KeyCode.K), default));

	[TestMethod]
	public void Constructor_RepeatedStroke_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.K)));

	[TestMethod]
	public void Constructor_CopiesTheStrokeArray()
	{
		KeyCombo[] strokes = [Ctrl(KeyCode.K), Ctrl(KeyCode.S)];

		var chord = new KeyChord(strokes);

		strokes[1] = new KeyCombo(KeyCode.X, KeyModifierSet.None);

		Assert.AreEqual(Ctrl(KeyCode.S), chord.Strokes[1]);
	}

	[TestMethod]
	public void DefaultValue_IsNotInitialized()
	{
		KeyChord chord = default;

		Assert.IsFalse(chord.IsInitialized);
		Assert.AreEqual(0, chord.StrokeCount);
		Assert.IsTrue(chord.Strokes.IsEmpty);
	}

	[TestMethod]
	public void ImplicitConversion_FromCombo_CreatesAOneStrokeChord()
	{
		KeyChord chord = new KeyCombo(KeyCode.S, KeyModifierSet.Control);

		Assert.IsTrue(chord.IsInitialized);
		Assert.AreEqual(1, chord.StrokeCount);
		Assert.AreEqual(new KeyCombo(KeyCode.S, KeyModifierSet.Control), chord.Strokes[0]);
	}

	[TestMethod]
	public void Equality_ComparesStrokesInOrder()
	{
		var chord = new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S));

		Assert.AreEqual(chord, new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S)));
		Assert.AreNotEqual(chord, new KeyChord(Ctrl(KeyCode.S), Ctrl(KeyCode.K)));
		Assert.AreNotEqual(chord, new KeyChord(Ctrl(KeyCode.K)));
		Assert.IsTrue(chord == new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S)));
		Assert.IsTrue(chord != new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.F)));
	}

	[TestMethod]
	public void GetHashCode_EqualChords_AreInterchangeableInHashSet()
	{
		var chords = new HashSet<KeyChord>
		{
			new(Ctrl(KeyCode.K), Ctrl(KeyCode.S)),
			new(Ctrl(KeyCode.K), Ctrl(KeyCode.S)),
			new(Ctrl(KeyCode.K), Ctrl(KeyCode.F)),
			default
		};

		Assert.HasCount(3, chords);
	}

	// ---- Typed-text parse ----

	[TestMethod]
	[DataRow("Ctrl+K Ctrl+S")]
	[DataRow("ctrl+k ctrl+s")]
	[DataRow("Ctrl+K, Ctrl+S")]
	[DataRow("ctrl+k,ctrl+s")]
	[DataRow("  CTRL + K ,  CTRL + S  ")]
	[DataRow("Ctrl + K Ctrl + S")]
	[DataRow("Ctrl+K\tCtrl+S")]
	public void TryParse_TwoStrokeText_ReturnsTheChord(string text)
	{
		var expected = new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S));

		Assert.IsTrue(KeyChord.TryParse(text, out KeyChord chord), $"'{text}' does not parse.");
		Assert.AreEqual(expected, chord);
	}

	[TestMethod]
	public void TryParse_SingleStrokeText_ReturnsAOneStrokeChord()
	{
		Assert.IsTrue(KeyChord.TryParse("Alt+F4", out KeyChord chord));
		Assert.AreEqual(1, chord.StrokeCount);
		Assert.AreEqual(new KeyCombo(KeyCode.F4, KeyModifierSet.Alt), chord.Strokes[0]);
	}

	[TestMethod]
	public void TryParse_TwoStrokeTextWithoutModifiers_ReturnsBothStrokes()
	{
		// "Ctrl+K Z" is a legitimate two-stroke chord: the continuation stroke carries no modifier of its own.
		Assert.IsTrue(KeyChord.TryParse("Ctrl+K Z", out KeyChord chord));
		Assert.AreEqual(new KeyChord(Ctrl(KeyCode.K), new KeyCombo(KeyCode.Z, KeyModifierSet.None)), chord);
	}

	[TestMethod]
	[DataRow("Ctrl+K,", 1)]
	[DataRow("Ctrl+K,, Ctrl+S", 2)]
	[DataRow("Ctrl+K  Ctrl+S", 2)]
	public void TryParse_StraySeparators_AreTolerated(string text, int expectedStrokeCount)
	{
		Assert.IsTrue(KeyChord.TryParse(text, out KeyChord chord), $"'{text}' does not parse.");

		// The stray separator does not add a stroke; the chord is the strokes that were actually named.
		Assert.AreEqual(expectedStrokeCount, chord.StrokeCount, $"'{text}' has the wrong stroke count.");
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow(",,,")]
	[DataRow("Ctrl")]
	[DataRow("Ctrl+K Ctrl+K")]
	public void TryParse_InvalidChordText_ReturnsFalseAndDefault(string? text)
	{
		Assert.IsFalse(KeyChord.TryParse(text, out KeyChord chord), $"'{text}' must not parse.");
		Assert.AreEqual(default(KeyChord), chord);
	}

	/// <summary>
	/// Pins the round-trip the typed-text grammar exists for: the default formatter's own output is
	/// accepted and yields the stroke it was rendered from, for every key and every modifier combination.
	/// </summary>
	[TestMethod]
	public void TryParse_DefaultDisplayText_RoundTripsEveryStroke()
	{
		foreach (KeyCode keyCode in Enum.GetValues<KeyCode>())
		{
			for (int mask = 0; mask < 16; mask++)
			{
				var combo = new KeyCombo(keyCode, (KeyModifierSet)mask);
				string displayText = KeyDisplayTextFormatter.Default.GetDisplayText(combo);

				Assert.IsTrue(KeyCombo.TryParse(displayText, out KeyCombo parsed), $"'{displayText}' does not parse.");
				Assert.AreEqual(combo, parsed, $"'{displayText}' does not round-trip.");
			}
		}
	}

	[TestMethod]
	public void TryParse_DefaultDisplayText_RoundTripsChords()
	{
		KeyChord[] chords =
		[
			new(Ctrl(KeyCode.K), Ctrl(KeyCode.S)),
			new(Ctrl(KeyCode.K), new KeyCombo(KeyCode.F, KeyModifierSet.None)),
			new(new KeyCombo(KeyCode.F4, KeyModifierSet.Alt)),
			new(Ctrl(KeyCode.K), new KeyCombo(KeyCode.D5, KeyModifierSet.Shift), new KeyCombo(KeyCode.Enter, KeyModifierSet.None))
		];

		foreach (KeyChord chord in chords)
		{
			string displayText = KeyDisplayTextFormatter.Default.GetDisplayText(chord);

			Assert.IsTrue(KeyChord.TryParse(displayText, out KeyChord parsed), $"'{displayText}' does not parse.");
			Assert.AreEqual(chord, parsed, $"'{displayText}' does not round-trip.");
		}
	}
}
