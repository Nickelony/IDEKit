#if AVALONIAEDIT
using AvaloniaEdit.CodeCompletion;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
#endif
using Nickelony.IDEKit.Infrastructure;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Stores the timing, sizing, and window-behavior options used by the <see cref="TextCompletionController"/>.
/// </summary>
/// <remarks>
/// <para>
/// A host can start from <see cref="Default"/> and override only the options it needs with a <c>with</c>
/// expression; the instance initializers below are the defaults that <see cref="Default"/> carries. The sizing
/// defaults describe a conventional completion item list with optional icon and detail columns: a
/// 420-pixel content-width floor, a 52-pixel horizontal chrome, a 920-pixel maximum window width, a
/// 300-pixel maximum height (the editor's own cap), a 24-pixel icon column reserved for items that supply
/// an image, and a 12-pixel gap between text and detail whenever detail text is present.
/// </para>
/// <para>
/// The width options live in the two spaces of the sizing formula: <see cref="WindowMinContentWidth"/> is a
/// content-space floor applied before the chrome is added, while <see cref="WindowMaxWidth"/> and
/// <see cref="WindowMaxHeight"/> are window-space caps. The controller rejects an options record whose
/// maximum window width cannot hold the minimum content width plus the chrome, because such a record could
/// never honor its own floor.
/// </para>
/// <para>
/// Different item templates require different values; a host that measures its items with a
/// different layout supplies <see cref="TextCompletionControllerHooks.MeasureItemWidth"/> instead of these numbers.
/// </para>
/// <para>
/// Every numeric option validates itself when it is assigned, so an invalid value is rejected at the
/// offending <c>with</c> expression or object initializer instead of when the controller is constructed;
/// only the width relationship that spans three options is rejected at controller construction.
/// </para>
/// </remarks>
public sealed record TextCompletionControllerOptions
{
	private readonly TimeSpan _requestDebounceDelay = TimeSpan.FromMilliseconds(120.0);
	private readonly TimeSpan _tooltipResolveDelay = TimeSpan.FromMilliseconds(120.0);
	private readonly double _windowMinContentWidth = CompletionWindowDefaults.DefaultWindowMinContentWidth;
	private readonly double _windowMaxWidth = CompletionWindowDefaults.DefaultWindowMaxWidth;
	private readonly double _windowMaxHeight = CompletionWindowDefaults.DefaultWindowMaxHeight;
	private readonly double _windowHorizontalChrome = CompletionWindowDefaults.DefaultWindowHorizontalChrome;
	private readonly double _itemIconWidth = CompletionWindowDefaults.DefaultItemIconWidth;
	private readonly double _itemDetailSpacing = CompletionWindowDefaults.DefaultItemDetailSpacing;

	/// <summary>
	/// Gets the debounce delay before a scheduled completion request runs. Defaults to 120 milliseconds.
	/// </summary>
	/// <remarks>
	/// The debounce is deliberate for editor triggers: it collapses a burst of keystrokes into one provider
	/// round trip, trading a little latency for far fewer requests. A host that prefers a snappier popup (with
	/// more requests) lowers this value; the delay is fully host-tunable.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	public TimeSpan RequestDebounceDelay
	{
		get => _requestDebounceDelay;
		init => _requestDebounceDelay = NumericValidation.NonNegative(value, nameof(RequestDebounceDelay));
	}

	/// <summary>
	/// Gets the delay before a completion tooltip update is resolved. Defaults to 120 milliseconds.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	public TimeSpan TooltipResolveDelay
	{
		get => _tooltipResolveDelay;
		init => _tooltipResolveDelay = NumericValidation.NonNegative(value, nameof(TooltipResolveDelay));
	}

	/// <summary>
	/// Gets the minimum content width of the completion window, measured before the window chrome is added.
	/// The measured content width (floored at this value) plus <see cref="WindowHorizontalChrome"/> becomes the
	/// window width, capped by <see cref="WindowMaxWidth"/>. Defaults to 420.
	/// </summary>
	/// <remarks>
	/// The controller rejects an options record whose <see cref="WindowMaxWidth"/> is smaller than this value
	/// plus <see cref="WindowHorizontalChrome"/>, because the window could never honor this floor.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double WindowMinContentWidth
	{
		get => _windowMinContentWidth;
		init => _windowMinContentWidth = NumericValidation.FiniteNonNegative(value, nameof(WindowMinContentWidth));
	}

