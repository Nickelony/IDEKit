namespace Nickelony.IDEKit.Core.Text;

/// <summary>Counts the run of one repeated character immediately before a token and reports whether that run is odd.</summary>
/// <remarks>
/// The probe owns the count; the caller walks its own storage and feeds each preceding character to <see cref="Consume(char)"/>,
/// so a span-backed and a snapshot-backed caller share the rule without copying. The walk starts at the token
/// and moves backwards, and stops when <see cref="Consume(char)"/> returns <see langword="false"/>; <see cref="IsEscaped"/> reports the full run
/// only after the walk stops.
/// </remarks>
internal ref struct EscapeRunProbe
{
	private readonly char _escapeCharacter;
	private int _count;

	/// <param name="escapeCharacter">The character that repeats within an escape run.</param>
	internal EscapeRunProbe(char escapeCharacter)
	{
		_escapeCharacter = escapeCharacter;
		_count = 0;
	}

	/// <summary>Indicates whether the counted run is odd, so the token that follows it belongs to an escape sequence.</summary>
	internal readonly bool IsEscaped => (_count & 1) != 0;

	/// <summary>Consumes the character immediately before the characters consumed so far.</summary>
	/// <param name="character">The character to test.</param>
	/// <returns><see langword="true"/> when the character is part of the run and the walk continues; otherwise, <see langword="false"/>.</returns>
	internal bool Consume(char character)
	{
		if (character != _escapeCharacter)
			return false;

		_count++;
		return true;
	}
}
