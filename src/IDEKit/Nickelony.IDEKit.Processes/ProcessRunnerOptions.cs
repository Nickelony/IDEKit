namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Configures the bounded waits of <see cref="ProcessRunner"/> - how long a terminated process may
/// take to exit and how long the redirected pipes may take to close - and how much of each redirected
/// stream a run captures.
/// </summary>
/// <remarks>
/// <para>
/// The two grace periods default to five seconds and the capture is unbounded by default. A host can
/// shorten a period to keep a tool integration responsive; the run then reports more exits as
/// unobserved and more output as unavailable, because the runner never waits past the configured
/// period. A host can also bound the capture so a flooding tool cannot exhaust memory while it runs.
/// </para>
/// <para>
/// The options type carries no diagnostic callback or logging dependency: a difficulty the runner
/// cannot recover from - a termination that did not take, an exit it could not observe, or a capture
/// that failed or exceeded its grace period - is reported through the outcome and the
/// <see langword="null"/> exit code or output, without a reason.
/// </para>
/// </remarks>
public sealed record ProcessRunnerOptions
{
	private readonly TimeSpan _terminationGracePeriod = TimeSpan.FromSeconds(5);
	private readonly TimeSpan _outputDrainGracePeriod = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Gets the time to wait for a terminated process to exit before the exit is reported as unobserved.
	/// </summary>
	/// <remarks>
	/// The period applies only after the runner attempted termination. Zero confirms only an exit that is
	/// already observable, so a timeout or cancellation reports no exit code unless the process exited on
	/// its own.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or it is longer than
	/// <see cref="int.MaxValue"/> milliseconds.</exception>
	public TimeSpan TerminationGracePeriod
	{
		get => _terminationGracePeriod;
		init => _terminationGracePeriod = ValidateGracePeriod(value, nameof(TerminationGracePeriod));
	}

	/// <summary>
	/// Gets the time to wait for the redirected pipes to close after the exit was observed.
	/// </summary>
	/// <remarks>
	/// The period applies only when output was redirected. It bounds the wait for descendants that
	/// inherited a redirected stream and keep it open; Zero reports the output as unavailable unless the
	/// capture already completed.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative or it is longer than
	/// <see cref="int.MaxValue"/> milliseconds.</exception>
	public TimeSpan OutputDrainGracePeriod
	{
		get => _outputDrainGracePeriod;
		init => _outputDrainGracePeriod = ValidateGracePeriod(value, nameof(OutputDrainGracePeriod));
	}

	private readonly int? _maxCapturedCharactersPerStream;

	/// <summary>
	/// Gets the maximum number of characters captured from each redirected stream, or
	/// <see langword="null"/> when the capture is unbounded.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bound applies while the process runs: a stream that exceeds it keeps being drained, so the
	/// process never blocks on its own output, but the excess is discarded. A capture that reached the
	/// bound is reported as truncated; the reported text then holds at most the bound, so output that
	/// was exactly the bound long is reported as truncated too.
	/// </para>
	/// <para>
	/// The default is <see langword="null"/>, so a host that does not set the bound keeps the whole
	/// capture. Zero captures nothing and reports any output as truncated.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int? MaxCapturedCharactersPerStream
	{
		get => _maxCapturedCharactersPerStream;
		init => _maxCapturedCharactersPerStream = value is { } characters && characters < 0
			? throw new ArgumentOutOfRangeException(
				nameof(MaxCapturedCharactersPerStream), value, "The capture bound must not be negative.")
			: value;
	}

	/// <summary>
	/// Gets the default options: a five-second termination grace period, a five-second output-drain grace
	/// period, and an unbounded capture. A host overrides only the members it needs with a <c>with</c>
	/// expression.
	/// </summary>
	public static ProcessRunnerOptions Default { get; } = new();

	// The timer that enforces a grace period rejects values beyond its own ceiling, so the period is bounded
	// here; the bound matches the request timeout so every configured duration shares one ceiling.
	private static TimeSpan ValidateGracePeriod(TimeSpan value, string propertyName)
		=> value < TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue
			? throw new ArgumentOutOfRangeException(
				propertyName, value, "The grace period must be non-negative and at most int.MaxValue milliseconds.")
			: value;
}
