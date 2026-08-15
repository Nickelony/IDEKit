#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Requests;
using PointerClickArgs = Avalonia.Input.PointerReleasedEventArgs;
using TextInputArgs = Avalonia.Input.TextInputEventArgs;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Requests;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PointerClickArgs = System.Windows.Input.MouseButtonEventArgs;
using TextInputArgs = System.Windows.Input.TextCompositionEventArgs;
#endif
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Completion;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Coordinates completion request scheduling, window lifecycle, sizing, tooltip presentation, and the
/// commit-character input policy.
/// </summary>
/// <remarks>
/// <para>
/// The window lifecycle is delegated to a <see cref="CompletionWindowCoordinator"/> the controller creates
/// for the text area; it shows at most one window and replaces it when a new one is opened. The tooltip
/// pipeline is delegated to an internal presenter, and the request lifetime runs on the shared core request
/// coordinator behind the <see cref="Requests"/> session.
/// </para>
/// <para>
/// The controller must be created and used on the thread that owns the text area, because it touches the text
/// area's document and the completion window directly and its debounce timers run on the text area's
/// dispatcher. Provider request continuations are marshalled back to that thread before the decision is
/// applied, even when the creating thread captured no synchronization context.
/// </para>
/// <para>
/// The controller installs the commit-character input policy on the text area while it is alive (see
/// <see cref="TextCompletionControllerOptions.AcceptOnCommitCharacters"/>): a typed character that the
/// selected item declares through <see cref="ICommitCharacterCompletionData"/> commits the item while the
/// character itself is typed, so one keystroke both accepts the item and inserts the character. Disposal
/// removes the policy with the controller's other subscriptions.
/// </para>
/// </remarks>
public sealed class TextCompletionController : IDisposable
{
	// A query that cannot match a completion item; it forces the completion list to invalidate its
	// memoized query when a refresh has to display a replaced item set for an empty query (see RefreshWindow).
	private const string FilterResetQuery = "\0";

	private readonly TextArea _textArea;
	private readonly TextCompletionControllerOptions _options;
	private readonly Action<CompletionWindow>? _configureWindow;
	private readonly Func<TextCompletionItem, ICompletionData>? _completionItemFactory;
	private readonly Func<ICompletionData, (string Text, string? Detail)>? _getDisplayInfo;
	private readonly Func<ICompletionData, double>? _measureItemWidth;
	private readonly CompletionRequestScheduler _requestScheduler;
	private readonly CompletionTooltipPresenter _tooltipPresenter;
	private readonly RequestLifetime _lifetime;
	private IDisposable? _inputStageSubscription;
	private IDisposable? _itemClickSubscription;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionController"/> class.
	/// </summary>
	/// <param name="textArea">The text area the controller serves.</param>
	/// <param name="skin">The skin applied to created completion windows.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="hooks">The optional host hooks, or <see langword="null"/> when the host needs none.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="skin"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The supplied options cannot fit the minimum content width plus the horizontal window chrome into the
	/// maximum window width.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public TextCompletionController(
		TextArea textArea,
		CompletionWindowSkin skin,
		TextCompletionControllerOptions? options = null,
		TextCompletionControllerHooks? hooks = null,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(skin);

		// The controller touches the text area's document and its windows directly and its debounce timers
		// run on the text area's dispatcher, so creating it off the text area's thread is a caller error.
		textArea.Dispatcher.VerifyAccess();

		ILogger resolvedLogger = logger ?? NullLogger.Instance;
		_textArea = textArea;
		_options = options ?? TextCompletionControllerOptions.Default;

		ValidateOptions(_options);

		WindowCoordinator = new CompletionWindowCoordinator(textArea, skin);

		_configureWindow = hooks?.ConfigureWindow;
		_completionItemFactory = hooks?.CompletionItemFactory;
		_getDisplayInfo = hooks?.GetDisplayInfo;
		_measureItemWidth = hooks?.MeasureItemWidth;

		// The presenter arms its resolve timer on construction, so debounced description updates work
		// even when the host schedules no requests.
		_tooltipPresenter = new CompletionTooltipPresenter(
			_options,
			hooks?.TooltipSkin ?? CompletionTooltipSkin.Default,
			new CompletionTooltipPresenterContext(
				textArea.Dispatcher,
				() => WindowCoordinator.ActiveWindow,
				SetTooltipState,
				static window => CompletionWindowTooltipAccess.TryGetTooltip(window, out ToolTip? tooltip) ? tooltip : null),
			hooks?.ResolveDescriptionAsync,
			hooks?.ConfigureTooltip,
			resolvedLogger);

