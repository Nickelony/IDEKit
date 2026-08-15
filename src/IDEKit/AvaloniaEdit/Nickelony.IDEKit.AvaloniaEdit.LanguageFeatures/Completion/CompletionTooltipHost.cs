using Avalonia.Controls;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

/// <summary>
/// Applies placement and visibility to a completion window's tooltip through Avalonia's attached properties.
/// </summary>
/// <remarks>
/// The tooltip presenter is shared binding source, and the tooltip API is the one spot where the two engines
/// disagree on shape: WPF exposes placement and visibility as instance members of the tooltip, while Avalonia
/// models them as attached properties of the control the tooltip belongs to. This type is the Avalonia half of
/// that seam. Both bindings declare the same members, so the shared presenter reads one name.
/// </remarks>
internal static class CompletionTooltipHost
{
	/// <include file="../../../../../shared/docs/CompletionTooltipHost.xml" path="doc/members/member[@name='ApplyPlacement']/*"/>
	public static void ApplyPlacement(ToolTip tooltip, ListBox placementTarget, CompletionTooltipSkin skin)
	{
		ToolTip.SetPlacement(placementTarget, skin.Placement);
		ToolTip.SetHorizontalOffset(placementTarget, skin.HorizontalOffset);
	}

	/// <include file="../../../../../shared/docs/CompletionTooltipHost.xml" path="doc/members/member[@name='IsOpen']/*"/>
	public static bool IsOpen(ToolTip tooltip)
		=> ToolTip.GetIsOpen(tooltip);

	/// <include file="../../../../../shared/docs/CompletionTooltipHost.xml" path="doc/members/member[@name='SetIsOpen']/*"/>
	public static void SetIsOpen(ToolTip tooltip, bool isOpen)
		=> ToolTip.SetIsOpen(tooltip, isOpen);
}
