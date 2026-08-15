#if AVALONIAEDIT
using Avalonia.Controls;
using AvaloniaEdit.CodeCompletion;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using System.Windows.Controls;
#endif
using Nickelony.IDEKit.IntelliSense.Completion;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Groups the optional host hooks used by the <see cref="TextCompletionController"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every hook is optional. A controller without hooks still opens windows, tracks presentation state,
/// maps decision items through the default completion data adapter, and shows the synchronous
/// descriptions that the editor provides, but it schedules requests only when
/// <see cref="ScheduledRequestAsync"/> is supplied, and it cannot configure the window and tooltip or
/// resolve asynchronous tooltip descriptions.
/// </para>
/// <para>
/// Host hooks should not throw. Failures raised on the standard request pipeline - <see cref="ResolveDescriptionAsync"/>,
/// <see cref="ScheduledRequestAsync"/>, and the window, tooltip, and item-factory hooks applied while a decision is
/// committed - are contained and logged, so they cannot escape timer or dispatcher callbacks. A failure that
/// escapes from another entry point (for example a direct window open or refresh call) propagates to its caller;
/// when it happens while a window is created or refreshed, the window is closed and the tracked state is reset.
/// The individual properties document their exact behavior.
/// </para>
/// <para>
/// The flat bag is deliberate: grouping the hooks into nested sizing and presentation records would not reduce
/// the number of members a host sets, and one optional hook object keeps wiring declarative. The hooks fall into
/// three roles - the presentation hooks (<see cref="ConfigureWindow"/>, <see cref="ConfigureTooltip"/>,
/// <see cref="TooltipSkin"/>, and <see cref="CompletionItemFactory"/>), the sizing hooks (<see cref="GetDisplayInfo"/>
/// and <see cref="MeasureItemWidth"/>), and the request and tooltip pipeline hooks
/// (<see cref="ResolveDescriptionAsync"/> and <see cref="ScheduledRequestAsync"/>) - and each property documents
/// its own behavior.
/// </para>
/// </remarks>
public sealed class TextCompletionControllerHooks
{
	/// <summary>
	/// Gets the hook that configures a completion window before it is shown, or reconfigures an open window
	/// when a refresh replaces its items.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The hook is invoked after the controller applied its baseline window settings (sizing, replacement
	/// offsets, and items) and immediately before the window is shown, so any value the hook sets - including
	/// the window width - wins over the controller's sizing. The hook runs again when an open window is
	/// refreshed in place, after the new items were applied.
	/// </para>
	/// <para>
	/// The controller measures the window width from the editor's font configuration before the hook runs,
	/// so a host that renders items with a different font sizes the window for the editor's font unless it
	/// supplies <see cref="MeasureItemWidth"/>; setting the window font in this hook does not change the
	/// measurement.
	/// </para>
	/// <para>
	/// An exception it throws propagates to the <see cref="TextCompletionController.OpenOrRefresh"/> caller
	/// after the window is closed and the tracked state is reset.
	/// </para>
	/// </remarks>
	public Action<CompletionWindow>? ConfigureWindow { get; init; }

	/// <summary>
	/// Gets the hook that configures the completion tooltip after the controller applies its skin.
	/// </summary>
	/// <remarks>
	/// The controller applies the <see cref="TooltipSkin"/> chrome before invoking this hook, so a host can
	/// override any of those values for its own skin (the tooltip keeps the editor's open-on-hover behavior).
	/// An exception the hook throws propagates to the <see cref="TextCompletionController.OpenOrRefresh"/> caller
	/// after the window is closed (the created window, or the open window when the failure happens during a
	/// refresh) and the tracked state is reset. When the editor's tooltip cannot be resolved, this hook is not
	/// invoked.
	/// </remarks>
	public Action<ToolTip>? ConfigureTooltip { get; init; }

	/// <summary>
	/// Gets the factory that maps provider items to completion data, or <see langword="null"/> for the
	/// package's default <see cref="TextCompletionItemCompletionData"/> adapter.
	/// </summary>
	/// <remarks>
	/// The factory is the single mapping seam for decision items: it applies wherever the controller maps
	/// shared items to completion data (<see cref="TextCompletionController.ApplyDecision"/> and
	/// <see cref="TextCompletionController.RequestAsync"/>). A factory that returns <see langword="null"/> for
	/// an item makes the mapping fail with <see cref="System.InvalidOperationException"/>, because the item is
	/// neither skippable nor representable; a host should only map shapes it can actually present.
	/// </remarks>
	public Func<TextCompletionItem, ICompletionData>? CompletionItemFactory { get; init; }

