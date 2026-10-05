using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Owns the completion tooltip pipeline: tooltip access and chrome styling, the debounced description resolution, and the tooltip reporting
/// that feeds the completion presentation state.
/// </summary>
/// <remarks>
/// <para>
/// The presenter is created and used on the editor thread: its resolve timer runs on the editor dispatcher, and the tooltip it styles belongs
/// to the editor's completion window.
/// </para>
/// <para>
/// The completion window does not expose its tooltip publicly, so the tooltip is resolved through <see cref="CompletionTooltipResolver"/>.
/// When the running editor provides no tooltip, tooltip styling and description resolution are disabled, and the first failure is logged once.
/// </para>
/// <para>
/// The tooltip is decorated rather than replaced, because the editor's stock selection-changed handler owns that tooltip
/// instance and cannot be disabled without touching the private field. The stock writer and this presenter must therefore render strings identically;
/// that parity is a permanent constraint of sharing an engine-owned surface.
/// </para>
/// <para>
/// A Markdown documentation is shown as the element the host's Markdown builder returns. The stock handler writes the documentation's text first,
/// so the presenter replaces it in the same selection change, before the frame that would show the raw Markdown. The element is built
/// once per documentation, and the debounced pass that follows reuses it.
/// </para>
/// <para>
/// Without a host resolver, the debounced pass resolves the selected item of the default adapter through its own resolve callback,
/// so the documentation a language server sends with the resolve reaches the tooltip with its kind. An item that carries no callback, and an item
/// of a host's own data, resolves nothing.
/// </para>
/// <para>
/// The presenter does not own the completion presentation state; it reports tooltip visibility and content through the state callback supplied
/// by the completion presenter, and it reads the tracked window through the completion presenter's active-window accessor. Neither callback ties
/// the tooltip presenter to the completion presenter instance.
/// </para>
/// </remarks>
internal sealed partial class CompletionTooltipPresenter : IDisposable
{
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
	private readonly IUiDispatcher _dispatcher;
	private readonly Func<CompletionWindow?> _getActiveWindow;
	private readonly Action<object?, bool> _setTooltipState;
	private readonly Func<CompletionWindow, ToolTip?> _tryGetTooltip;
	private readonly Func<ICompletionData, CancellationToken, Task<object?>>? _resolveDescriptionAsync;
	private readonly Action<ToolTip>? _configureTooltip;
	private readonly Func<string, UIElement?>? _buildMarkdownContent;
	private readonly DispatcherDebouncer _updateDebouncer;
	private readonly LatestRequestGate _updates = new();

	private WindowSubscription? _subscription;

	// The resolved item of each adapter whose resolve succeeded.
	private readonly ConditionalWeakTable<CompletionItemAdapter, CompletionItem> _resolvedItems = [];

	// The element the builder returned last and the Markdown it was built from: the selection change builds it and the debounced pass reuses it.
	private (string Markdown, UIElement Element)? _builtMarkdown;

	private bool _isAccessWarningLogged;
	private bool _isDisposed;

	/// <param name="context">The dispatcher and callbacks the presenter runs against.</param>
	/// <param name="theme">The tooltip chrome applied to each window's tooltip.</param>
	/// <param name="hooks">
	/// The host hooks the presenter reads its description resolution,
	/// tooltip configuration and Markdown building from, or <see langword="null"/> when the host supplied none. Without a resolve hook,
	/// the resolve is left to the item of the default adapter.
	/// </param>
	/// <param name="resolveDelay">The delay before a selection change updates the tooltip.</param>
	/// <param name="logger">The logger for tooltip failures.</param>
	internal CompletionTooltipPresenter(
		CompletionTooltipPresenterContext context,
		CompletionTooltipTheme theme,
		CompletionPresenterHooks? hooks,
		TimeSpan resolveDelay,
		ILogger logger
	)
	{
		_logger = logger;
		_dispatcher = context.Dispatcher;
		Theme = theme;
		_getActiveWindow = context.GetActiveWindow;
		_setTooltipState = context.SetTooltipState;
		_tryGetTooltip = context.TryGetTooltip;
		_resolveDescriptionAsync = hooks?.ResolveDescriptionAsync;
		_configureTooltip = hooks?.ConfigureTooltip;
		_buildMarkdownContent = hooks?.BuildMarkdownContent;

		// The resolve debouncer and its timer are created on construction, and each selection change arms the
		// timer, so debounced description updates work even when the host schedules no requests. The debouncer
		// is bound to the editor dispatcher explicitly, even when the presenter was created on a thread whose
		// dispatcher is not the editor's.
		_updateDebouncer = new DispatcherDebouncer(resolveDelay, context.Dispatcher);
	}

