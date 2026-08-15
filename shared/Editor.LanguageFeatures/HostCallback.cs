using Microsoft.Extensions.Logging;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures;
#endif

/// <summary>
/// Runs a host-supplied callback inside the language-feature controllers' shared containment: a throwing
/// callback is reported through the controller's host-callback logger and reported as not applied instead of
/// escaping into a dispatcher, timer, or event-handler frame.
/// </summary>
/// <remarks>
/// <para>
/// The hover, signature-help, and code-action controllers invoke host callbacks (tooltip and popup writers,
/// request builders, caret getters) from controller-owned frames whose failures must not escape. Each
/// controller keeps its own event-id specific <c>LoggerMessage</c> method and passes it here, so the
/// try/catch/log-and-report-not-applied shape is defined once instead of being repeated per call site.
/// </para>
/// <para>
/// The helper contains every exception, including <see cref="OperationCanceledException"/>: a host callback
/// is synchronous user code with no cancellation contract, so a cancellation it throws is treated like any
/// other host failure.
/// </para>
/// </remarks>
internal static class HostCallback
{
	/// <summary>
	/// Runs a host callback that reports no result.
	/// </summary>
	/// <param name="callback">The host callback to run.</param>
	/// <param name="logger">The logger the failure is reported to.</param>
	/// <param name="log">The controller's host-callback logger.</param>
	/// <returns><see langword="true"/> when the callback completed; otherwise, <see langword="false"/>.</returns>
	internal static bool TryRun(Action callback, ILogger logger, Action<ILogger, Exception?> log)
	{
		try
		{
			callback();
			return true;
		}
		catch (Exception exception)
		{
			log(logger, exception);
			return false;
		}
	}

	/// <summary>
	/// Runs a host callback that produces a result.
	/// </summary>
	/// <typeparam name="TResult">The callback's result type.</typeparam>
	/// <param name="callback">The host callback to run.</param>
	/// <param name="logger">The logger the failure is reported to.</param>
	/// <param name="log">The controller's host-callback logger.</param>
	/// <param name="result">
	/// The callback's result when it completed; otherwise, the default of <typeparamref name="TResult"/>.
	/// </param>
	/// <returns><see langword="true"/> when the callback completed; otherwise, <see langword="false"/>.</returns>
	internal static bool TryRun<TResult>(
		Func<TResult> callback,
		ILogger logger,
		Action<ILogger, Exception?> log,
		out TResult result)
	{
		try
		{
			result = callback();
			return true;
		}
		catch (Exception exception)
		{
			log(logger, exception);
			result = default!;
			return false;
		}
	}
}
