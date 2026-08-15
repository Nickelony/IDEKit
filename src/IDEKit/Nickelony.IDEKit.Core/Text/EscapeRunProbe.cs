namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Counts the run of one repeated character that ends immediately before a token and reports whether
/// that run is odd, so "the token starts an escape sequence" follows one rule for every reader.
/// </summary>
/// <remarks>
/// <para>
/// The probe owns the rule and the count; the caller walks its own storage and feeds each preceding
/// character to <see cref="Consume(char)"/>. The comment scanner reads a
/// <see cref="ReadOnlySpan{T}"/> while the auto-closing resolver reads an
/// <see cref="ITextSnapshot"/> through <see cref="ITextSnapshot.GetCharAt(int)"/>, and materializing
/// a snapshot only to share the walk would cost a copy per keystroke - the same reason
/// <see cref="IdentifierBoundaryWalker"/> keeps its own reader generic instead of taking a span.
/// </para>
/// <para>
/// The walk starts at the token and moves backwards; it stops when <see cref="Consume(char)"/>
/// returns <see langword="false"/>, which is also the answer for a token at the very start of the
/// text. <see cref="IsEscaped"/> is meaningful only after the walk stops.
/// </para>
/// </remarks>
internal ref struct EscapeRunProbe
{
	private readonly char _escapeCharacter;
	private int _count;

	/// <summary>
	/// Initializes a new instance of the <see cref="EscapeRunProbe"/> struct.
	/// </summary>
	/// <param name="escapeCharacter">The character that repeats within an escape run.</param>
	internal EscapeRunProbe(char escapeCharacter)
	{
		_escapeCharacter = escapeCharacter;
		_count = 0;
	}

	/// <summary>
	/// Gets a value indicating whether the counted run is odd, so the token that follows it belongs to
	/// an escape sequence.
	/// </summary>
	internal readonly bool IsEscaped => (_count & 1) != 0;

	/// <summary>
	/// Consumes the character immediately before the characters consumed so far.
	/// </summary>
	/// <param name="character">The character to test.</param>
	/// <returns>
	/// <see langword="true"/> when the character is part of the run and the walk continues;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	internal bool Consume(char character)
	{
		if (character != _escapeCharacter)
			return false;

		_count++;
		return true;
	}
}
