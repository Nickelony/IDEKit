using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Core.Validation;
#if AVALONIAEDIT
using Avalonia.Threading;
#else
using System.Windows.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Threading;

#else

namespace Nickelony.IDEKit.AvalonEdit.Threading;

#endif

/// <summary>Adapts the engine's <see cref="Dispatcher"/> to the toolkit-neutral <see cref="IUiDispatcher"/> interface.</summary>
/// <remarks>
/// A host that runs the editor on its UI thread uses <see cref="FromCurrent"/>; one that marshals to a specific dispatcher passes
/// it to the constructor. The adapter holds no state beyond the wrapped dispatcher and is safe to share.
/// </remarks>
public sealed class UiDispatcher : IUiDispatcher
{
	private readonly Dispatcher _dispatcher;

	/// <param name="dispatcher">The dispatcher to adapt; must not be <see langword="null"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <see langword="null"/>.</exception>
	public UiDispatcher(Dispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		_dispatcher = dispatcher;
	}

	/// <summary>An adapter over the current UI thread's dispatcher.</summary>
#if AVALONIAEDIT
	public static UiDispatcher FromCurrent() => new(Dispatcher.UIThread);
#else
	public static UiDispatcher FromCurrent() => new(Dispatcher.CurrentDispatcher);
#endif

	/// <summary>Returns an adapter over the given engine dispatcher.</summary>
	/// <param name="dispatcher">The dispatcher to adapt; must not be <see langword="null"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <see langword="null"/>.</exception>
	public static UiDispatcher From(Dispatcher dispatcher) => new(dispatcher);

	/// <inheritdoc/>
	public bool CheckAccess() => _dispatcher.CheckAccess();

	/// <inheritdoc/>
	public void VerifyAccess() => _dispatcher.VerifyAccess();

	/// <inheritdoc/>
	public void Invoke(Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		if (_dispatcher.CheckAccess())
			callback();
		else
			_dispatcher.Invoke(callback);
	}

	/// <inheritdoc/>
	public TResult Invoke<TResult>(Func<TResult> callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		return _dispatcher.CheckAccess() ? callback() : _dispatcher.Invoke(callback);
	}

	/// <inheritdoc/>
	public Task InvokeAsync(Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		return InvokeCoreAsync(callback);
	}

	/// <inheritdoc/>
	public void Post(Action callback, UiDispatcherPriority priority = UiDispatcherPriority.Normal)
	{
		ArgumentNullException.ThrowIfNull(callback);

#if AVALONIAEDIT
		_dispatcher.Post(callback, ToEnginePriority(priority));
#else
		_dispatcher.BeginInvoke(ToEnginePriority(priority), callback);
#endif
	}

	/// <inheritdoc/>
	public IDisposable Schedule(TimeSpan delay, Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		delay = NumericValidation.NonNegative(delay, nameof(delay));

#if AVALONIAEDIT
		var timer = new DispatcherTimer(delay, DispatcherPriority.Background, _dispatcher);
#else
		var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = delay };
#endif
		var handle = new ScheduledTimer(timer, callback);

		timer.Tick += handle.HandleTick;
		timer.Start();
		return handle;
	}

	// The guard runs before this async method, so a null callback throws at the call instead of faulting the returned task.
	private async Task InvokeCoreAsync(Action callback)
	{
		if (_dispatcher.CheckAccess())
		{
			callback();
			return;
		}

#if AVALONIAEDIT
		await _dispatcher.InvokeAsync(callback).GetTask().ConfigureAwait(true);
#else
		await _dispatcher.InvokeAsync(callback).Task.ConfigureAwait(true);
#endif
	}

	private static DispatcherPriority ToEnginePriority(UiDispatcherPriority priority)
	{
		return priority switch
		{
			UiDispatcherPriority.Background => DispatcherPriority.Background,
			UiDispatcherPriority.ContextIdle => DispatcherPriority.ContextIdle,
			UiDispatcherPriority.Render => DispatcherPriority.Render,
			_ => DispatcherPriority.Normal,
		};
	}

	/// <summary>Owns a one-shot timer: it fires the callback once and cancels on disposal.</summary>
	private sealed class ScheduledTimer : IDisposable
	{
		private readonly DispatcherTimer _timer;
		private readonly Action _callback;

		private bool _isCompleted;

		internal ScheduledTimer(DispatcherTimer timer, Action callback)
		{
			_timer = timer;
			_callback = callback;
		}

		internal void HandleTick(object? sender, EventArgs e)
		{
			if (_isCompleted)
				return;

			_isCompleted = true;
			_timer.Stop();
			_timer.Tick -= HandleTick;
			_callback();
		}

		public void Dispose()
		{
			if (_isCompleted)
				return;

			_isCompleted = true;
			_timer.Stop();
			_timer.Tick -= HandleTick;
		}
	}
}