	/// <summary>
	/// Gets the hook that resolves the selected item's description asynchronously.
	/// </summary>
	/// <remarks>
	/// The hook is invoked for every selected item after the item's synchronous
	/// <see cref="ICompletionData.Description"/> was applied. The returned content replaces the currently shown
	/// tooltip content; returning <see langword="null"/> content hides the tooltip instead. Hosts whose items
	/// only have a synchronous description do not set this hook, or return the item's current description to
	/// keep it visible. The token is canceled when a newer tooltip update starts, when the completion window is
	/// closed, and when the controller is disposed; implementations should observe it and cancel pending work.
	/// </remarks>
	public Func<ICompletionData, CancellationToken, Task<object?>>? ResolveDescriptionAsync { get; init; }

	/// <summary>
	/// Gets the completion tooltip skin, or <see langword="null"/> for <see cref="CompletionTooltipSkin.Default"/>.
	/// </summary>
	/// <remarks>
	/// The skin describes the chrome the controller applies to the editor's completion tooltip (placement,
	/// offset, border, padding, and the two optional brushes); <see cref="ConfigureTooltip"/> runs after the
	/// skin and can override any value the skin applied (and style whatever the record does not cover). The
	/// skin is validated when the controller is constructed.
	/// </remarks>
	public CompletionTooltipSkin? TooltipSkin { get; init; }

	/// <summary>
	/// Gets the hook that supplies the display text and detail used for width measurement.
	/// </summary>
	/// <remarks>
	/// The returned tuple's <c>Text</c> is the item's display text and <c>Detail</c> is its optional detail
	/// text, or <see langword="null"/> when the item has none. The default measurement uses both, adding
	/// <see cref="TextCompletionControllerOptions.ItemDetailSpacing"/> when detail is present; a host whose
	/// items only supply display text leaves this hook unset. A host with a different item template measures
	/// its own layout through <see cref="MeasureItemWidth"/> instead.
	/// </remarks>
	public Func<ICompletionData, (string Text, string? Detail)>? GetDisplayInfo { get; init; }

	/// <summary>
	/// Gets the hook that measures the content width of a completion item, replacing the default
	/// measurement (display text plus the optional icon column and detail gap).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The hook returns the item's content width in device-independent pixels, excluding
	/// <see cref="TextCompletionControllerOptions.WindowHorizontalChrome"/>. Items are measured until the
	/// content width the window can display is reached, and the window width is the largest measurement (or
	/// <see cref="TextCompletionControllerOptions.WindowMinContentWidth"/> when nothing is measured) plus the
	/// chrome, clamped between the minimum content width and the maximum window width.
	/// </para>
	/// <para>
	/// The returned width must be finite; a non-finite value is rejected with
	/// <see cref="InvalidOperationException"/> because it would silently turn the window width into an
	/// auto-sized value. A negative width cannot widen the window and is ignored.
	/// </para>
	/// <para>
	/// This hook lets hosts with a different item template measure their own layout; when it is omitted, the
	/// default measurement uses <see cref="TextCompletionControllerOptions.ItemIconWidth"/>,
	/// <see cref="TextCompletionControllerOptions.ItemDetailSpacing"/>, and the display information hook.
	/// </para>
	/// </remarks>
	public Func<ICompletionData, double>? MeasureItemWidth { get; init; }

	/// <summary>
	/// Gets the callback a scheduled completion request runs, or <see langword="null"/> when the controller
	/// should not schedule requests.
	/// </summary>
	/// <remarks>
	/// The controller hands this callback to its request scheduler, so
	/// <see cref="TextCompletionController.ScheduleRequest"/> runs it after the configured debounce delay when
	/// the host's trigger policy calls it; without the callback, scheduling is a no-op and the host drives
	/// requests only through <see cref="TextCompletionController.RequestAsync"/> or the
	/// <see cref="TextCompletionController.Requests"/> session. The callback typically invokes
	/// <see cref="TextCompletionController.RequestAsync"/> from a host field or property, because the hooks
	/// object is constructed before the controller it belongs to. A failure the callback throws is logged and
	/// leaves the tracked state intact, and a cancellation it reports is ignored.
	/// </remarks>
	public Func<Task>? ScheduledRequestAsync { get; init; }
}
