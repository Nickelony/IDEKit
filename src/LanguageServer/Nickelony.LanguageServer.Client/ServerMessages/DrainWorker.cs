using System.Threading.Channels;

namespace Nickelony.LanguageServer.Client;

/// <summary>Runs a drain pass on a background loop each time it is signaled, and counts the passes in flight.</summary>
/// <remarks>
/// A bounded one-slot wake signal collapses a burst of signals into one pass, and it drops a write that finds it full instead of failing the write.
/// The loop ends when the signal completes or the lifetime is canceled, and a pass that is already running finishes. The delivery model lives
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class DrainWorker
{
	private readonly Action _drain;
	private readonly CancellationToken _lifetimeToken;

	private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1)
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
			FullMode = BoundedChannelFullMode.DropWrite,
		}
	);

	// Passes in flight; counting them closes the gap between taking a pending item and finishing its delivery.
	private int _activePasses;

	// Set once Complete runs, so an item that arrives after teardown started is dropped instead of being queued into
	// a pipeline whose loops are completing and will never deliver it.
	private int _isCompleted;

	/// <param name="drain">Delivers everything that is pending; the worker calls it once per wake-up.</param>
	/// <param name="lifetimeToken">Signals client disposal to the loop.</param>
	internal DrainWorker(Action drain, CancellationToken lifetimeToken)
	{
		_drain = drain;
		_lifetimeToken = lifetimeToken;
	}

	/// <summary>Whether a pass is running, which covers the gap between taking a pending item and finishing its delivery.</summary>
	internal bool HasPassInFlight => Volatile.Read(ref _activePasses) != 0;

	/// <summary>Whether <see cref="Complete"/> ran.</summary>
	internal bool IsCompleted => Volatile.Read(ref _isCompleted) != 0;

	/// <summary>Wakes the loop for a drain pass.</summary>
	/// <returns><see langword="false"/> when the wake signal completed; otherwise, <see langword="true"/>.</returns>
	internal bool TrySignal() => _signal.Writer.TryWrite(true);

	/// <summary>Runs a drain pass for each wake-up until the wake signal completes or the lifetime is canceled.</summary>
	internal async Task RunAsync()
	{
		ChannelReader<bool> reader = _signal.Reader;

		try
		{
			while (await reader.WaitToReadAsync(_lifetimeToken).ConfigureAwait(false))
			{
				Interlocked.Increment(ref _activePasses);

				try
				{
					while (reader.TryRead(out _))
					{ }

					_drain();
				}
				finally
				{
					Interlocked.Decrement(ref _activePasses);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on dispose.
		}
	}

	/// <summary>Completes the wake signal so the loop finishes during disposal; the owner drops what it still has pending.</summary>
	internal void Complete()
	{
		// Mark completion first so a concurrent signal becomes a no-op instead of queueing an item that this
		// teardown will never deliver; then complete the wake signal.
		Interlocked.Exchange(ref _isCompleted, 1);
		_signal.Writer.TryComplete();
	}
}
