namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Describes the current state of the completion presentation.</summary>
/// <param name="IsListVisible">Whether the completion list is currently shown and tracked.</param>
/// <param name="IsRequestScheduled">Whether a completion request is currently scheduled.</param>
/// <param name="IsTooltipVisible">Whether the completion tooltip is visible.</param>
/// <param name="TooltipContent">The content of the completion tooltip, when visible.</param>
/// <remarks>
/// The tooltip content travels as a <see cref="CompletionTooltipContent"/>, which stores the item's documentation object unchanged
/// (a string stays a string) and documents its own equality contract; a host that renders the state decides how to present it. An in-flight request
/// is not part of the visibility union: the union counts only a scheduled (debounced) request, while a host observes an in-flight request
/// through the task that the controller's <c>RequestAsync</c> returns.
/// </remarks>
public readonly record struct CompletionPresentationState(
	bool IsListVisible,
	bool IsRequestScheduled,
	bool IsTooltipVisible,
	CompletionTooltipContent? TooltipContent
)
{
	/// <summary>The empty presentation state with no visible list or tooltip, no scheduled request, and no content.</summary>
	public static CompletionPresentationState Empty { get; } =
		new(IsListVisible: false, IsRequestScheduled: false, IsTooltipVisible: false, TooltipContent: null);

	/// <summary>
	/// Whether the completion presentation is visible or a request is pending: the list or tooltip is visible, or a debounced request is scheduled.
	/// </summary>
	public bool IsPresentationVisibleOrRequestScheduled => IsListVisible || IsTooltipVisible || IsRequestScheduled;
}
