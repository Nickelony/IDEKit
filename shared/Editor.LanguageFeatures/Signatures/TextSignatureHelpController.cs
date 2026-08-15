using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#if AVALONIAEDIT
using Avalonia.Threading;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Requests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Requests;
using System.Windows.Threading;
#endif
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Signatures;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Signatures;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
#endif

/// <summary>
/// Coordinates shared signature help request state, refresh scheduling, overload navigation of the
/// shown payload, and optional presentation updates. Results from invalidated requests are ignored,
/// so a superseded request cannot replace newer state; a completed request without a result
/// dismisses the presentation instead of leaving stale content visible.
/// </summary>
/// <remarks>
/// <para>
/// The controller must be created and used on the thread that owns the signature help popup and the host
/// callbacks, because it invokes them directly. Its refresh timer runs on the dispatcher supplied to the
/// constructor, or on the creating thread's dispatcher when none is supplied; a host that creates the
/// controller before its owner thread has a dispatcher supplies that dispatcher explicitly. Provider
/// continuations are marshalled back to the dispatcher, even when the creating thread captured no
/// synchronization context.
/// </para>
/// <para>
/// Requests are reported to the provider hook with a <see cref="TextSignatureHelpContext"/>: a request from
/// <see cref="RequestAsync"/> carries the caller's trigger kind and character, a request from
/// <see cref="ScheduleRefresh"/> carries the refresh trigger kind (a content change by default), and a request
/// issued while signature help is visible is marked as a retrigger with the visible payload attached, so the
/// provider can keep the selected overload stable.
/// </para>
/// <para>
/// Requests follow the latest-wins rule of the shared <see cref="LatestRequestCoordinator"/>: starting a
/// request cancels the previous request's token so a cooperative provider can stop early and discards the
/// superseded result even when the provider ignores the token, and an immediate request supersedes a
/// debounced refresh. A superseded request therefore never delays the newest one.
/// </para>
/// <para>
/// Host callbacks should not throw: failures inside the caret-offset getter and the show and dismiss callbacks
/// are contained and logged so they cannot escape timer or dispatcher callbacks. A throwing caret-offset getter
/// cancels the pending refresh, like a getter that reports no caret. The dismiss callback is invoked on every
/// dismissal, including <see cref="Dispose"/> and dismissals with nothing visible, so implementations must be
/// idempotent.
/// </para>
/// </remarks>
public sealed partial class TextSignatureHelpController : IDisposable
{
	// Signature help uses package log event ids 1020-1021 (see the README for the canonical table).
	[LoggerMessage(
		EventId = 1020,
		EventName = "SignatureHelpRequestFailed",
		Level = LogLevel.Warning,
		Message = "Signature help request failed."
	)]
	private static partial void LogSignatureHelpRequestFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1021,
		EventName = "SignatureHelpHostCallbackFailed",
		Level = LogLevel.Warning,
		Message = "A signature help host callback failed."
	)]
	private static partial void LogSignatureHelpHostCallbackFailed(ILogger logger, Exception? exception);

	private readonly ILogger _logger;
	private readonly Dispatcher _dispatcher;
	private readonly Func<int> _getCurrentCaretOffset;
	private readonly Func<int, TextSignatureHelpContext, CancellationToken, Task<TextSignatureHelp?>> _requestSignatureHelpAsync;
	private readonly Action<TextSignatureHelp> _showSignatureHelp;
	private readonly Action _dismissSignatureHelp;
	private readonly TextSignatureHelpControllerOptions _options;
	private readonly RequestLifetime _lifetime = new();
	private readonly DispatcherDebouncer _refreshDebouncer;

	// Written on the dispatcher and read from a request continuation's thread - including the finally that
	// clears it when the dispatcher hop could not run - so the reference is volatile for cross-thread
	// visibility; the reference check that guards the clear keeps it owned by a single request.
	private volatile Task? _inFlightRequest;
	private TextSignatureHelp? _signatureHelp;
	private bool _isVisible;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureHelpController"/> class.
	/// </summary>
	/// <param name="hooks">The required signature help callbacks.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <param name="dispatcher">
	/// The dispatcher the refresh timer and the provider continuations run on, or <see langword="null"/> for
	/// the creating thread's dispatcher.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="hooks"/> is <see langword="null"/>, or a required hook is <see langword="null"/>.
	/// </exception>
	public TextSignatureHelpController(
		TextSignatureHelpControllerHooks hooks,
		TextSignatureHelpControllerOptions? options = null,
		ILogger? logger = null,
		Dispatcher? dispatcher = null)
	{
		ArgumentNullException.ThrowIfNull(hooks);
		ArgumentNullException.ThrowIfNull(hooks.GetCurrentCaretOffset);
		ArgumentNullException.ThrowIfNull(hooks.RequestSignatureHelpAsync);
		ArgumentNullException.ThrowIfNull(hooks.ShowSignatureHelp);
		ArgumentNullException.ThrowIfNull(hooks.DismissSignatureHelp);

		_logger = logger ?? NullLogger.Instance;
		_dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
		_getCurrentCaretOffset = hooks.GetCurrentCaretOffset;
		_requestSignatureHelpAsync = hooks.RequestSignatureHelpAsync;
		_showSignatureHelp = hooks.ShowSignatureHelp;
		_dismissSignatureHelp = hooks.DismissSignatureHelp;

		// The options validate themselves when they are assigned and are stored for the controller's
		// lifetime; the debounce timer is bound to the supplied dispatcher explicitly so a controller
		// created on a thread without a dispatcher (or with the wrong one) still refreshes on the owner's
		// dispatcher.
		_options = options ?? TextSignatureHelpControllerOptions.Default;
		_refreshDebouncer = new DispatcherDebouncer(_options.RefreshDebounceDelay, _dispatcher);
	}

	/// <summary>
	/// Gets the current signature help presentation state.
	/// </summary>
	public TextSignatureHelpPresentationState CurrentPresentation { get; private set; } = TextSignatureHelpPresentationState.Empty;

	/// <summary>
	/// Dismisses the current signature help presentation and invalidates pending work. An in-flight provider
	/// request is canceled as well.
	/// </summary>
	/// <remarks>
	/// The host dismiss callback is invoked even when nothing is visible; <see cref="Dispose"/> also invokes
	/// the dismiss callback before the controller shuts down. Implementations must therefore be idempotent.
	/// </remarks>
	public void Dismiss()
	{
		if (_lifetime.IsDisposed)
			return;

		DismissCore();
	}

	private void DismissCore()
	{
		CancelScheduledRefreshCore();
		InvalidateRequestsCore();
		_lifetime.CancelInFlightRequest();
		DismissPresentation();
	}

	/// <summary>
	/// Requests signature help at the specified offset and reports the request through the provider hook.
	/// A newer request supersedes an in-flight one instead of queueing behind it.
	/// </summary>
	/// <param name="offset">The zero-based document offset to request signature help for.</param>
	/// <param name="triggerKind">The action that caused the request; the default is an explicit invocation.</param>
	/// <param name="triggerCharacter">
	/// The trigger character for <see cref="TextSignatureHelpTriggerKind.TriggerCharacter"/>, or
	/// <see langword="null"/> otherwise.
	/// </param>
	/// <returns>
	/// A task that completes after the request finished, was superseded, or was canceled. The controller
	/// contains provider failures, so the returned task does not fault; a failure is logged instead.
	/// </returns>
	/// <remarks>
	/// Starting the request cancels the previous request's token and drops any debounced refresh. The older
	/// result is discarded even when its provider ignored the cancellation, so the latest request wins.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative.</exception>
	public Task RequestAsync(
		int offset,
		TextSignatureHelpTriggerKind triggerKind = TextSignatureHelpTriggerKind.Invoked,
		string? triggerCharacter = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);

		if (_lifetime.IsDisposed)
			return Task.CompletedTask;

		return RequestAsyncCore(offset, triggerKind, triggerCharacter);
	}

	/// <summary>
	/// Schedules a debounced refresh at the current caret offset. A negative caret offset (the host
	/// reports no caret) cancels any pending refresh instead of starting one.
	/// </summary>
	/// <param name="triggerKind">
	/// The action that caused the refresh; the default is a content change, which matches the LSP definition of
	/// caret moves and document content changes. A host that debounces a trigger-character request passes
	/// <see cref="TextSignatureHelpTriggerKind.TriggerCharacter"/> instead.
	/// </param>
	/// <param name="triggerCharacter">
	/// The trigger character for <see cref="TextSignatureHelpTriggerKind.TriggerCharacter"/>, or
	/// <see langword="null"/> otherwise.
	/// </param>
	/// <remarks>
	/// The refresh is reported to the provider hook with the supplied trigger kind and character. Scheduling
	/// again restarts the debounce delay; when it elapses, the refresh runs as the latest request, so an
	/// in-flight request is superseded instead of delaying it. The caret offset is read when the refresh is
	/// scheduled, not when the debounce elapses.
	/// </remarks>
	public void ScheduleRefresh(
		TextSignatureHelpTriggerKind triggerKind = TextSignatureHelpTriggerKind.ContentChange,
		string? triggerCharacter = null)
	{
		if (_lifetime.IsDisposed)
			return;

		// A throwing caret getter cannot name a refresh offset; the failure is contained like the other host
		// callbacks and the pending refresh is canceled like the no-caret case.
		if (!HostCallback.TryRun(_getCurrentCaretOffset, _logger, LogSignatureHelpHostCallbackFailed, out int offset))
		{
			CancelScheduledRefreshCore();
			return;
		}

		if (offset < 0)
		{
			CancelScheduledRefreshCore();
			return;
		}

		_refreshDebouncer.Arm(() => RunScheduledRefresh(offset, triggerKind, triggerCharacter));
		UpdatePresentationState();
	}

	/// <summary>
	/// Cancels any pending debounced refresh.
	/// </summary>
	public void CancelScheduledRefresh()
	{
		if (_lifetime.IsDisposed)
			return;

		CancelScheduledRefreshCore();
	}

	/// <summary>
	/// Selects the next available signature of the currently shown signature help, wrapping around at
	/// the end by default, so a host can offer overload navigation without another provider request.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when a different signature became active; <see langword="false"/> when
	/// nothing is visible, only one signature is available, or the navigation dismissed the presentation
	/// at the last signature because <see cref="TextSignatureHelpControllerOptions.Cycle"/> is disabled.
	/// </returns>
	/// <remarks>The host show callback is invoked again with the updated payload, so a host that recreates its
	/// popup on every show sees one recreation per overload step. With cycling disabled, navigating past
	/// the last signature dismisses the presentation instead of wrapping.</remarks>
	public bool SelectNextSignature()
	{
		return SelectSignature(1);
	}

	/// <summary>
	/// Selects the previous available signature of the currently shown signature help, wrapping around
	/// at the start by default, so a host can offer overload navigation without another provider request.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when a different signature became active; <see langword="false"/> when
	/// nothing is visible, only one signature is available, or the navigation dismissed the presentation
	/// at the first signature because <see cref="TextSignatureHelpControllerOptions.Cycle"/> is disabled.
	/// </returns>
	/// <remarks>Behaves like <see cref="SelectNextSignature"/> in the opposite direction, including the extra
	/// host show callback and, with <see cref="TextSignatureHelpControllerOptions.Cycle"/> disabled, the
	/// dismissal at the first signature.</remarks>
	public bool SelectPreviousSignature()
	{
		return SelectSignature(-1);
	}

	private bool SelectSignature(int offset)
	{
		if (_lifetime.IsDisposed || _signatureHelp is not { } currentSignatureHelp)
			return false;

		int signatureCount = currentSignatureHelp.Signatures.Count;

		if (signatureCount <= 1)
			return false;

		int targetIndex = currentSignatureHelp.ActiveSignatureIndex + offset;

		if (targetIndex < 0 || targetIndex >= signatureCount)
		{
			// Navigating past an edge wraps around by default; a host that disables cycling gets the
			// standard dismiss-at-the-edge affordance instead.
			if (!_options.Cycle)
			{
				Dismiss();
				return false;
			}

			targetIndex = (targetIndex % signatureCount + signatureCount) % signatureCount;
		}

		return TryShowSignatureHelp(currentSignatureHelp.WithActiveSignature(targetIndex));
	}

	// A debounce callback that starts the scheduled refresh. The request contains its own failures and
	// reports its state changes, so the task needs no observer.
	private void RunScheduledRefresh(int offset, TextSignatureHelpTriggerKind triggerKind, string? triggerCharacter)
		=> _ = RequestAsyncCore(offset, triggerKind, triggerCharacter);

	private void CancelScheduledRefreshCore()
	{
		_refreshDebouncer.Cancel();
		UpdatePresentationState();
	}

	/// <summary>
	/// Marks outstanding requests as stale so completed results are ignored. The in-flight provider request is
	/// not canceled; use <see cref="CancelInFlightRequest"/> or <see cref="Dismiss"/> for that.
	/// </summary>
	public void InvalidateRequests()
	{
		if (_lifetime.IsDisposed)
			return;

		InvalidateRequestsCore();
	}

	private void InvalidateRequestsCore()
	{
		_lifetime.InvalidateRequests();

		// The invalidated request may still be running, but its result can no longer be applied, so the
		// presentation no longer reports it as in flight.
		_inFlightRequest = null;
		UpdatePresentationState();
	}

	/// <summary>
	/// Cancels the in-flight provider request's token, if any, so a cooperative provider can stop early. The
	/// canceled request's result is also rejected because its token is canceled. Canceling does not clear the
	/// presentation or a pending refresh; use <see cref="Dismiss"/> to hide signature help and drop pending work.
	/// </summary>
	public void CancelInFlightRequest()
	{
		_lifetime.CancelInFlightRequest();
	}

	/// <summary>
	/// Dismisses signature help, cancels any pending refresh, and invalidates any in-flight request. An
	/// in-flight provider request is canceled as well, and the host dismiss callback is invoked.
	/// </summary>
	/// <remarks>
	/// Disposal invokes the host dismiss callback as part of the shutdown, and the controller does not invoke
	/// any host callback afterwards.
	/// </remarks>
	public void Dispose()
	{
		if (_lifetime.IsDisposed)
			return;

		_lifetime.Dispose();
		_refreshDebouncer.Dispose();
		DismissCore();
	}

	private bool TryShowSignatureHelp(TextSignatureHelp signatureInfo)
	{
		// The host failed to show the popup; the tracked state stays on the previous content instead of
		// claiming visibility that was never established.
		if (!HostCallback.TryRun(() => _showSignatureHelp(signatureInfo), _logger, LogSignatureHelpHostCallbackFailed))
			return false;

		_signatureHelp = signatureInfo;
		_isVisible = true;
		UpdatePresentationState();
		return true;
	}

	private void DismissPresentation()
	{
		// The tracked state is cleared before the callback runs so a throwing dismiss cannot leave the
		// controller claiming that signature help is still visible.
		_signatureHelp = null;
		_isVisible = false;

		HostCallback.TryRun(_dismissSignatureHelp, _logger, LogSignatureHelpHostCallbackFailed);

		UpdatePresentationState();
	}

	private void UpdatePresentationState()
	{
		CurrentPresentation = new TextSignatureHelpPresentationState(
			_signatureHelp,
			_isVisible,
			_inFlightRequest is not null,
			_refreshDebouncer.IsPending);
	}

	private TextSignatureHelpContext CreateRequestContext(
		TextSignatureHelpTriggerKind triggerKind,
		string? triggerCharacter)
	{
		// The controller owns the retrigger state: signature help that is currently showing means the
		// request is a retrigger, and the visible payload lets the provider keep the selected overload
		// stable across the content change that triggered the request.
		return new TextSignatureHelpContext(
			triggerKind,
			triggerCharacter,
			isRetrigger: _signatureHelp is not null,
			activeSignatureHelp: _signatureHelp);
	}

	private async Task RequestAsyncCore(int offset, TextSignatureHelpTriggerKind triggerKind, string? triggerCharacter)
	{
		// An immediate request supersedes a scheduled refresh: the debounced offset is older than the
		// offset requested now, so letting the timer fire afterwards could publish stale content.
		_refreshDebouncer.Cancel();

		var state = (
			Offset: offset,
			Context: CreateRequestContext(triggerKind, triggerCharacter));

		// The coordinator cancels the previous request when a newer one starts, discards a superseded
		// result even when its provider ignored the token, and never applies after disposal. Its apply
		// callback runs after the provider's await, which resumes on a thread-pool thread when the creating
		// thread captured no synchronization context, so the host popup callback is marshalled to the
		// dispatcher explicitly.
		Task<RequestOutcome> request = _lifetime.Coordinator.RunAsync(
			state: state,
			computeAsync: (state, cancellationToken) => _requestSignatureHelpAsync(state.Offset, state.Context, cancellationToken),
			canApply: (_, _) => !_lifetime.IsDisposed,
			apply: signatureInfo => DispatcherInvocation.Run(_dispatcher, () =>
			{
				if (signatureInfo is null)
				{
					// A null result means there is nothing to present, so the popup is dismissed even when
					// it was visible at request start; otherwise, a stale signature could stay on screen
					// after the caret left the call.
					DismissPresentation();
					return;
				}

				TryShowSignatureHelp(signatureInfo);
			}));

		_inFlightRequest = request;
		UpdatePresentationState();

		Exception? failure = null;

		try
		{
			await request.ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			// The coordinator classifies cancellation as an outcome; anything else is a provider failure.
			failure = exception;
		}

		// The continuation resumes on the captured context when the creating thread captured one; without a
		// context it resumes on a thread-pool thread, so the failure report and the in-flight bookkeeping -
		// which touches the dispatcher-bound refresh timer - are marshalled to the dispatcher explicitly.
		bool stateClearedOnDispatcher = false;

		try
		{
			await DispatcherInvocation.RunAsync(_dispatcher, () =>
			{
				if (failure is not null && !_lifetime.IsDisposed)
					LogSignatureHelpRequestFailed(_logger, failure);

				// Only the request that currently owns the in-flight state may clear it, so a superseded request
				// that completes later cannot clear its successor's state.
				if (ReferenceEquals(_inFlightRequest, request))
				{
					_inFlightRequest = null;
					stateClearedOnDispatcher = true;

					if (!_lifetime.IsDisposed)
						UpdatePresentationState();
				}
			});
		}
		catch (OperationCanceledException)
		{
			// The dispatcher shut down before the bookkeeping could run; the request is over either way, so
			// there is no state left to report to.
		}
		catch (InvalidOperationException)
		{
			// The dispatcher rejects further work while it shuts down; the request is over either way.
		}
		finally
		{
			// The in-flight state must not depend on the dispatcher hop above: if the dispatcher shut down
			// before the marshalled bookkeeping ran, the request would otherwise stay recorded as in flight
			// forever. Clearing it here on the continuation thread keeps IsRequestInFlight truthful, and the
			// reference check preserves the ownership rule for a superseded request.
			if (!stateClearedOnDispatcher && ReferenceEquals(_inFlightRequest, request))
				_inFlightRequest = null;
		}
	}
}
