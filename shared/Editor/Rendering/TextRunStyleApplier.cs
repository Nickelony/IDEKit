#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;
#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Rendering;

#else

namespace Nickelony.IDEKit.AvalonEdit.Rendering;

#endif

/// <summary>Applies <see cref="TextRunStyle"/> values to visual line elements.</summary>
/// <remarks>
/// <para>
/// The helpers contain the paint-time style application shared by colorizing transformers. The foreground brush is set
/// when the style carries one, the typeface changes only when the style requests bold or italic text, and the text decorations are set when the style
/// carries a non-empty collection.
/// </para>
/// <para>
/// The typeface overload accepts a caller-derived typeface so a transformer can cache the derived value per style
/// and base typeface; caching is the caller's responsibility, and the helper performs none. The supplied typeface is applied under the same condition
/// as the derived one: only when the style requests bold or italic text.
/// </para>
/// <para>
/// Brushes and decoration collections should be immutable before they are applied, so a shared value can be used between elements without observing
/// a later mutation.
/// </para>
/// </remarks>
public static class TextRunStyleApplier
{
	/// <summary>
	/// Applies the style to the visual line element, deriving the typeface from the element's current typeface when the style requests bold
	/// or italic text.
	/// </summary>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
	public static void Apply(VisualLineElement element, TextRunStyle style)
	{
		ArgumentNullException.ThrowIfNull(element);
		VisualLineElementTextRunProperties properties = element.TextRunProperties;

		if (style.Foreground is not null)
			properties.SetForegroundBrush(style.Foreground);

		if (style.IsBold || style.IsItalic)
			properties.SetTypeface(CreateTypeface(properties.Typeface, style));

		if (style.TextDecorations is { Count: > 0 })
			properties.SetTextDecorations(style.TextDecorations);
	}

	/// <summary>
	/// Applies the style to the visual line element, using the supplied typeface for the element when the style requests bold or italic text.
	/// </summary>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <param name="typeface">
	/// The typeface to apply when the style requests bold or italic text, typically a value derived
	/// with <see cref="CreateTypeface(Typeface, TextRunStyle)"/> from the element's current typeface and cached by the caller.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/> is <see langword="null"/>, <paramref name="typeface"/> is <see langword="null"/> where the binding's typeface type
	/// is a reference type.
	/// </exception>
	public static void Apply(VisualLineElement element, TextRunStyle style, Typeface typeface)
	{
		ArgumentNullException.ThrowIfNull(element);
#if !AVALONIAEDIT
		ArgumentNullException.ThrowIfNull(typeface);
#endif
		VisualLineElementTextRunProperties properties = element.TextRunProperties;

		if (style.Foreground is not null)
			properties.SetForegroundBrush(style.Foreground);

		if (style.IsBold || style.IsItalic)
			properties.SetTypeface(typeface);

		if (style.TextDecorations is { Count: > 0 })
			properties.SetTextDecorations(style.TextDecorations);
	}

	/// <summary>Creates a typeface by applying the style's bold and italic settings to a base typeface.</summary>
	/// <param name="baseTypeface">The base typeface to derive from.</param>
	/// <param name="style">The style whose bold and italic settings are applied.</param>
	/// <returns>The derived typeface.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="baseTypeface"/> is <see langword="null"/> where the binding's typeface type is a reference type.
	/// </exception>
	/// <remarks>
	/// A disabled setting preserves the corresponding weight or style from the base typeface.
	/// </remarks>
	public static Typeface CreateTypeface(Typeface baseTypeface, TextRunStyle style)
	{
#if !AVALONIAEDIT
		ArgumentNullException.ThrowIfNull(baseTypeface);
#endif
		FontStyle fontStyle = style.IsItalic ? FontStyles.Italic : baseTypeface.Style;
		FontWeight fontWeight = style.IsBold ? FontWeights.Bold : baseTypeface.Weight;

		// Reapplying traits the base typeface already carries must not allocate a new typeface. The paint
		// path applies cached styles to many elements per pass, and a style whose traits already match the
		// base (for example bold text inside a bold base) would otherwise allocate per element.
		if (fontStyle == baseTypeface.Style && fontWeight == baseTypeface.Weight)
			return baseTypeface;

		return new Typeface(fontFamily: baseTypeface.FontFamily, style: fontStyle, weight: fontWeight, stretch: baseTypeface.Stretch);
	}
}
