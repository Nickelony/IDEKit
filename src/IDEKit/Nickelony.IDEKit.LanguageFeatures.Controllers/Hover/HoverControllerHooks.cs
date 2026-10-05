using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;

/// <summary>Groups the required and optional host hooks used by the <see cref="HoverController"/>.</summary>
/// <remarks>
/// <para>
/// The hooks carry the host's decisions only. The controller reads the document offset under the pointer from its <see cref="IHoverPointerSource"/>,
/// and it asks the provider for the content. A controller without a provider shows the diagnostic that <see cref="BuildDecision"/> supplies
/// and nothing else.
/// </para>
/// <para>
/// Display is a single decision, so the controller exposes one display callback instead of separate diagnostic, hover, and combined callbacks:
/// it calls <see cref="ShowHover"/> with the hover content, the diagnostic, both, or neither, and the host renders or hides in one place.
/// </para>
/// </remarks>
public sealed class HoverControllerHooks
{
	/// <summary>
	/// The optional hook that builds the hover evaluation for a hovered offset, or <see langword="null"/> to request hover at the hovered offset
	/// and show the content.
	/// </summary>
	/// <remarks>
	/// It is invoked once per hover evaluation, plus once more when a completed result is about to be accepted and the pointer maps to a hovered
	/// offset different from the evaluated one, so it should be cheap or cache its result. A host supplies it to show a diagnostic, or to decline
	/// a hover while another popup is open.
	/// </remarks>
	public Func<int, HoverDecision>? BuildDecision { get; init; }

	/// <summary>The hook that builds the provider request for a request offset; it returns <see langword="null"/> to veto the request.</summary>
	/// <remarks>
	/// The hook is required when the controller has a provider,
	/// and a controller without one never calls it. The offset is <see cref="HoverDecision.RequestOffset"/> of the evaluation state, and the host
	/// supplies the document identifier and text the request carries. A <see langword="null"/> request concludes the evaluation like a state
	/// that declines the request: pending work is superseded and the diagnostic fallback is shown when the state allows it. The hook runs
	/// once per request on the controller's dispatcher thread. A hook that captures a document identifier serves that one document,
	/// as the
	/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#one-controller-per-document">document rule</see>
	/// says.
	/// </remarks>
	public Func<int, HoverRequest?>? BuildRequest { get; init; }

	/// <summary>
	/// The optional hook that resolves the request offset for a hovered offset. Returning <see langword="null"/> vetoes the completed result
	/// even when the hovered offset itself did not change; the hook must agree with <see cref="BuildDecision"/> so a completed result can be matched
	/// to the latest hover target.
	/// </summary>
	/// <remarks>
	/// The request always uses the offset carried by <see cref="BuildDecision"/>'s returned state, so the hook must agree with that state.
	/// When the hook is omitted and the builder requests hover for an offset that differs from the hovered offset, the completed result
	/// cannot be matched to the pointer position: it is discarded and the mismatch is logged once. Hosts that gate or remap the hover target
	/// (for example to only resolve complete words) supply the hook so the acceptance check remaps the pointer position the same way.
	/// </remarks>
	public Func<int, int?>? ResolveRequestOffset { get; init; }

	/// <summary>The hook that shows or hides the host's hover popup for a hover evaluation.</summary>
	/// <remarks>
	/// <para>
	/// The controller reports the admitted display decision of each concluded hover evaluation through this callback. The decision is a hover
	/// content, a diagnostic, or both. When the evaluation has nothing to show, both content arguments are <see langword="null"/>, so the host
	/// hides its popup instead of leaving stale content visible. That happens when the pointer left the hover target, the request state
	/// disallows the content, or a failed request resolved no fallback. Displaying and hiding live in one callback, so the newest evaluation
	/// always wins.
	/// </para>
	/// <para>
	/// Evaluations the controller discards do not invoke the callback and end without a display decision:
	/// <list type="bullet">
	/// <item>A result or a failure fallback that a newer evaluation, a cancellation, or an invalidation superseded.</item>
	/// <item>A result or a failure fallback rejected by the pointer or context-version check.</item>
	/// <item>An evaluation that concludes after disposal.</item>
	/// <item>A failure raised before a hovered offset was resolved.</item>
	/// </list>
	/// </para>
	/// <para>
	/// The third argument is the document offset the evaluation targets, where a popup anchors when its content carries no range: the request
	/// offset when the evaluation asked the provider, otherwise the hovered offset, and <see langword="null"/> when the evaluation found no hover target. The
	/// <c>Show</c> member of a binding's <c>HoverPresenter</c> matches the callback, so a host passes it as the hook.
	/// </para>
	/// <para>
	/// A host that also hides its popup on pointer-leave events must keep that hide path idempotent, because an evaluation with nothing to show
	/// calls the callback with both content arguments <see langword="null"/>.
	/// The callback should not throw; a failure escaping it is contained and logged.
	/// </para>
	/// </remarks>
	public required Action<HoverInformation?, Diagnostic?, int?> ShowHover { get; init; }

	/// <summary>The optional provider of a host context version that guards asynchronous hover work.</summary>
	/// <remarks>
	/// The controller captures the version before a request starts and discards the completed result when the version changed while the request
	/// was in flight. The provider must be cheap to call and return a value that changes when pending work admitted for an earlier host state
	/// must be abandoned, for example a document session, load, or replacement generation counter.
	/// </remarks>
	public Func<int>? ContextVersionProvider { get; init; }
}
