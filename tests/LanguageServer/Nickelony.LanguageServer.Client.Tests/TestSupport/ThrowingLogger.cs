using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>A logger whose every enabled write throws, as a faulty logging provider does.</summary>
internal sealed class ThrowingLogger : ILogger
{
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
		throw new InvalidOperationException("Simulated logger failure.");
}
