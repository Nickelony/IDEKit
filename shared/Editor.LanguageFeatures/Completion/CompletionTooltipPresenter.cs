#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
#endif
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Infrastructure;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Owns the completion tooltip pipeline: tooltip access and chrome styling, the debounced description
/// resolution, and the tooltip reporting that feeds the completion presentation state.
/// </summary>
/// <remarks>
/// <para>
/// The presenter is created and used on the editor thread, because its resolve timer runs on the creating
/// thread's dispatcher and the tooltip it styles belongs to the editor's completion window.
/// </para>
/// <para>
/// The completion window does not expose its tooltip publicly, so the tooltip is resolved through
/// <see cref="CompletionWindowTooltipAccess"/>. When the running editor provides no tooltip, tooltip styling
/// and description resolution are disabled, and the first failure is logged once so an engine upgrade that
/// starts exposing a tooltip becomes visible to the host.
/// </para>
/// <para>
/// The tooltip is decorated rather than replaced: the editor's stock selection-changed handler writes the same
/// tooltip instance for every item description and cannot be disabled without touching the private field, so a
/// host-owned second tooltip would either render next to the stock one or still depend on that field. Keeping
/// one tooltip means the stock writer and this presenter must render strings identically; that parity is a
/// deliberate, permanent constraint of sharing an engine-owned surface, kept pinned by a rendering-parity
/// test so an engine rendering change fails loudly. Replacing the decoration with a host-owned details
/// surface (or an upstream public tooltip) is the only alternative.
/// </para>
/// <para>
/// The presenter does not own the completion presentation state; it reports tooltip visibility and content
/// through the state callback supplied by the completion controller, and it reads the tracked window
/// through the controller's active-window accessor. Both callbacks keep the presenter independent of the
/// controller instance.
/// </para>
/// </remarks>
internal sealed partial class CompletionTooltipPresenter : IDisposable
{
	// The completion tooltip uses package log event ids 1001 (resolve failed) and 1002 (unsupported
	// access); the README carries the canonical id table.
	[LoggerMessage(
		EventId = 1001,
		EventName = "TooltipResolutionFailed",
		Level = LogLevel.Warning,
		Message = "Failed to resolve completion tooltip content."
	)]
	private static partial void LogTooltipResolutionFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1002,
		EventName = "TooltipAccessUnsupported",
		Level = LogLevel.Warning,
		Message = "The editor does not expose its completion window tooltip, so completion tooltip styling and "
			+ "description resolution are disabled. This is reported once so a missing, renamed, or "
			+ "unsupported tooltip member stays visible to the host."
	)]
	private static partial void LogTooltipAccessUnsupported(ILogger logger, Exception? exception);

	private readonly ILogger _logger;
	private readonly Dispatcher _dispatcher;
	private readonly CompletionTooltipSkin _skin;
	private readonly Func<CompletionWindow?> _getActiveWindow;
	private readonly Action<object?, bool> _setTooltipState;
	private readonly Func<CompletionWindow, ToolTip?> _tryGetTooltip;
	private readonly Func<ICompletionData, CancellationToken, Task<object?>>? _resolveDescriptionAsync;
	private readonly Action<ToolTip>? _configureTooltip;
	private readonly DispatcherDebouncer _updateDebouncer;
	private readonly RequestTokenSource _updateTokens = new();
	private WindowSubscription? _subscription;
	private CancellationTokenSource? _resolveCancellation;
	private bool _accessWarningLogged;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionTooltipPresenter"/> class.
	/// </summary>
	/// <param name="options">The controller options the tooltip timing is read from.</param>
	/// <param name="skin">The tooltip chrome applied to each window's tooltip.</param>
	/// <param name="context">The dispatcher and callbacks the presenter runs against.</param>
	/// <param name="resolveDescriptionAsync">
	/// The host callback that resolves a selected item's description, or <see langword="null"/> when the host
	/// supplied none.
	/// </param>
	/// <param name="configureTooltip">
	/// The host callback that configures a window's tooltip after the skin is applied, or <see langword="null"/>
	/// when the host supplied none.
	/// </param>
	/// <param name="logger">The logger for tooltip failures.</param>
	internal CompletionTooltipPresenter(
		TextCompletionControllerOptions options,
		CompletionTooltipSkin skin,
		CompletionTooltipPresenterContext context,
		Func<ICompletionData, CancellationToken, Task<object?>>? resolveDescriptionAsync,
		Action<ToolTip>? configureTooltip,
		ILogger logger)
	{
		_logger = logger;
		_dispatcher = context.Dispatcher;
		_skin = skin;
		_getActiveWindow = context.GetActiveWindow;
		_setTooltipState = context.SetTooltipState;
		_tryGetTooltip = context.TryGetTooltip;
		_resolveDescriptionAsync = resolveDescriptionAsync;
		_configureTooltip = configureTooltip;

		// The resolve debouncer and its timer are created on construction, and each selection change arms the
		// timer, so debounced description updates work even when the host schedules no requests. The debouncer
		// is bound to the editor dispatcher explicitly, even when the controller was created on a thread whose
		// dispatcher is not the editor's.
		_updateDebouncer = new DispatcherDebouncer(options.TooltipResolveDelay, context.Dispatcher);
	}

	/// <summary>
	/// Gets a value indicating whether a debounced tooltip update is waiting for its delay.
	/// </summary>
	internal bool IsUpdatePending => _updateDebouncer.IsPending;

	/// <summary>
	/// Applies the tooltip skin to a newly created completion window and wires the list selection changes
	/// that schedule debounced tooltip updates.
	/// </summary>
	/// <remarks>
	/// The configuration hook supplied to the constructor runs after the skin, so a host can override any of
	/// the skin's values; a throwing hook propagates to the completion controller's window rollback scope
	/// together with the window hook. When the editor provides no tooltip, the skin and the hook
	/// are skipped and the one-time warning has been logged. Each call subscribes the window's list box once:
	/// a subscription left over from an earlier window is detached first, and the completion controller
	/// detaches this window's subscription through <see cref="Detach"/> when the window closes.
	/// </remarks>
	/// <param name="completionWindow">The completion window to skin.</param>
	internal void ApplySkin(CompletionWindow completionWindow)
	{
		ListBox listBox = completionWindow.CompletionList.ListBox;

		if (_tryGetTooltip(completionWindow) is not ToolTip tooltip)
		{
			LogAccessUnsupportedOnce();
			return;
		}

		if (_skin.Background is not null)
			tooltip.Background = _skin.Background;

		if (_skin.BorderBrush is not null)
			tooltip.BorderBrush = _skin.BorderBrush;

		tooltip.BorderThickness = _skin.BorderThickness;
		tooltip.Padding = _skin.Padding;
		CompletionTooltipHost.ApplyPlacement(tooltip, listBox, _skin);

		// The hook runs after the skin so a host can override placement, offsets, and padding.
		_configureTooltip?.Invoke(tooltip);

		// A previous subscription can only still exist when its window was never detached; the coordinator
		// shows at most one window at a time, so this reset is defensive and keeps the subscription single.
		Detach();

		// The editor's stock selection-changed handler already opens the tooltip for any non-null description
		// (wrapping only strings in a TextBlock) and fires immediately. This subscription adds a debounced
		// pass that can await ResolveDescriptionAsync and report the tooltip content through the completion
		// presentation state. The handler is a named member so it can be detached when the window closes, and
		// the update it schedules re-checks that this window is still the tracked one before it writes.
		_subscription = new WindowSubscription(completionWindow, listBox, tooltip);
		listBox.SelectionChanged += HandleCompletionListSelectionChanged;
	}

	/// <summary>
	/// Detaches the list-selection handler installed by <see cref="ApplySkin"/> and cancels pending tooltip work.
	/// </summary>
	/// <remarks>
	/// The completion controller calls this when a window closes, so a closed window's list box can no longer
	/// schedule tooltip updates and a queued selection change can no longer write into that window's tooltip.
	/// The method is idempotent.
	/// </remarks>
	internal void Detach()
	{
		if (_subscription is WindowSubscription subscription)
			subscription.ListBox.SelectionChanged -= HandleCompletionListSelectionChanged;

		_subscription = null;
		CancelUpdate();
	}

	/// <summary>
	/// Closes the tooltip of the given completion window when the tooltip is accessible.
	/// </summary>
	/// <remarks>
	/// The editor's own window close also closes the tooltip; closing it here keeps the tracked tooltip state
	/// consistent before the window is closed and untracked.
	/// </remarks>
	/// <param name="completionWindow">The completion window whose tooltip is closed.</param>
	internal void CloseTooltip(CompletionWindow completionWindow)
	{
		if (_tryGetTooltip(completionWindow) is ToolTip tooltip)
			CompletionTooltipHost.SetIsOpen(tooltip, false);
	}

	/// <summary>
	/// Cancels a pending or in-flight tooltip update.
	/// </summary>
	/// <remarks>
	/// A running resolver observes the cancellation through the token passed to it; a result it produces after
	/// the cancellation is discarded by the update token.
	/// </remarks>
	internal void CancelUpdate()
	{
		_updateTokens.Invalidate();
		_updateDebouncer.Cancel();
		CancelResolveCancellation();
	}

	/// <summary>
	/// Stops the tooltip update timer and cancels pending tooltip work.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		Detach();
		_updateDebouncer.Dispose();
	}

	// A selection change on the subscribed list box schedules one debounced update for that window's tooltip.
	private void HandleCompletionListSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_subscription is WindowSubscription subscription)
			ScheduleUpdate(subscription.Window, subscription.Tooltip);
	}

	// A dispatcher debounce callback that runs the asynchronous update; like a timer handler it is an async
	// void dispatcher method, so the update itself contains every failure.
	private async void RunUpdate(CompletionWindow window, ToolTip tooltip, long updateToken)
	{
		var resolveCancellation = new CancellationTokenSource();
		_resolveCancellation = resolveCancellation;

		await UpdateAsync(window, tooltip, updateToken, resolveCancellation).ConfigureAwait(true);
	}

	// A selection change schedules one debounced update; starting a new update invalidates the previous
	// token and cancels a resolver that is still running.
	private void ScheduleUpdate(CompletionWindow window, ToolTip tooltip)
	{
		long updateToken = _updateTokens.BeginRequest();
		CancelResolveCancellation();
		_updateDebouncer.Arm(() => RunUpdate(window, tooltip, updateToken));
	}

	private async Task UpdateAsync(
		CompletionWindow window,
		ToolTip tooltip,
		long updateToken,
		CancellationTokenSource resolveCancellation)
	{
		try
		{
			CancellationToken cancellationToken = resolveCancellation.Token;

			// An update writes only into the tooltip of the window that scheduled it, and only while that
			// window is still the tracked one. Window teardown cancels outstanding updates, but a closed or
			// replaced window's list box can still raise a queued selection change; comparing the tracked
			// window for identity (instead of only checking for null) keeps such an update from writing
			// content into a stale tooltip.
			if (_isDisposed
				|| cancellationToken.IsCancellationRequested
				|| !ReferenceEquals(_getActiveWindow(), window))
			{
				return;
			}

			ListBox listBox = window.CompletionList.ListBox;

			if (listBox.SelectedItem is not ICompletionData item)
			{
				CompletionTooltipHost.SetIsOpen(tooltip, false);
				_setTooltipState(null, false);
				return;
			}

			object? description = item.Description;

			if (description is not null)
				ApplyContent(tooltip, description);
			else
			{
				CompletionTooltipHost.SetIsOpen(tooltip, false);
				_setTooltipState(null, false);
			}

			if (_resolveDescriptionAsync is not null)
			{
				if (!_updateTokens.IsCurrent(updateToken))
					return;

				object? resolvedDescription = await _resolveDescriptionAsync(item, cancellationToken).ConfigureAwait(true);

				// The resolver's continuation resumes on the captured context when the creating thread has one.
				// Without a context (a manually pumped dispatcher, tooling, tests) it resumes on a thread-pool
				// thread, so the tooltip-affine remainder is marshalled to the editor thread explicitly.
				await DispatcherInvocation.RunAsync(
					_dispatcher,
					() => ApplyResolvedDescription(window, tooltip, updateToken, item, resolvedDescription, cancellationToken));
			}
		}
		catch (OperationCanceledException)
		{
			// A superseded or canceled resolve does not report a failure.
		}
		catch (Exception exception)
		{
			if (_isDisposed)
				return;

			LogTooltipResolutionFailed(_logger, exception);

			// Only the update that still owns the token and whose window is still tracked may close the
			// tooltip: a resolver that faults after a newer update (or a replacement window) already took
			// over must not clear the newer state. The tooltip and the presentation callback are
			// editor-thread affine, so the cleanup is marshalled; a dispatcher that is shutting down
			// rejects the hop, and the update is over either way.
			try
			{
				await DispatcherInvocation.RunAsync(_dispatcher, () =>
				{
					if (!_updateTokens.IsCurrent(updateToken) || !ReferenceEquals(_getActiveWindow(), window))
						return;

					CompletionTooltipHost.SetIsOpen(tooltip, false);
					_setTooltipState(null, false);
				});
			}
			catch (OperationCanceledException)
			{
				// The dispatcher shut down before the cleanup could run.
			}
			catch (InvalidOperationException)
			{
				// The dispatcher rejects further work while it shuts down.
			}
			catch (Exception cleanupException)
			{
				// A host presentation callback threw while the tooltip was being closed; report it instead of
				// letting it escape this asynchronous update.
				LogTooltipResolutionFailed(_logger, cleanupException);
			}
		}
		finally
		{
			// The update owns the token, so disposing its source here is safe even when the resolve was
			// canceled while it ran.
			resolveCancellation.Dispose();

			if (ReferenceEquals(_resolveCancellation, resolveCancellation))
				_resolveCancellation = null;
		}
	}

	// Applies or drops a resolved description after the resolver's token and the update token were checked.
	// Runs on the editor thread so the tooltip access and the presentation-state callback stay thread-affine.
	private void ApplyResolvedDescription(
		CompletionWindow window,
		ToolTip tooltip,
		long updateToken,
		ICompletionData item,
		object? resolvedDescription,
		CancellationToken cancellationToken)
	{
		if (_isDisposed || cancellationToken.IsCancellationRequested || !_updateTokens.IsCurrent(updateToken))
			return;

		// The window must still be the tracked one and must still have the resolved item selected; a
		// replaced window's tooltip must not receive a late description.
		if (!ReferenceEquals(_getActiveWindow(), window)
			|| !ReferenceEquals(window.CompletionList.ListBox.SelectedItem, item))
		{
			return;
		}

		if (resolvedDescription is not null)
			ApplyContent(tooltip, resolvedDescription);
		else
		{
			CompletionTooltipHost.SetIsOpen(tooltip, false);
			_setTooltipState(null, false);
		}
	}

	private void ApplyContent(ToolTip tooltip, object content)
	{
		// Two writers update this tooltip: the editor's stock selection-changed handler (which renders strings in
		// a wrapping TextBlock) and this debounced path, which additionally awaits the description resolver and
		// updates the presentation state. Reproduce the stock wrapping so both writers render identically.
		object tooltipContent = content is string text
			? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
			: content;

		tooltip.Content = tooltipContent;
		_setTooltipState(content, true);

		if (!CompletionTooltipHost.IsOpen(tooltip))
		{
			CompletionTooltipHost.SetIsOpen(tooltip, true);
		}
		else
		{
			tooltip.InvalidateMeasure();
			tooltip.InvalidateVisual();
		}
	}

	// The tooltip accessor fails when the editor no longer exposes its tooltip; the first failure per
	// presenter is logged once so an engine upgrade becomes visible to the host.
	private void LogAccessUnsupportedOnce()
	{
		if (_accessWarningLogged)
			return;

		_accessWarningLogged = true;
		LogTooltipAccessUnsupported(_logger, null);
	}

	// The update that owns the token disposes its source when it returns; canceling only keeps the token
	// safely observable for a resolver that is still running.
	private void CancelResolveCancellation()
	{
		CancellationTokenSource? resolveCancellation = _resolveCancellation;
		_resolveCancellation = null;
		resolveCancellation?.Cancel();
	}

	// The window, list box, and tooltip one Style call subscribed to; the list box raises the selection
	// changes that schedule debounced updates for that window's tooltip.
	private sealed record WindowSubscription(CompletionWindow Window, ListBox ListBox, ToolTip Tooltip);
}