	/// <summary>Indicates whether a debounced tooltip update is waiting for its delay.</summary>
	internal bool IsUpdatePending => _updateDebouncer.IsPending;

	/// <summary>
	/// The tooltip chrome that <see cref="ApplyTheme"/> applies to the next window; a window that is open keeps its tooltip chrome.
	/// </summary>
	internal CompletionTooltipTheme Theme { get; set; }

	/// <summary>
	/// Applies the tooltip theme to a newly created completion window and wires the list selection changes that schedule debounced tooltip updates.
	/// </summary>
	/// <param name="completionWindow">The completion window to style.</param>
	/// <remarks>
	/// The configuration hook supplied to the constructor runs after the theme, so a host can override any of the theme's values; a throwing hook
	/// propagates to the completion presenter's window rollback scope together with the window hook. When the editor provides no tooltip,
	/// the theme and the hook are skipped and the one-time warning is logged. Each call subscribes the window's list box once: a subscription left
	/// over from an earlier window is detached first, and the completion presenter detaches this window's subscription through <see cref="Detach"/>
	/// when the window closes.
	/// </remarks>
	internal void ApplyTheme(CompletionWindow completionWindow)
	{
		ListBox listBox = completionWindow.CompletionList.ListBox;

		if (_tryGetTooltip(completionWindow) is not ToolTip tooltip)
		{
			LogAccessUnsupportedOnce();
			return;
		}

		if (Theme.Background is not null)
			tooltip.Background = Theme.Background;

		if (Theme.BorderBrush is not null)
			tooltip.BorderBrush = Theme.BorderBrush;

		tooltip.BorderThickness = Theme.BorderThickness;
		tooltip.Padding = Theme.Padding;
		tooltip.PlacementTarget = listBox;
		tooltip.Placement = Theme.Placement;
		tooltip.HorizontalOffset = Theme.HorizontalOffset;

		// The hook runs after the theme so a host can override placement, offsets, and padding.
		_configureTooltip?.Invoke(tooltip);

		// A previous subscription can only still exist when its window was never detached; the presenter
		// shows at most one window at a time, so this reset is defensive and keeps the subscription single.
		Detach();

		// The editor's stock selection-changed handler already opens the tooltip for any non-null description
		// (wrapping only strings in a TextBlock) and fires immediately. This subscription adds a debounced
		// pass that can await ResolveDescriptionAsync and report the tooltip content through the completion
		// presentation state. The handler is a named member so it can be detached when the window closes, and
		// the update it schedules re-checks that this window is still the tracked one before it writes. It
		// listens on the completion list, which the editor's handler subscribed to before this one, so a
		// Markdown documentation replaces the stock handler's text in the same selection change.
		_subscription = new WindowSubscription(completionWindow, completionWindow.CompletionList, tooltip);
		completionWindow.CompletionList.SelectionChanged += HandleCompletionListSelectionChanged;
	}

	/// <summary>Detaches the list-selection handler installed by <see cref="ApplyTheme"/> and cancels pending tooltip work.</summary>
	/// <remarks>
	/// The completion presenter calls this when a window closes, so a closed window's list box stops scheduling tooltip updates and a queued
	/// selection change cannot write into that window's tooltip. The method is idempotent.
	/// </remarks>
	internal void Detach()
	{
		if (_subscription is WindowSubscription subscription)
			subscription.List.SelectionChanged -= HandleCompletionListSelectionChanged;

		_subscription = null;
		_builtMarkdown = null;
		CancelUpdate();
	}

	/// <summary>Closes the tooltip of the given completion window when the tooltip is accessible.</summary>
	/// <param name="completionWindow">The completion window whose tooltip is closed.</param>
	/// <remarks>
	/// The editor's own window close also closes the tooltip; closing it here keeps the tracked tooltip state consistent before the window is closed
	/// and untracked.
	/// </remarks>
	internal void CloseTooltip(CompletionWindow completionWindow)
	{
		if (_tryGetTooltip(completionWindow) is ToolTip tooltip)
			tooltip.IsOpen = false;
	}

	/// <summary>Cancels a pending or in-flight tooltip update.</summary>
	/// <remarks>
	/// A running resolver observes the cancellation through the token passed to it; a result it produces after the cancellation is discarded
	/// because the cancellation invalidated its update.
	/// </remarks>
	internal void CancelUpdate()
	{
		_updates.Invalidate();
		_updates.CancelPendingRequest();
		_updateDebouncer.Cancel();
	}

