using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

/// <summary>Provides the WPF half of the brush and typeface readers that the shared TextMate rendering tests call.</summary>
internal static class TextMateRenderingTestHelpers
{
	/// <summary>
	/// Tries to read the color of a brush that is a plain solid color; the binding's solid-brush type differs, so the runtime test is contained here.
	/// </summary>
	/// <param name="brush">The brush to read, or <see langword="null"/>.</param>
	/// <param name="color">The brush color when the brush is a solid color; otherwise, the default color.</param>
	/// <returns><see langword="true"/> when the brush is a solid-color brush.</returns>
	internal static bool TryGetBrushColor(Brush? brush, out Color color)
	{
		if (brush is SolidColorBrush solidBrush)
		{
			color = solidBrush.Color;
			return true;
		}

		color = default;
		return false;
	}

	/// <summary>Asserts that two typefaces are the same cached value.</summary>
	/// <param name="expected">The expected typeface.</param>
	/// <param name="actual">The actual typeface.</param>
	/// <remarks>
	/// The WPF typeface type is a reference type, where reference identity is checkable.
	/// </remarks>
	internal static void AssertSameTypeface(Typeface expected, Typeface actual) => Assert.AreSame(expected, actual);
}
