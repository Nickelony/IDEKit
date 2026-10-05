using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>
/// Admits completion requests as the newest one, debounces the scheduled request, and applies a decision only while its request is still current.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="CompletionController"/> owns one gate and forwards its request members to it. The gate presents nothing: it hands an accepted
/// decision to the delegate it was given and reports the scheduled-request flag through a callback. A new request cancels the token
/// of the request in flight, and a decision is applied only while no newer request, cancellation, invalidation, disposal, or host context change made
/// it stale.
/// </para>
/// <para>
/// The gate runs the scheduled callback and applies decisions on the thread of its dispatcher, so use it from that thread.
/// A provider continuation can resume on a thread-pool thread when the thread that started the request has no synchronization context that posts
/// to the dispatcher, so every decision is applied through the dispatcher, even when the continuation already runs there. The dispatched step
/// compares a generation that every new request, cancellation, and invalidation advances, so a decision that one of them made stale is never applied,
/// whichever thread the continuation ran on.
/// </para>
/// </remarks>
internal sealed partial class CompletionRequestGate : IDisposable
{
	[LoggerMessage(EventId = 1000, EventName = "CompletionRequestFailed", Level = LogLevel.Warning, Message = "Completion request failed.")]
	private static partial void LogCompletionRequestFailed(ILogger logger, Exception? exception);

	private readonly IUiDispatcher _dispatcher;
	private readonly Func<CompletionSessionDirective, bool> _applyDecision;
	private readonly Func<CancellationToken, Task>? _scheduledRequestAsync;
	private readonly Func<int>? _contextVersionProvider;
	private readonly Action<bool>? _setRequestScheduled;
	private readonly ILogger _logger;
	private readonly DispatcherDebouncer _requestDebouncer;
	private readonly RequestLifetime _lifetime = new();

	/// <param name="dispatcher">The dispatcher whose thread the gate runs the debounce timer, the scheduled callback, and the apply step on.</param>
	/// <param name="applyDecision">
	/// Applies an accepted decision and reports whether it opened or refreshed a window. The gate calls it on the dispatcher's thread.
	/// </param>
	/// <param name="scheduledRequestAsync">
	/// The callback a scheduled request runs, or <see langword="null"/> when the host schedules no requests. It receives a token
	/// that disposal cancels.
	/// </param>
	/// <param name="contextVersionProvider">
	/// The provider of a host context version, or <see langword="null"/> when the host version never changes. A decision is discarded
	/// when the version changed since its request started.
	/// </param>
	/// <param name="setRequestScheduled">
	/// The callback that reports the scheduled-request flag, or <see langword="null"/> when nobody observes it.
	/// </param>
	/// <param name="options">The controller options the debounce delay is read from, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <exception cref="InvalidOperationException">The caller is not on the thread of <paramref name="dispatcher"/>.</exception>
	internal CompletionRequestGate(
		IUiDispatcher dispatcher,
		Func<CompletionSessionDirective, bool> applyDecision,
		Func<CancellationToken, Task>? scheduledRequestAsync = null,
		Func<int>? contextVersionProvider = null,
		Action<bool>? setRequestScheduled = null,
		CompletionControllerOptions? options = null,
		ILogger? logger = null
	)
	{
		// The debounce timer and the apply step belong to the dispatcher's thread, so creating the gate elsewhere is a caller error.
		dispatcher.VerifyAccess();

		_dispatcher = dispatcher;
		_applyDecision = applyDecision;
		_scheduledRequestAsync = scheduledRequestAsync;
		_contextVersionProvider = contextVersionProvider;
		_setRequestScheduled = setRequestScheduled;
		_logger = logger ?? NullLogger.Instance;

		_requestDebouncer = new DispatcherDebouncer((options ?? CompletionControllerOptions.Default).RequestDebounceDelay, dispatcher);
	}

	/// <summary>Indicates whether the gate was disposed.</summary>
	internal bool IsDisposed => _lifetime.IsDisposed;

	/// <summary>Schedules the scheduled callback to run after the debounce delay; the call has no effect without a callback.</summary>
	/// <remarks>
	/// Arming again while a request is scheduled restarts the delay. The callback runs on the dispatcher's thread, a failure of it is logged,
	/// and a cancellation it reports is ignored.
	/// </remarks>
	internal void ScheduleRequest()
	{
		if (_lifetime.IsDisposed || _scheduledRequestAsync is null)
			return;

		_requestDebouncer.Arm(() => _ = RunScheduledRequestAsync());
		_setRequestScheduled?.Invoke(true);
	}

	/// <summary>Cancels the scheduled callback, if one is waiting for its delay; the request in flight is not affected.</summary>
	internal void CancelScheduledRequest()
	{
		if (_lifetime.IsDisposed)
			return;

		_requestDebouncer.Cancel();
		_setRequestScheduled?.Invoke(false);
	}

