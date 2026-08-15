using System.Text.RegularExpressions;

namespace Nickelony.KeyBindings;

/// <summary>
/// Represents an ordered sequence of one or more <see cref="KeyCombo"/> strokes, typed in succession.
/// </summary>
/// <remarks>
/// <para>
/// A chord is the unit a binding is declared in; <c>Control+K, S</c> for example is two strokes (stroke
/// one <c>Control+K</c>, stroke two plain <c>S</c>) and three physical key presses. Each stroke carries
/// its own modifiers.
/// </para>
/// <para>
/// A single keystroke is a one-stroke chord, which is why a <see cref="KeyCombo"/> converts implicitly
/// to a <see cref="KeyChord"/>: the conversion is a semantic convenience, not a compatibility shim.
/// </para>
/// <para>
/// A chord must contain at least one stroke, every stroke must be initialized, and no stroke may
/// repeat, so the sequence always names a distinct ordered key press path. The <see langword="default"/>
/// value has no strokes and reports <see cref="IsInitialized"/> as <see langword="false"/>.
/// </para>
/// <para>
/// Equality compares the strokes in order, so two chords built independently from equal strokes are
/// equal. The type is a plain <see langword="struct"/> rather than a record because its backing store
/// is an array, which a record would compare by reference.
/// </para>
/// </remarks>
public readonly partial struct KeyChord : IEquatable<KeyChord>
{
	private readonly KeyCombo[]? _strokes;

	/// <summary>
	/// The separators that end one stroke and start the next in the typed-text grammar.
	/// </summary>
	private static readonly char[] s_strokeSeparators = [' ', '\t', ','];

	/// <summary>
	/// Matches whitespace that only spaces out the <c>+</c> join inside a single stroke, so it is removed
	/// before the stroke separators are applied. Whitespace on either side of a <c>+</c> is otherwise
	/// indistinguishable from a stroke separator, which would split <c>Ctrl + K</c> into three strokes.
	/// </summary>
	/// <returns>The generated matcher.</returns>
	[GeneratedRegex(@"\s+\+\s*|\s*\+\s+", RegexOptions.CultureInvariant)]
	private static partial Regex WhitespaceAroundPlus();

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyChord"/> struct.
	/// </summary>
	/// <param name="strokes">
	/// The strokes in press order; must contain at least one initialized combo and must not repeat a combo.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="strokes"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="strokes"/> is empty, contains an uninitialized key combo, or repeats a key combo.
	/// </exception>
	public KeyChord(params KeyCombo[] strokes)
	{
		ArgumentNullException.ThrowIfNull(strokes);

		ValidateStrokes(strokes);

		// The strokes are copied so later mutation of the caller's array cannot alter the chord.
		_strokes = [.. strokes];
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="KeyChord"/> struct from a stroke sequence.
	/// </summary>
	/// <param name="strokes">
	/// The strokes in press order; must contain at least one initialized combo and must not repeat a combo.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="strokes"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="strokes"/> is empty, contains an uninitialized key combo, or repeats a key combo.
	/// </exception>
	public KeyChord(IEnumerable<KeyCombo> strokes)
	{
		ArgumentNullException.ThrowIfNull(strokes);

		KeyCombo[] materialized = [.. strokes];

		ValidateStrokes(materialized);

		_strokes = materialized;
	}

	/// <summary>
	/// Gets the number of strokes in the chord; <c>0</c> for the <see langword="default"/> value.
	/// </summary>
	public int StrokeCount => _strokes?.Length ?? 0;

	/// <summary>
	/// Gets the strokes in press order; empty for the <see langword="default"/> value.
	/// </summary>
	/// <remarks>
	/// The span points at the chord's own storage, so it is valid only for the value it was read from and
	/// must not be captured across an <see langword="await"/> or a <see langword="yield"/> boundary.
	/// </remarks>
	public ReadOnlySpan<KeyCombo> Strokes => _strokes ?? [];

	/// <summary>
	/// Gets a value indicating whether the chord contains at least one stroke and is therefore a usable
	/// chord. The <see langword="default"/> value reports <see langword="false"/>.
	/// </summary>
	public bool IsInitialized => _strokes is { Length: > 0 };

	/// <summary>
	/// Converts a single key combo to the one-stroke chord that represents it.
	/// </summary>
	/// <param name="combo">The key combo to convert.</param>
	/// <returns>A chord that contains <paramref name="combo"/> as its only stroke.</returns>
	public static implicit operator KeyChord(KeyCombo combo) => new(combo);

	/// <summary>
	/// Tries to parse a chord from text a user typed, such as <c>Ctrl+K Ctrl+S</c> or <c>ctrl+k, ctrl+s</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is an input grammar for typed text, not the persisted override format. Strokes are separated by
	/// a space, a tab, or a comma, and each one is parsed by <see cref="KeyCombo.TryParse(string?, out KeyCombo)"/>,
	/// so a stroke is a <see cref="KeyCode"/> name with optional <see cref="KeyModifierSet"/> joined by
	/// <c>+</c>, matching is case-insensitive, and the modifiers also accept the operating-system aliases
	/// (<c>cmd</c>, <c>win</c>, <c>super</c>, ...). A run of separators is tolerated, and a chord that
	/// repeats a stroke is rejected because a chord never names the same stroke twice.
	/// </para>
	/// <para>
	/// The text parsed here is what <see cref="KeyDisplayTextFormatter"/> renders, so a chord
	/// round-trips through that formatter's display text and back. Presentation conventions of other
	/// formatters (such as <c>Esc</c> or <c>Num 5</c>) are not part of this grammar.
	/// </para>
	/// <para>
	/// The persisted override format keeps its own strict parser, which accepts only the exact canonical
	/// member names and never a numeric form, so tolerating typed input here does not loosen what a stored
	/// document may contain.
	/// </para>
	/// </remarks>
	/// <param name="text">The text to parse.</param>
	/// <param name="keyChord">
	/// The parsed chord when the method returns <see langword="true"/>; otherwise, the
	/// <see langword="default"/> chord.
	/// </param>
	/// <returns><see langword="true"/> when the text is a valid chord; otherwise, <see langword="false"/>.</returns>
	public static bool TryParse(string? text, out KeyChord keyChord)
	{
		keyChord = default;

		if (string.IsNullOrWhiteSpace(text))
			return false;

		string[] strokeTexts = WhitespaceAroundPlus()
			.Replace(text, "+")
			.Split(s_strokeSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

		if (strokeTexts.Length == 0)
			return false;

		var strokes = new List<KeyCombo>(strokeTexts.Length);

		foreach (string strokeText in strokeTexts)
		{
			// The repetition check runs before the constructor so a repeated stroke is rejected as invalid
			// text rather than throwing out of the chord it cannot build.
			if (!KeyCombo.TryParse(strokeText, out KeyCombo stroke) || strokes.Contains(stroke))
				return false;

			strokes.Add(stroke);
		}

		keyChord = new KeyChord(strokes);
		return true;
	}

	/// <summary>
	/// Determines whether two chords contain the same strokes in the same order.
	/// </summary>
	public static bool operator ==(KeyChord left, KeyChord right) => left.Equals(right);

	/// <summary>
	/// Determines whether two chords differ in their strokes or in their stroke order.
	/// </summary>
	public static bool operator !=(KeyChord left, KeyChord right) => !left.Equals(right);

	/// <inheritdoc/>
	public bool Equals(KeyChord other) => Strokes.SequenceEqual(other.Strokes);

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is KeyChord other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();

		foreach (KeyCombo stroke in Strokes)
			hash.Add(stroke);

		return hash.ToHashCode();
	}

	private static void ValidateStrokes(ReadOnlySpan<KeyCombo> strokes)
	{
		if (strokes.IsEmpty)
			throw new ArgumentException("A key chord must contain at least one key combo.", nameof(strokes));

		for (int index = 0; index < strokes.Length; index++)
		{
			if (!strokes[index].IsInitialized)
				throw new ArgumentException("A key chord must not contain an uninitialized key combo.", nameof(strokes));

			for (int previous = 0; previous < index; previous++)
			{
				if (strokes[previous] == strokes[index])
					throw new ArgumentException("A key chord must not repeat a key combo.", nameof(strokes));
			}
		}
	}
}
