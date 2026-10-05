using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;

/// <summary>Describes the current state of the signature help presentation.</summary>
/// <param name="SignatureHelp">The signature help information, when available.</param>
/// <param name="IsVisible">Whether the signature help popup is visible.</param>
/// <param name="IsRequestInFlight">Whether a signature help request is currently in flight.</param>
/// <param name="IsRequestScheduled">Whether a signature help request is scheduled.</param>
/// <remarks>
/// The visibility union counts an in-flight request and a scheduled request, which a host may present as pending activity.
/// </remarks>
public readonly record struct SignatureHelpPresentationState(
	SignatureHelpInformation? SignatureHelp,
	bool IsVisible,
	bool IsRequestInFlight,
	bool IsRequestScheduled
)
{
	public static SignatureHelpPresentationState Empty { get; } =
		new(SignatureHelp: null, IsVisible: false, IsRequestInFlight: false, IsRequestScheduled: false);

	/// <summary>Whether the popup is visible, a request is in flight, or a request is scheduled.</summary>
	public bool IsPresentationVisibleOrRequestScheduled => IsVisible || IsRequestInFlight || IsRequestScheduled;
}
