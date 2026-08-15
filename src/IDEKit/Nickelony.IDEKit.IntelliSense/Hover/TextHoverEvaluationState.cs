using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.IDEKit.IntelliSense.Hover;

/// <summary>
/// Describes the host hover evaluation input and diagnostic-display state for a hovered offset.
/// </summary>
/// <remarks>
/// <para>
/// A host builds this state from the hovered offset (for example the identifier at that offset) and
/// its diagnostic information, then passes it to its hover logic, which uses the flags to decide
/// whether to request hover content or fall back to a diagnostic message. This is hover evaluation
/// input rather than a provider contract or a request snapshot: it describes what the editor may
/// show at one hover offset, not a document snapshot.
/// </para>
/// <para>
/// The host's hover logic applies one precedence rule: a completed request uses
/// <see cref="CanShowHoverContent"/> and combines the hover content with the diagnostic (either may be
/// absent); when the request was not made or failed, the diagnostic is shown alone only when
/// <see cref="CanShowDiagnosticFallback"/> allows it. The flags describe the permissions the host
/// grants for this hover state rather than the request outcome: the host's logic already knows
/// whether a request was issued or failed and sets the flags accordingly. The host selects which
/// diagnostic to surface when its diagnostics provider offers more than one (for example the first
/// or the highest-severity one), because the presentation renders exactly one diagnostic message.
/// </para>
/// <para>
/// The record uses value equality over all components, including <see cref="DiagnosticInfo"/>, which
/// itself uses structural equality.
/// </para>
/// <para>
/// Every member is an init-only property rather than a positional parameter, so a construction site
/// names the two adjacent permission booleans (<see cref="CanShowHoverContent"/> and
/// <see cref="CanShowDiagnosticFallback"/>) instead of relying on argument order.
/// </para>
/// <para>
/// This is the deliberate exception to the rule that hover presentation ships with the editor
/// binding: it carries no rendering and no toolkit type, only the framework-neutral permission flags
/// a host's hover logic decides on, so it belongs with the other hover payload values.
/// </para>
/// </remarks>
public readonly record struct TextHoverEvaluationState
{
	/// <summary>
	/// Gets a value indicating whether a hover request should be issued for the hovered offset.
	/// </summary>
	public bool ShouldRequestHover { get; init; }

	/// <summary>
	/// Gets the zero-based UTF-16 offset for which hover is requested; meaningful when
	/// <see cref="ShouldRequestHover"/> is <see langword="true"/>. When <see cref="ShouldRequestHover"/>
	/// is set, the offset must address a position within the document snapshot, because the hover
	/// request path validates it.
	/// </summary>
	public int RequestOffset { get; init; }

	/// <summary>
	/// Gets a value indicating whether hover content may be shown for this hover state.
	/// </summary>
	public bool CanShowHoverContent { get; init; }

	/// <summary>
	/// Gets a value indicating whether diagnostic information may be shown when a hover request is not
	/// made or fails; successful requests use <see cref="CanShowHoverContent"/>.
	/// </summary>
	public bool CanShowDiagnosticFallback { get; init; }

	/// <summary>
	/// Gets the diagnostic information at the hovered offset, when available.
	/// </summary>
	public TextDiagnostic? DiagnosticInfo { get; init; }
}
