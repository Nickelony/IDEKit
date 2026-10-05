using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;

/// <summary>Groups the required host hooks used by the <see cref="SignatureHelpController"/>.</summary>
/// <param name="BuildRequest">
/// Builds the provider request for a zero-based document offset and trigger context; it returns <see langword="null"/> to veto the request.
/// The <see cref="SignatureHelpContext"/> describes how the request was triggered and carries the visible payload for retriggers, so the host
/// passes it to the request it builds. The hook runs on the controller's dispatcher thread when a request starts. A <see langword="null"/> request,
/// like a <see langword="null"/> provider result, dismisses the current signature help presentation. The token the controller passes
/// to the provider is canceled when a newer request supersedes this one, when <see cref="SignatureHelpController.CancelInFlightRequest"/> is called,
/// and when the controller is dismissed or disposed. Results of a canceled request are rejected even when the provider ignores the token.
/// </param>
/// <param name="ShowSignatureHelp">
/// Shows the given signature help information at the document offset of the request that produced it, where a popup anchors.
/// The controller assumes the host displayed the popup when the hook returns without throwing and tracks the visibility itself.
/// Overload navigation (<see cref="SignatureHelpController.SelectNextSignature"/> and <see cref="SignatureHelpController.SelectPreviousSignature"/>)
/// invokes it again with the updated payload and the same offset without another provider request, so a host that recreates its popup on every show
/// sees one recreation per overload step.
/// </param>
/// <param name="DismissSignatureHelp">
/// Dismisses the current signature help presentation. The callback is invoked on every dismissal,
/// including <see cref="SignatureHelpController.Dispose"/> and dismissals with nothing visible, so implementations must be idempotent
/// and must tolerate hiding a popup that is not shown.
/// </param>
/// <remarks>
/// The hooks carry the host's decisions only: the controller reads
/// the caret through the delegate it was given and asks the provider for the signature help. The controller rejects
/// a hook that is <see langword="null"/> with an <see cref="ArgumentNullException"/>; <see langword="default"/>, an explicit <see langword="null"/>,
/// and an object initializer that omits a hook all produce one. A <c>BuildRequest</c> that captures a document identifier serves that one document,
/// as the
/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#one-controller-per-document">document rule</see>
/// says.
/// </remarks>
public readonly record struct SignatureHelpControllerHooks(
	Func<int, SignatureHelpContext, SignatureHelpRequest?> BuildRequest,
	Action<SignatureHelpInformation, int> ShowSignatureHelp,
	Action DismissSignatureHelp
);
