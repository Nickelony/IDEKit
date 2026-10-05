namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Shows the decisions of a <see cref="CompletionController"/> in an editor's completion window and reports what the window shows.</summary>
/// <remarks>
/// <para>
/// A binding implements the contract over its completion window, and a host with another editor implements it over its own popup.
/// The controller calls <see cref="ApplyDirective"/> when a decision is ready and <see cref="Dismiss"/> when the editor replaces its document,
/// both on its dispatcher's thread, and it reads the state members when it publishes <see cref="CompletionController.CurrentPresentation"/>,
/// so a presenter keeps them current at the moment its window or tooltip changes.
/// </para>
/// <para>
/// The presenter owns the window lifecycle, its sizing and its tooltip, and the caller owns the presenter:
/// the controller never disposes it, so the caller dismisses and disposes it after the controller. A presenter that closes its window
/// by itself calls <see cref="CompletionController.CancelScheduledRequest"/>, so no request fires for a window that is gone.
/// </para>
/// </remarks>
public interface ICompletionPresenter
{
	/// <summary>Whether a completion list is shown and tracked.</summary>
	bool IsListVisible { get; }

	/// <summary>Whether the completion tooltip is visible.</summary>
	bool IsTooltipVisible { get; }

	/// <summary>The content of the completion tooltip, or <see langword="null"/> when it shows nothing.</summary>
	CompletionTooltipContent? TooltipContent { get; }

	/// <summary>
	/// Shows a decision that the controller accepted: a requested close is performed first, and a decision with items opens or refreshes the list.
	/// </summary>
	/// <param name="directive">The decision to show.</param>
	/// <returns><see langword="true"/> when a list was opened or refreshed; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The controller calls it for the decision of the newest request after it moved the decision onto the word at the caret, and it does not call
	/// it for a decision that a newer request, a cancellation, an invalidation or a host context change made stale. A failure the presenter throws
	/// is contained and logged by the controller's request pipeline.
	/// </remarks>
	bool ApplyDirective(CompletionSessionDirective directive);

	/// <summary>Closes the list and hides the tooltip, whether or not a list is shown.</summary>
	/// <remarks>
	/// The controller calls it once, when the editor replaces its document, so the presenter leaves no list
	/// of the earlier document behind. The call does nothing visible when nothing is shown.
	/// </remarks>
	void Dismiss();
}
