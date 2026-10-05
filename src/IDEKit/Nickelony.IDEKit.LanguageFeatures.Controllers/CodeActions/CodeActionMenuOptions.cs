using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

/// <summary>Stores the menu presentation options used by a binding's code-action presenter.</summary>
/// <remarks>
/// The record holds the menu geometry as plain numbers, so it names no toolkit type, and it stays
/// apart from <see cref="CodeActionControllerOptions"/> because only the presenter reads it. A host can start from <see cref="Default"/> and override
/// only the options it needs with a <c>with</c> expression, and every option validates itself when it is assigned, so an invalid value is rejected
/// at the offending <c>with</c> expression or object initializer instead of when the presenter is created.
/// </remarks>
public sealed record CodeActionMenuOptions
{
	private readonly double _maxHeight = 320.0;
	private readonly double _anchorXOffset = 2.0;
	private readonly double _caretAnchorYOffset = 2.0;
	private readonly double _marginAnchorYOffset = 4.0;

	/// <summary>The maximum height of the actions menu in device-independent pixels; a taller item list scrolls; defaults to 320.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double MaxHeight
	{
		get => _maxHeight;
		init => _maxHeight = NumericValidation.FiniteNonNegative(value, nameof(MaxHeight));
	}

	/// <summary>
	/// The horizontal offset between the text view's left edge and the menu's left edge in device-independent pixels, applied to the caret-anchored
	/// and the margin-anchored paths; defaults to 2.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double AnchorXOffset
	{
		get => _anchorXOffset;
		init => _anchorXOffset = NumericValidation.FiniteNonNegative(value, nameof(AnchorXOffset));
	}

	/// <summary>
	/// The vertical offset below the anchor line's text top in device-independent pixels, applied to the caret-anchored path; defaults to 2.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double CaretAnchorYOffset
	{
		get => _caretAnchorYOffset;
		init => _caretAnchorYOffset = NumericValidation.FiniteNonNegative(value, nameof(CaretAnchorYOffset));
	}

	/// <summary>
	/// The vertical offset below the click position in device-independent pixels, applied to the margin-anchored path; defaults to 4.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	/// <remarks>
	/// The margin path anchors at the click position instead of the line top, and its larger default offset keeps the mouse release that opened
	/// the menu from landing on a menu item.
	/// </remarks>
	public double MarginAnchorYOffset
	{
		get => _marginAnchorYOffset;
		init => _marginAnchorYOffset = NumericValidation.FiniteNonNegative(value, nameof(MarginAnchorYOffset));
	}

	public static CodeActionMenuOptions Default { get; } = new();
}
