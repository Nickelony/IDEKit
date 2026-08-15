using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Pins the hook containment policy: synchronous and asynchronous hook faults are logged and contained, while
/// asynchronous-hook cancellation stays silent as expected teardown.
/// </summary>
[TestClass]
public sealed class HookContainmentTests
{
	[TestMethod]
	public void Invoke_WhenTheSynchronousHookThrows_LogsTheFailure()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		HookContainment.Invoke(logScope, "Test", () => throw new InvalidOperationException("Simulated hook failure."), "test hook");

		string log = logScope.Logs.Single();

		Assert.IsTrue(log.StartsWith("Warn|", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("test hook", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Simulated hook failure.", StringComparison.Ordinal), log);
	}

	[TestMethod]
	public void Invoke_WithAResult_WhenTheSynchronousHookThrows_ReturnsTheFallbackValue()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		int result = HookContainment.Invoke(logScope, "Test", new Func<int>(() => throw new InvalidOperationException("Simulated hook failure.")), 42, "value hook");

		Assert.AreEqual(42, result);
		Assert.IsTrue(logScope.Logs.Single().Contains("value hook", StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task InvokeAsync_WhenTheHookThrows_LogsTheFailure()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		await HookContainment.InvokeAsync(logScope, "Test",
			() => Task.FromException(new InvalidOperationException("Simulated async hook failure.")), "async hook").ConfigureAwait(false);

		string log = logScope.Logs.Single();

		Assert.IsTrue(log.StartsWith("Warn|", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("async hook", StringComparison.Ordinal), log);
		Assert.IsTrue(log.Contains("Simulated async hook failure.", StringComparison.Ordinal), log);
	}

	[TestMethod]
	public async Task InvokeAsync_WhenTheHookIsCanceled_StaysSilent()
	{
		using var logScope = new TestLoggerScope(LogLevel.Trace);

		await HookContainment.InvokeAsync(logScope, "Test",
			() => Task.FromCanceled(new CancellationToken(canceled: true)), "canceled hook").ConfigureAwait(false);

		Assert.AreEqual(0, logScope.Logs.Count);
	}
}
