using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.TextMate;

/// <summary>Parses the color spellings of TextMate theme data into <see cref="ArgbColor"/> values.</summary>
/// <remarks>
/// The recognized forms are <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c>, and <c>#RRGGBBAA</c> with ASCII hexadecimal digits of either case.
/// The alpha component comes last, as TextMate and VS Code spell it, and a three-digit or six-digit form is opaque.
/// </remarks>
internal static class TextMateColorParser
{
	/// <summary>Tries to parse a TextMate theme color.</summary>
	/// <param name="value">The color value; leading and trailing whitespace is ignored.</param>
	/// <param name="color">The parsed color when parsing succeeds; otherwise, the default color.</param>
	/// <returns><see langword="true"/> when <paramref name="value"/> is a recognized color spelling; otherwise, <see langword="false"/>.</returns>
	internal static bool TryParse(string? value, out ArgbColor color)
	{
		color = default;

		if (string.IsNullOrWhiteSpace(value))
			return false;

		ReadOnlySpan<char> text = value.AsSpan().Trim();

		if (text[0] != '#')
			return false;

		ReadOnlySpan<char> digits = text[1..];

		foreach (char digit in digits)
		{
			if (!char.IsAsciiHexDigit(digit))
				return false;
		}

		switch (digits.Length)
		{
			case 3:
				color = new ArgbColor(A: 0xFF, R: Doubled(digits[0]), G: Doubled(digits[1]), B: Doubled(digits[2]));
				return true;

			case 4:
				color = new ArgbColor(A: Doubled(digits[3]), R: Doubled(digits[0]), G: Doubled(digits[1]), B: Doubled(digits[2]));
				return true;

			case 6:
				color = new ArgbColor(A: 0xFF, R: Pair(digits, 0), G: Pair(digits, 2), B: Pair(digits, 4));
				return true;

			case 8:
				color = new ArgbColor(A: Pair(digits, 6), R: Pair(digits, 0), G: Pair(digits, 2), B: Pair(digits, 4));
				return true;

			default:
				return false;
		}
	}

	private static int HexValue(char digit) => digit <= '9' ? digit - '0' : (digit | 0x20) - 'a' + 10;

	private static byte Doubled(char digit)
	{
		int value = HexValue(digit);
		return (byte)((value << 4) | value);
	}

	private static byte Pair(ReadOnlySpan<char> digits, int start) => (byte)((HexValue(digits[start]) << 4) | HexValue(digits[start + 1]));
}
