#if AVALONIAEDIT
using Avalonia;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Requests;
using HoverEventArgs = Avalonia.Input.PointerEventArgs;
using OwnerElement = Avalonia.Controls.Control;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Requests;
using System.Windows;
using System.Windows.Input;
using HoverEventArgs = System.Windows.Input.MouseEventArgs;
using OwnerElement = System.Windows.FrameworkElement;
#endif
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
#endif

/// <summary>
/// Coordinates hover requests, request invalidation, and hover-versus-diagnostic tooltip display.
/// Completed results are displayed only when the request is still current, the pointer still maps
/// to the same request offset, and the optional host context version is unchanged.
/// </summary>
/// <remarks>
/// <para>
/// Failures inside a hover evaluation are contained rather than propagated to the caller: the failure is
/// logged and, when a valid hovered offset was resolved before the failure, the display decision falls back
/// to a diagnostic tooltip when one is available (and to a hide request when none is). Failures that occur
/// before a valid hovered offset exists - for example inside <c>GetOffsetFromPoint</c> - are only logged,
/// because there is no display target to apply an evaluation to. Because the containment wraps the whole
/// evaluation, it also covers exceptions thrown by the host tooltip callback. A provider failure is applied
/// only while its request is still the current one: a superseded request that faults after a newer
/// evaluation already published its decision leaves that newer state untouched. The controller does not
/// debounce hover requests; hover-intent policy (an initial delay or a required pointer stability window)
/// stays with the host, which decides when to call <see cref="HandleMouseHoverAsync"/>.
/// </para>
/// <para>
/// The controller must be created and used on the thread that owns the hovered element and the host
/// callbacks, because it invokes them directly and applies presentation state inline. Provider requests stay
/// asynchronous and their continuations are marshalled back to that thread, even when the creating thread
/// captured no synchronization context.
/// </para>
/// <para>
/// The controller exposes no readable presentation state: the host owns the hover tooltip surface and
/// receives every display decision through the single <see cref="TextHoverControllerHooks.ShowTooltip"/>
/// callback, so there is no controller-side <c>CurrentPresentation</c> to keep in sync (unlike the completion
/// controller, whose completion window and tooltip are hosted by its window coordinator).
/// </para>
/// </remarks>
public sealed partial class TextHoverController : IDisposable
{
	// Hover uses package log event ids 1010-1012 (see the README for the canonical table).
	[LoggerMessage(
		EventId = 1010,
		EventName = "HoverRequestFailed",
		Level = LogLevel.Warning,
		Message = "Hover request failed."
	)]
	private static partial void LogHoverRequestFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1011,
		EventName = "HoverHostCallbackFailed",
		Level = LogLevel.Warning,
		Message = "A hover host callback failed."
	)]
	private static partial void LogHoverHostCallbackFailed(ILogger logger, Exception? exception);

	[LoggerMessage(
		EventId = 1012,
		EventName = "HoverOffsetMismatch",
		Level = LogLevel.Warning,
		Message = "BuildEvaluationState requests a different offset than the hovered offset, but no ResolveRequestOffset "
			+ "hook is supplied. Completed hover results cannot be matched to the pointer position and will be discarded."
	)]
	private static partial void LogHoverOffsetMismatch(ILogger logger, Exception? exception);

	private readonly ILogger _logger;
	private readonly OwnerElement _owner;
	private readonly Func<Point, int?> _getOffsetFromPoint;
	private readonly Func<int, TextHoverEvaluationState> _buildEvaluationState;
	private readonly Func<int, CancellationToken, Task<TextHoverInfo?>> _requestHoverAsync;
	private readonly Func<int, int?>? _resolveRequestOffset;
	private readonly Func<Point>? _getCurrentPointerPosition;
	private readonly Action<TextHoverInfo?, TextDiagnostic?> _showTooltip;
	private readonly Func<int>? _contextVersionProvider;

	private readonly RequestLifetime _lifetime = new();
	private bool _offsetMismatchWarningLogged;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextHoverController"/> class.
	/// </summary>
	/// <param name="owner">The element that hover positions are resolved against.</param>
	/// <param name="hooks">The required and optional hover callbacks.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="owner"/> or <paramref name="hooks"/> is <see langword="null"/>, or a required hook is
	/// <see langword="null"/>.
	/// </exception>
	public TextHoverController(OwnerElement owner, TextHoverControllerHooks hooks, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(hooks);
		ArgumentNullException.ThrowIfNull(hooks.GetOffsetFromPoint);
		ArgumentNullException.ThrowIfNull(hooks.BuildEvaluationState);
		ArgumentNullException.ThrowIfNull(hooks.RequestHoverAsync);
		ArgumentNullException.ThrowIfNull(hooks.ShowTooltip);

		// The controller reads pointer state and shows tooltips on the owner's thread; creation on another thread
		// fails fast like the completion and code-action controllers instead of failing non-deterministically
		// on first use.
		owner.Dispatcher.VerifyAccess();

		_logger = logger ?? NullLogger.Instance;
		_owner = owner;
		_getOffsetFromPoint = hooks.GetOffsetFromPoint;
		_buildEvaluationState = hooks.BuildEvaluationState;
		_requestHoverAsync = hooks.RequestHoverAsync;
		_resolveRequestOffset = hooks.ResolveRequestOffset;
#if AVALONIAEDIT
		_getCurrentPointerPosition = hooks.GetCurrentPointerPosition;
#else
		_getCurrentPointerPosition = hooks.GetCurrentPointerPosition ?? (() => Mouse.GetPosition(_owner));
#endif
		_showTooltip = hooks.ShowTooltip;
		_contextVersionProvider = hooks.ContextVersionProvider;
	}

	/// <summary>
	/// Handles a pointer-hover event and shows hover or diagnostic content when appropriate, or asks the host
	/// to hide its hover tooltip when the evaluation has nothing to show.
	/// </summary>
	/// <param name="e">The pointer event to handle.</param>
	/// <returns>
	/// A task that completes after the hover evaluation concluded. Failures are contained: the evaluation is
	/// logged (and falls back to the diagnostic tooltip when one was resolved before the failure) instead of
	/// faulting the task.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	public async Task HandleMouseHoverAsync(HoverEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

		if (_lifetime.IsDisposed)
			return;

		int? hoveredOffset = null;
		TextHoverEvaluationState evaluationState = default;
		bool requestStarted = false;
		bool failureReported = false;

		try
		{
			hoveredOffset = _getOffsetFromPoint(e.GetPosition(_owner));

			if (hoveredOffset is null)
			{
				// The latest evaluation found no hover target, so pending work is superseded before the
				// "pointer left" result is applied, and the host hides its tooltip.
				SupersedePendingRequest();
				_showTooltip(null, null);
				return;
			}

			evaluationState = _buildEvaluationState(hoveredOffset.Value);

			if (!evaluationState.ShouldRequestHover)
			{
				// The latest evaluation decided that no request should be made, so an older in-flight
				// request must not publish its result after the fact.
				SupersedePendingRequest();
				ShowDiagnosticFallback(evaluationState);
				return;
			}

			// A request build that remaps the offset only works with the matching request-offset hook; without
			// it the completed result can never match the pointer position, so the host is warned once.
			if (_resolveRequestOffset is null && evaluationState.RequestOffset != hoveredOffset.Value)
				LogOffsetMismatchOnce();

			int contextVersion = _contextVersionProvider?.Invoke() ?? 0;
			var requestState = (EvaluationState: evaluationState, ContextVersion: contextVersion);

			requestStarted = true;

			// The coordinator cancels the previous hover request when a newer one starts and discards
			// results that are no longer current; the pointer and context checks run as part of the
			// marshalled publication. Its publication callbacks run after the provider's await, which
			// resumes on a thread-pool thread when the creating thread captured no synchronization
			// context, so the pointer resolution and the host display callback are marshalled to the
			// owner's thread explicitly. A failure is reported through the failure callback, which the
			// coordinator invokes only while this request still owns the slot, so a superseded provider
			// failure neither reaches the host nor replaces the newer evaluation's display decision.
			await _lifetime.Coordinator.RunAsync(
				state: requestState,
				computeAsync: (state, cancellationToken) => _requestHoverAsync(state.EvaluationState.RequestOffset, cancellationToken),
				canApply: (_, _) => !_lifetime.IsDisposed,
				apply: hoverInfo => DispatcherInvocation.Run(
					_owner.Dispatcher,
					() => ShowCompletedTooltip(requestState, hoveredOffset.Value, hoverInfo)),
				onFailure: exception =>
				{
					failureReported = true;
					LogHoverRequestFailed(_logger, exception);
					ApplyFailureState(evaluationState);
				}).ConfigureAwait(true);
		}
		catch (OperationCanceledException)
		{
			// A superseded hover request reports cancellation, not a failure; the superseding evaluation has
			// already applied its own display decision.
		}
		catch (Exception exception)
		{
			if (_lifetime.IsDisposed || failureReported)
				return;

			LogHoverRequestFailed(_logger, exception);

			// A failure raised before any request was admitted - a throwing offset resolution or state
			// builder - has no request to own the display decision; the tooltip is hidden when a hovered
			// offset was resolved. A failure of the request itself was either reported by the failure
			// callback (which also applied its fallback) or belongs to a superseded request, which only
			// leaves its log entry.
			if (!requestStarted && hoveredOffset is not null)
				ApplyFailureState(evaluationState);
		}
	}

	/// <summary>
	/// Cancels the in-flight hover request, if any. The canceled request's result is rejected because its
	/// cancellation token is canceled; use <see cref="InvalidateRequests"/> to reject outstanding requests
	/// without canceling them.
	/// </summary>
	public void CancelInFlightRequest()
	{
		_lifetime.CancelInFlightRequest();
	}

	// Supersedes pending work: the in-flight request is canceled and its result is rejected as stale.
	// The latest hover evaluation applies this when it decides that no request will be made.
	private void SupersedePendingRequest()
	{
		_lifetime.CancelAndInvalidate();
	}

	/// <summary>
	/// Marks outstanding hover requests as stale so completed results are ignored.
	/// </summary>
	public void InvalidateRequests()
	{
		_lifetime.InvalidateRequests();
	}

	/// <summary>
	/// Cancels the in-flight hover request and marks outstanding work as stale.
	/// </summary>
	/// <remarks>
	/// Disposal does not dismiss host tooltips: the host must close any visible hover or diagnostic tooltip
	/// as part of its own teardown, and the controller does not call <c>ShowTooltip</c> after disposal.
	/// </remarks>
	public void Dispose()
	{
		_lifetime.Dispose();
	}

	private bool IsContextVersionCurrent(int contextVersion)
		=> _contextVersionProvider is null || contextVersion == _contextVersionProvider();

	// Publishes a completed hover result on the owner's thread: the pointer, context, and offset checks run
	// here as part of publication, and an accepted result is shown with the evaluation state resolved for the
	// pointer's current offset. Running the checks here instead of in a separate current-state predicate keeps
	// the acceptance decision and the display in one dispatched step.
	private void ShowCompletedTooltip(
		(TextHoverEvaluationState EvaluationState, int ContextVersion) state,
		int hoveredOffset,
		TextHoverInfo? hoverInfo)
	{
		if (TryResolveDisplayState(state, hoveredOffset, out TextHoverEvaluationState displayState))
			ShowBestTooltip(hoverInfo, displayState);
	}

	/// <summary>
	/// Checks whether a completed hover result may be published and resolves the evaluation state that the
	/// publication callback displays.
	/// </summary>
	/// <remarks>
	/// The result is rejected when the controller is disposed, when the host context version changed since the
	/// request started, when the pointer no longer resolves to an offset, or when the pointer now targets a
	/// different offset than the request. Without a pointer-position hook, the pointer re-check is skipped and
	/// the offset captured when the request started is reused.
	/// </remarks>
	/// <param name="state">The evaluation state captured when the hover request started, with the host context version.</param>
	/// <param name="hoveredOffset">The offset that was hovered when the request started.</param>
	/// <param name="displayState">
	/// The evaluation state to display for an accepted result; <see langword="default"/> when the result is rejected.
	/// </param>
	/// <returns><see langword="true"/> when the completed result may be displayed; otherwise, <see langword="false"/>.</returns>
	private bool TryResolveDisplayState(
		(TextHoverEvaluationState EvaluationState, int ContextVersion) state,
		int hoveredOffset,
		out TextHoverEvaluationState displayState)
	{
		displayState = default;

		if (_lifetime.IsDisposed || !IsContextVersionCurrent(state.ContextVersion))
			return false;

		// A binding that cannot resolve an ambient pointer position has no pointer liveness re-check, so the
		// offset captured when the request started is reused.
		int? currentHoveredOffset = _getCurrentPointerPosition is null
			? hoveredOffset
			: _getOffsetFromPoint(_getCurrentPointerPosition());

		if (currentHoveredOffset is null)
			return false;

		// A host that supplies the hook owns the hover targeting decision; a null answer means the
		// host vetoed the position and must not fall back to the raw hovered offset.
		int? currentRequestOffset = _resolveRequestOffset is null
			? currentHoveredOffset
			: _resolveRequestOffset(currentHoveredOffset.Value);

		if (currentRequestOffset != state.EvaluationState.RequestOffset)
			return false;

		// The earlier evaluation state is reused when the pointer still targets the same offset.
		displayState = currentHoveredOffset.Value == hoveredOffset
			? state.EvaluationState
			: _buildEvaluationState(currentHoveredOffset.Value);

		return true;
	}

	private void LogOffsetMismatchOnce()
	{
		if (_offsetMismatchWarningLogged)
			return;

		_offsetMismatchWarningLogged = true;
		LogHoverOffsetMismatch(_logger, null);
	}

	// The failure recovery runs a host tooltip callback from inside a catch block; a throwing callback there
	// - or a dispatcher that is shutting down while the callback is marshalled - would escape the caller's
	// event handler, so failures are logged instead.
	private void ApplyFailureState(TextHoverEvaluationState evaluationState)
		=> HostCallback.TryRun(
			() => DispatcherInvocation.Run(_owner.Dispatcher, () => ShowDiagnosticFallback(evaluationState)),
			_logger,
			LogHoverHostCallbackFailed);

	// Reports the diagnostic fallback when the evaluation state allows it, and a hide request otherwise, so
	// every evaluation ends in exactly one display decision; the state supplies the permissions, and this
	// controller applies the fallback rule.
	private void ShowDiagnosticFallback(TextHoverEvaluationState evaluationState)
		=> _showTooltip(null, evaluationState.CanShowDiagnosticFallback ? evaluationState.DiagnosticInfo : null);

	private void ShowBestTooltip(TextHoverInfo? hoverInfo, TextHoverEvaluationState evaluationState)
	{
		if (!evaluationState.CanShowHoverContent)
		{
			// The display state forbids the tooltip, so the evaluation resolves to hiding it.
			_showTooltip(null, null);
			return;
		}

		// Hover content and diagnostic messages are non-blank by construction, so the state passes
		// them through directly; a null hover result still hides the tooltip.
		_showTooltip(hoverInfo, evaluationState.DiagnosticInfo);
	}
}
