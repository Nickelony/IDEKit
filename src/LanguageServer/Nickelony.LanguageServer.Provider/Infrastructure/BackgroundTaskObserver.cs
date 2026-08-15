namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Observes provider-owned background tasks with the package's shared exception policy.
/// </summary>
/// <remarks>
/// Cancellation and disposal are expected outcomes, transport failures are debug-level diagnostics, and anything
/// else is logged as a warning, so faults on provider-owned background tasks are always observed and logged.
/// </remarks>
internal static class BackgroundTaskObserver
{
	/// <summary>
	/// Observes <paramref name="task"/> without blocking the caller.
	/// </summary>
	/// <param name="logger">The logger that receives failure diagnostics.</param>
	/// <param name="task">The background task to observe.</param>
	/// <param name="operation">The operation name used in log messages.</param>
	internal static void Observe(ILogger logger, Task task, string operation)
	{
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(task);
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);

		_ = ObserveAsync(logger, task, operation);

		static async Task ObserveAsync(ILogger observerLogger, Task observedTask, string observedOperation)
		{
			try
			{
				await observedTask.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{ }
			catch (IOException exception)
			{
				observerLogger.LogDebug(exception, "Background operation '{Operation}' failed with a transport error.", observedOperation);
			}
			catch (ObjectDisposedException)
			{ }
			catch (Exception exception)
			{
				observerLogger.LogWarning(exception, "Background operation '{Operation}' failed.", observedOperation);
			}
		}
	}
}
