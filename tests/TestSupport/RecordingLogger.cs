using Microsoft.Extensions.Logging;

namespace Nickelony.Testing;

/// <summary>Records <see cref="ILogger"/> output for test assertions, from any thread.</summary>
/// <remarks>
/// It is also an <see cref="ILoggerFactory"/> that hands out itself, so a typed
/// logger from <see cref="LoggerFactoryExtensions.CreateLogger{T}(ILoggerFactory)"/> and a plain <see cref="ILogger"/> parameter share one capture.
/// <see cref="Entries"/> holds each entry as a typed record, and <see cref="Logs"/> renders the same entries as text.
/// </remarks>
internal sealed class RecordingLogger : IDisposable, ILogger, ILoggerFactory
{
	private readonly LogLevel _minLevel;
	private readonly object _syncRoot = new();
	private readonly List<LogEntry> _entries = [];

	/// <param name="minLevel">The lowest level that the logger captures.</param>
	public RecordingLogger(LogLevel minLevel = LogLevel.Trace) => _minLevel = minLevel;

	/// <summary>A thread-safe snapshot of the captured entries, in log order, including each entry's level and event id.</summary>
	public IReadOnlyList<LogEntry> Entries
	{
		get
		{
			lock (_syncRoot)
				return [.. _entries];
		}
	}

	/// <summary>A thread-safe snapshot of the formatted messages, in log order.</summary>
	public IReadOnlyList<string> Messages
	{
		get
		{
			lock (_syncRoot)
				return [.. _entries.Select(entry => entry.Message)];
		}
	}

	/// <summary>A thread-safe snapshot of the entries rendered as <c>Level|Message|ExceptionMessage</c>, in log order.</summary>
	/// <remarks>
	/// A failure message can print the whole list, and a test can search it for the text of a message or of its exception.
	/// </remarks>
	public IReadOnlyList<string> Logs
	{
		get
		{
			lock (_syncRoot)
				return [.. _entries.Select(entry => $"{FormatLevel(entry.Level)}|{entry.Message}|{entry.ExceptionMessage}")];
		}
	}

	/// <summary>Determines whether any captured entry was written at <paramref name="level"/> or higher.</summary>
	/// <remarks>
	/// Tests that only need to observe that a diagnostic was emitted assert through this member instead of matching message wording, so a reworded
	/// message does not break the test while the level stays pinned. A test that must distinguish several diagnostics in one window, or that asserts
	/// a message is absent, still matches the wording deliberately.
	/// </remarks>
	public bool HasEntryAtLeast(LogLevel level)
	{
		lock (_syncRoot)
			return _entries.Any(entry => entry.Level >= level);
	}

	/// <inheritdoc/>
	public ILogger CreateLogger(string categoryName) => this;

	/// <inheritdoc/>
	public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException("RecordingLogger does not accept external providers.");

	/// <inheritdoc/>
	public void Dispose() { }

	/// <inheritdoc/>
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull => null;

	/// <inheritdoc/>
	public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

	/// <inheritdoc/>
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel))
			return;

		LogEntry entry = new(Level: logLevel, eventId, Message: formatter(state, exception), ExceptionMessage: exception?.Message);

		lock (_syncRoot)
			_entries.Add(entry);
	}

	private static string FormatLevel(LogLevel logLevel)
	{
		return logLevel switch
		{
			LogLevel.Trace => "Trace",
			LogLevel.Debug => "Debug",
			LogLevel.Information => "Info",
			LogLevel.Warning => "Warn",
			LogLevel.Error => "Error",
			LogLevel.Critical => "Fatal",
			_ => logLevel.ToString(),
		};
	}

	/// <summary>Describes one captured log entry.</summary>
	/// <param name="EventId">The entry's event id, including its name when the producer supplied one.</param>
	/// <param name="ExceptionMessage">The message of the entry's exception, or <see langword="null"/> when it carried none.</param>
	public readonly record struct LogEntry(LogLevel Level, EventId EventId, string Message, string? ExceptionMessage);
}