		_requestScheduler = new CompletionRequestScheduler(
			_options,
			textArea.Dispatcher,
			hooks?.ScheduledRequestAsync,
			SetRequestScheduled,
			resolvedLogger);
		Requests = new TextCompletionRequestSession();

		// The test-only read seams are grouped in their own internal object; the accessor reads the tooltip
		// presenter's debounce state after the presenter exists.
		TestHooks = new TextCompletionControllerTestHooks(() => _tooltipPresenter.IsUpdatePending);

		// The controller's disposal gate and cancel/invalidate pipeline live in the shared lifetime, which
		// gates the same coordinator the request session exposes so both share one request lifetime.
		_lifetime = new RequestLifetime(Requests.Coordinator);

		// The coordinator belongs to this controller and raises the event for every shown window it closes,
		// so the controller observes window closures regardless of whether the host, the user, or a
		// replacement closed the window.
		WindowCoordinator.WindowClosed += HandleTrackedWindowClosed;

		// The commit-character policy runs on both text-input stages; the handlers document why each
		// stage is needed.
		_inputStageSubscription = TextCompletionHost.SubscribeInputStages(
			_textArea,
			HandlePreviewTextInput,
			HandleTextEntering);
	}

	/// <summary>
	/// Gets the test-only seams of the controller. The member is <see langword="internal"/> and reachable
	/// only through <c>InternalsVisibleTo</c>, so the controller's public shape carries no test surface.
	/// </summary>
	internal TextCompletionControllerTestHooks TestHooks { get; }

	// The individual option values validate themselves when they are assigned; only the relationship between
	// the content-space floor and the window-space maximum still needs a check that spans three options.
	private static void ValidateOptions(TextCompletionControllerOptions options)
	{
		if (options.WindowMaxWidth < options.WindowMinContentWidth + options.WindowHorizontalChrome)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options),
				options.WindowMaxWidth,
				"The maximum window width must be large enough for the minimum content width plus the horizontal window chrome.");
		}
	}

	/// <summary>
	/// Gets the current completion presentation state.
	/// </summary>
	public TextCompletionPresentationState CurrentPresentation { get; private set; } = TextCompletionPresentationState.Empty;

	/// <summary>
	/// Gets the completion window coordinator this controller created for the text area, so a host can observe
	/// the managed window and its closures and can close the window without going through the controller.
	/// </summary>
	/// <remarks>
	/// The controller owns the window lifecycle: windows are created, configured, sized, and shown only by the
	/// controller through <see cref="OpenOrRefresh"/> (or its decision-applying callers), so the coordinator's
	/// reported window and <see cref="CurrentPresentation"/> always describe the same window. Use
	/// <see cref="CloseWindow"/> to end the session with the controller's pending-work cleanup, or
	/// <c>WindowCoordinator.Close()</c> for a pure window close; both raise
	/// <see cref="CompletionWindowCoordinator.WindowClosed"/> for a shown window.
	/// </remarks>
	public CompletionWindowCoordinator WindowCoordinator { get; }

	/// <summary>
	/// Gets the request-tracking session for hosts that drive the completion request pipeline themselves
	/// instead of using <see cref="RequestAsync"/>: it begins and tracks requests, reports their
	/// cancellation token, and cancels or invalidates outstanding results.
	/// </summary>
	/// <remarks>
	/// The session shares its request lifetime with <see cref="RequestAsync"/>, so a manually driven
	/// request and a standard request supersede each other. Disposing the controller ends the session.
	/// </remarks>
	public TextCompletionRequestSession Requests { get; }

	/// <summary>
	/// Gets a value indicating whether the running editor assembly exposes the completion tooltip this
	/// controller styles and resolves descriptions for. The value is constant for the process.
	/// </summary>
	/// <remarks>
	/// This is the package's version-compat probe: the window's tooltip is reached through the binding's
	/// tooltip access seam, so a host can query whether the running editor exposes it before relying on
	/// description tooltips. When the tooltip is unavailable, description tooltips degrade (one warning is
	/// logged with event id 1002) while every other feature keeps working.
	/// </remarks>
	public static bool IsTooltipSupported => CompletionWindowTooltipAccess.IsFieldAvailable;

	/// <summary>
	/// Schedules a completion request to run after the configured debounce delay. Has no effect when the
	/// controller's hooks carry no <see cref="TextCompletionControllerHooks.ScheduledRequestAsync"/> callback.
	/// </summary>
	/// <remarks>
	/// Arming again while a request is scheduled restarts the debounce delay with the newest schedule call.
	/// The scheduled callback runs on the text area's dispatcher; a failure it throws is logged and leaves the
	/// tracked state intact, and a cancellation it reports is ignored.
	/// </remarks>
	public void ScheduleRequest()
		=> _requestScheduler.ScheduleRequest();

	/// <summary>
	/// Cancels the debounced completion request scheduled by <see cref="ScheduleRequest"/>, if one is pending.
	/// The in-flight provider request is not affected; use
	/// <see cref="TextCompletionRequestSession.CancelInFlightRequest"/> or
	/// <see cref="TextCompletionRequestSession.InvalidateRequests"/> on <see cref="Requests"/> for that.
	/// </summary>
	public void CancelScheduledRequest()
		=> _requestScheduler.CancelScheduledRequest();

	/// <summary>
	/// Closes the active completion window and hides any completion tooltip.
	/// </summary>
	/// <remarks>
	/// Closing does not invalidate in-flight requests: a completion result that was already requested can still
	/// reopen a window when the host applies its decision afterwards. A host that treats the close as the end of
	/// the completion session calls <see cref="TextCompletionRequestSession.InvalidateRequests"/> (or
	/// <see cref="TextCompletionRequestSession.CancelInFlightRequest"/> when the provider should stop early) on
	/// <see cref="Requests"/> together with this method.
	/// </remarks>
	public void CloseWindow()
	{
		if (_lifetime.IsDisposed)
			return;

		CloseWindowCore();
	}

	private void CloseWindowCore()
	{
		// A tracked window's tooltip is closed while the window is still tracked; the coordinator then raises
		// WindowClosed once and the handler runs the shared cleanup, so the cleanup is not duplicated here.
		if (WindowCoordinator.ActiveWindow is CompletionWindow completionWindow)
		{
			_tooltipPresenter.CloseTooltip(completionWindow);
			WindowCoordinator.Close();
			return;
		}

		// Nothing is tracked, so the coordinator raises no WindowClosed and cannot run the cleanup; run it
		// here, then close the created-but-unshown window the coordinator may still hold.
		CompleteCloseCleanup();
		WindowCoordinator.Close();
	}

	// A tracked window can close itself through a commit, Escape, or focus loss, and CloseWindowCore closes one
	// through the coordinator; both funnel through this single cleanup, so it runs exactly once per close. The
	// coordinator no longer tracks a window afterwards, so stale window or tooltip state must be cleared and the
	// window's list-box handlers must be detached.
	private void HandleTrackedWindowClosed(object? sender, EventArgs e)
		=> CompleteCloseCleanup();

	private void CompleteCloseCleanup()
	{
		CancelPendingCompletionWork();
		DetachWindowHandlers();
		ResetWindowPresentation();
	}

	private void CancelPendingCompletionWork()
	{
		_requestScheduler.CancelScheduledRequest();
		_tooltipPresenter.CancelUpdate();
	}

	private void ResetWindowPresentation()
	{
		SetWindowState(false);
		SetTooltipState(null, false);
	}

	/// <summary>
	/// Opens a completion window with the given items for the replacement range, or refreshes the tracked
	/// window in place when its replacement start is unchanged. The window sizes to its content up to the
	/// configured maximum height.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The replacement range uses zero-based document offsets with an exclusive end offset. A raw offset beyond the
	/// document end is clamped to the document length, so the replacement range can never extend past the current
	/// document. A negative offset or a raw end offset before the raw start offset is rejected instead, because
	/// such a range is always a caller error. Both offsets are compared before clamping, so an out-of-document
	/// start and end are only accepted when they are ordered correctly.
	/// </para>
	/// <para>
	/// A refresh keeps the open window when its replacement start still matches the supplied start: its items and
	/// end offset are replaced and the sizing and window hook re-applied, so per-keystroke completions do not
	/// recreate the window. A different start closes the old window and opens a new one, which also covers a
	/// window whose anchors moved with an edit. The supplied items are the candidate set for the typed word;
	/// the completion list re-ranks and filters them against the text between the two offsets. A
	/// <see langword="null"/> entry in the collection is dropped, matching the code-action path, because a null
	/// item would throw inside the completion list's filtering and rendering. An empty item collection returns
	/// <see langword="false"/> without opening or refreshing a window, and - unless
	/// <see cref="TextCompletionControllerOptions.CloseWhenEmpty"/> is disabled - a window whose list becomes
	/// empty after filtering is closed rather than showing the completion list's empty template. After an open, the
	/// initial item selection is established on the dispatcher at <c>ContextIdle</c> priority; a refresh
	/// re-selects synchronously as part of its re-filtering. A provider-preselected item that survived the
	/// filtering (see <see cref="IPreselectedCompletionData"/>) wins over the best match and is scrolled into
	/// view.
	/// </para>
	/// </remarks>
	/// <param name="items">The completion items to show.</param>
	/// <param name="startOffset">The zero-based start offset of the replacement range.</param>
	/// <param name="endOffset">The exclusive end offset of the replacement range.</param>
	/// <returns>
	/// <see langword="true"/> when the operation was applied (the window was opened or refreshed), even when the
	/// window closes itself right afterwards because its filtered list became empty - the close is part of the
	/// operation; otherwise, <see langword="false"/>. A text area without a document reports
	/// <see langword="false"/> as well, because the replacement range cannot be anchored.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> or <paramref name="endOffset"/> is negative, or <paramref name="endOffset"/> is
	/// smaller than <paramref name="startOffset"/>.
	/// </exception>
	public bool OpenOrRefresh(IEnumerable<ICompletionData> items, int startOffset, int endOffset)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentOutOfRangeException.ThrowIfNegative(startOffset, nameof(startOffset));
		ArgumentOutOfRangeException.ThrowIfNegative(endOffset, nameof(endOffset));

		// The raw offsets are compared before clamping so a malformed range is rejected even when both raw
		// offsets would clamp to the same document offset.
		if (endOffset < startOffset)
			throw new ArgumentOutOfRangeException(nameof(endOffset), endOffset, "The end offset must not be smaller than the start offset.");

		if (_lifetime.IsDisposed)
			return false;

		ICompletionData[] completionItems = FilterNullItems(items);

		if (completionItems.Length == 0)
			return false;

		// A detached document (a text area can outlive its document) cannot anchor the replacement range,
		// so the operation reports no-op instead of failing.
		TextDocument? document = _textArea.Document;

		if (document is null)
			return false;

		int clampedStartOffset = document.ClampOffset(startOffset);
		int clampedEndOffset = document.ClampOffset(endOffset);

		// A refresh keeps the open window while the replacement start is unchanged; the window re-anchors its
		// start on edits, so a shifted anchor falls back to a complete reopen.
		if (WindowCoordinator.ActiveWindow is CompletionWindow activeWindow && activeWindow.StartOffset == clampedStartOffset)
			return RefreshWindow(activeWindow, completionItems, clampedEndOffset);

		return OpenWindow(completionItems, clampedStartOffset, clampedEndOffset);
	}

	// Drops null entries a misbehaving host item factory can surface; a null item would throw inside
	// the completion list's filtering and rendering. This mirrors the code-action path, which skips null items too.
	private static ICompletionData[] FilterNullItems(IEnumerable<ICompletionData> items)
		=> items.Where(static item => item is not null).ToArray();

	private bool OpenWindow(ICompletionData[] completionItems, int startOffset, int endOffset)
	{
		CloseWindow();
		CompletionWindow completionWindow = WindowCoordinator.Create(maxHeight: _options.WindowMaxHeight);

		try
		{
			_tooltipPresenter.ApplySkin(completionWindow);
			ConfigureNonActivatingWindow(completionWindow);
			completionWindow.StartOffset = startOffset;
			completionWindow.EndOffset = endOffset;

			foreach (ICompletionData item in completionItems)
				completionWindow.CompletionList.CompletionData.Add(item);

			// The hook runs after the controller applied its baseline sizing, offsets, and items, immediately
			// before the window is shown, so anything the hook sets wins.
			ApplyWindowConfiguration(completionWindow, completionItems);

			WindowCoordinator.Show();
		}
		catch
		{
			// A throwing host hook or a failed show must not strand a created window or leave stale state behind.
			CloseWindowCore();
			throw;
		}

		SetWindowState(WindowCoordinator.IsWindowOpen);
		ScheduleInitialSelection();
		return true;
	}

	private bool RefreshWindow(CompletionWindow completionWindow, ICompletionData[] completionItems, int endOffset)
	{
		try
		{
			// The visible tooltip describes the previously selected item, so it is invalidated together with
			// the item set; the selection change below re-arms the tooltip update for the new selection.
			_tooltipPresenter.CancelUpdate();
			_tooltipPresenter.CloseTooltip(completionWindow);

			completionWindow.EndOffset = endOffset;

			IList<ICompletionData> completionData = completionWindow.CompletionList.CompletionData;
			completionData.Clear();

			foreach (ICompletionData item in completionItems)
				completionData.Add(item);

			ApplyWindowConfiguration(completionWindow, completionItems);

			// The completion list returns early when it is asked to select the query it already applied, which
			// would leave the filtered list of the previous item set visible. The empty query resets the list to
			// every new item and the real query re-filters it, but the list memoizes the empty query like any
			// other, so a refresh whose query is empty must first invalidate that memo with a query that cannot
			// match; otherwise, both calls are no-ops and the replaced item set is never displayed. The sequence
			// relies on two engine behaviors: the memoized early return in SelectItem, and the ItemsSource swap
			// to a filtered collection while IsFiltering is enabled (the default); re-verify both before
			// changing the call sequence.
			CompletionList completionList = completionWindow.CompletionList;
			string query = GetCompletionWindowQuery(completionWindow);

			if (query.Length == 0)
				completionList.SelectItem(FilterResetQuery);

			completionList.SelectItem(string.Empty);
			completionList.SelectItem(query);
			ApplyPreselection(completionWindow);

			CloseWindowIfEmpty();
		}
		catch
		{
			// A throwing host hook during a refresh must not leave a half-updated window behind.
			CloseWindow();
			throw;
		}

		SetWindowState(WindowCoordinator.IsWindowOpen);
		return true;
	}

	// The completion window is shown non-activatable by default, so the click handler selects the clicked item
	// explicitly; the selection change is also what schedules the tooltip update. The handler is harmless when
	// the native input path already selected the item, and it is skipped when the host opts into an activatable
	// window, where the native selection path applies.
	private void ConfigureNonActivatingWindow(CompletionWindow completionWindow)
	{
		if (!_options.NonActivatingWindow)
			return;

		CompletionWindowInterop.MakeNonActivatable(completionWindow);

		ListBox listBox = completionWindow.CompletionList.ListBox;
		_itemClickSubscription?.Dispose();
		_itemClickSubscription = TextCompletionHost.SubscribeItemClick(listBox, HandleCompletionListClick);
	}

	// Both list-box handlers of a window must not outlive it: a closed or replaced window's list box can
	// still raise a queued selection or click event, and the tooltip update such an event would schedule
	// belongs to the stale window. The presenter owns its selection handler and detaches it here.
	private void DetachWindowHandlers()
	{
		_itemClickSubscription?.Dispose();
		_itemClickSubscription = null;
		_tooltipPresenter.Detach();
	}

	/// <summary>
	/// Runs a completion request through the standard pipeline: the callback computes the decision (including
	/// any provider round trips), and the controller admits the request, rejects a superseded or canceled
	/// result, and applies the decision.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the convenience entry point for hosts that follow the standard completion scenario: call it
	/// from the trigger policy and let the callback produce the decision from the shared completion-session
	/// kernel or its own provider logic. The request is admitted through the shared core request coordinator
	/// behind <see cref="Requests"/>, so starting it cancels the previous request's token - whether that
	/// request was another standard request or a manually driven one - and a callback that completes after a
	/// newer request started or after <see cref="TextCompletionRequestSession.CancelInFlightRequest"/> was
	/// called is discarded, even when the provider ignored the token.
	/// </para>
	/// <para>
	/// The callback may observe the supplied cancellation token and may report cancellation by throwing
	/// <see cref="OperationCanceledException"/> or letting the provider throw it, which this method reports as
	/// <see langword="false"/>. Any other failure is contained the same way a failure on the scheduled path is:
	/// it is logged with event id 1000 and the method reports <see langword="false"/>, so timer, dispatcher, and
	/// event-handler callers never observe provider exceptions.
	/// </para>
	/// </remarks>
	/// <param name="requestAsync">The callback that computes the decision for this request.</param>
	/// <returns>
	/// <see langword="true"/> when the request was applied and opened or refreshed a completion window;
	/// <see langword="false"/> when the request was superseded, canceled, failed, or produced no window.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="requestAsync"/> is <see langword="null"/>.</exception>
	public async Task<bool> RequestAsync(
		Func<CancellationToken, Task<TextCompletionSessionDecision>> requestAsync)
	{
		ArgumentNullException.ThrowIfNull(requestAsync);

		if (_lifetime.IsDisposed)
			return false;

		bool decisionApplied = false;

		// The coordinator admits the request as the latest one, cancels the superseded request's token, and
		// classifies the outcome: a superseded or canceled request is never applied, even when its provider
		// ignored the token. The admission check and the apply step run after the provider's await, which
		// resumes on a thread-pool thread when the creating thread captured no synchronization context, so
		// they are marshalled to the text area thread explicitly instead of trusting the ambient context.
		Task<RequestOutcome> request = Requests.Coordinator.RunAsync(
			state: requestAsync,
			computeAsync: static (callback, cancellationToken) => callback(cancellationToken),
			canApply: (_, _) => !_lifetime.IsDisposed,
			apply: decision => decisionApplied = DispatcherInvocation.Run(
				_textArea.Dispatcher,
				() => ApplyDecision(decision)));

		try
		{
			RequestOutcome outcome = await request.ConfigureAwait(true);

			// The run completes only after the decision was applied, so the recorded result carries whether
			// the applied decision actually produced a window.
			return outcome == RequestOutcome.Completed && decisionApplied;
		}
		catch (OperationCanceledException)
		{
			// A superseded or canceled request reports cancellation, not a failure.
			return false;
		}
		catch (Exception exception)
		{
			// The callback runs inside a controller-owned pipeline (a trigger handler, a command, or the
			// scheduled path), so provider and host-callback failures are contained and logged instead of
			// faulting the caller, matching the hover and signature help controllers.
			if (!_lifetime.IsDisposed)
				_requestScheduler.LogRequestFailed(exception);

			return false;
		}
	}

	/// <summary>
	/// Applies a completion session decision. A requested close is performed first; when the decision
	/// requests items, the method maps them and opens or refreshes a completion window for the decision's
	/// replacement range.
	/// </summary>
	/// <remarks>
	/// Items are mapped through the <see cref="TextCompletionControllerHooks.CompletionItemFactory"/> constructor
	/// hook when it is set, and through the package's default <see cref="TextCompletionItemCompletionData"/> adapter
	/// otherwise, so the standard scenario works without a host mapper. A host that already holds shared
	/// <see cref="TextCompletionItem"/> instances opens them directly with
	/// <c>ApplyDecision(TextCompletionSessionDecision.Open(items, startOffset, endOffset))</c> and gets the
	/// same mapping. An empty item list returns <see langword="false"/> without mapping anything. The shared
	/// decision type guarantees that a decision carrying items also carries a valid, ordered replacement
	/// range, and <see cref="OpenOrRefresh"/> clamps offsets beyond the document end. The method applies the
	/// decision as the newest session state without re-checking request freshness, so a host that drives its
	/// own request steps must discard a superseded or canceled request before calling it - the same currency
	/// checks the standard pipeline applies through <see cref="Requests"/>.
	/// </remarks>
	/// <param name="decision">The decision to apply.</param>
	/// <returns>
	/// <see langword="true"/> when a completion window was opened or refreshed; otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The item factory returned <see langword="null"/> for one of the decision's items.
	/// </exception>
	public bool ApplyDecision(TextCompletionSessionDecision decision)
	{
		if (_lifetime.IsDisposed)
			return false;

		if (decision.ShouldClose)
			CloseWindow();

		// A decision without items requests no session, and an empty list would not open a window.
		if (decision.Items is not { Count: > 0 } decisionItems)
			return false;

		// The shared decision type ties the replacement range to the items, so both offsets are present
		// whenever items are.
		if (decision.StartOffset is not int startOffset || decision.EndOffset is not int endOffset)
			return false;

		Func<TextCompletionItem, ICompletionData> itemFactory = _completionItemFactory ?? CreateDefaultCompletionData;

		var items = new ICompletionData[decisionItems.Count];

		for (int i = 0; i < decisionItems.Count; i++)
		{
			ICompletionData item = itemFactory(decisionItems[i]);

			// A null return violates the factory contract in a way no caller can act on as a parameter
			// error, so it is reported as an invalid operation naming the offending item.
			if (item is null)
				throw new InvalidOperationException($"The completion item factory returned null for the item '{decisionItems[i].Label}'.");

			items[i] = item;
		}

		return OpenOrRefresh(items, startOffset, endOffset);
	}

	private static TextCompletionItemCompletionData CreateDefaultCompletionData(TextCompletionItem item) => new(item);

	/// <summary>
	/// Schedules the completion window to close if it is empty.
	/// </summary>
	/// <remarks>
	/// Empty completion windows are closed instead of showing the completion list's empty template. The close
	/// check is posted to the text area's dispatcher and runs asynchronously, so the window may still be open
	/// when this method returns.
	/// </remarks>
	public void ScheduleCloseIfEmpty()
	{
		if (_lifetime.IsDisposed)
			return;

		TextCompletionHost.Post(_textArea.Dispatcher, CloseWindowIfEmpty, DispatcherPriority.Background);
	}

	/// <summary>
	/// Cancels any pending completion tooltip update.
	/// </summary>
	public void CancelTooltipUpdate()
	{
		if (_lifetime.IsDisposed)
			return;

		_tooltipPresenter.CancelUpdate();
	}

	/// <summary>
	/// Stops completion scheduling and closes any open completion window or tooltip.
	/// </summary>
	/// <remarks>
	/// Disposal is synchronous: the debounce timers stop, an in-flight request token is canceled and
	/// invalidated, and the open window is closed. The controller does not start or continue any work
	/// afterwards, and a host that inspects <see cref="CurrentPresentation"/> after disposal sees the state of
	/// the closed session (no window, no scheduled request, no tooltip).
	/// </remarks>
	public void Dispose()
	{
		if (_lifetime.IsDisposed)
			return;

		// The close path uses the request scheduler and the tooltip presenter, so the tracked window is
		// closed before those helpers are disposed; closing first also keeps the coordinator's WindowClosed
		// handler attached for the close it performs.
		CloseWindowCore();

		WindowCoordinator.WindowClosed -= HandleTrackedWindowClosed;
		_inputStageSubscription?.Dispose();
		_inputStageSubscription = null;
		_lifetime.Dispose();
		_requestScheduler.Dispose();
		Requests.Dispose();
		_tooltipPresenter.Dispose();
	}

	private static void HandleCompletionListClick(ListBox listBox, PointerClickArgs e)
	{
#if AVALONIAEDIT
		if (e.Handled || e.InitialPressMouseButton != MouseButton.Left)
			return;

		if (e.Source is not Visual source)
			return;

		// The Avalonia items control exposes no container-from-element helper, so the item container is
		// resolved by walking the visual tree from the clicked element, which also covers non-visual content
		// such as a Run in an inline-based item template.
		ListBoxItem? listBoxItem = source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
		object? item = listBoxItem is null ? null : listBox.ItemFromContainer(listBoxItem);
#else
		if (e.Handled || e.OriginalSource is not DependencyObject originalSource)
			return;

		// ContainerFromElement resolves the item container from any clicked element, including non-visual
		// content such as a Run in an inline-based item template. Walking the visual tree instead would fail
		// because VisualTreeHelper rejects elements that are not Visuals.
		ListBoxItem? listBoxItem = ItemsControl.ContainerFromElement(listBox, originalSource) as ListBoxItem;
		object? item = listBoxItem?.DataContext;
#endif

		if (item is null)
			return;

		// The non-activatable window keeps the text area's keyboard focus, so the click selects the item
		// explicitly; selecting a different item raises SelectionChanged, which schedules the tooltip update.
		if (!ReferenceEquals(listBox.SelectedItem, item))
			listBox.SelectedItem = item;

		listBox.ScrollIntoView(item);
	}

	// The commit-character policy is installed on both text-input stages. The stage that runs before other
	// input subscribers (for example an auto-closing service) sees the character first, so a commit character
	// is accepted deterministically however the host ordered its input services; the other stage repeats the
	// check for input that bypasses the first one, which is how programmatic input is delivered. Neither
	// handler consumes the event: the typed character is still inserted after the committed text, so one
	// keystroke both accepts the item and types the character.
	private void HandlePreviewTextInput(TextInputArgs e)
		=> TryCommitOnCommitCharacter(e);

	private void HandleTextEntering(TextInputArgs e)
		=> TryCommitOnCommitCharacter(e);

	private void TryCommitOnCommitCharacter(TextInputArgs e)
	{
		if (_lifetime.IsDisposed || !_options.AcceptOnCommitCharacters || e.Handled)
			return;

		// Commit characters are single characters; any other input (for example a multi-character
		// composition result) never matches, so the window stays open.
		if (e.Text is not { Length: 1 } typedText)
			return;

		if (WindowCoordinator.ActiveWindow is not CompletionWindow completionWindow)
			return;

		if (completionWindow.CompletionList.SelectedItem is not ICommitCharacterCompletionData { CommitCharacters.Count: > 0 } completionData)
			return;

		if (!ContainsCommitCharacter(completionData.CommitCharacters, typedText))
			return;

		// The list closes the window and completes the selected item before the text pipeline inserts the
		// typed character at the caret the completion left behind.
		completionWindow.CompletionList.RequestInsertion(e);
	}

	private static bool ContainsCommitCharacter(IReadOnlyList<string> commitCharacters, string typedText)
	{
		for (int index = 0; index < commitCharacters.Count; index++)
		{
			if (string.Equals(commitCharacters[index], typedText, StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	private void SetRequestScheduled(bool isRequestScheduled)
		=> UpdatePresentationState(CurrentPresentation with { IsRequestScheduled = isRequestScheduled });

	private void SetWindowState(bool hasOpenWindow)
		=> UpdatePresentationState(CurrentPresentation with { IsListVisible = hasOpenWindow });

	private void SetTooltipState(object? tooltipContent, bool isTooltipVisible)
		=> UpdatePresentationState(CurrentPresentation with { TooltipContent = tooltipContent, IsTooltipVisible = isTooltipVisible });

	private void UpdatePresentationState(TextCompletionPresentationState state)
		=> CurrentPresentation = state;

	// Applies the content sizing and the host window hook in the order both window paths rely on: the
	// controller sizes to the new item set first, then the hook runs last so anything it sets wins.
	private void ApplyWindowConfiguration(CompletionWindow completionWindow, ICompletionData[] completionItems)
	{
		ResizeWindow(completionWindow, completionItems);
		_configureWindow?.Invoke(completionWindow);
	}

	private void ResizeWindow(CompletionWindow completionWindow, ICompletionData[] items)
	{
		double requiredWidth = CompletionWindowSizing.MeasureRequiredWidth(
			_textArea,
			_options,
			_getDisplayInfo,
			_measureItemWidth,
			items);

		// MeasureRequiredWidth already floors the content width at WindowMinContentWidth, so only the width
		// helper's clamp can bind here; the chrome/maximum relationship stays defined in the sizing helper.
#if AVALONIAEDIT
		// An Avalonia completion window is a popup that draws no chrome of its own, so the content width is
		// applied to its completion list.
		completionWindow.CompletionList.Width = CompletionWindowSizing.GetWindowWidth(_options, requiredWidth);
#else
		completionWindow.Width = CompletionWindowSizing.GetWindowWidth(_options, requiredWidth);
#endif
	}

	private void ScheduleInitialSelection()
		=> TextCompletionHost.Post(_textArea.Dispatcher, SelectInitialItem, DispatcherPriority.ContextIdle);

	private void SelectInitialItem()
	{
		if (WindowCoordinator.ActiveWindow is not CompletionWindow completionWindow)
			return;

		completionWindow.CompletionList.SelectItem(GetCompletionWindowQuery(completionWindow));
		ApplyPreselection(completionWindow);
		CloseWindowIfEmpty();
	}

	// A provider may mark one item as preselected (LSP's "preselect"); when such an item survived the
	// filtering it becomes the selection, because it is the server's explicit answer to the query. Without a
	// hint the library keeps the engine's best-match selection. The flag is read through
	// IPreselectedCompletionData, so the default adapter and custom host data follow the same policy. The
	// hint is re-applied on an in-place refresh as well, because a refresh replaces the item set with a fresh
	// provider answer - the newest answer to the current query - and a replaced item set cannot carry the
	// previously selected item over either way. The selection is scrolled into view explicitly: the engine's
	// selected-item setter does not scroll while its own selection path centers the best match, so a
	// preselected item ranked below the visible window would otherwise stay invisible.
	private static void ApplyPreselection(CompletionWindow completionWindow)
	{
		ItemCollection items = completionWindow.CompletionList.ListBox.Items;

		for (int index = 0; index < items.Count; index++)
		{
			if (items[index] is not ICompletionData completionItem)
				continue;

			if (completionItem is not IPreselectedCompletionData { IsPreselected: true })
				continue;

			completionWindow.CompletionList.ListBox.SelectedItem = completionItem;
			completionWindow.CompletionList.ScrollIntoView(completionItem);
			return;
		}
	}

	private void CloseWindowIfEmpty()
	{
		if (!_options.CloseWhenEmpty)
			return;

		if (WindowCoordinator.ActiveWindow is not CompletionWindow completionWindow)
			return;

		if (completionWindow.CompletionList.ListBox.Items.Count > 0)
			return;

		CloseWindow();
	}

	private string GetCompletionWindowQuery(CompletionWindow completionWindow)
	{
		// A detached document has no text to query, so the query is empty; otherwise, both offsets are
		// clamped to the current document so an edit between the window update and this query cannot
		// produce an out-of-range read.
		TextDocument? document = _textArea.Document;

		if (document is null)
			return string.Empty;

		int startOffset = document.ClampOffset(completionWindow.StartOffset);
		int endOffset = Math.Max(startOffset, document.ClampOffset(completionWindow.EndOffset));

		return endOffset > startOffset
			? document.GetText(startOffset, endOffset - startOffset)
			: string.Empty;
	}
}