	/// <summary>Stops the tooltip update timer and cancels pending tooltip work.</summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		Detach();
		_updateDebouncer.Dispose();
	}

	// A selection change on the subscribed list shows a Markdown documentation at once and schedules one debounced update for that window's tooltip.
	private void HandleCompletionListSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_subscription is not WindowSubscription subscription)
			return;

		ShowMarkdownDescription(subscription);
		ScheduleUpdate(subscription.Window, subscription.Tooltip);
	}

	// The editor's handler showed the selected item's description as text, which for Markdown is raw source, so the rendered content replaces it
	// before the next frame. Any other description keeps the editor's rendering until the debounced update.
	private void ShowMarkdownDescription(WindowSubscription subscription)
	{
		ListBox listBox = subscription.List.ListBox;

		// The window opens its list before it is tracked, and the tooltip of an untracked window is not this presenter's to write.
		if (
			ReferenceEquals(_getActiveWindow(), subscription.Window)
			&& listBox.SelectedItem is ICompletionData item
			&& DescriptionOf(item) is MarkupContent { Kind: MarkupKind.Markdown } documentation
		)
		{
			ApplyContent(subscription.Tooltip, listBox, documentation);
		}
	}

	// The description the tooltip shows for an item. A Markdown documentation of the default adapter keeps its kind, so the host's builder renders
	// it; every other description is what the item reports.
	private object? DescriptionOf(ICompletionData item) => item is CompletionItemAdapter adapter ? DocumentationOf(adapter.Item) : item.Description;

	// The adapter's description of an item, whose Markdown documentation stays a MarkupContent while a builder can render it. The cast keeps the
	// conditional from converting the text through MarkupContent's implicit operator.
	private object? DocumentationOf(CompletionItem item)
	{
		return _buildMarkdownContent is not null && item.Documentation is { Kind: MarkupKind.Markdown } markdown
			? (object)markdown
			: item.Documentation?.Value;
	}

	// A host's resolver wins. Without one, an item of the default adapter that carries a resolve callback resolves itself; an item without a
	// callback would only return itself, so it skips the resolve and the second write of the same content.
	private Func<ICompletionData, CancellationToken, Task<object?>>? ResolverFor(ICompletionData item)
	{
		return _resolveDescriptionAsync
			?? (
				item is CompletionItemAdapter { Item.CanResolve: true } adapter
					? (_, cancellationToken) => ResolveDocumentationAsync(adapter, cancellationToken)
					: null
			);
	}

	// A resolved item is kept with its adapter, so each item of a list asks the server once however often it is selected. A failed resolve keeps the
	// description the item already shows: it is logged like the tooltip's other failures and not kept, so the next selection asks again, and a
	// cancellation is silent because a newer selection superseded it.
	private async Task<object?> ResolveDocumentationAsync(CompletionItemAdapter adapter, CancellationToken cancellationToken)
	{
		try
		{
			if (!_resolvedItems.TryGetValue(adapter, out CompletionItem? resolved))
			{
				resolved = await adapter.Item.ResolveAsync(cancellationToken).ConfigureAwait(true);
				_resolvedItems.AddOrUpdate(adapter, resolved);
			}

			return DocumentationOf(resolved) ?? DescriptionOf(adapter);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogTooltipResolutionFailed(_logger, exception);
			return DescriptionOf(adapter);
		}
	}

	// A selection change schedules one debounced update; starting a new update supersedes the previous one and cancels a
	// resolver that is still running. The debouncer callback is synchronous, so it discards the returned task deliberately:
	// UpdateAsync contains every failure, and the discarded task never faults.
	private void ScheduleUpdate(CompletionWindow completionWindow, ToolTip tooltip)
	{
		RequestHandle update = _updates.BeginRequest();
		_updateDebouncer.Arm(() => _ = UpdateAsync(completionWindow, tooltip, update));
	}

	private async Task UpdateAsync(CompletionWindow completionWindow, ToolTip tooltip, RequestHandle update)
	{
		try
		{
			CancellationToken cancellationToken = update.CancellationToken;

			// An update writes only into the tooltip of the window that scheduled it, and only while that
			// window is still the tracked one. Window teardown cancels outstanding updates, but a closed or
			// replaced window's list box can still raise a queued selection change; comparing the tracked
			// window for identity (instead of only checking for null) keeps such an update from writing
			// content into a stale tooltip.
			if (_isDisposed || cancellationToken.IsCancellationRequested || !ReferenceEquals(_getActiveWindow(), completionWindow))
				return;

			ListBox listBox = completionWindow.CompletionList.ListBox;

			if (listBox.SelectedItem is not ICompletionData item)
			{
				tooltip.IsOpen = false;
				_setTooltipState(null, false);
				return;
			}

			object? description = DescriptionOf(item);

			if (description is not null)
			{
				ApplyContent(tooltip, listBox, description);
			}
			else
			{
				tooltip.IsOpen = false;
				_setTooltipState(null, false);
			}

			if (ResolverFor(item) is { } resolveAsync)
			{
				if (!_updates.IsCurrent(update))
					return;

				object? resolvedDescription = await resolveAsync(item, cancellationToken).ConfigureAwait(true);

				// The resolver's continuation resumes on the captured context when the creating thread has one.
				// Without a context (a manually pumped dispatcher, tooling, tests) it resumes on a thread-pool
				// thread, so the tooltip-affine remainder is marshaled to the editor thread explicitly.
				await _dispatcher
					.InvokeAsync(() => ApplyResolvedDescription(completionWindow, tooltip, update, item, resolvedDescription))
					.ConfigureAwait(true);
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

			// Only the update that still owns the update id and whose window is still tracked may close the
			// tooltip: a resolver that faults after a newer update (or a replacement window) already took
			// over must not clear the newer state. The tooltip and the presentation callback are
			// editor-thread affine, so the cleanup is marshaled; a dispatcher that is shutting down
			// rejects the hop, and the update is over either way.
			try
			{
				await _dispatcher
					.InvokeAsync(() =>
					{
						if (!_updates.IsCurrent(update) || !ReferenceEquals(_getActiveWindow(), completionWindow))
							return;

						tooltip.IsOpen = false;
						_setTooltipState(null, false);
					})
					.ConfigureAwait(true);
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
	}

	// Applies or drops a resolved description after the update was checked against the latest one.
	// Runs on the editor thread so the tooltip access and the presentation-state callback stay thread-affine.
	private void ApplyResolvedDescription(
		CompletionWindow completionWindow,
		ToolTip tooltip,
		RequestHandle update,
		ICompletionData item,
		object? resolvedDescription
	)
	{
		if (_isDisposed || !_updates.CanPublish(update))
			return;

		// The window must still be the tracked one and must still have the resolved item selected; a
		// replaced window's tooltip must not receive a late description.
		if (!ReferenceEquals(_getActiveWindow(), completionWindow) || !ReferenceEquals(completionWindow.CompletionList.ListBox.SelectedItem, item))
			return;

		if (resolvedDescription is not null)
		{
			ApplyContent(tooltip, completionWindow.CompletionList.ListBox, resolvedDescription);
		}
		else
		{
			tooltip.IsOpen = false;
			_setTooltipState(null, false);
		}
	}

	private void ApplyContent(ToolTip tooltip, ListBox listBox, object content)
	{
		// The editor's stock selection-changed handler (which renders strings in a wrapping TextBlock) and this
		// debounced path, which additionally awaits the description resolver and updates the presentation state,
		// write this tooltip; reproduce the stock wrapping so the rendering matches.
		tooltip.Content = content switch
		{
			MarkupContent { Kind: MarkupKind.Markdown } markdown when BuildMarkdown(markdown.Value) is { } rendered => rendered,
			MarkupContent markup => CreateText(markup.Value),
			string text => CreateText(text),
			_ => content,
		};

		_setTooltipState(content, true);

		if (!tooltip.IsOpen)
		{
			tooltip.IsOpen = true;
		}
		else
		{
			tooltip.InvalidateMeasure();
			tooltip.InvalidateVisual();
		}
	}

	private static TextBlock CreateText(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

	// The host's builder is host code that runs on the selection path, so its failure is logged and leaves the Markdown as plain text.
	private UIElement? BuildMarkdown(string markdown)
	{
		if (_buildMarkdownContent is null)
			return null;

		if (_builtMarkdown is { } built && string.Equals(built.Markdown, markdown, StringComparison.Ordinal))
			return built.Element;

		try
		{
			UIElement? element = _buildMarkdownContent(markdown);
			_builtMarkdown = element is null ? null : (markdown, element);

			return element;
		}
		catch (Exception exception)
		{
			LogTooltipResolutionFailed(_logger, exception);
			return null;
		}
	}

	// The tooltip accessor fails when the editor does not expose its tooltip; the first failure per
	// presenter is logged once so an engine upgrade becomes visible to the host.
	private void LogAccessUnsupportedOnce()
	{
		if (_isAccessWarningLogged)
			return;

		_isAccessWarningLogged = true;
		LogTooltipAccessUnsupported(_logger, null);
	}

	// The window, completion list, and tooltip that ApplyTheme subscribed to; the list raises the selection
	// changes that update and schedule updates for that tooltip.
	private sealed record WindowSubscription(CompletionWindow Window, CompletionList List, ToolTip Tooltip);
}
