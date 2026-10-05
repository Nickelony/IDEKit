namespace Nickelony.IDEKit.Core.Media;

/// <summary>Describes a color as 8-bit alpha, red, green, and blue channels, independent of any UI toolkit.</summary>
/// <param name="A">The alpha channel; <c>0</c> is fully transparent and <c>255</c> is fully opaque.</param>
/// <param name="R">The red channel.</param>
/// <param name="G">The green channel.</param>
/// <param name="B">The blue channel.</param>
/// <remarks>
/// An editor binding converts the value to the toolkit's own color type when it builds a brush or pen, so a shared option or theme never exposes
/// a host color type.
/// </remarks>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
	/// <summary>Opaque black, with an alpha of 255.</summary>
	public static ArgbColor Black { get; } = FromRgb(0, 0, 0);

	/// <summary>Opaque white, with an alpha of 255.</summary>
	public static ArgbColor White { get; } = FromRgb(255, 255, 255);

	/// <summary>Creates an opaque color from its red, green, and blue channels.</summary>
	public static ArgbColor FromRgb(byte r, byte g, byte b) => new(A: 255, r, g, b);

	/// <summary>Tries to parse a color written as hexadecimal digits after <c>#</c>.</summary>
	/// <param name="text">The text to parse; surrounding white space is ignored.</param>
	/// <param name="color">The parsed color when parsing succeeds; otherwise, the default value.</param>
	/// <returns><see langword="true"/> when <paramref name="text"/> is a hexadecimal color; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The accepted forms are <c>#RGB</c>, <c>#ARGB</c>, <c>#RRGGBB</c>, and <c>#AARRGGBB</c>, with digits of either case.
	/// The alpha channel comes first, as in the WPF and Avalonia notations, and an omitted alpha is fully opaque; a short form
	/// doubles each digit. Color names, <c>rgb(...)</c> functions, and other toolkit notations are not accepted, so a host that wants one parses
	/// it itself.
	/// </remarks>
	public static bool TryParse(string? text, out ArgbColor color)
	{
		color = default;

		if (text is null)
			return false;

		ReadOnlySpan<char> digits = text.AsSpan().Trim();

		if (digits.IsEmpty || digits[0] != '#')
			return false;

		digits = digits[1..];

		return digits.Length switch
		{
			3 => TryReadNibbles(digits, hasAlpha: false, shortForm: true, out color),
			4 => TryReadNibbles(digits, hasAlpha: true, shortForm: true, out color),
			6 => TryReadNibbles(digits, hasAlpha: false, shortForm: false, out color),
			8 => TryReadNibbles(digits, hasAlpha: true, shortForm: false, out color),
			_ => false,
		};
	}

	private static bool TryReadNibbles(ReadOnlySpan<char> digits, bool hasAlpha, bool shortForm, out ArgbColor color)
	{
		color = default;

		Span<byte> channels = stackalloc byte[4];

		int channelCount = hasAlpha ? 4 : 3;
		int digitsPerChannel = shortForm ? 1 : 2;

		for (int channel = 0; channel < channelCount; channel++)
		{
			int value = 0;

			for (int digit = 0; digit < digitsPerChannel; digit++)
			{
				int nibble = GetHexValue(digits[(channel * digitsPerChannel) + digit]);

				if (nibble < 0)
					return false;

				value = (value << 4) | nibble;
			}

			channels[channel] = (byte)(shortForm ? (value << 4) | value : value);
		}

		color = hasAlpha
			? new ArgbColor(A: channels[0], R: channels[1], G: channels[2], B: channels[3])
			: FromRgb(channels[0], channels[1], channels[2]);

		return true;
	}

	private static int GetHexValue(char character)
	{
		return character switch
		{
			>= '0' and <= '9' => character - '0',
			>= 'a' and <= 'f' => character - 'a' + 10,
			>= 'A' and <= 'F' => character - 'A' + 10,
			_ => -1,
		};
	}

	/// <summary>Returns the luma of the color as the weighted average of its gamma-compressed channels (Rec. 601).</summary>
	/// <returns>The luma in the range <c>0</c> to <c>1</c>.</returns>
	/// <remarks>
	/// The exact luminance model does not matter for callers that only decide whether a color is light or dark.
	/// </remarks>
	public double GetLuma() => ((0.299 * R) + (0.587 * G) + (0.114 * B)) / 255.0;

	/// <summary>Blends two colors by the supplied ratio, interpolating every channel.</summary>
	/// <param name="first">The color at a ratio of <c>0</c>.</param>
	/// <param name="second">The color at a ratio of <c>1</c>.</param>
	/// <param name="ratio">The blend ratio; <c>0</c> returns <paramref name="first"/> and <c>1</c> returns <paramref name="second"/>.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="ratio"/> is outside <c>0</c> to <c>1</c>, or is NaN.</exception>
	public static ArgbColor Blend(ArgbColor first, ArgbColor second, double ratio)
	{
		// A ratio outside the range extrapolates past the channel bounds, and the byte casts below would wrap such a value silently.
		if (double.IsNaN(ratio) || ratio < 0.0 || ratio > 1.0)
			throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "The blend ratio must be between 0 and 1.");

		double inverseRatio = 1.0 - ratio;

		return new ArgbColor(
			A: (byte)Math.Round((first.A * inverseRatio) + (second.A * ratio)),
			R: (byte)Math.Round((first.R * inverseRatio) + (second.R * ratio)),
			G: (byte)Math.Round((first.G * inverseRatio) + (second.G * ratio)),
			B: (byte)Math.Round((first.B * inverseRatio) + (second.B * ratio))
		);
	}
}
