using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Callbacks;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;

/// <summary>Coordinates hover requests, request invalidation, and hover-versus-diagnostic popup display.</summary>
/// <remarks>
/// <para>
/// The controller asks its <see cref="IHoverPointerSource"/> for the document offset under the pointer.
/// A completed result is displayed only when the request is still current, the source still reports the same request offset, and the optional
/// host context version is unchanged. A hover request that arrives while the source reports no offset, for example after the pointer left the editor
/// with no move since, has no hover target, so the evaluation requests nothing and asks the host to hide its popup.
/// </para>
/// <para>
/// Failures inside a hover evaluation are contained rather than propagated to the caller: the failure is logged and, when a valid
/// hovered offset was resolved before the failure, the display decision falls back to a diagnostic popup when one is available (and to a hide
/// request when none is). Failures that occur before a valid hovered offset exists - for example while resolving the offset under the pointer -
/// are only logged, because there is no display target to apply an evaluation to. Because the containment wraps the whole evaluation, it also covers
/// exceptions thrown by the host popup callback.
/// </para>
/// <para>
/// A provider failure shows its fallback under the same conditions as a completed result.
/// A superseded request that faults after a newer evaluation already published its decision leaves that newer state untouched, and so does a failure
/// that arrives after the pointer left or the host context changed. The controller does not debounce hover requests: hover-intent policy,
/// such as an initial delay, belongs to whoever calls <see cref="RequestAsync"/>, which in the editor bindings is the pointer source
/// once the pointer rests. <include file="../../../../shared/docs/ThreadAffinity.xml" path="doc/members/member[@name='ThreadAffinity.Remarks']/*"/>
/// </para>
/// <para>
/// The dispatched publication steps compare a generation that every new request, supersession, cancellation, and invalidation advances,
/// and they check that the controller is not disposed, so a result or a fallback that one of them made stale is never displayed,
/// whichever thread the provider continuation ran on.
/// </para>
/// <para>
/// The controller exposes no readable presentation state: the host owns the hover popup and receives every display decision through the single
/// <see cref="HoverControllerHooks.ShowHover"/> callback.
/// </para>
/// </remarks>
public sealed partial class HoverController : IDisposable
{
	[LoggerMessage(EventId = 1010, EventName = "HoverRequestFailed", Level = LogLevel.Warning, Message = "Hover request failed.")]
	private static partial void LogHoverRequestFailed(ILogger logger, Exception? exception);

