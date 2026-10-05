using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Text;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.AvalonEdit.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>
/// Shows completion in the window of an editor: it creates, sizes, themes and closes the completion windows of one text area, shows the tooltip
/// of the selected item, and applies the commit-character input policy.
/// </summary>
/// <remarks>
/// <para>
/// The presenter is the window half of completion.
/// The engine-free <see cref="CompletionController"/> owns the requests, and <see cref="CompletionControllerFactory.Create"/> joins
/// the two over one text area: the controller hands each decision to <see cref="ApplyDirective"/>,
/// and the presenter cancels the scheduled request when its window closes. The presenter delegates the window lifecycle
/// to a <see cref="CompletionWindowPresenter"/>, which shows at most one window and replaces it when a new one is opened, and the tooltip pipeline
/// to an internal presenter. <include file="../../docs/ThreadAffinity.xml" path="doc/members/member[@name='ThreadAffinity.Remarks']/*"/>
/// </para>
/// <para>
/// The presenter installs the commit-character input policy on the text area
/// while it is alive (see <see cref="CompletionPresentationOptions.AcceptOnCommitCharacters"/>): a typed character that the selected item declares
/// through <see cref="ICompletionCommitCharacters"/> commits the item while the character itself is typed, so one keystroke both accepts the item
/// and inserts the character.
/// </para>
/// <para>
/// The caller owns the presenter: it creates it, passes it to the factory, keeps it, and disposes it after the controller. Disposal closes the window
/// and the tooltip, removes the commit-character policy and the subscriptions that the factory handed over, and makes every member return
/// without effect.
/// </para>
/// </remarks>
public sealed partial class CompletionPresenter : ICompletionPresenter, IDisposable
{
	private readonly TextArea _textArea;

	[SuppressMessage(
		"Performance",
		"CA1859:Use concrete types when possible for improved performance",
		Justification = "Shared presenter code holds the neutral dispatcher interface, never the engine adapter."
	)]
	private readonly IUiDispatcher _dispatcher;

	private readonly CompletionWindowOptions _windowOptions;
	private readonly Action<CompletionWindow>? _configureWindow;
	private readonly Func<CompletionItem, ICompletionData>? _completionItemFactory;
	private readonly CompletionItemAdapterHooks? _itemAdapterHooks;

	private readonly CompletionWindowSizeApplier _sizeApplier;
	private readonly CompletionCommitCharacterObserver _commitObserver;

	private IDisposable? _itemClickSubscription;

	// The controller and the engine subscriptions that the factory wired to this presenter, which the latest wiring replaces.
	private CompletionController? _controller;
	private IDisposable? _subscriptions;

	// What the controller publishes as the presentation state.
	private bool _isListVisible;
	private bool _isTooltipVisible;

	private CompletionTooltipContent? _tooltipContent;

	private bool _isDisposed;

	/// <param name="textArea">The text area the presenter serves.</param>
	/// <param name="presentation">
	/// The themes, the completion window options and the commit-character and tooltip settings,
	/// or <see langword="null"/> for <see cref="CompletionPresentationOptions.Default"/>.
	/// </param>
	/// <param name="hooks">The optional host hooks, or <see langword="null"/> when the host needs none.</param>
	/// <param name="logger">An optional logger for tooltip failures.</param>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The window options of <paramref name="presentation"/> cannot fit the minimum content width plus the horizontal window chrome into the maximum
	/// window width.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns <paramref name="textArea"/>.</exception>
	public CompletionPresenter(
		TextArea textArea,
		CompletionPresentationOptions? presentation = null,
		CompletionPresenterHooks? hooks = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);

		presentation ??= CompletionPresentationOptions.Default;

		// The presenter touches the text area's document and its windows directly and its tooltip timer runs on the text area's dispatcher,
		// so creating it off the text area's thread is a caller error.
		textArea.Dispatcher.VerifyAccess();

		_textArea = textArea;
		_dispatcher = UiDispatcher.From(textArea.Dispatcher);
		_windowOptions = presentation.WindowOptions;

