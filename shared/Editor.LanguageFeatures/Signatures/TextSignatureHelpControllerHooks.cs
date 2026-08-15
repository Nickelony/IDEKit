using Nickelony.IDEKit.IntelliSense.Signatures;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Signatures;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
#endif

/// <summary>
/// Groups the required host hooks used by the <see cref="TextSignatureHelpController"/>. The caret, request,
/// show, and dismiss hooks are required.
/// </summary>
/// <remarks>
/// See the individual properties for the hook contracts.
/// </remarks>
public sealed class TextSignatureHelpControllerHooks
{
	/// <summary>
	/// Gets the hook that returns the current zero-based document caret offset, or a negative value when
	/// there is no caret.
	/// </summary>
	/// <remarks>
	/// The hook is read when a debounced refresh is scheduled. A throwing getter is contained and logged
	/// like the other host callbacks: the pending refresh is canceled instead of starting one, matching a
	/// getter that reports no caret.
	/// </remarks>
	public required Func<int> GetCurrentCaretOffset { get; init; }

	/// <summary>
	/// Gets the hook that requests signature help asynchronously for a zero-based document offset,
	/// trigger context, and cancellation token.
	/// </summary>
	/// <remarks>
	/// The <see cref="TextSignatureHelpContext"/> describes how the request was triggered and carries
	/// the previously shown payload for retriggers. The token is canceled when a newer request supersedes
	/// this one, when <see cref="TextSignatureHelpController.CancelInFlightRequest"/> is called, and when
	/// the controller is dismissed or disposed; implementations should observe it so the active provider
	/// call can stop early. Results of a canceled request are rejected even when the provider ignores the
	/// token. A <see langword="null"/> result dismisses the current signature help presentation.
	/// </remarks>
	public required Func<int, TextSignatureHelpContext, CancellationToken, Task<TextSignatureHelp?>> RequestSignatureHelpAsync { get; init; }

	/// <summary>
	/// Gets the hook that shows the given signature help information. The controller assumes the host
	/// displayed the popup when the hook returns without throwing and tracks the visibility itself.
	/// </summary>
	/// <remarks>
	/// Overload navigation (<see cref="TextSignatureHelpController.SelectNextSignature"/> and
	/// <see cref="TextSignatureHelpController.SelectPreviousSignature"/>) invokes this callback again with the
	/// updated payload without another provider request, so a host that recreates its popup on every show sees
	/// one recreation per overload step.
	/// </remarks>
	public required Action<TextSignatureHelp> ShowSignatureHelp { get; init; }

	/// <summary>
	/// Gets the hook that dismisses the current signature help presentation.
	/// </summary>
	/// <remarks>
	/// The callback is invoked on every dismissal, including <see cref="TextSignatureHelpController.Dispose"/> and
	/// dismissals with nothing visible, so implementations must be idempotent and must tolerate hiding a popup
	/// that is not shown.
	/// </remarks>
	public required Action DismissSignatureHelp { get; init; }
}