	[LoggerMessage(EventId = 1011, EventName = "HoverHostCallbackFailed", Level = LogLevel.Warning, Message = "A hover host callback failed.")]
	private static partial void LogHoverHostCallbackFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1012,
		EventName = "HoverOffsetMismatch",
		Level = LogLevel.Warning,
		Message = "BuildDecision requests a different offset than the hovered offset, but no ResolveRequestOffset "
			+ "hook is supplied. Completed hover results cannot be matched to the pointer position and will be discarded."
	)]
	private static partial void LogHoverOffsetMismatch(ILogger logger, Exception? exception);

	private readonly ILogger _logger;
	private readonly IHoverPointerSource _pointerSource;
	private readonly IHoverProvider? _provider;
	private readonly IUiDispatcher _dispatcher;
	private readonly Func<int, HoverDecision> _buildDecision;
	private readonly Func<int, HoverRequest?>? _buildRequest;
	private readonly Func<int, int?>? _resolveRequestOffset;
	private readonly Action<HoverInformation?, Diagnostic?, int?> _showHover;
	private readonly Func<int>? _contextVersionProvider;

	private readonly RequestLifetime _lifetime = new();
	private bool _isOffsetMismatchWarningLogged;

	/// <param name="pointerSource">
	/// The source of the document offset under the pointer. The controller owns it from the moment this constructor returns and disposes
	/// it in <see cref="Dispose"/>; when the constructor throws, the caller still owns the source.
	/// </param>
	/// <param name="provider">
	/// The provider that supplies the hover content for a request, or <see langword="null"/> for a controller that shows the diagnostic
	/// under the pointer and asks for no hover content.
	/// </param>
	/// <param name="hooks">The hover callbacks; the hook group documents which members are required.</param>
	/// <param name="dispatcher">The dispatcher whose thread the interactive members, the pointer reads and the display callback run on.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="pointerSource"/>, <paramref name="hooks"/>, or <paramref name="dispatcher"/> is <see langword="null"/>, or a required
	/// hook is <see langword="null"/>; <see cref="HoverControllerHooks.BuildRequest"/> is required when <paramref name="provider"/> is supplied.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread of <paramref name="dispatcher"/>.</exception>
	public HoverController(
		IHoverPointerSource pointerSource,
		IHoverProvider? provider,
		HoverControllerHooks hooks,
		IUiDispatcher dispatcher,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(pointerSource);
		ArgumentNullException.ThrowIfNull(hooks);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(hooks.ShowHover);

		if (provider is not null)
			ArgumentNullException.ThrowIfNull(hooks.BuildRequest);

		// The controller reads the pointer and shows popups on the dispatcher's thread, so creation on another thread fails fast and
		// RequestAsync verifies the thread before it reads the source.
		dispatcher.VerifyAccess();

		_logger = logger ?? NullLogger.Instance;
		_pointerSource = pointerSource;
		_provider = provider;
		_dispatcher = dispatcher;
		_buildDecision = hooks.BuildDecision ?? RequestAndShowContentAt;
		_buildRequest = hooks.BuildRequest;
		_resolveRequestOffset = hooks.ResolveRequestOffset;
		_showHover = hooks.ShowHover;
		_contextVersionProvider = hooks.ContextVersionProvider;
	}

	/// <summary>
	/// Requests hover presentation content for the document offset under the pointer, showing hover or diagnostic content when appropriate,
	/// or asking the host to hide its hover popup when the evaluation has nothing to show.
	/// </summary>
	/// <returns>
	/// A task that completes after the hover evaluation concluded. Failures are contained: the evaluation is logged (and falls back to the diagnostic
	/// popup when one was resolved before the failure) instead of faulting the task.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the controller's dispatcher thread; the returned task faults.</exception>
	public async Task RequestAsync()
	{
		if (_lifetime.IsDisposed)
			return;

		_dispatcher.VerifyAccess();

		int? hoveredOffset = null;
		HoverDecision decision = default;

		bool requestStarted = false;
		bool failureReported = false;

		try
		{
			hoveredOffset = _pointerSource.GetHoveredOffset();

			if (hoveredOffset is null)
			{
				// The latest evaluation found no hover target, so pending work is superseded before the
				// "pointer left" result is applied, and the host hides its popup.
				SupersedePendingRequest();
				_showHover(null, null, null);
				return;
			}

			decision = _buildDecision(hoveredOffset.Value);

			if (!decision.ShouldRequestHover || _provider is not { } provider || _buildRequest is not { } buildRequest)
			{
				// The latest evaluation decided that no request should be made, or the controller has no provider to ask, so an older
				// in-flight request must not publish its result after the fact.
				SupersedePendingRequest();
				ShowDiagnosticFallback(decision, hoveredOffset.Value);
				return;
			}

			// The host vetoes a request it cannot serve, for example a document it does not track, which concludes the evaluation like a
			// state that declined the request.
			HoverRequest? request = buildRequest(decision.RequestOffset);

			if (request is null)
			{
				SupersedePendingRequest();
				ShowDiagnosticFallback(decision, decision.RequestOffset);
				return;
			}

			// A request build that remaps the offset only works with the matching request-offset hook; without
			// it the completed result can never match the pointer position, so the host is warned once.
			if (_resolveRequestOffset is null && decision.RequestOffset != hoveredOffset.Value)
				LogOffsetMismatchOnce();

			int contextVersion = _contextVersionProvider?.Invoke() ?? 0;

			// The request starts a new generation, so a result of an earlier request cannot publish after this point.
			int generation = _lifetime.AdvanceGeneration();

			(HoverDecision Decision, int ContextVersion, int Generation) requestState = (
				Decision: decision,
				ContextVersion: contextVersion,
				Generation: generation
			);

			requestStarted = true;

			// The gate cancels the previous hover request when a newer one starts and discards its
			// superseded results; the pointer, context and generation checks run in the marshaled publication. Its
			// publication callbacks run after the provider's await, which can resume on a thread-pool thread,
			// so the pointer resolution and the host display callback marshal to the owner's thread. A failure
			// is reported through the failure callback, invoked only while this request owns the slot, so a
			// superseded failure neither reaches the host nor replaces the newer display decision.
			await _lifetime
				.RunAsync(
					state: requestState,
					computeAsync: (_, cancellationToken) => provider.GetHoverAsync(request, cancellationToken),
					apply: hoverInfo => _dispatcher.Invoke(() => ShowCompletedHover(requestState, hoveredOffset.Value, hoverInfo)),

					onFailure: exception =>
					{
						failureReported = true;
						LogHoverRequestFailed(_logger, exception);
						ApplyRequestFailure(requestState, hoveredOffset.Value);
					}
				)
				.ConfigureAwait(true);
		}
		catch (OperationCanceledException)
		{
			// A superseded hover request reports cancellation, not a failure; the superseding evaluation applies
			// its own display decision.
		}
		catch (Exception exception)
		{
			if (_lifetime.IsDisposed || failureReported)
				return;

			LogHoverRequestFailed(_logger, exception);

			// A failure raised before any request was admitted - a throwing state or request builder - has no
			// request to own the display decision; the popup is hidden when a hovered offset was resolved.
			// A failure of the request itself was either reported by the failure
			// callback (which also applied its fallback) or belongs to a superseded request, which only
			// leaves its log entry. The failing evaluation is the newest one and runs on this thread, so it supersedes the
			// outstanding request like an evaluation that concludes without one and shows its fallback at once.
			if (!requestStarted && hoveredOffset is { } failedOffset)
			{
				SupersedePendingRequest();

				// A failure after the decision asked for a request, such as a throwing request builder, targets the request offset, and an
				// earlier one targets the hovered offset.
				int targetOffset = decision.ShouldRequestHover ? decision.RequestOffset : failedOffset;
				CallbackContainment.TryRun(() => ShowDiagnosticFallback(decision, targetOffset), ReportHostCallbackFault);
			}
		}
	}

	/// <summary>Cancels the in-flight hover request, if any.</summary>
	/// <remarks>
	/// The canceled request's result is rejected because its cancellation token is canceled; use <see cref="InvalidateRequests"/> to reject
	/// outstanding requests without canceling them.
	/// </remarks>
	public void CancelInFlightRequest()
	{
		_lifetime.CancelInFlightRequest();
		_lifetime.AdvanceGeneration();
	}

	// Supersedes pending work: the in-flight request is canceled and its result is rejected as stale.
	// The latest hover evaluation applies this when it decides that no request will be made.
	private void SupersedePendingRequest()
	{
		_lifetime.CancelAndInvalidate();
		_lifetime.AdvanceGeneration();
	}

	/// <summary>Marks outstanding hover requests as stale so completed results are ignored.</summary>
	public void InvalidateRequests()
	{
		_lifetime.InvalidateRequests();
		_lifetime.AdvanceGeneration();
	}

	/// <summary>Cancels the in-flight hover request, marks outstanding work as stale, and disposes the pointer source.</summary>
	/// <remarks>
	/// Disposal does not dismiss host popups: the host must close any visible hover or diagnostic popup as part of its own teardown,
	/// and the controller does not call <c>ShowHover</c> after disposal. After disposal, <see cref="RequestAsync"/> returns a completed task
	/// and <see cref="CancelInFlightRequest"/> and <see cref="InvalidateRequests"/> do nothing.
	/// </remarks>
	public void Dispose()
	{
		if (_lifetime.IsDisposed)
			return;

		_lifetime.Dispose();
		_pointerSource.Dispose();
	}

	private bool IsContextVersionCurrent(int contextVersion) => _contextVersionProvider is null || contextVersion == _contextVersionProvider();

	// Publishes a completed hover result on the owner's thread: the generation, pointer, context, and offset checks run
	// here as part of publication, and an accepted result is shown with the evaluation state resolved for the
	// pointer's current offset. Running the checks here instead of in a separate current-state predicate keeps
	// the acceptance decision and the display in one dispatched step. A throwing check still propagates so the
	// caller's fallback applies; only the host show callback is contained and logged as a host-callback failure
	// instead of a request failure.
	private void ShowCompletedHover(
		(HoverDecision Decision, int ContextVersion, int Generation) state,
		int hoveredOffset,
		HoverInformation? hoverInfo
	)
	{
		if (TryResolveDisplayState(state, hoveredOffset, out HoverDecision displayState))
			CallbackContainment.TryRun(() => ShowBestHover(hoverInfo, displayState, state.Decision.RequestOffset), ReportHostCallbackFault);
	}

	/// <summary>
	/// Checks whether the outcome of a hover request, a completed result or the fallback of a failure, may be displayed and resolves the evaluation
	/// state that the publication callback displays.
	/// </summary>
	/// <param name="state">The evaluation state captured when the hover request started, with the host context version and the generation.</param>
	/// <param name="hoveredOffset">The offset that was hovered when the request started.</param>
	/// <param name="displayState">
	/// The evaluation state to display for an accepted outcome; <see langword="default"/> when the outcome is rejected.
	/// </param>
	/// <returns><see langword="true"/> when the outcome may be displayed; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The outcome is rejected when the controller is disposed, when a newer request, a supersession, a cancellation, or an invalidation
	/// made the request stale, when the host context version changed since the request started, when the pointer resolves to no offset,
	/// or when the pointer now maps to a different request offset than the one the request carries.
	/// </remarks>
	private bool TryResolveDisplayState(
		(HoverDecision Decision, int ContextVersion, int Generation) state,
		int hoveredOffset,
		out HoverDecision displayState
	)
	{
		displayState = default;

		if (!_lifetime.IsCurrent(state.Generation) || !IsContextVersionCurrent(state.ContextVersion))
			return false;

		// The pointer is re-checked against the same offset before a completed result is shown; a pointer that left the
		// editor, or maps to no offset, rejects the result.
		if (_pointerSource.GetHoveredOffset() is not { } currentHoveredOffset)
			return false;

		// A host that supplies the hook owns the hover targeting decision; a null answer means the
		// host vetoed the position and must not fall back to the raw hovered offset.
		int? currentRequestOffset = _resolveRequestOffset is null ? currentHoveredOffset : _resolveRequestOffset(currentHoveredOffset);

		if (currentRequestOffset != state.Decision.RequestOffset)
			return false;

		// The earlier evaluation state is reused when the pointer still targets the same offset.
		displayState = currentHoveredOffset == hoveredOffset ? state.Decision : _buildDecision(currentHoveredOffset);

		return true;
	}

	private void LogOffsetMismatchOnce()
	{
		if (_isOffsetMismatchWarningLogged)
			return;

		_isOffsetMismatchWarningLogged = true;
		LogHoverOffsetMismatch(_logger, null);
	}

	// The failure recovery runs a host popup callback from the failure callback; a throwing callback there
	// - or a dispatcher that is shutting down while the callback is marshaled - would escape the caller's
	// event handler, so failures are logged instead. The fallback is dispatched and accepted under the conditions of a completed
	// result, so it shows nothing when a newer evaluation, a disposal, a pointer that left, or a host context change made the failing
	// request stale in the meantime.
	private void ApplyRequestFailure((HoverDecision Decision, int ContextVersion, int Generation) state, int hoveredOffset)
	{
		CallbackContainment.TryRun(
			() =>
				_dispatcher.Invoke(() =>
				{
					if (TryResolveDisplayState(state, hoveredOffset, out HoverDecision displayState))
						ShowDiagnosticFallback(displayState, state.Decision.RequestOffset);
				}),

			ReportHostCallbackFault
		);
	}

	private void ReportHostCallbackFault(Exception exception) => LogHoverHostCallbackFailed(_logger, exception);

	// Reports the diagnostic fallback when the evaluation state allows it, and a hide request otherwise, so
	// every evaluation ends in exactly one display decision; the state supplies the permissions, and this
	// controller applies the fallback rule.
	// The decision of a host that supplies no BuildDecision: request hover at the hovered offset and show the content.
	private static HoverDecision RequestAndShowContentAt(int hoveredOffset)
	{
		return new()
		{
			ShouldRequestHover = true,
			RequestOffset = hoveredOffset,
			CanShowHoverContent = true,
		};
	}

	private void ShowDiagnosticFallback(HoverDecision decision, int? targetOffset) =>
		_showHover(null, decision.CanShowDiagnosticFallback ? decision.Diagnostic : null, targetOffset);

	private void ShowBestHover(HoverInformation? hoverInfo, HoverDecision decision, int? targetOffset)
	{
		if (!decision.CanShowHoverContent)
		{
			// The display state forbids the popup, so the evaluation resolves to hiding it.
			_showHover(null, null, targetOffset);
			return;
		}

		// Hover content and diagnostic messages are non-blank by construction, so the state passes
		// them through directly; a null hover result passes through as no hover content.
		_showHover(hoverInfo, decision.Diagnostic, targetOffset);
	}
}
