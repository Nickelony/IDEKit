using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Avalonia.Controls;
using AvaloniaEdit.CodeCompletion;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using System.Windows;
using System.Windows.Controls;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>Groups the optional host hooks used by the <see cref="CompletionPresenter"/>.</summary>
/// <remarks>
/// <para>
/// Every hook is optional. Without hooks, the presenter still opens windows, tracks presentation state,
/// maps decision items through the default completion data adapter, and shows the descriptions that the editor provides, but the window and tooltip
/// stay unconfigured and the tooltip renders no Markdown. The hooks that guide the requests, the scope at the caret and the host context version,
/// belong to the engine-free <see cref="CompletionControllerHooks"/>.
/// </para>
/// <para>
/// Host hooks should not throw. Failures raised on the standard request pipeline - the callbacks and hooks it calls while a request
/// runs and while its decision is committed - are contained and logged, so they cannot escape timer or dispatcher callbacks. A failure that escapes
/// from another entry point (for example a direct window open or refresh call) propagates to its caller; when it happens while a window
/// is created or refreshed, the window is closed and the tracked state is reset.
/// </para>
/// <para>
/// The flat bag is deliberate: grouping the hooks into nested sizing and tooltip records would not reduce the number of members
/// a host sets, and one optional hook object keeps wiring declarative. The window and tooltip themes are not hooks but inputs of their own and travel
/// in <see cref="CompletionPresentationOptions"/>.
/// </para>
/// </remarks>
public sealed class CompletionPresenterHooks
{
	/// <summary>
	/// The hook that configures a completion window before it is shown, or reconfigures an open window when a refresh replaces its items.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The hook is invoked after the presenter applied its baseline window settings (sizing, replacement offsets, and items)
	/// and immediately before the window is shown, so any value the hook sets - including the window width - wins over the presenter's sizing.
	/// The hook runs again when an open window is refreshed in place, after the new items were applied.
	/// </para>
	/// <para>
	/// The presenter measures the window width from the editor's font configuration before the hook runs, so a host that renders
	/// items with a different font sizes the window for the editor's font unless it supplies <see cref="MeasureItemWidth"/>; setting the window font
	/// in this hook does not change the measurement.
	/// </para>
	/// <para>
	/// An exception it throws propagates out of <see cref="CompletionPresenter.OpenOrRefresh"/> and <see cref="CompletionPresenter.ApplyDirective"/>
	/// after the window is closed and the tracked state is reset, whereas <see cref="CompletionController.RequestAsync"/> contains and logs it.
	/// </para>
	/// </remarks>
	public Action<CompletionWindow>? ConfigureWindow { get; init; }

#if !AVALONIAEDIT
	/// <summary>The hook that configures the completion tooltip after the presenter applies its theme.</summary>
	/// <remarks>
	/// The presenter applies the <see cref="CompletionPresentationOptions.TooltipTheme"/> chrome before invoking this hook, so a host
	/// can override any of those values for its own theme; the editor's own selection handler still opens the tooltip. An exception the hook throws
	/// is handled like one from <see cref="ConfigureWindow"/>. The hook runs only when a window is opened, not when an open window is refreshed,
	/// and it is not invoked when the editor's tooltip cannot be resolved.
	/// </remarks>
	public Action<ToolTip>? ConfigureTooltip { get; init; }
#endif

	/// <summary>
	/// The factory that maps provider items to completion data, or <see langword="null"/> for the package's
	/// default <see cref="CompletionItemAdapter"/>.
	/// </summary>
	/// <remarks>
	/// The factory is the single mapping point for decision items:
	/// it applies wherever the presenter maps shared items to completion data, in <see cref="CompletionPresenter.ApplyDirective"/>,
	/// which also shows the decision of <see cref="CompletionController.RequestAsync"/>. A factory that returns <see langword="null"/> for an item
	/// makes the mapping fail with <see cref="System.InvalidOperationException"/> instead of dropping the item, so a host should only map shapes
	/// it can actually present.
	/// </remarks>
	public Func<CompletionItem, ICompletionData>? CompletionItemFactory { get; init; }

