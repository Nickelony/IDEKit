namespace Nickelony.IDEKit.Core.LineStatus;

/// <summary>The uniform scale and the offsets that place an icon's geometry in a line-status margin.</summary>
/// <param name="Scale">The uniform scale that fits the geometry to the editor font.</param>
/// <param name="IconHeight">The height of the scaled icon.</param>
/// <param name="OffsetX">The horizontal offset that centers the scaled icon in the margin.</param>
/// <param name="OffsetY">The vertical offset that moves the top edge of the scaled icon to the origin.</param>
/// <remarks>
/// <see cref="LineStatusIconOperations.Place"/> computes it; the margin adds the icon's vertical position within its line
/// to <paramref name="OffsetY"/>.
/// </remarks>
public readonly record struct LineStatusIconPlacement(double Scale, double IconHeight, double OffsetX, double OffsetY);
