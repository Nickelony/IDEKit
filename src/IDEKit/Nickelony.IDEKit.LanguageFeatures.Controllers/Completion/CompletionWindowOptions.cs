using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Stores the sizing and behavior options of the completion window that a binding's <c>CompletionPresenter</c> shows.</summary>
/// <remarks>
/// <para>
/// The record holds plain numbers and flags only, so it names no toolkit type and sits with the other neutral completion options,
/// such as <see cref="CompletionControllerOptions"/>. The binding's presenter applies the geometry to its window and honors the activation policy
/// with its own toolkit handlers. A host can start from <see cref="Default"/> and override only the options it needs with a <c>with</c> expression;
/// the instance initializers below are the defaults that <see cref="Default"/> carries.
/// </para>
/// <para>
/// The presenter rejects an options record whose <see cref="MaxWidth"/> cannot hold
/// <see cref="MinContentWidth"/> plus <see cref="HorizontalChrome"/>, because such a record could never honor its own floor.
/// </para>
/// <para>
/// The width, chrome, icon, and spacing defaults describe the library's own item template, a list with an optional
/// icon column and optional detail text, and are not an editor requirement. A host with a different template overrides them or supplies the binding's
/// <c>CompletionPresenterHooks.MeasureItemWidth</c> hook instead of these numbers.
/// </para>
/// <para>
/// Every numeric option validates itself when it is assigned, so an invalid value is rejected at the offending <c>with</c> expression or object
/// initializer instead of when the presenter is constructed; only the cross-option width relationship is rejected at presenter construction.
/// </para>
/// </remarks>
public sealed record CompletionWindowOptions
{
	private const double DefaultMinContentWidth = 420.0;
	private const double DefaultMaxWidth = 920.0;
	private const double DefaultMaxHeight = 300.0;
	private const double DefaultHorizontalChrome = 52.0;
	private const double DefaultItemIconWidth = 24.0;
	private const double DefaultItemDetailSpacing = 12.0;

	private readonly double _minContentWidth = DefaultMinContentWidth;
	private readonly double _maxWidth = DefaultMaxWidth;
	private readonly double _maxHeight = DefaultMaxHeight;
	private readonly double _horizontalChrome = DefaultHorizontalChrome;
	private readonly double _itemIconWidth = DefaultItemIconWidth;
	private readonly double _itemDetailSpacing = DefaultItemDetailSpacing;

	/// <summary>The minimum content width of the completion window, measured before the window chrome is added; defaults to 420.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	/// <remarks>
	/// The measured content width (floored at this value) plus <see cref="HorizontalChrome"/> becomes
	/// the window width, capped by <see cref="MaxWidth"/>. The presenter rejects an options record whose <see cref="MaxWidth"/> is smaller
	/// than this value plus <see cref="HorizontalChrome"/>, because the window could never honor this floor.
	/// </remarks>
	public double MinContentWidth
	{
		get => _minContentWidth;
		init => _minContentWidth = NumericValidation.FiniteNonNegative(value, nameof(MinContentWidth));
	}

	/// <summary>
	/// The maximum width of the completion window, including the window chrome; the measured content width plus <see cref="HorizontalChrome"/>
	/// is clamped to this value; defaults to 920.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double MaxWidth
	{
		get => _maxWidth;
		init => _maxWidth = NumericValidation.FiniteNonNegative(value, nameof(MaxWidth));
	}

	/// <summary>The maximum height of the completion window, which sizes to its content up to this cap; defaults to 300.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double MaxHeight
	{
		get => _maxHeight;
		init => _maxHeight = NumericValidation.FiniteNonNegative(value, nameof(MaxHeight));
	}

	/// <summary>
	/// The horizontal window chrome added to the measured content width when the completion window width is computed; defaults to 52.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double HorizontalChrome
	{
		get => _horizontalChrome;
		init => _horizontalChrome = NumericValidation.FiniteNonNegative(value, nameof(HorizontalChrome));
	}

	/// <summary>The width the default measurement adds for an item that supplies an image, covering its icon column; defaults to 24.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	/// <remarks>
	/// The width is added only when the item's <c>Image</c> is not <see langword="null"/>; an item without an image
	/// is measured as wide as its text (and detail). The binding's own <c>CompletionItemAdapter</c> renders no image, so its items are measured
	/// without the column. A host whose template reserves an icon column even for imageless items measures its items through the binding's
	/// <c>CompletionPresenterHooks.MeasureItemWidth</c> hook instead.
	/// </remarks>
	public double ItemIconWidth
	{
		get => _itemIconWidth;
		init => _itemIconWidth = NumericValidation.FiniteNonNegative(value, nameof(ItemIconWidth));
	}

	/// <summary>The horizontal spacing between an item's text and its detail text; defaults to 12.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double ItemDetailSpacing
	{
		get => _itemDetailSpacing;
		init => _itemDetailSpacing = NumericValidation.FiniteNonNegative(value, nameof(ItemDetailSpacing));
	}

	/// <summary>Whether the completion window is kept from activating when it is clicked; defaults to <see langword="true"/>.</summary>
	/// <remarks>
	/// With the default, the presenter applies the binding's non-activation policy to the created window and installs explicit click selection,
	/// so a click in the list never takes focus from the editor. A host that wants the completion list to activate and take focus normally -
	/// for example an embedded editor - sets this option to <see langword="false"/>, and the presenter then installs neither.
	/// </remarks>
	public bool PreventActivation { get; init; } = true;

	/// <summary>
	/// Whether the completion window is closed when its filtered list becomes empty, instead of showing the editor's empty list template;
	/// defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// The check runs when the window is shown (at <c>ContextIdle</c> priority after the initial selection) and as part of an in-place refresh.
	/// A host that prefers the editor's empty template sets this option to <see langword="false"/>.
	/// </remarks>
	public bool DismissWhenEmpty { get; init; } = true;

	/// <summary>The widest content the completed window can display: <see cref="MaxWidth"/> minus <see cref="HorizontalChrome"/>.</summary>
	/// <remarks>
	/// The measurement's early-exit cap and <see cref="GetWindowWidth"/> share this definition.
	/// </remarks>
	public double MaxContentWidth => MaxWidth - HorizontalChrome;

	/// <summary>The default options.</summary>
	public static CompletionWindowOptions Default { get; } = new();

	/// <summary>
	/// Returns the window width for a measured content width: the content width plus the horizontal chrome, capped at the maximum width.
	/// </summary>
	/// <param name="contentWidth">The measured content width, before the window chrome is added.</param>
	/// <returns>The width to apply to the completion window.</returns>
	public double GetWindowWidth(double contentWidth) => Math.Min(MaxWidth, contentWidth + HorizontalChrome);

	/// <summary>Checks the relation between the width members, which each member cannot check alone.</summary>
	/// <param name="parameterName">The name of the argument that carried the options, which the exception reports.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <see cref="MaxWidth"/> is smaller than <see cref="MinContentWidth"/> plus <see cref="HorizontalChrome"/>.
	/// </exception>
	/// <remarks>
	/// Each member validates itself when it is assigned, but a <c>with</c> expression assigns the members one at a time, so the relation
	/// can only be checked once the options are complete. A host that builds options calls this before using them.
	/// </remarks>
	public void Validate(string parameterName)
	{
		if (MaxWidth < MinContentWidth + HorizontalChrome)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				MaxWidth,
				"The maximum window width must be large enough for the minimum content width plus the horizontal window chrome."
			);
		}
	}
}