	/// <summary>
	/// The hooks the presenter passes to each default <see cref="CompletionItemAdapter"/>, or <see langword="null"/> for adapters without hooks.
	/// </summary>
	/// <remarks>
	/// The hooks apply only while <see cref="CompletionItemFactory"/> is unset; a host factory that wraps its items in the adapter passes
	/// them to the adapter's constructor itself.
	/// </remarks>
	public CompletionItemAdapterHooks? ItemAdapter { get; init; }

#if !AVALONIAEDIT
	/// <summary>The hook that resolves the selected item's description asynchronously.</summary>
	/// <remarks>
	/// <para>
	/// The hook is invoked for every selected item after the item's synchronous <see cref="ICompletionData.Description"/> was applied.
	/// The returned content replaces the currently shown tooltip content; returning <see langword="null"/> content
	/// hides the tooltip instead. A returned <see cref="Nickelony.IDEKit.LanguageFeatures.Markup.MarkupContent"/> shows by its kind:
	/// Markdown through <see cref="BuildMarkdownContent"/>, any other text as plain text. Hosts whose items only have a synchronous description
	/// do not set this hook, or return the item's current description to keep it visible. The token is canceled when a newer tooltip update starts,
	/// when the completion window is closed, and when the presenter is disposed; implementations should observe it and cancel pending work.
	/// </para>
	/// <para>
	/// Without the hook, the tooltip resolves the selected item of the default <see cref="CompletionItemAdapter"/> through the item's
	/// own resolve callback, after the same debounce, and shows the resolved documentation by its kind. An item without a resolve callback
	/// and an item of a host's own completion data resolve nothing, and a failed resolve keeps the description already shown and is logged.
	/// </para>
	/// </remarks>
	public Func<ICompletionData, CancellationToken, Task<object?>>? ResolveDescriptionAsync { get; init; }

	/// <summary>The hook that builds the tooltip content of a Markdown documentation, such as a rendered Markdown document.</summary>
	/// <remarks>
	/// <para>
	/// The hook receives the Markdown text of the documentation that the default <see cref="CompletionItemAdapter"/> carries, and the text
	/// of a Markdown <see cref="Nickelony.IDEKit.LanguageFeatures.Markup.MarkupContent"/> that a host's <see cref="ResolveDescriptionAsync"/> returns
	/// for any item. A documentation of the plain-text kind never reaches it, and a host item's own <see cref="ICompletionData.Description"/> shows
	/// as it is. The tooltip shows the returned element in place of the text, and a <see langword="null"/> return, or a failure the hook throws,
	/// leaves the Markdown as plain text.
	/// </para>
	/// <para>
	/// Without the hook the tooltip shows the Markdown as plain text. The item's <see cref="ICompletionData.Description"/> returns the same text
	/// either way.
	/// </para>
	/// </remarks>
	public Func<string, UIElement?>? BuildMarkdownContent { get; init; }
#endif

	/// <summary>The hook that supplies the display text and detail used for width measurement.</summary>
	/// <remarks>
	/// The returned tuple's <c>Text</c> is the item's display text and <c>Detail</c> is its optional detail text,
	/// or <see langword="null"/>
	/// when the item has none. The default measurement uses both, adding <see cref="CompletionWindowOptions.ItemDetailSpacing"/> when detail is present;
	/// a host whose items only supply display text leaves this hook unset. A host with a different item template measures its own layout
	/// through <see cref="MeasureItemWidth"/> instead.
	/// </remarks>
	public Func<ICompletionData, (string Text, string? Detail)>? GetDisplayInfo { get; init; }

	/// <summary>
	/// The hook that measures the content width of a completion item, replacing the default measurement (display text plus the optional icon column
	/// and detail gap).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The hook returns the item's content width in device-independent pixels, excluding <see cref="CompletionWindowOptions.HorizontalChrome"/>.
	/// Items are measured until the content width reaches the largest content width the window can display.
	/// The largest measurement, floored at <see cref="CompletionWindowOptions.MinContentWidth"/>, is the content width, and the window adds
	/// the chrome to it, capped at <see cref="CompletionWindowOptions.MaxWidth"/>.
	/// </para>
	/// <para>
	/// The returned width must be finite; a non-finite value is rejected with <see cref="InvalidOperationException"/> because it would silently turn
	/// the window width into an auto-sized value. A negative width cannot widen the window and is ignored.
	/// </para>
	/// <para>
	/// This hook lets hosts with a different item template measure their own layout; when it is omitted,
	/// the default measurement uses <see cref="CompletionWindowOptions.ItemIconWidth"/>, <see cref="CompletionWindowOptions.ItemDetailSpacing"/>,
	/// and the display information hook.
	/// </para>
	/// </remarks>
	public Func<ICompletionData, double>? MeasureItemWidth { get; init; }
}
