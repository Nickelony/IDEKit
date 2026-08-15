using Microsoft.Extensions.Logging;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Captures formatted log messages for test assertions.
/// </summary>
internal sealed class CapturingLogger : ILogger
{
	/// <summary>
	/// Gets the formatted messages captured so far, in log order.
	/// </summary>
	public List<string> Messages { get; } = [];

	/// <summary>
	/// Gets the entries captured so far, in log order, including each entry's level and event id.
	/// </summary>
	public List<LogEntry> Entries { get; } = [];

	/// <inheritdoc/>
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		=> null;

	/// <inheritdoc/>
	public bool IsEnabled(LogLevel logLevel)
		=> true;

	/// <inheritdoc/>
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		string message = formatter(state, exception);

		Messages.Add(message);
		Entries.Add(new LogEntry(logLevel, eventId, message));
	}

	/// <summary>
	/// Describes one captured log entry.
	/// </summary>
	/// <param name="Level">The entry's log level.</param>
	/// <param name="EventId">The entry's event id, including its name when the producer supplied one.</param>
	/// <param name="Message">The formatted message.</param>
	public readonly record struct LogEntry(LogLevel Level, EventId EventId, string Message);
}
