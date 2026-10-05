namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>A one-based line, and optionally a one-based column, that a user typed as <c>line</c> or <c>line:column</c>.</summary>
/// <param name="Line">The line, which is at least zero as typed; a mode clamps it to its document.</param>
/// <param name="Column">The column, or <see langword="null"/> when the text named none.</param>
/// <remarks>
/// The go to line mode reads its query with <see cref="TryParse"/>, and a files mode reads the <c>:12</c> after a file name with it, so both accept
/// the same text.
/// </remarks>
public readonly record struct LineColumnQuery(int Line, int? Column)
{
	/// <summary>Reads <c>line</c> or <c>line:column</c>.</summary>
	/// <param name="text">The text to read.</param>
	/// <param name="query">The line and column when the method returns <see langword="true"/>; otherwise, <see langword="default"/>.</param>
	/// <returns><see langword="true"/> when the text is a line, or a line and a column; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// A line or a column is a run of decimal digits, and the whitespace around a number is ignored. A number past the largest <see cref="int"/>
	/// counts as that largest value. An empty column, as in <c>12:</c>, is no column. Anything else, such as a sign, a third number or a letter,
	/// is no position.
	/// </remarks>
	public static bool TryParse(string? text, out LineColumnQuery query)
	{
		query = default;

		if (text is null)
			return false;

		string[] parts = text.Split(':');

		if (parts.Length > 2 || !TryParseNumber(parts[0], out int line))
			return false;

		if (parts.Length == 1 || parts[1].Trim().Length == 0)
		{
			query = new LineColumnQuery(line, Column: null);
			return true;
		}

		if (!TryParseNumber(parts[1], out int column))
			return false;

		query = new LineColumnQuery(line, column);

		return true;
	}

	// Digits only, saturating at the largest int, so a number that does not fit still means "the end".
	private static bool TryParseNumber(string text, out int value)
	{
		value = 0;

		ReadOnlySpan<char> digits = text.AsSpan().Trim();

		if (digits.IsEmpty)
			return false;

		long accumulated = 0;

		foreach (char digit in digits)
		{
			if (digit is < '0' or > '9')
				return false;

			accumulated = Math.Min(int.MaxValue, (accumulated * 10) + (digit - '0'));
		}

		value = (int)accumulated;

		return true;
	}
}
