using System.Diagnostics.CodeAnalysis;

#if QUICKPICKFAMILY
namespace Nickelony.QuickPick;

#else
namespace Nickelony.IDEKit.Core.Callbacks;

#endif

/// <summary>
/// Runs a host-supplied callback so that its fault is reported and never escapes into the dispatcher, timer or event-handler frame that invoked it.
/// </summary>
/// <remarks>
/// <para>
/// The caller passes the call that reports the fault, so the helper needs no logger and every package keeps its own log text and event id.
/// </para>
/// <para>
/// Every exception is contained, <see cref="OperationCanceledException"/> included: a host callback is synchronous
/// code with no cancellation contract, so a cancellation it throws is treated like any other host failure. An exception thrown by <c>reportFault</c>
/// itself propagates.
/// </para>
/// </remarks>
#if QUICKPICKFAMILY
internal
#else
public
#endif
static class CallbackContainment
{
	/// <summary>Runs a host callback that reports no result.</summary>
	/// <param name="callback">The host callback to run.</param>
	/// <param name="reportFault">Reports the exception that <paramref name="callback"/> threw.</param>
	/// <returns><see langword="true"/> when the callback completed; otherwise, <see langword="false"/>.</returns>
	public static bool TryRun(Action callback, Action<Exception> reportFault)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ArgumentNullException.ThrowIfNull(reportFault);

		try
		{
			callback();
			return true;
		}
		catch (Exception exception)
		{
			reportFault(exception);
			return false;
		}
	}

	/// <summary>Runs a host callback that produces a result.</summary>
	/// <typeparam name="TResult">The callback's result type.</typeparam>
	/// <param name="callback">The host callback to run.</param>
	/// <param name="reportFault">Reports the exception that <paramref name="callback"/> threw.</param>
	/// <param name="result">The callback's result when it completed; otherwise, the default of <typeparamref name="TResult"/>.</param>
	/// <returns><see langword="true"/> when the callback completed; otherwise, <see langword="false"/>.</returns>
	public static bool TryRun<TResult>(Func<TResult> callback, Action<Exception> reportFault, [MaybeNullWhen(false)] out TResult result)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ArgumentNullException.ThrowIfNull(reportFault);

		try
		{
			result = callback();
			return true;
		}
		catch (Exception exception)
		{
			reportFault(exception);
			result = default;
			return false;
		}
	}

	/// <summary>Runs a host callback that produces a result, and returns a fallback value when it throws.</summary>
	/// <typeparam name="TResult">The callback's result type.</typeparam>
	/// <param name="callback">The host callback to run.</param>
	/// <param name="fallbackValue">The value returned when <paramref name="callback"/> throws.</param>
	/// <param name="reportFault">Reports the exception that <paramref name="callback"/> threw.</param>
	/// <returns>The callback's result, or <paramref name="fallbackValue"/> when the callback threw.</returns>
	public static TResult Invoke<TResult>(Func<TResult> callback, TResult fallbackValue, Action<Exception> reportFault)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ArgumentNullException.ThrowIfNull(reportFault);

		try
		{
			return callback();
		}
		catch (Exception exception)
		{
			reportFault(exception);
			return fallbackValue;
		}
	}
}
