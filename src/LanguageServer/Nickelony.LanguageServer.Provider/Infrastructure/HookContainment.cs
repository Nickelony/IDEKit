namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Runs language hooks with the provider framework's containment policy: an unexpected hook fault is logged and
/// contained instead of escaping a framework entry point, and asynchronous-hook cancellation is treated as
/// expected teardown.
/// </summary>
/// <remarks>
/// The shared failure log text lives here so every contained hook call site reports failures identically. The
/// asynchronous overload treats cancellation as expected teardown on purpose; the synchronous overloads treat it
/// like any other hook fault because a synchronous hook has no cancellation contract.
/// </remarks>
internal static class HookContainment
{
	/// <summary>
	/// Runs a void hook and contains an unexpected hook fault.
	/// </summary>
	/// <param name="logger">The logger that receives the failure diagnostic.</param>
	/// <param name="providerDisplayName">The provider name used in the failure log.</param>
	/// <param name="hook">The hook invocation.</param>
	/// <param name="hookDescription">The hook description used in the failure log.</param>
	internal static void Invoke(ILogger logger, string providerDisplayName, Action hook, string hookDescription)
	{
		try
		{
			hook();
		}
		catch (Exception exception)
		{
			LogFailure(logger, providerDisplayName, hookDescription, exception);
		}
	}

	/// <summary>
	/// Runs a hook and contains an unexpected hook fault, returning the fallback value instead.
	/// </summary>
	/// <typeparam name="TResult">The hook result type.</typeparam>
	/// <param name="logger">The logger that receives the failure diagnostic.</param>
	/// <param name="providerDisplayName">The provider name used in the failure log.</param>
	/// <param name="hook">The hook invocation.</param>
	/// <param name="fallbackValue">The value returned when the hook throws.</param>
	/// <param name="hookDescription">The hook description used in the failure log.</param>
	/// <returns>The hook result, or <paramref name="fallbackValue"/> when the hook throws.</returns>
	internal static TResult Invoke<TResult>(ILogger logger, string providerDisplayName, Func<TResult> hook, TResult fallbackValue, string hookDescription)
	{
		try
		{
			return hook();
		}
		catch (Exception exception)
		{
			LogFailure(logger, providerDisplayName, hookDescription, exception);

			return fallbackValue;
		}
	}

	/// <summary>
	/// Runs an asynchronous hook and contains an unexpected hook fault; cancellation is treated as expected
	/// teardown and stays silent.
	/// </summary>
	/// <param name="logger">The logger that receives the failure diagnostic.</param>
	/// <param name="providerDisplayName">The provider name used in the failure log.</param>
	/// <param name="hook">The asynchronous hook invocation.</param>
	/// <param name="hookDescription">The hook description used in the failure log.</param>
	internal static async Task InvokeAsync(ILogger logger, string providerDisplayName, Func<Task> hook, string hookDescription)
	{
		try
		{
			await hook().ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{ }
		catch (Exception exception)
		{
			LogFailure(logger, providerDisplayName, hookDescription, exception);
		}
	}

	private static void LogFailure(ILogger logger, string providerDisplayName, string hookDescription, Exception exception)
		=> logger.LogWarning(exception, "The {HookDescription} hook failed for the {DisplayName} provider; the failure is contained.",
			hookDescription, providerDisplayName);
}
