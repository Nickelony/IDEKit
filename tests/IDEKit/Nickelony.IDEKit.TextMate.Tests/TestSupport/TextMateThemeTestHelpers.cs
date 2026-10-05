using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.TextMate.Tests;

internal static class TextMateThemeTestHelpers
{
	internal static TextMateTokenTheme CreateTheme(params TextMateTokenThemeRule[] rules) => new() { Rules = rules };

	/// <summary>Returns the resolved foreground in uppercase <c>#AARRGGBB</c> form.</summary>
	/// <param name="style">The style whose foreground is read.</param>
	/// <returns>The foreground color in uppercase <c>#AARRGGBB</c> form.</returns>
	internal static string GetForegroundColor(TextMateTokenStyle style)
	{
		Assert.IsNotNull(style.Foreground);

		ArgbColor color = style.Foreground.Value;

		return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
	}
}
