#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;
#else
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
namespace Nickelony.IDEKit.AvalonEdit.Rendering;
#endif

/// <summary>
/// Applies <see cref="ITextRunStyle"/> values to visual line elements.
/// </summary>
/// <remarks>
/// <para>
/// The helpers contain the paint-time style application shared by colorizing transformers: the
/// foreground brush is set when the style carries one, the typeface is changed only when the style
/// requests bold or italic text, and the text decorations are set when the style carries a non-empty
/// collection. Text decorations are applied through the element's <c>SetTextDecorations</c> setter.
/// </para>
/// <para>
/// The generic overloads never box a struct style: their null check is a runtime type test that the
/// value-type instantiation skips, and the constrained member calls need no conversion. The struct-based
/// contract avoids the per-element <c>HighlightingBrush.GetBrush(context)</c> call and the heap allocation
/// that the engine's <c>HighlightingColor</c> requires. A colorizing transformer that already builds a
/// <c>HighlightingColor</c> for the engine's own pipeline can keep using it; the two models serve
/// different pipelines.
/// </para>
/// <para>
/// The typeface overload accepts a caller-derived typeface, so a transformer can cache the derived
/// value per style and base typeface instead of constructing one for every element it paints. Caching
/// is the caller's responsibility: this helper performs no caching. The supplied typeface is applied
/// under the same condition as the derived one: only when the style requests bold or italic text.
/// </para>
/// <para>
/// Brushes and decoration collections should be immutable before they are applied, so a shared value can
/// be used between elements without observing a later mutation.
/// </para>
/// </remarks>
public static class TextRunStyleApplier
{
	/// <summary>
	/// Applies the style to the visual line element, deriving the typeface from the element's current
	/// typeface when the style requests bold or italic text.
	/// </summary>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/> or <paramref name="style"/> is <see langword="null"/>.
	/// </exception>
	public static void Apply(VisualLineElement element, ITextRunStyle style)
		=> Apply<ITextRunStyle>(element, style);

	/// <summary>
	/// Applies the style to the visual line element, deriving the typeface from the element's current
	/// typeface when the style requests bold or italic text, without boxing a struct style.
	/// </summary>
	/// <typeparam name="TTextRunStyle">The style type; pass the concrete type to avoid boxing.</typeparam>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/> is <see langword="null"/>, or <paramref name="style"/> is a
	/// <see langword="null"/> reference.
	/// </exception>
	public static void Apply<TTextRunStyle>(VisualLineElement element, TTextRunStyle style)
		where TTextRunStyle : ITextRunStyle
	{
		ArgumentNullException.ThrowIfNull(element);
		ThrowIfNullStyle(style);

		VisualLineElementTextRunProperties properties = element.TextRunProperties;

		if (style.Foreground is not null)
			properties.SetForegroundBrush(style.Foreground);

		if (style.IsBold || style.IsItalic)
			properties.SetTypeface(CreateTypeface(properties.Typeface, style));

		if (style.TextDecorations is { Count: > 0 })
			properties.SetTextDecorations(style.TextDecorations);
	}

	/// <summary>
	/// Applies the style to the visual line element, using the supplied typeface for the element when
	/// the style requests bold or italic text.
	/// </summary>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <param name="typeface">
	/// The typeface to apply when the style requests bold or italic text, typically a value derived
	/// with <see cref="CreateTypeface"/> from the element's current typeface and cached by the caller.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/> or <paramref name="style"/> is <see langword="null"/>, or
	/// <paramref name="typeface"/> is <see langword="null"/> where the binding's typeface type is a
	/// reference type.
	/// </exception>
	public static void Apply(VisualLineElement element, ITextRunStyle style, Typeface typeface)
		=> Apply<ITextRunStyle>(element, style, typeface);

