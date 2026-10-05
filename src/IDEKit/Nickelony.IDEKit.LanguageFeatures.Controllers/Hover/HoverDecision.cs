using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;

/// <summary>Describes the host hover evaluation input and diagnostic-display state for a hovered offset.</summary>
/// <remarks>
/// A host builds this from the hovered offset and its diagnostic information. A completed request combines hover
/// content with the diagnostic and is gated by <see cref="CanShowHoverContent"/>; a request that was not made or failed shows the diagnostic alone,
/// gated by <see cref="CanShowDiagnosticFallback"/>.
/// </remarks>
public readonly record struct HoverDecision
{
	public bool ShouldRequestHover { get; init; }

	/// <summary>
	/// The zero-based UTF-16 offset for which hover is requested; meaningful when <see cref="ShouldRequestHover"/> is <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// When <see cref="ShouldRequestHover"/> is set, the offset must address a position within the document snapshot, because the hover request path
	/// validates it.
	/// </remarks>
	public int RequestOffset { get; init; }

	public bool CanShowHoverContent { get; init; }

	/// <summary>
	/// Whether diagnostic information may be shown when a hover request is not made or fails;
	/// successful requests use <see cref="CanShowHoverContent"/>.
	/// </summary>
	public bool CanShowDiagnosticFallback { get; init; }

	/// <summary>The diagnostic information at the hovered offset, when available.</summary>
	public Diagnostic? Diagnostic { get; init; }
}