		presentation.WindowOptions.Validate(nameof(presentation));
		WindowPresenter = new CompletionWindowPresenter(textArea, presentation.WindowTheme);

		_configureWindow = hooks?.ConfigureWindow;
		_completionItemFactory = hooks?.CompletionItemFactory;
		_itemAdapterHooks = hooks?.ItemAdapter;

		_sizeApplier = new CompletionWindowSizeApplier(
			textArea,
			getDisplayInfo: hooks?.GetDisplayInfo,
			measureItemWidth: hooks?.MeasureItemWidth,
			options: _windowOptions
		);

		InitializeTooltip(presentation, hooks, logger ?? NullLogger.Instance);

		// The window presenter belongs to this presenter and raises the event for every shown window it closes,
		// so this presenter observes window closures regardless of whether the host, the user, or a
		// replacement closed the window.
		WindowPresenter.WindowClosed += HandleTrackedWindowClosed;

		_commitObserver = new CompletionCommitCharacterObserver(textArea, () => WindowPresenter.ActiveWindow, presentation.AcceptOnCommitCharacters);
	}

	/// <summary>
	/// The completion window presenter this presenter created for the text area, so a host can observe the managed window and its closures
	/// and can close the window without going through the request pipeline.
	/// </summary>
	/// <remarks>
	/// This presenter owns the window lifecycle: windows are created, configured, sized,
	/// and shown only by this presenter through <see cref="OpenOrRefresh"/> (or its decision-applying callers),
	/// so the window presenter's reported window and the presentation state of the controller always describe the same window.
	/// Use <see cref="Dismiss"/> to close the window and the tooltip, or <c>WindowPresenter.Close()</c> to close the shown window; closing a shown
	/// window by either route raises <see cref="CompletionWindowPresenter.WindowClosed"/>, and this presenter then cancels the scheduled request
	/// and the pending tooltip update.
	/// </remarks>
	public CompletionWindowPresenter WindowPresenter { get; }

	/// <summary>
	/// The theme of the completion windows this presenter opens; it starts as <see cref="CompletionPresentationOptions.WindowTheme"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the presenter's dispatcher thread.</exception>
	/// <remarks>
	/// A host that switches themes assigns the new one: the next window takes it, and a window that is open keeps its chrome.
	/// </remarks>
	public CompletionWindowTheme WindowTheme
	{
		get => WindowPresenter.Theme;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_dispatcher.VerifyAccess();

			WindowPresenter.Theme = value;
		}
	}

	/// <summary>Dismisses the active completion window and hides any completion tooltip.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the presenter's dispatcher thread.</exception>
	/// <remarks>
	/// Dismissing does not invalidate in-flight requests: a completion result that was already requested can still reopen
	/// a window when the controller applies its decision afterwards. A host that treats the dismissal as the end of the completion session
	/// calls <see cref="CompletionController.InvalidateRequests"/> (or <see cref="CompletionController.CancelInFlightRequest"/> when the provider
	/// should stop early) together with this method.
	/// </remarks>
	public void Dismiss()
	{
		if (_isDisposed)
			return;

		_dispatcher.VerifyAccess();

		DismissCore();
	}

	private void DismissCore()
	{
		// A tracked window's tooltip is closed while the window is still tracked; the window presenter then raises
		// WindowClosed once and the handler runs the shared cleanup, so the cleanup is not duplicated here.
		if (WindowPresenter.ActiveWindow is CompletionWindow completionWindow)
		{
			CloseTooltip(completionWindow);
			WindowPresenter.Close();
			return;
		}

		// Nothing is tracked, so the window presenter raises no WindowClosed and cannot run the cleanup; run it
		// here, then close the created-but-unshown window the window presenter may still hold.
		CompleteDismissCleanup();
		WindowPresenter.Close();
	}

	// A tracked window can close itself through a commit, Escape, or focus loss, and DismissCore closes one
	// through the window presenter; both funnel through this single cleanup, so it runs exactly once per close. The
	// window presenter holds no window afterwards, so stale window or tooltip state must be cleared and the
	// window's list-box handlers must be detached.
	private void HandleTrackedWindowClosed(object? sender, EventArgs e) => CompleteDismissCleanup();

	private void CompleteDismissCleanup()
	{
		_controller?.CancelScheduledRequest();
		CancelTooltipUpdateCore();
		DetachWindowHandlers();
		SetWindowState(false);
		SetTooltipState(null, false);
		_sizeApplier.Reset();
	}

	/// <summary>
	/// Opens a completion window with the given items for the replacement range, or refreshes the tracked window in place when its replacement
	/// start is unchanged; the window sizes to its content up to the configured maximum height.
	/// </summary>
	/// <param name="items">The completion items to show.</param>
	/// <param name="replacementRange">The zero-based range the chosen item replaces; the end is exclusive.</param>
	/// <returns>
	/// <see langword="true"/> when the operation was applied (the window was opened or refreshed), even when the window closes itself right
	/// afterwards because its filtered list became empty - the close is part of the operation; otherwise, <see langword="false"/>. A text area
	/// without a document reports <see langword="false"/> as well, because the replacement range cannot be anchored.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the presenter's dispatcher thread.</exception>
	/// <remarks>
	/// <para>
	/// The replacement range uses zero-based document offsets with an exclusive end offset. An offset beyond the document end is clamped
	/// to the document length, so the replacement range can never extend past the current document.
	/// </para>
	/// <para>
	/// A refresh keeps the open window when its replacement start still matches the supplied start: its items and end offset are replaced
	/// and the sizing and window hook re-applied, so per-keystroke completions do not recreate the window. A different start closes the old window
	/// and opens a new one, which also covers a window whose anchors moved with an edit. The supplied items are the candidate set for the typed word;
	/// the completion list re-ranks and filters them against the text between the two offsets.
	/// </para>
	/// <para>
	/// A <see langword="null"/> entry in the collection is dropped, matching the code-action path, because a <see langword="null"/> item
	/// would throw inside the completion list's filtering and rendering. An empty item collection returns <see langword="false"/> without opening
	/// or refreshing a window, and - unless <see cref="CompletionWindowOptions.DismissWhenEmpty"/> is disabled - a window whose list becomes empty
	/// after filtering is closed rather than showing the completion list's empty template.
	/// </para>
	/// <para>
	/// After an open, the initial item selection is established on the dispatcher at <c>ContextIdle</c> priority; a refresh re-selects
	/// synchronously as part of its re-filtering. A provider-preselected item that survived the filtering (see <see cref="ICompletionPreselection"/>)
	/// wins over the best match and is scrolled into view.
	/// </para>
	/// </remarks>
	public bool OpenOrRefresh(IReadOnlyList<ICompletionData> items, TextRange replacementRange)
	{
		ArgumentNullException.ThrowIfNull(items);

		if (_isDisposed)
			return false;

		_dispatcher.VerifyAccess();

		// The collection belongs to the host, which may keep or change it, so the presenter works on an array of its own.
		var ownedItems = new ICompletionData[items.Count];

		for (int index = 0; index < ownedItems.Length; index++)
			ownedItems[index] = items[index];

		return OpenOrRefreshOwned(ownedItems, replacementRange);
	}

	/// <summary>Opens or refreshes the completion window for items that the presenter owns, after the disposal and thread checks.</summary>
	/// <param name="ownedItems">
	/// The items to show, in an array that no host holds, so the presenter reuses it when it has no <see langword="null"/> entry.
	/// </param>
	/// <param name="replacementRange">The zero-based range the chosen item replaces; the end is exclusive.</param>
	/// <returns>Whether the operation was applied, as <see cref="OpenOrRefresh"/> reports it.</returns>
	private bool OpenOrRefreshOwned(ICompletionData[] ownedItems, TextRange replacementRange)
	{
		// A null item would throw inside the completion list's filtering and rendering, so null entries are dropped,
		// as the code-action path does.
		ICompletionData[] completionItems = NonNullItems(ownedItems);

		if (completionItems.Length == 0)
			return false;

		// A detached document (a text area can outlive its document) cannot anchor the replacement range,
		// so the operation reports no-op instead of failing.
		TextDocument? document = _textArea.Document;

		if (document is null)
			return false;

		int clampedStartOffset = document.ClampOffset(replacementRange.Offset);
		int clampedEndOffset = document.ClampOffset(replacementRange.EndOffset);

		// A refresh keeps the open window while the replacement start is unchanged; the window re-anchors its
		// start on edits, so a shifted anchor falls back to a complete reopen.
		if (WindowPresenter.ActiveWindow is CompletionWindow activeWindow && activeWindow.StartOffset == clampedStartOffset)
			return RefreshWindow(activeWindow, completionItems, clampedEndOffset);

		return OpenWindow(completionItems, clampedStartOffset, clampedEndOffset);
	}

	/// <summary>Returns the items without their <see langword="null"/> entries.</summary>
	/// <param name="items">The items to filter.</param>
	/// <returns><paramref name="items"/> itself when it has no <see langword="null"/> entry; otherwise, a copy that omits them.</returns>
	internal static ICompletionData[] NonNullItems(ICompletionData[] items)
	{
		int nullCount = 0;

		foreach (ICompletionData item in items)
		{
			if (item is null)
				nullCount++;
		}

		if (nullCount == 0)
			return items;

		var kept = new ICompletionData[items.Length - nullCount];
		int next = 0;

		foreach (ICompletionData item in items)
		{
			if (item is not null)
				kept[next++] = item;
		}

		return kept;
	}

	private bool OpenWindow(ICompletionData[] completionItems, int startOffset, int endOffset)
	{
		Dismiss();
		CompletionWindow completionWindow = WindowPresenter.Create(maxHeight: _windowOptions.MaxHeight);

		try
		{
			ApplyTooltipTheme(completionWindow);
			ConfigureNonActivatingWindow(completionWindow);
			completionWindow.StartOffset = startOffset;
			completionWindow.EndOffset = endOffset;

			CompletionListSelection.ReplaceItems(completionWindow, completionItems);

			// The hook runs after the presenter applied its baseline sizing, offsets, and items, immediately
			// before the window is shown, so anything the hook sets wins.
			ApplyWindowConfiguration(completionWindow, completionItems);

			WindowPresenter.Show();
		}
		catch
		{
			// A throwing host hook or a failed show must not strand a created window or leave stale state behind.
			DismissCore();
			throw;
		}

		SetWindowState(WindowPresenter.IsWindowOpen);
		ScheduleInitialSelection();
		return true;
	}

	private bool RefreshWindow(CompletionWindow completionWindow, ICompletionData[] completionItems, int endOffset)
	{
		try
		{
			// The visible tooltip describes the earlier selection, so it is invalidated together with
			// the item set; the selection change below re-arms the tooltip update for the new selection.
			CancelTooltipUpdateCore();
			CloseTooltip(completionWindow);

			completionWindow.EndOffset = endOffset;

			CompletionListSelection.ReplaceItems(completionWindow, completionItems);
			ApplyWindowConfiguration(completionWindow, completionItems);

			// The refresh must display the replaced item set even when the query did not change.
			CompletionListSelection.RefreshFilter(completionWindow, _textArea);

			DismissWhenEmpty();
		}
		catch
		{
			// A throwing host hook during a refresh must not leave a half-updated window behind.
			Dismiss();
			throw;
		}

		SetWindowState(WindowPresenter.IsWindowOpen);
		return true;
	}

	// The click handler selects the clicked item explicitly; the selection change is also what schedules the
	// tooltip update. The handler is harmless when the native input path already selected the item, and it is
	// skipped when the host opts into an activatable window, where the native selection path applies.
	private void ConfigureNonActivatingWindow(CompletionWindow completionWindow)
	{
		if (!_windowOptions.PreventActivation)
			return;

		CompletionSeam.EnsureNonActivatable(completionWindow);

		ListBox listBox = completionWindow.CompletionList.ListBox;
		_itemClickSubscription?.Dispose();
		_itemClickSubscription = CompletionSeam.SubscribeItemClick(listBox, CompletionListSelection.SelectClickedItem);
	}

	// Both list-box handlers of a window must not outlive it: a closed or replaced window's list box can
	// still raise a queued selection or click event, and the tooltip update such an event would schedule
	// belongs to the stale window. The presenter owns its selection handler and detaches it here.
	private void DetachWindowHandlers()
	{
		_itemClickSubscription?.Dispose();
		_itemClickSubscription = null;
		DetachTooltip();
	}

	/// <summary>
	/// Applies a completion session decision; a requested close is performed first, and when the decision requests items the method maps
	/// them and opens or refreshes a completion window for the decision's replacement range.
	/// </summary>
	/// <param name="directive">The decision to apply.</param>
	/// <returns><see langword="true"/> when a completion window was opened or refreshed; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the presenter's dispatcher thread, or the item factory returned <see langword="null"/> for one of the decision's items.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Items are mapped through the <see cref="CompletionPresenterHooks.CompletionItemFactory"/> hook when it is set, and through the package's
	/// default <see cref="CompletionItemAdapter"/> with the <see cref="CompletionPresenterHooks.ItemAdapter"/> hooks otherwise, so the standard
	/// scenario works without a host mapper. A host that already holds shared <see cref="CompletionItem"/> instances opens them directly
	/// with <c>ApplyDirective(CompletionSessionDirective.Open(items, replacementRange))</c> and gets the same mapping.
	/// </para>
	/// <para>
	/// An empty item list returns <see langword="false"/> without mapping anything. A <see cref="CompletionSessionDirective.NoMatches"/> decision
	/// (its <see cref="CompletionSessionDirective.AllItemsFilteredOut"/> is <see langword="true"/>) is a deliberate no-op: the current word
	/// filtered every candidate out and the user is still typing, so the window is left as it is and the method returns <see langword="false"/>
	/// without closing it. The shared decision type guarantees that a decision carrying items also carries a replacement range,
	/// and <see cref="OpenOrRefresh"/> clamps offsets beyond the document end. The method applies the decision as the newest session state
	/// without re-checking request freshness, so a host that calls it directly instead of through <see cref="CompletionController.RequestAsync"/>
	/// must discard a superseded or canceled decision itself.
	/// </para>
	/// </remarks>
	public bool ApplyDirective(CompletionSessionDirective directive)
	{
		if (_isDisposed)
			return false;

		_dispatcher.VerifyAccess();

		if (directive.ShouldClose)
			Dismiss();

		// A decision without items requests no session, and an empty list would not open a window.
		if (directive.Items is not { Count: > 0 } decisionItems)
			return false;

		// The shared decision type ties the replacement range to the items, so the range is present
		// whenever items are.
		if (directive.ReplacementRange is not TextRange replacementRange)
			return false;

		// The array is built here and no host receives it, so the open or refresh reuses it instead of copying it.
		ICompletionData[] items = CompletionItemAdapter.CreateAll(decisionItems, _completionItemFactory, _itemAdapterHooks);

		return OpenOrRefreshOwned(items, replacementRange);
	}

	/// <summary>Schedules the completion window to close if it is empty.</summary>
	/// <remarks>
	/// Empty completion windows are closed instead of showing the completion list's empty template,
	/// unless <see cref="CompletionWindowOptions.DismissWhenEmpty"/> is disabled. The close check is posted to the text area's dispatcher
	/// and runs asynchronously, so the window may still be open when this method returns. The post makes the method safe to call from any thread.
	/// </remarks>
	public void ScheduleDismissWhenEmpty()
	{
		if (_isDisposed)
			return;

		_dispatcher.Post(DismissWhenEmpty, UiDispatcherPriority.Background);
	}

	/// <summary>Closes any open completion window or tooltip, and stops the presenter.</summary>
	/// <remarks>
	/// Disposal is synchronous and does not verify the thread, because it runs during the teardown of the host. The caller disposes the presenter
	/// after the controller. After disposal, <see cref="OpenOrRefresh"/> and <see cref="ApplyDirective"/> return <see langword="false"/>
	/// and the other members return without effect.
	/// </remarks>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		// The close path uses the tooltip presenter, so the tracked window is closed before the tooltip is disposed; closing first also keeps
		// the WindowClosed handler attached for the close it performs.
		DismissCore();

		_isDisposed = true;

		WindowPresenter.WindowClosed -= HandleTrackedWindowClosed;

		_commitObserver.Dispose();
		_subscriptions?.Dispose();

		_subscriptions = null;
		_controller = null;

		DisposeTooltip();
	}

	/// <summary>Wires the controller the factory created, replacing an earlier wiring, so the closures of the window reach it.</summary>
	/// <param name="controller">The controller, whose scheduled request a closure of the window cancels.</param>
	/// <param name="subscriptions">
	/// The engine subscriptions that forward the typed characters and the document replacement to the controller, which the presenter disposes
	/// when a later wiring replaces them or the presenter is disposed.
	/// </param>
	internal void Attach(CompletionController controller, IDisposable subscriptions)
	{
		_subscriptions?.Dispose();
		_controller = controller;
		_subscriptions = subscriptions;
	}

	/// <summary>Creates the tooltip pipeline for the text area, if the engine has one to style.</summary>
	/// <param name="presentation">The presentation options that carry the tooltip theme.</param>
	/// <param name="hooks">The host hooks that carry the tooltip hooks, or <see langword="null"/>.</param>
	/// <param name="logger">The logger for failures of the pipeline.</param>
	private partial void InitializeTooltip(CompletionPresentationOptions presentation, CompletionPresenterHooks? hooks, ILogger logger);

	/// <summary>Applies the tooltip chrome to a window the presenter created.</summary>
	/// <param name="completionWindow">The created window.</param>
	private partial void ApplyTooltipTheme(CompletionWindow completionWindow);

	/// <summary>Hides the tooltip of a window.</summary>
	/// <param name="completionWindow">The window whose tooltip is hidden.</param>
	private partial void CloseTooltip(CompletionWindow completionWindow);

	/// <summary>Cancels the pending tooltip update.</summary>
	private partial void CancelTooltipUpdateCore();

	/// <summary>Detaches the tooltip handlers from the closed window's list.</summary>
	private partial void DetachTooltip();

	/// <summary>Releases the tooltip pipeline.</summary>
	private partial void DisposeTooltip();

	private void SetWindowState(bool hasOpenWindow) => _isListVisible = hasOpenWindow;

	private void SetTooltipState(object? tooltipContent, bool isTooltipVisible)
	{
		_tooltipContent = tooltipContent is null ? null : new CompletionTooltipContent(tooltipContent);
		_isTooltipVisible = isTooltipVisible;
	}

	// The controller reads these when it publishes the presentation state; a host reads the same facts from the controller.
	bool ICompletionPresenter.IsListVisible => _isListVisible;

	bool ICompletionPresenter.IsTooltipVisible => _isTooltipVisible;

	CompletionTooltipContent? ICompletionPresenter.TooltipContent => _tooltipContent;

	// Applies the content sizing and the host window hook in the order both window paths rely on: the
	// presenter sizes to the new item set first, then the hook runs last so anything it sets wins.
	private void ApplyWindowConfiguration(CompletionWindow completionWindow, ICompletionData[] completionItems)
	{
		_sizeApplier.Apply(completionWindow, completionItems);
		_configureWindow?.Invoke(completionWindow);
	}

	private void ScheduleInitialSelection() => _dispatcher.Post(SelectInitialItem, UiDispatcherPriority.ContextIdle);

	private void SelectInitialItem()
	{
		if (WindowPresenter.ActiveWindow is not CompletionWindow completionWindow)
			return;

		CompletionListSelection.SelectInitialItem(completionWindow, _textArea);
		DismissWhenEmpty();
	}

	private void DismissWhenEmpty()
	{
		if (!_windowOptions.DismissWhenEmpty)
			return;

		if (WindowPresenter.ActiveWindow is not CompletionWindow completionWindow)
			return;

		if (completionWindow.CompletionList.ListBox.Items.Count > 0)
			return;

		Dismiss();
	}
}
