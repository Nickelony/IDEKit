using System.Text.RegularExpressions;

namespace Nickelony.KeyBindings;

/// <summary>Represents an ordered sequence of one or more <see cref="KeyStroke"/> values, typed in succession.</summary>
/// <remarks>
/// A chord is the unit a binding is declared in: <c>Control+K, S</c> is two strokes, each carrying its own modifiers. A single stroke
/// is the one-stroke chord <see cref="KeyStroke"/> converts to implicitly; a chord holds at least one initialized stroke and never repeats a stroke.
/// The <see langword="default"/> value has no strokes and reports <see cref="IsInitialized"/> as <see langword="false"/>.
/// </remarks>
public readonly partial struct KeyChord : IEquatable<KeyChord>
{
	private readonly KeyStroke[]? _strokes;

	// The hash is derived only from the strokes, which never change after construction, so it is computed once
	// per chord instead of on every lookup that uses the chord as a key.
	private readonly int _hashCode;

	/// <summary>The separators that end one stroke and start the next in the typed-text grammar.</summary>
	private static readonly char[] s_strokeSeparators = [' ', '\t', ','];

	/// <summary>
	/// Matches the whitespace that spaces out the <c>+</c> join inside a single stroke, so the match can be replaced with a bare <c>+</c>
	/// before the stroke separators split the text into strokes.
	/// </summary>
	[GeneratedRegex(@"\s+\+\s*|\s*\+\s+", RegexOptions.CultureInvariant)]
	private static partial Regex WhitespaceAroundPlus();

	/// <param name="strokes">The strokes in press order; must contain at least one initialized stroke and must not repeat a stroke.</param>
	/// <exception cref="ArgumentNullException"><paramref name="strokes"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="strokes"/> is empty, contains an uninitialized stroke, or repeats a stroke.</exception>
	public KeyChord(params KeyStroke[] strokes)
	{
		ArgumentNullException.ThrowIfNull(strokes);

		ValidateStrokes(strokes);

		// The strokes are copied so later mutation of the caller's array cannot alter the chord.
		_strokes = [.. strokes];
		_hashCode = ComputeHashCode(_strokes);
	}

	/// <param name="strokes">The strokes in press order; validated exactly like the public constructor.</param>
	/// <exception cref="ArgumentException"><paramref name="strokes"/> is empty, contains an uninitialized stroke, or repeats a stroke.</exception>
	internal KeyChord(ReadOnlySpan<KeyStroke> strokes)
	{
		ValidateStrokes(strokes);

		// The span is copied so the chord owns its strokes independently of the caller's buffer.
		_strokes = strokes.ToArray();
		_hashCode = ComputeHashCode(_strokes);
	}

	/// <summary>The number of strokes in the chord; <c>0</c> for the <see langword="default"/> value.</summary>
	public int StrokeCount => _strokes?.Length ?? 0;

	/// <summary>The strokes in press order; empty for the <see langword="default"/> value.</summary>
	/// <remarks>
	/// The span points at the chord's own storage, so it is valid only for the value it was read from and must not be captured
	/// across an <see langword="await"/> or a <see langword="yield"/> boundary.
	/// </remarks>
	public ReadOnlySpan<KeyStroke> Strokes => _strokes ?? [];

	public bool IsInitialized => _strokes is { Length: > 0 };

	/// <summary>Converts a single key stroke to the one-stroke chord that represents it.</summary>
	/// <param name="stroke">The key stroke to convert.</param>
	/// <returns>A chord that contains <paramref name="stroke"/> as its only stroke.</returns>
	public static implicit operator KeyChord(KeyStroke stroke) => new(stroke);

	/// <summary>Tries to parse a chord from text a user typed, such as <c>Ctrl+K Ctrl+S</c> or <c>ctrl+k, ctrl+s</c>.</summary>
	/// <param name="text">The text to parse.</param>
	/// <param name="keyChord">
	/// The parsed chord when the method returns <see langword="true"/>; otherwise, the <see langword="default"/> chord.
	/// </param>
	/// <returns><see langword="true"/> when the text is a valid chord; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// <para>
	/// This is an input grammar for typed text, not the persisted override format. Strokes are separated by a space, a tab, or a comma,
	/// and each one is parsed by <see cref="KeyStroke.TryParse(string?, out KeyStroke)"/>. A run of separators is tolerated, and a chord that repeats
	/// a stroke is rejected. The text is what <c>KeyDisplayTextFormatter</c> renders, so a chord round-trips through that display text.
	/// </para>
	/// </remarks>
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

		var strokes = new List<KeyStroke>(strokeTexts.Length);

		foreach (string strokeText in strokeTexts)
		{
			// The repetition check runs before the constructor, so a repeated stroke is rejected as invalid
			// text instead of throwing from the constructor.
			if (!KeyStroke.TryParse(strokeText, out KeyStroke stroke) || strokes.Contains(stroke))
				return false;

			strokes.Add(stroke);
		}

		keyChord = new KeyChord([.. strokes]);
		return true;
	}

	/// <summary>Determines whether two chords contain the same strokes in the same order.</summary>
	public static bool operator ==(KeyChord left, KeyChord right) => left.Equals(right);

	/// <summary>Determines whether two chords differ in their strokes or in their stroke order.</summary>
	public static bool operator !=(KeyChord left, KeyChord right) => !left.Equals(right);

	/// <inheritdoc/>
	public bool Equals(KeyChord other) => Strokes.SequenceEqual(other.Strokes);

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is KeyChord other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode() => _hashCode;

	private static int ComputeHashCode(ReadOnlySpan<KeyStroke> strokes)
	{
		var hash = new HashCode();

		foreach (KeyStroke stroke in strokes)
			hash.Add(stroke);

		return hash.ToHashCode();
	}

	private static void ValidateStrokes(ReadOnlySpan<KeyStroke> strokes)
	{
		if (strokes.IsEmpty)
			throw new ArgumentException("A key chord must contain at least one stroke.", nameof(strokes));

		for (int index = 0; index < strokes.Length; index++)
		{
			if (!strokes[index].IsInitialized)
				throw new ArgumentException("A key chord must not contain an uninitialized stroke.", nameof(strokes));

			for (int previous = 0; previous < index; previous++)
			{
				if (strokes[previous] == strokes[index])
					throw new ArgumentException("A key chord must not repeat a stroke.", nameof(strokes));
			}
		}
	}
}
