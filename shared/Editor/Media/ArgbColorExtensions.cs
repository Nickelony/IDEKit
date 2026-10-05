using Nickelony.IDEKit.Core.Media;
#if AVALONIAEDIT
using Avalonia.Media;
using Avalonia.Media.Immutable;
#else
using System.Windows.Media;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Media;

#else

namespace Nickelony.IDEKit.AvalonEdit.Media;

#endif

/// <summary>Converts the engine-free <see cref="ArgbColor"/> to the engine's color and brush types.</summary>
public static class ArgbColorExtensions
{
	/// <summary>Converts the color to the engine's color.</summary>
	/// <param name="color">The color to convert.</param>
	/// <returns>The engine color with the same alpha and channel values.</returns>
	public static Color ToColor(this ArgbColor color) => Color.FromArgb(a: color.A, r: color.R, g: color.G, b: color.B);

#if AVALONIAEDIT
	/// <summary>Converts the color to an immutable brush.</summary>
	/// <param name="color">The color of the brush.</param>
	/// <returns>An immutable solid-color brush, because Avalonia has no <c>Freeze</c>.</returns>
	public static ImmutableSolidColorBrush ToBrush(this ArgbColor color) => BrushAndPenFactory.CreateBrush(color.ToColor());
#else
	/// <summary>Converts the color to a frozen brush.</summary>
	/// <param name="color">The color of the brush.</param>
	/// <returns>A frozen solid-color brush, which several elements can share.</returns>
	public static SolidColorBrush ToBrush(this ArgbColor color) => BrushAndPenFactory.CreateBrush(color.ToColor());
#endif
}