	/// <summary>Cancels the token of the request in flight, if any, so a cooperative provider can stop early.</summary>
	/// <remarks>
	/// The canceled request's decision is discarded because its token is canceled. After disposal the call does nothing.
	/// </remarks>
	internal void CancelInFlightRequest()
	{
		_lifetime.CancelInFlightRequest();
		_lifetime.AdvanceGeneration();
	}

	/// <summary>Marks outstanding requests as stale so their decisions are discarded when they complete, without canceling the provider.</summary>
	internal void InvalidateRequests()
	{
		_lifetime.InvalidateRequests();
		_lifetime.AdvanceGeneration();
	}

	/// <summary>
	/// Runs a request as the newest one: the callback computes the decision, and the gate rejects a superseded, canceled, or stale result and applies
	/// an accepted one.
	/// </summary>
	/// <param name="requestAsync">The callback that computes the decision for this request.</param>
	/// <returns>
	/// A task whose result is <see langword="true"/> when the decision was applied and the apply delegate reported a window;
	/// <see langword="false"/> when the request was superseded, canceled, failed, or produced no window.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the gate's dispatcher thread; the returned task faults.</exception>
	/// <remarks>
	/// Starting a request cancels the token of the request in flight. The callback may report cancellation
	/// by throwing <see cref="OperationCanceledException"/>, which the task reports as <see langword="false"/>; any other failure is logged
	/// and reported the same way, so no caller observes a provider exception. After disposal the task yields <see langword="false"/>.
	/// </remarks>
	internal async Task<bool> RequestAsync(Func<CancellationToken, Task<CompletionSessionDirective>> requestAsync)
	{
		if (_lifetime.IsDisposed)
			return false;

		_dispatcher.VerifyAccess();

		bool decisionApplied = false;

		try
		{
			// The version is captured before the request starts, so a host state change while the provider runs is detected when the
			// decision is about to apply. The provider is host code, so a failure of it is contained like a provider failure.
			int contextVersion = _contextVersionProvider?.Invoke() ?? 0;
			int generation = _lifetime.AdvanceGeneration();

			// The lifetime admits the request as the newest one, cancels the superseded request's token, and classifies the outcome. The apply
			// step runs after the provider's await, which resumes on a thread-pool thread when the creating thread captured no synchronization
			// context, so it is marshaled to the dispatcher explicitly. The generation and the version are checked in that same dispatched step,
			// so a cancellation or a host change on the dispatcher's thread cannot land between the check and the decision it guards.
			Task<RequestOutcome> request = _lifetime.RunAsync(
				state: requestAsync,
				computeAsync: static (callback, cancellationToken) => callback(cancellationToken),

				apply: decision =>
					decisionApplied = _dispatcher.Invoke(() =>
						_lifetime.IsCurrent(generation) && IsContextVersionCurrent(contextVersion) && _applyDecision(decision)
					)
			);

			RequestOutcome outcome = await request.ConfigureAwait(true);

			// The run completes only after the decision was applied, so the recorded result carries whether the applied decision produced a window.
			return outcome == RequestOutcome.Completed && decisionApplied;
		}
		catch (OperationCanceledException)
		{
			// A superseded or canceled request reports cancellation, not a failure.
			return false;
		}
		catch (Exception exception)
		{
			if (!_lifetime.IsDisposed)
				LogCompletionRequestFailed(_logger, exception);

			return false;
		}
	}

	/// <summary>
	/// Stops the debounce timer, cancels the request in flight and the token of the scheduled callback, and rejects later requests.
	/// </summary>
	/// <remarks>
	/// The method is idempotent. The scheduled-request flag keeps the value it had, so the owner clears it before it disposes the gate.
	/// </remarks>
	public void Dispose()
	{
		_lifetime.Dispose();
		_requestDebouncer.Dispose();
	}

	private bool IsContextVersionCurrent(int contextVersion) => _contextVersionProvider is null || _contextVersionProvider() == contextVersion;

	// A dispatcher debounce callback that starts the scheduled asynchronous request. The returned task is discarded deliberately: the debouncer
	// callback is synchronous, and the method contains every failure, so the discarded task never faults and a failure reaches the host through
	// the logger instead of escaping the dispatcher.
	private async Task RunScheduledRequestAsync()
	{
		_setRequestScheduled?.Invoke(false);

		if (_scheduledRequestAsync is null)
			return;

		try
		{
			await _scheduledRequestAsync(_lifetime.LifetimeToken).ConfigureAwait(true);
		}
		catch (OperationCanceledException)
		{
			// A scheduled request that observes its token being canceled reports cancellation, not a failure.
		}
		catch (Exception exception)
		{
			if (_lifetime.IsDisposed)
				return;

			LogCompletionRequestFailed(_logger, exception);
		}
	}
}