	/// <summary>
	/// Applies the style to the visual line element, using the supplied typeface for the element when
	/// the style requests bold or italic text, without boxing a struct style.
	/// </summary>
	/// <typeparam name="TTextRunStyle">The style type; pass the concrete type to avoid boxing.</typeparam>
	/// <param name="element">The visual line element to apply the style to.</param>
	/// <param name="style">The style to apply.</param>
	/// <param name="typeface">
	/// The typeface to apply when the style requests bold or italic text, typically a value derived
	/// with <see cref="CreateTypeface{TTextRunStyle}(Typeface, TTextRunStyle)"/> from the element's current
	/// typeface and cached by the caller.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="element"/> is <see langword="null"/>, <paramref name="typeface"/> is
	/// <see langword="null"/> where the binding's typeface type is a reference type, or
	/// <paramref name="style"/> is a <see langword="null"/> reference.
	/// </exception>
	public static void Apply<TTextRunStyle>(VisualLineElement element, TTextRunStyle style, Typeface typeface)
		where TTextRunStyle : ITextRunStyle
	{
		ArgumentNullException.ThrowIfNull(element);
#if !AVALONIAEDIT
		ArgumentNullException.ThrowIfNull(typeface);
#endif
		ThrowIfNullStyle(style);

		VisualLineElementTextRunProperties properties = element.TextRunProperties;

		if (style.Foreground is not null)
			properties.SetForegroundBrush(style.Foreground);

		if (style.IsBold || style.IsItalic)
			properties.SetTypeface(typeface);

		if (style.TextDecorations is { Count: > 0 })
			properties.SetTextDecorations(style.TextDecorations);
	}

	/// <summary>
	/// Creates a typeface by applying the style's bold and italic settings to a base typeface.
	/// A disabled setting preserves the corresponding weight or style from the base typeface.
	/// </summary>
	/// <param name="baseTypeface">The base typeface to derive from.</param>
	/// <param name="style">The style whose bold and italic settings are applied.</param>
	/// <returns>The derived typeface.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="style"/> is <see langword="null"/>, or <paramref name="baseTypeface"/> is
	/// <see langword="null"/> where the binding's typeface type is a reference type.
	/// </exception>
	public static Typeface CreateTypeface(Typeface baseTypeface, ITextRunStyle style)
		=> CreateTypeface<ITextRunStyle>(baseTypeface, style);

	/// <summary>
	/// Creates a typeface by applying the style's bold and italic settings to a base typeface,
	/// without boxing a struct style.
	/// A disabled setting preserves the corresponding weight or style from the base typeface.
	/// </summary>
	/// <typeparam name="TTextRunStyle">The style type; pass the concrete type to avoid boxing.</typeparam>
	/// <param name="baseTypeface">The base typeface to derive from.</param>
	/// <param name="style">The style whose bold and italic settings are applied.</param>
	/// <returns>The derived typeface.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="style"/> is a <see langword="null"/> reference, or <paramref name="baseTypeface"/> is
	/// <see langword="null"/> where the binding's typeface type is a reference type.
	/// </exception>
	public static Typeface CreateTypeface<TTextRunStyle>(Typeface baseTypeface, TTextRunStyle style)
		where TTextRunStyle : ITextRunStyle
	{
#if !AVALONIAEDIT
		ArgumentNullException.ThrowIfNull(baseTypeface);
#endif
		ThrowIfNullStyle(style);

		FontStyle fontStyle = style.IsItalic ? FontStyles.Italic : baseTypeface.Style;
		FontWeight fontWeight = style.IsBold ? FontWeights.Bold : baseTypeface.Weight;

		// Reapplying traits the base typeface already carries must not allocate a new typeface: the paint
		// path applies cached styles to many elements per pass, and a style whose traits already match the
		// base (for example bold text inside a bold base) would otherwise allocate per element.
		if (fontStyle == baseTypeface.Style && fontWeight == baseTypeface.Weight)
			return baseTypeface;

		return new Typeface(baseTypeface.FontFamily, fontStyle, fontWeight, baseTypeface.Stretch);
	}

	/// <summary>
	/// Rejects a <see langword="null"/> style reference. The runtime type test keeps the null check out
	/// of the value-type instantiation, so a struct style is never boxed on the paint path.
	/// </summary>
	/// <typeparam name="TTextRunStyle">The style type.</typeparam>
	/// <param name="style">The style to check.</param>
	/// <exception cref="ArgumentNullException"><paramref name="style"/> is a <see langword="null"/> reference.</exception>
	private static void ThrowIfNullStyle<TTextRunStyle>(TTextRunStyle style)
		where TTextRunStyle : ITextRunStyle
	{
		if (!typeof(TTextRunStyle).IsValueType && style is null)
			throw new ArgumentNullException(nameof(style));
	}
}
