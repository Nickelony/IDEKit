#if AVALONIAEDIT
using AvaloniaEdit.CodeCompletion;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Stores the shared default sizing values for the completion window and its item measurement so the
/// options record and the sizing helper cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// The width floor, chrome, icon width, and detail spacing values describe a conventional completion item
/// list with an optional icon column and optional detail text; this is the library's template profile
/// rather than an editor requirement. Only <see cref="DefaultWindowMaxHeight"/> mirrors the editor, whose own
/// <see cref="CompletionWindow"/> caps its height at 300. Hosts with a
/// different template either override the corresponding options or replace the measurement entirely through
/// <see cref="TextCompletionControllerHooks.MeasureItemWidth"/>.
/// </para>
/// </remarks>
internal static class CompletionWindowDefaults
{
	/// <summary>
	/// The default minimum content width of the completion window in device-independent pixels, measured
	/// before the window chrome is added.
	/// </summary>
	internal const double DefaultWindowMinContentWidth = 420.0;

	/// <summary>
	/// The default maximum height of the completion window in device-independent pixels.
	/// </summary>
	internal const double DefaultWindowMaxHeight = 300.0;

	/// <summary>
	/// The default maximum width of the completion window in device-independent pixels.
	/// </summary>
	internal const double DefaultWindowMaxWidth = 920.0;

	/// <summary>
	/// The default horizontal window chrome in device-independent pixels that is added to the measured
	/// item content width.
	/// </summary>
	internal const double DefaultWindowHorizontalChrome = 52.0;

	/// <summary>
	/// The default width the measurement adds for a completion item that supplies an image, in
	/// device-independent pixels.
	/// </summary>
	internal const double DefaultItemIconWidth = 24.0;

	/// <summary>
	/// The default horizontal spacing between an item's text and its detail text in device-independent pixels.
	/// </summary>
	internal const double DefaultItemDetailSpacing = 12.0;
}
