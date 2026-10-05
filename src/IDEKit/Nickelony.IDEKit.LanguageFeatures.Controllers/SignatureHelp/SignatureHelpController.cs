using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Callbacks;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;

/// <summary>
/// Coordinates shared signature help request state, request scheduling, overload navigation of the shown payload, and optional presentation updates.
/// </summary>
/// <remarks>
/// <para>
/// Results from invalidated requests are ignored,
/// so a superseded request cannot replace newer state; a completed request without a result dismisses the presentation instead of leaving
/// stale content visible. <include file="../../../../shared/docs/ThreadAffinity.xml" path="doc/members/member[@name='ThreadAffinity.Remarks']/*"/>
/// </para>
/// <para>
/// The controller reads the caret through the delegate it was given, and its debounce timer and provider
/// continuations run on, or are marshaled back to, the dispatcher it was given. The dispatched publication step checks once more that its request
/// is still current, so a result that a newer request, a dismissal, an invalidation, a cancellation, or the disposal made stale never shows,
/// whichever thread its continuation ran on.
/// </para>
/// <para>
/// While the controller is live, the interactive members verify that the caller is on the dispatcher
/// and throw <see cref="InvalidOperationException"/> otherwise. <see cref="CancelScheduledRequest"/> and <see cref="CancelInFlightRequest"/> skip
/// that check.
/// </para>
/// <para>
/// Requests carry a <see cref="SignatureHelpContext"/>: a request from <see cref="RequestAsync"/> carries the caller's trigger kind and character,
/// a request from <see cref="ScheduleRequest"/> carries the trigger kind it was scheduled with (a content change by default), and a request issued
/// while signature help is visible is marked as a retrigger with the visible payload attached, so the provider can keep the selected overload stable.
/// The host builds the provider request from the offset and the context.
/// </para>
/// <para>
/// Requests follow the latest-wins rule of the shared <see cref="LatestRequestGate"/>: starting a request cancels the previous request's token
/// so a cooperative provider can stop early and discards the superseded result even when the provider ignores the token, and an immediate request
/// supersedes a scheduled request. A superseded request therefore never delays the newest one.
/// </para>
/// <para>
/// Host callbacks should not throw: failures inside the request, show, and dismiss callbacks, and in the caret read of a debounced request,
/// are contained and logged so they cannot escape timer or dispatcher callbacks. The dismiss callback is invoked on every dismissal,
/// including <see cref="Dispose"/> and dismissals with nothing visible, so implementations must be idempotent.
/// </para>
/// </remarks>
public sealed partial class SignatureHelpController : IDisposable
{
	[LoggerMessage(EventId = 1020, EventName = "SignatureHelpRequestFailed", Level = LogLevel.Warning, Message = "Signature help request failed.")]
	private static partial void LogSignatureHelpRequestFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1021,
		EventName = "SignatureHelpHostCallbackFailed",
		Level = LogLevel.Warning,
		Message = "A signature help host callback failed."
	)]
	private static partial void LogSignatureHelpHostCallbackFailed(ILogger logger, Exception? exception);

	private readonly ILogger _logger;
	private readonly Func<int?> _getCaretOffset;
	private readonly ISignatureHelpProvider _provider;
	private readonly IUiDispatcher _dispatcher;
	private readonly Func<int, SignatureHelpContext, SignatureHelpRequest?> _buildRequest;
	private readonly Action<SignatureHelpInformation, int> _showSignatureHelp;
	private readonly Action _dismissSignatureHelp;
	private readonly SignatureHelpControllerOptions _options;
	private readonly RequestLifetime _lifetime = new();
	private readonly DispatcherDebouncer _requestDebouncer;

	// Written on the dispatcher and read from a request continuation's thread - including the finally that
	// clears it when the dispatcher hop could not run - so the reference is volatile for cross-thread
	// visibility; the reference check that guards the clear keeps it owned by a single request.
	private volatile Task? _inFlightRequest;
	private SignatureHelpInformation? _signatureHelp;

	// The request offset of the shown payload, which overload navigation shows the next payload at.
	private int _signatureHelpOffset;
	private bool _isVisible;

	/// <param name="getCaretOffset">
	/// Reads the caret offset of the editor the controller serves on the dispatcher thread; it returns <see langword="null"/> or a negative value
	/// when the editor has no document and so no caret. On the debounce path a failure is logged and counts as no caret.
	/// </param>
	/// <param name="provider">The provider that supplies the signature help for a request.</param>
	/// <param name="hooks">The required signature help callbacks.</param>
	/// <param name="dispatcher">The dispatcher the debounce timer and the provider continuations run on.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="getCaretOffset"/>, <paramref name="provider"/>, or <paramref name="dispatcher"/> is <see langword="null"/>, or a required hook
	/// is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread of <paramref name="dispatcher"/>.</exception>
	public SignatureHelpController(
		Func<int?> getCaretOffset,
		ISignatureHelpProvider provider,
		SignatureHelpControllerHooks hooks,
		IUiDispatcher dispatcher,
		SignatureHelpControllerOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(getCaretOffset);
		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(hooks.BuildRequest);
		ArgumentNullException.ThrowIfNull(hooks.ShowSignatureHelp);
		ArgumentNullException.ThrowIfNull(hooks.DismissSignatureHelp);

		// The controller reads the caret and publishes presentation state on the dispatcher's thread, so creating it elsewhere is a caller error.
		dispatcher.VerifyAccess();

		_logger = logger ?? NullLogger.Instance;
		_getCaretOffset = getCaretOffset;
		_provider = provider;
		_dispatcher = dispatcher;
		_buildRequest = hooks.BuildRequest;
		_showSignatureHelp = hooks.ShowSignatureHelp;
		_dismissSignatureHelp = hooks.DismissSignatureHelp;

		// The options validate themselves when they are assigned and are stored for the controller's
		// lifetime; the debounce timer is bound to the dispatcher explicitly so a scheduled request runs on
		// the dispatcher that serves the editor.
		_options = options ?? SignatureHelpControllerOptions.Default;
		_requestDebouncer = new DispatcherDebouncer(_options.RequestDebounceDelay, _dispatcher);
	}

	/// <summary>The presentation state the controller last published: popup visibility, an in-flight request, and a scheduled request.</summary>
	public SignatureHelpPresentationState CurrentPresentation { get; private set; } = SignatureHelpPresentationState.Empty;

	/// <summary>Dismisses the current signature help presentation, cancels any in-flight provider request, and invalidates pending work.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// The host dismiss callback is invoked even when nothing is visible; <see cref="Dispose"/> also invokes the dismiss
	/// callback before the controller shuts down. Implementations must therefore be idempotent. A call after the controller was disposed returns
	/// without touching the dispatcher, so it is safe from any thread.
	/// </remarks>
	public void Dismiss()
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		DismissCore();
	}

	private void DismissCore()
	{
		CancelScheduledRequestCore();
		InvalidateRequestsCore();
		_lifetime.CancelInFlightRequest();
		DismissPresentation();
	}

	/// <summary>Requests signature help at the specified offset and asks the provider for it.</summary>
	/// <param name="offset">The zero-based document offset to request signature help for.</param>
	/// <param name="triggerKind">The action that caused the request; the default is an explicit invocation.</param>
	/// <param name="triggerCharacter">
	/// The trigger character for <see cref="SignatureHelpTriggerKind.TriggerCharacter"/>, or <see langword="null"/> otherwise.
	/// </param>
	/// <param name="cancellationToken">
	/// A token that can cancel the request; cancellation is observed at the shared request gate's checkpoints, like the controller's own supersession
	/// and dismissal.
	/// </param>
	/// <returns>
	/// A task that completes after the request finished, was superseded, or was canceled, and reports <see langword="true"/>
	/// when the request published a signature help presentation and <see langword="false"/> when it was superseded, canceled,
	/// dismissed (a provider result of <see langword="null"/>), failed, or produced no popup. The controller contains provider failures,
	/// so the returned task does not fault; a failure is logged instead.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// Starting the request cancels the previous request's token and drops any scheduled request. The older result is discarded
	/// even when its provider ignored the cancellation, so the latest request wins.
	/// </remarks>
	public Task<bool> RequestAsync(
		int offset,
		SignatureHelpTriggerKind triggerKind = SignatureHelpTriggerKind.Invoked,
		string? triggerCharacter = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);

		if (_lifetime.IsDisposed)
			return Task.FromResult(false);

		_dispatcher.VerifyAccess();

		return RequestCoreAsync(offset, triggerKind, triggerCharacter, cancellationToken);
	}

	/// <summary>Schedules a debounced request at the current caret offset.</summary>
	/// <param name="triggerKind">
	/// The action that caused the request; the default is a content change,
	/// which matches the LSP definition of caret moves and document content changes. A host that debounces a trigger-character request passes
	/// <see cref="SignatureHelpTriggerKind.TriggerCharacter"/> instead.
	/// </param>
	/// <param name="triggerCharacter">
	/// The trigger character for <see cref="SignatureHelpTriggerKind.TriggerCharacter"/>, or <see langword="null"/> otherwise.
	/// </param>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// The request carries the supplied trigger kind and character. Scheduling again restarts the debounce delay; when it elapses, the request runs
	/// as the latest request, so an in-flight request is superseded instead of delaying it. An editor without a document has no caret,
	/// so scheduling cancels the scheduled request; the offset the request carries is read when the debounce elapses, so a caret that moved
	/// during the debounce window is honored.
	/// </remarks>
	public void ScheduleRequest(SignatureHelpTriggerKind triggerKind = SignatureHelpTriggerKind.ContentChange, string? triggerCharacter = null)
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		if (GetCaretOffset() < 0)
		{
			CancelScheduledRequestCore();
			return;
		}

		_requestDebouncer.Arm(() => RunScheduledRequest(triggerKind, triggerCharacter));
		UpdatePresentationState();
	}

	/// <summary>
	/// Cancels the scheduled request, if any. The in-flight provider request is left running; use <see cref="CancelInFlightRequest"/> for it.
	/// </summary>
	public void CancelScheduledRequest()
	{
		if (_lifetime.IsDisposed)
			return;

		CancelScheduledRequestCore();
	}

	/// <summary>
	/// Selects the next available signature of the currently shown signature help, wrapping around at the end by default, so a host can offer
	/// overload navigation without another provider request.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when a different signature became active; <see langword="false"/> when nothing is visible,
	/// only one signature is available, or the navigation dismissed the presentation at the last signature
	/// because <see cref="SignatureHelpControllerOptions.Cycle"/> is disabled.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// The host show callback is invoked again with the updated payload, so a host that recreates its popup on every show sees one recreation
	/// per overload step. With cycling disabled, navigating past the last signature dismisses the presentation instead of wrapping.
	/// </remarks>
	public bool SelectNextSignature()
	{
		if (_lifetime.IsDisposed)
			return false;

		_dispatcher.VerifyAccess();

		return SelectSignature(1);
	}

	/// <summary>
	/// Selects the previous available signature of the currently shown signature help, wrapping around at the start by default, so a host can offer
	/// overload navigation without another provider request.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when a different signature became active; <see langword="false"/> when nothing is visible,
	/// only one signature is available, or the navigation dismissed the presentation at the first signature
	/// because <see cref="SignatureHelpControllerOptions.Cycle"/> is disabled.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// Behaves like <see cref="SelectNextSignature"/> in the opposite direction, including the extra host
	/// show callback and, with <see cref="SignatureHelpControllerOptions.Cycle"/> disabled, the dismissal at the first signature.
	/// </remarks>
	public bool SelectPreviousSignature()
	{
		if (_lifetime.IsDisposed)
			return false;

		_dispatcher.VerifyAccess();

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

			targetIndex = ((targetIndex % signatureCount) + signatureCount) % signatureCount;
		}

		return TryShowSignatureHelp(currentSignatureHelp.WithActiveSignature(targetIndex), _signatureHelpOffset);
	}

	// A debounce callback that starts the scheduled request. The caret is read now rather than at scheduling
	// time, so a caret that moved during the debounce is requested at its current position (an LSP request
	// carries the position at request time). An editor that lost its document cancels the request, like the
	// no-caret case at scheduling time. The caret delegate is host code running inside a timer callback, so a
	// failure is contained and counts as no caret, and a disposal that a tick raced with ends the callback
	// before it reads the host. The request contains its own failures and reports its state changes, so the
	// task needs no observer.
	private void RunScheduledRequest(SignatureHelpTriggerKind triggerKind, string? triggerCharacter)
	{
		if (_lifetime.IsDisposed)
			return;

		if (!CallbackContainment.TryRun(callback: GetCaretOffset, reportFault: ReportHostCallbackFault, result: out int offset) || offset < 0)
		{
			// The debouncer cleared the scheduled request before it called here, so the published state has to say so.
			UpdatePresentationState();
			return;
		}

		_ = RequestCoreAsync(offset, triggerKind, triggerCharacter, cancellationToken: CancellationToken.None);
	}

	// The caret offset of the editor, or a negative value when it has no document.
	private int GetCaretOffset() => _getCaretOffset() ?? -1;

	private void ReportHostCallbackFault(Exception exception) => LogSignatureHelpHostCallbackFailed(_logger, exception);

	private void CancelScheduledRequestCore()
	{
		_requestDebouncer.Cancel();
		UpdatePresentationState();
	}

	/// <summary>Marks outstanding requests as stale so completed results are ignored.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread.</exception>
	/// <remarks>
	/// The in-flight provider request is not canceled; use <see cref="CancelInFlightRequest"/> or <see cref="Dismiss"/> for that.
	/// </remarks>
	public void InvalidateRequests()
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		InvalidateRequestsCore();
	}

	private void InvalidateRequestsCore()
	{
		_lifetime.InvalidateRequests();
		_lifetime.AdvanceGeneration();

		// The invalidated request may still be running, but its result cannot be applied, so the
		// presentation does not report it as in flight.
		_inFlightRequest = null;
		UpdatePresentationState();
	}

	/// <summary>Cancels the in-flight provider request's token, if any, so a cooperative provider can stop early.</summary>
	/// <remarks>
	/// The canceled request's result is also rejected because its token is canceled. Canceling does not clear the presentation
	/// or a scheduled request; use <see cref="Dismiss"/> to hide signature help and drop pending work.
	/// </remarks>
	public void CancelInFlightRequest()
	{
		_lifetime.CancelInFlightRequest();
		_lifetime.AdvanceGeneration();
	}

	/// <summary>Dismisses signature help, cancels any scheduled and in-flight request, and invokes the host dismiss callback.</summary>
	/// <remarks>
	/// The controller does not invoke any host callback after disposal. After disposal, <see cref="RequestAsync"/> throws only for a negative
	/// offset and otherwise returns a task that yields <see langword="false"/>, the two selection members return <see langword="false"/>,
	/// and the other members return without effect, so every call after disposal is safe from any thread.
	/// </remarks>
	public void Dispose()
	{
		if (_lifetime.IsDisposed)
			return;

		// Disposal is the shutdown path and is deliberately thread-agnostic, unlike the interactive members,
		// so it does not verify dispatcher access: it may run while the host tears the editor down. Its teardown
		// (the debouncer and the contained host dismiss callback) is contained, so it cannot escape.
		_lifetime.Dispose();
		_requestDebouncer.Dispose();

		DismissCore();
	}

	private bool TryShowSignatureHelp(SignatureHelpInformation signatureHelp, int offset)
	{
		// The host failed to show the popup; the tracked state stays on the previous content instead of
		// claiming visibility that was never established.
		if (!CallbackContainment.TryRun(() => _showSignatureHelp(signatureHelp, offset), ReportHostCallbackFault))
			return false;

		_signatureHelp = signatureHelp;
		_signatureHelpOffset = offset;
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

		CallbackContainment.TryRun(_dismissSignatureHelp, ReportHostCallbackFault);

		UpdatePresentationState();
	}

	private void UpdatePresentationState()
	{
		CurrentPresentation = new SignatureHelpPresentationState(
			SignatureHelp: _signatureHelp,
			IsVisible: _isVisible,
			IsRequestInFlight: _inFlightRequest is not null,
			IsRequestScheduled: _requestDebouncer.IsPending
		);
	}

	private SignatureHelpContext CreateRequestContext(SignatureHelpTriggerKind triggerKind, string? triggerCharacter)
	{
		// The controller owns the retrigger state: signature help that is currently showing means the
		// request is a retrigger, and the visible payload lets the provider keep the selected overload
		// stable across the content change that triggered the request.
		return new SignatureHelpContext(triggerKind, triggerCharacter, isRetrigger: _signatureHelp is not null, activeSignatureHelp: _signatureHelp);
	}

	private async Task<bool> RequestCoreAsync(
		int offset,
		SignatureHelpTriggerKind triggerKind,
		string? triggerCharacter,
		CancellationToken cancellationToken
	)
	{
		// An immediate request supersedes a scheduled request: the debounced offset is older than the
		// offset requested now, so letting the timer fire afterwards could publish stale content.
		_requestDebouncer.Cancel();

		// The request starts a new generation, so a result of an earlier request cannot publish after this point.
		int generation = _lifetime.AdvanceGeneration();

		(int Offset, SignatureHelpContext Context) state = (Offset: offset, Context: CreateRequestContext(triggerKind, triggerCharacter));

		// Set inside the apply callback, which the gate invokes only for a completed request, so a
		// superseded or canceled request never reports a presentation it did not publish.
		bool presentationPublished = false;

		// The gate cancels the previous request when a newer one starts, discards a superseded
		// result even when its provider ignored the token, and never applies after disposal. Its apply
		// callback runs after the provider's await, which resumes on a thread-pool thread when the creating
		// thread captured no synchronization context, so the host popup callback is marshaled to the
		// dispatcher explicitly.
		Task<RequestOutcome> request = _lifetime.RunAsync(
			state,

			computeAsync: (state, token) =>
			{
				// A host that cannot serve the document vetoes the request; the null result dismisses the presentation, like a
				// provider that has nothing to present.
				SignatureHelpRequest? signatureHelpRequest = _buildRequest(state.Offset, state.Context);

				return signatureHelpRequest is null
					? Task.FromResult<SignatureHelpInformation?>(null)
					: _provider.GetSignatureHelpAsync(signatureHelpRequest, token);
			},

			apply: signatureHelp =>
				_dispatcher.Invoke(() =>
				{
					// The gate's checks run on the thread the continuation resumed on, before this step is dispatched, so when that is
					// not the dispatcher's thread a newer request, a dismissal, an invalidation, a cancellation, or the disposal can land in
					// between. A result that went stale shows nothing, and no host callback runs after disposal.
					if (!_lifetime.IsCurrent(generation))
						return;

					if (signatureHelp is null)
					{
						// A null result means there is nothing to present, so the popup is dismissed even when
						// it was visible at request start; otherwise, a stale signature could stay on screen
						// after the caret left the call.
						DismissPresentation();
						return;
					}

					presentationPublished = TryShowSignatureHelp(signatureHelp, state.Offset);
				}),

			cancellationToken: cancellationToken
		);

		_inFlightRequest = request;
		UpdatePresentationState();

		RequestOutcome outcome = RequestOutcome.Unknown;
		Exception? failure = null;

		try
		{
			outcome = await request.ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			// The gate classifies cancellation as an outcome; anything else is a provider failure.
			failure = exception;
		}

		// The continuation resumes on the captured context when the creating thread captured one; without a
		// context it resumes on a thread-pool thread, so the failure report and the in-flight bookkeeping -
		// which touches the dispatcher-bound debounce timer - are marshaled to the dispatcher explicitly.
		bool stateClearedOnDispatcher = false;

		try
		{
			await _dispatcher
				.InvokeAsync(() =>
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
				})
				.ConfigureAwait(true);
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
			// before the marshaled bookkeeping ran, the request would otherwise stay recorded as in flight
			// forever. Clearing it here on the continuation thread keeps the field from claiming a request that is
			// over, so the next presentation update reports it as finished, and the reference check preserves the
			// ownership rule for a superseded request.
			if (!stateClearedOnDispatcher && ReferenceEquals(_inFlightRequest, request))
				_inFlightRequest = null;
		}

		// Only a completed request that actually published a presentation reports success; a superseded,
		// canceled, rejected, dismissed, or failed request reports false.
		return outcome == RequestOutcome.Completed && presentationPublished;
	}
}