	/// <summary>
	/// Gets the maximum width of the completion window, including the window chrome; the measured content
	/// width plus <see cref="WindowHorizontalChrome"/> is clamped to this value. Defaults to 920.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double WindowMaxWidth
	{
		get => _windowMaxWidth;
		init => _windowMaxWidth = NumericValidation.FiniteNonNegative(value, nameof(WindowMaxWidth));
	}

	/// <summary>
	/// Gets the maximum height of the completion window. The window sizes to its content up to this cap.
	/// Defaults to 300, which is the editor's own <c>CompletionWindow</c> height cap.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double WindowMaxHeight
	{
		get => _windowMaxHeight;
		init => _windowMaxHeight = NumericValidation.FiniteNonNegative(value, nameof(WindowMaxHeight));
	}

	/// <summary>
	/// Gets the horizontal window chrome added to the measured content width when the completion window width
	/// is computed. Defaults to 52.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double WindowHorizontalChrome
	{
		get => _windowHorizontalChrome;
		init => _windowHorizontalChrome = NumericValidation.FiniteNonNegative(value, nameof(WindowHorizontalChrome));
	}

	/// <summary>
	/// Gets the width the default measurement adds for an item that supplies an image, covering its icon
	/// column. Defaults to 24.
	/// </summary>
	/// <remarks>
	/// The width is added only when the item's <see cref="ICompletionData.Image"/> is not
	/// <see langword="null"/>; an item without an image is measured as wide as its text (and detail). The
	/// package's own <see cref="TextCompletionItemCompletionData"/> renders no image, so its items are
	/// measured without the column. A host whose template reserves an icon column even for imageless items
	/// measures its items through <see cref="TextCompletionControllerHooks.MeasureItemWidth"/> instead.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double ItemIconWidth
	{
		get => _itemIconWidth;
		init => _itemIconWidth = NumericValidation.FiniteNonNegative(value, nameof(ItemIconWidth));
	}

	/// <summary>
	/// Gets the horizontal spacing between an item's text and its detail text. Defaults to 12.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double ItemDetailSpacing
	{
		get => _itemDetailSpacing;
		init => _itemDetailSpacing = NumericValidation.FiniteNonNegative(value, nameof(ItemDetailSpacing));
	}

	/// <summary>
	/// Gets a value indicating whether the completion window is kept from activating when it is clicked, so a
	/// click in the list never takes focus from the editor. Defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// With the default, the controller applies the binding's non-activation policy to the created window and
	/// installs explicit click selection, so a click in the list never takes focus from the editor. A host that
	/// wants the completion list to activate and take focus normally - for example an embedded editor - sets
	/// this option to <see langword="false"/>, and the controller then installs neither. The non-activating
	/// window is the library's default because an editor keeps the keyboard focus while the user picks an item,
	/// matching the editor's own completion windows.
	/// </remarks>
	public bool NonActivatingWindow { get; init; } = true;

	/// <summary>
	/// Gets a value indicating whether the completion window is closed when its filtered list becomes empty,
	/// instead of showing the editor's empty list template. Defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// The check runs when the window is shown (at <c>ContextIdle</c> priority after the initial selection) and
	/// as part of an in-place refresh. A host that prefers the editor's empty template sets this option to
	/// <see langword="false"/>.
	/// </remarks>
	public bool CloseWhenEmpty { get; init; } = true;

	/// <summary>
	/// Gets a value indicating whether a character that the selected item declares as a commit character
	/// accepts the item while the character is typed. Defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// The policy consults the selected item through <see cref="ICommitCharacterCompletionData"/>: when the
	/// typed character is one of the item's declared characters, the item is committed and the character is
	/// typed in the same input step. Items that declare no characters, and hosts whose item mapping does not
	/// implement the interface, are unaffected; a host that wants an editor that never accepts on a typed
	/// character sets this option to <see langword="false"/>.
	/// </remarks>
	public bool AcceptOnCommitCharacters { get; init; } = true;

	/// <summary>
	/// Gets the default options: a 120-millisecond debounce and tooltip delay, the library's item-template
	/// sizing profile described in the type remarks, a non-activatable completion window, closing a window
	/// whose filtered list became empty, and accepting the selected item on a typed commit character.
	/// </summary>
	public static TextCompletionControllerOptions Default { get; } = new();
}
