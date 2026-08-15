using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Testing;

/// <summary>
/// Captures <see cref="ILogger"/> output for test assertions.
/// </summary>
/// <remarks>
/// Implements <see cref="ILoggerFactory"/> so typed loggers can be obtained via
/// <see cref="LoggerFactoryExtensions.CreateLogger{T}(ILoggerFactory)"/>, and <see cref="ILogger"/>
/// so it can be passed directly to APIs that accept a non-generic logger.
/// Each entry is rendered as <c>Level|Message|ExceptionMessage</c> to match the previous NLog layout.
/// </remarks>
internal sealed class TestLoggerScope : IDisposable, ILogger, ILoggerFactory
{
	private readonly LogLevel _minLevel;
	private readonly object _logsSync = new();
	private readonly List<string> _logs = [];

	public TestLoggerScope(LogLevel minLevel)
		=> _minLevel = minLevel;

	/// <summary>
	/// Gets a thread-safe snapshot of the captured log entries.
	/// </summary>
	public IReadOnlyList<string> Logs
	{
		get
		{
			lock (_logsSync)
				return [.. _logs];
		}
	}

	/// <summary>
	/// Determines whether any captured entry was written at <paramref name="level"/> or higher.
	/// </summary>
	/// <param name="level">The minimum level to match.</param>
	/// <returns><see langword="true"/> when at least one entry meets the level.</returns>
	/// <remarks>
	/// Tests that only need to observe that a diagnostic was emitted assert through this member instead of
	/// matching message wording, so a reworded message does not break the test while the level stays pinned.
	/// A test that must distinguish several diagnostics in one window, or that asserts a message is absent,
	/// still matches the wording deliberately.
	/// </remarks>
	public bool HasEntryAtLeast(LogLevel level)
	{
		lock (_logsSync)
		{
			foreach (string entry in _logs)
			{
				if (TryReadLevel(entry, out LogLevel entryLevel) && entryLevel >= level)
					return true;
			}
		}

		return false;
	}

	private static bool TryReadLevel(string entry, out LogLevel level)
	{
		int separatorIndex = entry.IndexOf('|', StringComparison.Ordinal);

		if (separatorIndex <= 0)
		{
			level = LogLevel.None;
			return false;
		}

		level = entry[..separatorIndex] switch
		{
			"Trace" => LogLevel.Trace,
			"Debug" => LogLevel.Debug,
			"Info" => LogLevel.Information,
			"Warn" => LogLevel.Warning,
			"Error" => LogLevel.Error,
			"Fatal" => LogLevel.Critical,
			_ => LogLevel.None
		};

		return level != LogLevel.None;
	}

	public ILogger CreateLogger(string categoryName) => this;

	public void AddProvider(ILoggerProvider provider)
		=> throw new NotSupportedException("TestLoggerScope does not accept external providers.");

	public void Dispose()
	{ }

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel))
			return;

		lock (_logsSync)
			_logs.Add($"{FormatLevel(logLevel)}|{formatter(state, exception)}|{exception?.Message}");
	}

	public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	private static string FormatLevel(LogLevel logLevel) => logLevel switch
	{
		LogLevel.Trace => "Trace",
		LogLevel.Debug => "Debug",
		LogLevel.Information => "Info",
		LogLevel.Warning => "Warn",
		LogLevel.Error => "Error",
		LogLevel.Critical => "Fatal",
		_ => logLevel.ToString()
	};
}
