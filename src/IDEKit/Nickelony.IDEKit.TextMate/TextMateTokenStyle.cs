using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.TextMate;

/// <summary>The resolved style of a TextMate token: a foreground color and its font traits, independent of any UI toolkit.</summary>
/// <param name="Foreground">The foreground color, or <see langword="null"/> when no rule or theme default sets one.</param>
/// <param name="IsBold">Whether the token is bold.</param>
/// <param name="IsItalic">Whether the token is italic.</param>
/// <param name="IsUnderline">Whether the token is underlined.</param>
/// <param name="IsStrikethrough">Whether the token is struck through.</param>
/// <remarks>
/// An editor binding converts the style to the toolkit's own brush, typeface, and text decorations before it paints.
/// </remarks>
public readonly record struct TextMateTokenStyle(ArgbColor? Foreground, bool IsBold, bool IsItalic, bool IsUnderline, bool IsStrikethrough)
{
	/// <summary>A style that requests no formatting.</summary>
	public static TextMateTokenStyle Empty { get; } =
		new(Foreground: null, IsBold: false, IsItalic: false, IsUnderline: false, IsStrikethrough: false);

	/// <summary>Indicates whether the style requests any formatting.</summary>
	public bool HasFormatting => Foreground is not null || IsBold || IsItalic || IsUnderline || IsStrikethrough;
}
