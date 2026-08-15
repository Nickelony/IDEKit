using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Completion;
#if AVALONIAEDIT
using Avalonia.Threading;
#else
using System.Windows.Threading;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Owns the debounced completion request scheduling: the debounce timer, the callback of a scheduled
/// request, and the scheduled-request flag reported through the completion controller's state callback.
/// </summary>
/// <remarks>
/// <para>
/// The scheduler is created and used on the editor thread, because its debounce timer runs on the editor
/// dispatcher it is given; the completion controller passes the text area's dispatcher. The request
/// lifetime itself - the request identifiers, the cancellation tokens, and the standard request
/// pipeline - is owned by the core request coordinator behind the controller's
/// <see cref="TextCompletionRequestSession"/>.
/// </para>
/// <para>
/// Disposal stops the debounce timer. The stored state stays observable afterwards, mirroring the
/// completion controller's post-disposal contract.
/// </para>
/// </remarks>
internal sealed partial class CompletionRequestScheduler : IDisposable
{
	// Completion request failures use package log event id 1000 (see the README for the canonical table).
	[LoggerMessage(
		EventId = 1000,
		EventName = "CompletionRequestFailed",
		Level = LogLevel.Warning,
		Message = "Completion request failed."
	)]
	private static partial void LogCompletionRequestFailed(ILogger logger, Exception? exception);

	private readonly ILogger _logger;
	private readonly Func<Task>? _scheduledRequestAsync;
	private readonly Action<bool> _setRequestScheduled;
	private readonly DispatcherDebouncer _requestDebouncer;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionRequestScheduler"/> class.
	/// </summary>
	/// <param name="options">The controller options the request debounce delay is read from.</param>
	/// <param name="dispatcher">The editor dispatcher the request debounce timer runs on.</param>
	/// <param name="scheduledRequestAsync">
	/// The callback a scheduled request runs, or <see langword="null"/> when the host schedules no requests.
	/// </param>
	/// <param name="setRequestScheduled">
	/// The callback that reports the scheduled-request flag to the completion presentation state.
	/// </param>
	/// <param name="logger">The logger for request failures.</param>
	internal CompletionRequestScheduler(
		TextCompletionControllerOptions options,
		Dispatcher dispatcher,
		Func<Task>? scheduledRequestAsync,
		Action<bool> setRequestScheduled,
		ILogger logger)
	{
		_logger = logger;
		_scheduledRequestAsync = scheduledRequestAsync;
		_setRequestScheduled = setRequestScheduled;
		_requestDebouncer = new DispatcherDebouncer(options.RequestDebounceDelay, dispatcher);
	}

	/// <remarks>Implements the contract the completion controller documents for
	/// <see cref="TextCompletionController.ScheduleRequest"/>.</remarks>
	internal void ScheduleRequest()
	{
		if (_isDisposed || _scheduledRequestAsync is null)
			return;

		_requestDebouncer.Arm(RunScheduledRequest);
		_setRequestScheduled(true);
	}

	/// <remarks>Implements the contract the completion controller documents for
	/// <see cref="TextCompletionController.CancelScheduledRequest"/>. The controller guards its public
	/// entry point and calls this again while pending work is canceled during a window close and during
	/// controller disposal after the scheduler itself was disposed, where the debouncer's own
	/// post-disposal no-op keeps the call safe.</remarks>
	internal void CancelScheduledRequest()
	{
		_requestDebouncer.Cancel();
		_setRequestScheduled(false);
	}

	/// <summary>
	/// Logs a completion request failure with the package's event id <c>1000</c>, for the scheduled request
	/// path and the controller's standard request pipeline.
	/// </summary>
	/// <param name="exception">The failure to log.</param>
	internal void LogRequestFailed(Exception exception)
		=> LogCompletionRequestFailed(_logger, exception);

	/// <summary>
	/// Stops the debounce timer. The method is idempotent.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_requestDebouncer.Dispose();
	}

	// A dispatcher debounce callback that starts the scheduled asynchronous request. Like a timer handler it
	// is an async void dispatcher method, so every failure is contained here; a failure reaches the host
	// through the logger instead of escaping the dispatcher.
	private async void RunScheduledRequest()
	{
		_setRequestScheduled(false);

		if (_scheduledRequestAsync is null)
			return;

		try
		{
			await _scheduledRequestAsync().ConfigureAwait(true);
		}
		catch (OperationCanceledException)
		{
			// A scheduled request that observes the request token being canceled reports cancellation,
			// not a failure.
		}
		catch (Exception exception)
		{
			if (_isDisposed)
				return;

			LogCompletionRequestFailed(_logger, exception);
		}
	}
}
