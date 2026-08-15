using Nickelony.IDEKit.Infrastructure;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
#endif

/// <summary>
/// Stores the menu presentation options used by a code-action controller.
/// </summary>
/// <remarks>
/// The menu geometry is toolkit presentation, so it lives with the binding instead of the
/// neutral controller options record, whose members stay editor-framework neutral. A host can start
/// from <see cref="Default"/> and override only the options it needs with a <c>with</c> expression,
/// and every option validates itself when it is assigned, so an invalid value is rejected at the
/// offending <c>with</c> expression or object initializer instead of when the controller is created.
/// </remarks>
public sealed record TextCodeActionMenuOptions
{
	private readonly double _maxHeight = 320.0;
	private readonly double _anchorXOffset = 2.0;
	private readonly double _caretAnchorYOffset = 2.0;
	private readonly double _marginAnchorYOffset = 4.0;

	/// <summary>
	/// Gets the maximum height of the actions menu in device-independent pixels; a taller item list
	/// scrolls. Defaults to 320.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double MaxHeight
	{
		get => _maxHeight;
		init => _maxHeight = NumericValidation.FiniteNonNegative(value, nameof(MaxHeight));
	}

	/// <summary>
	/// Gets the horizontal offset between the text view's left edge and the menu's left edge in
	/// device-independent pixels, applied to both the caret- and the margin-anchored path. Defaults to 2.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double AnchorXOffset
	{
		get => _anchorXOffset;
		init => _anchorXOffset = NumericValidation.FiniteNonNegative(value, nameof(AnchorXOffset));
	}

	/// <summary>
	/// Gets the vertical offset below the anchor line's text top in device-independent pixels, applied to
	/// the caret-anchored path. Defaults to 2.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double CaretAnchorYOffset
	{
		get => _caretAnchorYOffset;
		init => _caretAnchorYOffset = NumericValidation.FiniteNonNegative(value, nameof(CaretAnchorYOffset));
	}

	/// <summary>
	/// Gets the vertical offset below the click position in device-independent pixels, applied to the
	/// margin-anchored path. Defaults to 4.
	/// </summary>
	/// <remarks>
	/// The margin path anchors at the click position instead of the line top, and its larger default
	/// offset keeps the mouse release that opened the menu from landing on a menu item.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double MarginAnchorYOffset
	{
		get => _marginAnchorYOffset;
		init => _marginAnchorYOffset = NumericValidation.FiniteNonNegative(value, nameof(MarginAnchorYOffset));
	}

	/// <summary>
	/// Gets the default options: a 320 device-independent-pixel menu height cap, a horizontal anchor
	/// offset of 2, and vertical anchor offsets of 2 (caret) and 4 (margin).
	/// </summary>
	public static TextCodeActionMenuOptions Default { get; } = new();
}
